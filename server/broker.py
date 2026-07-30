"""Agent ↔ iOS 消息中转：维护在线 agent 与 iOS 客户端集合。

设计要点：
- 单 agent 模式（Windows 端唯一）
- iOS 多端在线广播
- iOS→agent 异步指令 + 可选 request_id 等待 command_result
"""

from __future__ import annotations

import asyncio
import json
import logging
import time
from typing import Any, Optional, Set

from aiohttp import web

logger = logging.getLogger(__name__)

_agent_ws: Optional[web.WebSocketResponse] = None
_app_clients: Set[web.WebSocketResponse] = set()
_pending_commands: dict[str, asyncio.Future] = {}
_lock = asyncio.Lock()
_cmd_seq = 0


class AgentOfflineError(Exception):
    pass


def is_agent_online() -> bool:
    return _agent_ws is not None and not _agent_ws.closed


def get_status_payload() -> dict[str, Any]:
    return {
        "agent_online": is_agent_online(),
        "apps_online": len(_app_clients),
        "server_time": int(time.time()),
    }


async def register_agent(ws: web.WebSocketResponse) -> None:
    global _agent_ws
    async with _lock:
        old = _agent_ws
        _agent_ws = ws
    if old is not None and old is not ws and not old.closed:
        try:
            await old.close(code=4000, message=b"replaced by new agent")
        except Exception:
            pass
    logger.info("Agent 已上线")
    await broadcast_to_apps({"type": "agent_online", "data": {}, "ts": int(time.time())})
    await broadcast_to_apps({"type": "status_update", "data": get_status_payload(), "ts": int(time.time())})


async def unregister_agent(ws: web.WebSocketResponse) -> None:
    global _agent_ws
    async with _lock:
        if _agent_ws is not ws:
            return
        _agent_ws = None
    logger.info("Agent 已离线")
    for rid, fut in list(_pending_commands.items()):
        if not fut.done():
            fut.set_exception(AgentOfflineError("agent offline"))
        _pending_commands.pop(rid, None)
    await broadcast_to_apps({"type": "agent_offline", "data": {}, "ts": int(time.time())})
    await broadcast_to_apps({"type": "status_update", "data": get_status_payload(), "ts": int(time.time())})


def register_app(ws: web.WebSocketResponse) -> None:
    _app_clients.add(ws)
    logger.info("iOS 客户端接入，当前 %d 个", len(_app_clients))


def unregister_app(ws: web.WebSocketResponse) -> None:
    _app_clients.discard(ws)
    logger.info("iOS 客户端断开，剩余 %d 个", len(_app_clients))


async def broadcast_to_apps(message: dict[str, Any]) -> None:
    if not _app_clients:
        return
    raw = json.dumps(message, ensure_ascii=False)
    dead: list[web.WebSocketResponse] = []
    for ws in list(_app_clients):
        if ws.closed:
            dead.append(ws)
            continue
        try:
            await ws.send_str(raw)
        except Exception as e:
            logger.warning("广播到 iOS 失败: %s", e)
            dead.append(ws)
    for ws in dead:
        _app_clients.discard(ws)


async def send_to_agent(message: dict[str, Any]) -> bool:
    ws = _agent_ws
    if ws is None or ws.closed:
        return False
    try:
        await ws.send_str(json.dumps(message, ensure_ascii=False))
        return True
    except Exception as e:
        logger.warning("发送到 agent 失败: %s", e)
        return False


def _next_cmd_id() -> str:
    global _cmd_seq
    _cmd_seq += 1
    return f"cmd-{int(time.time())}-{_cmd_seq}"


async def send_command(
    cmd_type: str,
    data: Optional[dict[str, Any]] = None,
    wait: bool = False,
    timeout: float = 60.0,
) -> dict[str, Any]:
    """向 agent 下发指令。wait=True 等待 command_result。"""
    if not is_agent_online():
        raise AgentOfflineError("agent offline")

    request_id = _next_cmd_id()
    message = {
        "type": cmd_type,
        "request_id": request_id,
        "data": data or {},
        "ts": int(time.time()),
    }

    fut: Optional[asyncio.Future] = None
    if wait:
        fut = asyncio.get_running_loop().create_future()
        _pending_commands[request_id] = fut

    ok = await send_to_agent(message)
    if not ok:
        _pending_commands.pop(request_id, None)
        raise AgentOfflineError("agent offline")

    if not wait or fut is None:
        return {"ok": True, "queued": True, "request_id": request_id}

    try:
        return await asyncio.wait_for(fut, timeout=timeout)
    except asyncio.TimeoutError:
        return {"ok": False, "queued": True, "request_id": request_id, "error": "timeout"}
    finally:
        _pending_commands.pop(request_id, None)


def resolve_command_result(msg: dict[str, Any]) -> None:
    rid = msg.get("request_id")
    if not rid:
        return
    fut = _pending_commands.get(rid)
    if fut is not None and not fut.done():
        fut.set_result(msg)


# ─── Agent 上行消息处理 ───


async def handle_agent_message(msg: dict[str, Any]) -> None:
    """agent 上行：转发业务事件给 iOS + 持久化。"""
    msg_type = msg.get("type") or ""
    ts = int(msg.get("ts") or time.time())
    data = msg.get("data") if isinstance(msg.get("data"), dict) else {}

    if msg_type == "hello":
        logger.info("Agent hello: %s", data)
        await broadcast_to_apps({"type": "log", "data": {"level": "info", "msg": f"agent hello: {data}"}, "ts": ts})
        return

    if msg_type == "status":
        await broadcast_to_apps({"type": "status_update", "data": get_status_payload(), "ts": ts})
        return

    if msg_type == "command_result":
        resolve_command_result(msg)
        # 同时广播结果给 iOS（让发起方以外的 app 也能感知）
        await broadcast_to_apps({"type": "command_result", "data": msg, "ts": ts})
        return

    if msg_type == "new_message":
        # agent 回推 AI 回复
        conv_id = data.get("conversation_id") or data.get("conv_id") or ""
        role = data.get("role") or "assistant"
        content = data.get("content") or ""
        title = data.get("title") or data.get("conversation_title") or ""
        if conv_id:
            import db
            db.upsert_conversation(conv_id, title=title, last_message_at=ts)
            if content:
                db.insert_message(
                    conv_id,
                    role,
                    content,
                    reasoning_content=data.get("reasoning_content"),
                    effort=data.get("effort"),
                    auxiliary_trace=data.get("auxiliary_trace"),
                    ts=ts,
                )
        await broadcast_to_apps({"type": "new_message", "data": data, "ts": ts})
        return

    if msg_type == "file_tree":
        # 文件树结果：resolve 等待中的 command_result Future + 转发给 iOS
        resolve_command_result(msg)
        await broadcast_to_apps({"type": "file_tree", "data": data, "ts": ts})
        return

    if msg_type == "log":
        await broadcast_to_apps({"type": "log", "data": data, "ts": ts})
        return

    # 兜底：原样广播
    await broadcast_to_apps({"type": msg_type, "data": data, "ts": ts})
