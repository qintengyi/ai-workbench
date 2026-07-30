"""REST API 路由。

响应统一：{code, msg, data}
- 成功 code = 0
- 失败 code = 非 0（沿用 HTTP 状态码语义：400/401/404/500/503）

路由表（见 ARCHITECTURE.md）：
  POST /api/auth/login
  GET  /api/status
  GET/POST /api/conversations
  GET/POST /api/conversations/{id}/messages
  GET/POST /api/files
  GET  /api/providers
  PUT  /api/providers/{id}
  GET  /health
"""

from __future__ import annotations

import logging
import time
from typing import Any, Optional

from aiohttp import web

import auth
import broker
import db

logger = logging.getLogger(__name__)


def _ok(data: Any = None, msg: str = "success") -> web.Response:
    return web.json_response({"code": 0, "msg": msg, "data": data})


def _err(code: int, msg: str, data: Any = None) -> web.Response:
    return web.json_response({"code": code, "msg": msg, "data": data}, status=code)


@web.middleware
async def auth_middleware(request: web.Request, handler):
    """除 login 与 /health 外，所有 /api/* 需要 Bearer token。"""
    path = request.path
    if path == "/api/auth/login" or not path.startswith("/api/"):
        return await handler(request)
    token = auth.extract_bearer(request.headers.get("Authorization"))
    if not token:
        token = request.rel_url.query.get("token")
    payload = auth.verify_token(token) if token else None
    if not payload:
        return _err(401, "unauthorized")
    request["user"] = payload
    return await handler(request)


# ─── auth ───


async def login(request: web.Request) -> web.Response:
    try:
        body = await request.json()
    except Exception:
        return _err(400, "invalid json body")
    username = (body.get("username") or "").strip()
    password = body.get("password") or ""
    if not username or not password:
        return _err(400, "username and password required")
    result = auth.login(username, password)
    if not result:
        return _err(401, "invalid username or password")
    return _ok(result)


# ─── status ───


async def get_status(request: web.Request) -> web.Response:
    payload = broker.get_status_payload()
    # 附带 provider/会话计数
    try:
        payload["providers"] = len(db.list_providers())
        payload["conversations"] = len(db.list_conversations(limit=200))
    except Exception:
        pass
    return _ok(payload)


# ─── conversations ───


async def list_or_create_conversations(request: web.Request) -> web.Response:
    if request.method == "GET":
        try:
            limit = int(request.rel_url.query.get("limit", "50"))
            offset = int(request.rel_url.query.get("offset", "0"))
        except ValueError:
            return _err(400, "invalid limit/offset")
        return _ok(db.list_conversations(limit=limit, offset=offset))
    # POST 新建会话
    try:
        body = await request.json()
    except Exception:
        return _err(400, "invalid json body")
    conv_id = body.get("id") or _gen_id("conv")
    title = body.get("title") or "新会话"
    provider_id = body.get("providerId") or body.get("provider_id")
    model_id = body.get("modelId") or body.get("model_id")
    db.upsert_conversation(conv_id, title=title, provider_id=provider_id, model_id=model_id)
    # 若 agent 在线，同步通知
    if broker.is_agent_online():
        try:
            await broker.send_to_agent({
                "type": "new_conversation",
                "request_id": "",
                "data": {"id": conv_id, "title": title, "providerId": provider_id, "modelId": model_id},
                "ts": int(time.time()),
            })
        except Exception as e:
            logger.warning("通知 agent 新建会话失败: %s", e)
    return _ok(db.get_conversation(conv_id))


async def messages_handler(request: web.Request) -> web.Response:
    """GET 列出消息 / POST 发送消息（转发给 agent 执行 AI 调用）。"""
    conv_id = request.match_info.get("id") or ""
    if not conv_id:
        return _err(400, "conversation id required")
    if request.method == "GET":
        try:
            limit = int(request.rel_url.query.get("limit", "100"))
        except ValueError:
            return _err(400, "invalid limit")
        before_raw = request.rel_url.query.get("before")
        before: Optional[int] = None
        if before_raw:
            try:
                before = int(before_raw)
            except ValueError:
                return _err(400, "invalid before")
        return _ok(db.list_messages(conv_id, limit=limit, before=before))
    # POST 发送消息
    if not broker.is_agent_online():
        return _err(503, "agent offline")
    try:
        body = await request.json()
    except Exception:
        return _err(400, "invalid json body")
    content = body.get("content")
    if not content or not isinstance(content, str):
        return _err(400, "content required")
    images = body.get("images") or []
    # 落库一条 user 消息
    msg_id = db.insert_message(conv_id, "user", content, images=images or None, ts=int(time.time()))
    # 转给 agent 执行 AI 调用
    try:
        forward = {
            "type": "send_message",
            "request_id": "",
            "data": {
                "conversation_id": conv_id,
                "content": content,
                "images": images,
                "msg_id": msg_id,
                "provider_id": body.get("providerId") or body.get("provider_id"),
                "model_id": body.get("modelId") or body.get("model_id"),
                "effort": body.get("effort"),
            },
            "ts": int(time.time()),
        }
        ok = await broker.send_to_agent(forward)
        if not ok:
            return _err(503, "agent offline")
    except broker.AgentOfflineError:
        return _err(503, "agent offline")
    except Exception as e:
        logger.exception("post_message 失败")
        return _err(500, str(e))
    return _ok({"ok": True, "queued": True, "msg_id": msg_id})


# ─── files（iOS 浏览 Windows E:\code 树，转发给 agent）───


async def files_handler(request: web.Request) -> web.Response:
    """GET 文件树 / POST 读文件内容。均转发给 agent 执行。"""
    if not broker.is_agent_online():
        return _err(503, "agent offline")
    if request.method == "GET":
        path = request.rel_url.query.get("path") or ""
        try:
            result = await broker.send_command(
                "browse_files",
                {"path": path},
                wait=True,
                timeout=30.0,
            )
        except broker.AgentOfflineError:
            return _err(503, "agent offline")
        except Exception as e:
            return _err(500, str(e))
        if not result.get("ok", True) and result.get("error") == "timeout":
            return _err(504, "agent timeout")
        return _ok(result.get("data", {}))
    # POST：读文件
    try:
        body = await request.json()
    except Exception:
        return _err(400, "invalid json body")
    path = body.get("path") or ""
    if not path:
        return _err(400, "path required")
    try:
        result = await broker.send_command(
            "read_file",
            {"path": path},
            wait=True,
            timeout=30.0,
        )
    except broker.AgentOfflineError:
        return _err(503, "agent offline")
    except Exception as e:
        return _err(500, str(e))
    if result.get("error") == "timeout":
        return _err(504, "agent timeout")
    return _ok(result.get("data") or result)


# ─── providers ───


async def list_providers(request: web.Request) -> web.Response:
    return _ok(db.list_providers())


async def put_provider(request: web.Request) -> web.Response:
    provider_id = request.match_info.get("id") or ""
    if not provider_id:
        return _err(400, "provider id required")
    try:
        body = await request.json()
    except Exception:
        return _err(400, "invalid json body")
    body["id"] = provider_id  # 强制对齐 path
    try:
        db.upsert_provider(body)
    except Exception as e:
        logger.exception("upsert provider 失败")
        return _err(500, str(e))
    return _ok(db.get_provider(provider_id))


async def delete_provider(request: web.Request) -> web.Response:
    provider_id = request.match_info.get("id") or ""
    if not provider_id:
        return _err(400, "provider id required")
    if not db.delete_provider(provider_id):
        return _err(404, "provider not found")
    return _ok({"deleted": provider_id})


def _gen_id(prefix: str = "id") -> str:
    import uuid
    return f"{prefix}-{uuid.uuid4().hex[:12]}"


# ─── 注册路由 ───


def setup_routes(app: web.Application) -> None:
    app.router.add_post("/api/auth/login", login)
    app.router.add_get("/api/status", get_status)

    # conversations：GET 列表 / POST 新建
    app.router.add_get("/api/conversations", list_or_create_conversations)
    app.router.add_post("/api/conversations", list_or_create_conversations)
    # messages：GET 历史 / POST 发送
    app.router.add_get("/api/conversations/{id}/messages", messages_handler)
    app.router.add_post("/api/conversations/{id}/messages", messages_handler)

    # files：GET 文件树 / POST 读文件
    app.router.add_get("/api/files", files_handler)
    app.router.add_post("/api/files", files_handler)

    # providers
    app.router.add_get("/api/providers", list_providers)
    app.router.add_put("/api/providers/{id}", put_provider)
    app.router.add_delete("/api/providers/{id}", delete_provider)
