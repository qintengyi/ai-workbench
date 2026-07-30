"""SQLite 存储层：WAL + BEGIN IMMEDIATE 短事务。

表：users / conversations / messages / providers
"""

from __future__ import annotations

import json
import logging
import sqlite3
import threading
import time
from contextlib import contextmanager
from typing import Any, Generator, Optional

from config import DB_PATH

logger = logging.getLogger(__name__)

_local = threading.local()
# SQLite 写入串行化（同进程内），保证 BEGIN IMMEDIATE 不抢锁抖动
_write_lock = threading.Lock()


def _connect() -> sqlite3.Connection:
    conn = sqlite3.connect(str(DB_PATH), check_same_thread=False, timeout=30)
    conn.row_factory = sqlite3.Row
    conn.execute("PRAGMA journal_mode=WAL")
    conn.execute("PRAGMA synchronous=NORMAL")
    conn.execute("PRAGMA foreign_keys=ON")
    return conn


def get_conn() -> sqlite3.Connection:
    """线程本地连接。"""
    conn = getattr(_local, "conn", None)
    if conn is None:
        conn = _connect()
        _local.conn = conn
    return conn


@contextmanager
def transaction() -> Generator[sqlite3.Connection, None, None]:
    """BEGIN IMMEDIATE 短事务，出错 rollback。"""
    conn = get_conn()
    with _write_lock:
        conn.execute("BEGIN IMMEDIATE")
        try:
            yield conn
            conn.commit()
        except Exception:
            conn.rollback()
            raise


def init_db() -> None:
    """创建表结构。"""
    conn = get_conn()
    conn.executescript(
        """
        CREATE TABLE IF NOT EXISTS users (
          id INTEGER PRIMARY KEY AUTOINCREMENT,
          username TEXT UNIQUE NOT NULL,
          password_hash TEXT NOT NULL,
          salt TEXT NOT NULL,
          created_at INTEGER NOT NULL
        );

        CREATE TABLE IF NOT EXISTS conversations (
          id TEXT PRIMARY KEY,
          title TEXT NOT NULL DEFAULT '',
          provider_id TEXT,
          model_id TEXT,
          created_at INTEGER NOT NULL,
          updated_at INTEGER NOT NULL,
          last_message_at INTEGER
        );
        CREATE INDEX IF NOT EXISTS idx_conv_updated ON conversations(updated_at DESC);

        CREATE TABLE IF NOT EXISTS messages (
          id TEXT PRIMARY KEY,
          conversation_id TEXT NOT NULL,
          role TEXT NOT NULL,
          content TEXT NOT NULL DEFAULT '',
          images TEXT,
          reasoning_content TEXT,
          effort TEXT,
          auxiliary_trace TEXT,
          ts INTEGER NOT NULL
        );
        CREATE INDEX IF NOT EXISTS idx_msg_conv ON messages(conversation_id, ts);

        CREATE TABLE IF NOT EXISTS providers (
          id TEXT PRIMARY KEY,
          name TEXT NOT NULL,
          vendor TEXT NOT NULL DEFAULT '',
          api_key TEXT NOT NULL DEFAULT '',
          url TEXT NOT NULL DEFAULT '',
          max_input_tokens INTEGER,
          max_output_tokens INTEGER,
          supports_tool_call INTEGER NOT NULL DEFAULT 0,
          supports_images INTEGER NOT NULL DEFAULT 0,
          supports_reasoning INTEGER NOT NULL DEFAULT 0,
          use_custom_protocol INTEGER NOT NULL DEFAULT 0,
          reasoning TEXT,
          is_auxiliary INTEGER NOT NULL DEFAULT 0,
          auxiliary_for TEXT,
          updated_at INTEGER NOT NULL
        );
        """
    )
    conn.commit()
    logger.info("SQLite 初始化完成: %s", DB_PATH)


# ─── users ───


def get_user_by_username(username: str) -> Optional[dict[str, Any]]:
    row = get_conn().execute(
        "SELECT * FROM users WHERE username = ?", (username,)
    ).fetchone()
    return dict(row) if row else None


def create_user(username: str, password_hash: str, salt: str) -> None:
    with transaction() as conn:
        conn.execute(
            "INSERT OR IGNORE INTO users (username, password_hash, salt, created_at) "
            "VALUES (?, ?, ?, ?)",
            (username, password_hash, salt, int(time.time())),
        )


def ensure_admin(username: str, password_hash: str, salt: str) -> None:
    if get_user_by_username(username) is None:
        create_user(username, password_hash, salt)
        logger.info("已创建默认用户: %s", username)


# ─── conversations ───


def upsert_conversation(
    conv_id: str,
    title: str = "",
    provider_id: Optional[str] = None,
    model_id: Optional[str] = None,
    last_message_at: Optional[int] = None,
) -> None:
    now = int(time.time())
    with transaction() as conn:
        conn.execute(
            """
            INSERT INTO conversations (id, title, provider_id, model_id, created_at, updated_at, last_message_at)
            VALUES (?, ?, ?, ?, ?, ?, ?)
            ON CONFLICT(id) DO UPDATE SET
              title = COALESCE(NULLIF(excluded.title, ''), conversations.title),
              provider_id = COALESCE(excluded.provider_id, conversations.provider_id),
              model_id = COALESCE(excluded.model_id, conversations.model_id),
              last_message_at = COALESCE(excluded.last_message_at, conversations.last_message_at),
              updated_at = excluded.updated_at
            """,
            (conv_id, title, provider_id, model_id, now, now, last_message_at or now),
        )


def list_conversations(limit: int = 50, offset: int = 0) -> list[dict[str, Any]]:
    limit = max(1, min(limit, 200))
    offset = max(0, offset)
    rows = get_conn().execute(
        "SELECT * FROM conversations ORDER BY COALESCE(last_message_at, updated_at) DESC LIMIT ? OFFSET ?",
        (limit, offset),
    ).fetchall()
    return [dict(r) for r in rows]


def get_conversation(conv_id: str) -> Optional[dict[str, Any]]:
    row = get_conn().execute(
        "SELECT * FROM conversations WHERE id = ?", (conv_id,)
    ).fetchone()
    return dict(row) if row else None


# ─── messages ───


def insert_message(
    conv_id: str,
    role: str,
    content: str,
    images: Optional[list] = None,
    reasoning_content: Optional[str] = None,
    effort: Optional[str] = None,
    auxiliary_trace: Optional[list] = None,
    msg_id: Optional[str] = None,
    ts: Optional[int] = None,
) -> str:
    import uuid

    mid = msg_id or str(uuid.uuid4())
    ts_v = ts or int(time.time())
    images_str = json.dumps(images, ensure_ascii=False) if images else None
    trace_str = json.dumps(auxiliary_trace, ensure_ascii=False) if auxiliary_trace else None
    with transaction() as conn:
        conn.execute(
            """
            INSERT INTO messages (id, conversation_id, role, content, images, reasoning_content, effort, auxiliary_trace, ts)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            (mid, conv_id, role, content, images_str, reasoning_content, effort, trace_str, ts_v),
        )
        conn.execute(
            "UPDATE conversations SET last_message_at = ?, updated_at = ? WHERE id = ?",
            (ts_v, ts_v, conv_id),
        )
        # 若会话不存在则建空壳
        conn.execute(
            "INSERT OR IGNORE INTO conversations (id, title, created_at, updated_at, last_message_at) "
            "VALUES (?, '', ?, ?, ?)",
            (conv_id, ts_v, ts_v, ts_v),
        )
    return mid


def list_messages(
    conv_id: str,
    limit: int = 100,
    before: Optional[int] = None,
) -> list[dict[str, Any]]:
    limit = max(1, min(limit, 500))
    if before is not None:
        rows = get_conn().execute(
            "SELECT * FROM messages WHERE conversation_id = ? AND ts < ? ORDER BY ts DESC LIMIT ?",
            (conv_id, before, limit),
        ).fetchall()
    else:
        rows = get_conn().execute(
            "SELECT * FROM messages WHERE conversation_id = ? ORDER BY ts DESC LIMIT ?",
            (conv_id, limit),
        ).fetchall()
    result = []
    for r in reversed(rows):
        d = dict(r)
        if d.get("images"):
            try:
                d["images"] = json.loads(d["images"])
            except (json.JSONDecodeError, TypeError):
                pass
        if d.get("auxiliary_trace"):
            try:
                d["auxiliary_trace"] = json.loads(d["auxiliary_trace"])
            except (json.JSONDecodeError, TypeError):
                pass
        result.append(d)
    return result


# ─── providers ───


def _provider_row_to_dict(row: sqlite3.Row) -> dict[str, Any]:
    d = dict(row)
    d["supportsToolCall"] = bool(d.pop("supports_tool_call"))
    d["supportsImages"] = bool(d.pop("supports_images"))
    d["supportsReasoning"] = bool(d.pop("supports_reasoning"))
    d["useCustomProtocol"] = bool(d.pop("use_custom_protocol"))
    d["apiKey"] = d.pop("api_key")
    d["maxInputTokens"] = d.pop("max_input_tokens")
    d["maxOutputTokens"] = d.pop("max_output_tokens")
    d["isAuxiliary"] = bool(d.pop("is_auxiliary"))
    d["auxiliaryFor"] = d.pop("auxiliary_for")
    d["vendor"] = d.pop("vendor")
    d["url"] = d.pop("url")
    d["name"] = d.pop("name")
    d["id"] = d.pop("id")
    if d.get("reasoning"):
        try:
            d["reasoning"] = json.loads(d["reasoning"])
        except (json.JSONDecodeError, TypeError):
            pass
    return d


def list_providers() -> list[dict[str, Any]]:
    rows = get_conn().execute(
        "SELECT * FROM providers ORDER BY updated_at DESC"
    ).fetchall()
    return [_provider_row_to_dict(r) for r in rows]


def get_provider(provider_id: str) -> Optional[dict[str, Any]]:
    row = get_conn().execute(
        "SELECT * FROM providers WHERE id = ?", (provider_id,)
    ).fetchone()
    return _provider_row_to_dict(row) if row else None


def upsert_provider(p: dict[str, Any]) -> None:
    now = int(time.time())
    reasoning = p.get("reasoning")
    reasoning_str = json.dumps(reasoning, ensure_ascii=False) if reasoning else None
    with transaction() as conn:
        conn.execute(
            """
            INSERT INTO providers (
              id, name, vendor, api_key, url,
              max_input_tokens, max_output_tokens,
              supports_tool_call, supports_images, supports_reasoning, use_custom_protocol,
              reasoning, is_auxiliary, auxiliary_for, updated_at
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            ON CONFLICT(id) DO UPDATE SET
              name = excluded.name,
              vendor = excluded.vendor,
              api_key = excluded.api_key,
              url = excluded.url,
              max_input_tokens = excluded.max_input_tokens,
              max_output_tokens = excluded.max_output_tokens,
              supports_tool_call = excluded.supports_tool_call,
              supports_images = excluded.supports_images,
              supports_reasoning = excluded.supports_reasoning,
              use_custom_protocol = excluded.use_custom_protocol,
              reasoning = excluded.reasoning,
              is_auxiliary = excluded.is_auxiliary,
              auxiliary_for = excluded.auxiliary_for,
              updated_at = excluded.updated_at
            """,
            (
                str(p["id"]),
                str(p.get("name") or p["id"]),
                str(p.get("vendor") or ""),
                str(p.get("apiKey") or ""),
                str(p.get("url") or ""),
                int(p.get("maxInputTokens") or 0),
                int(p.get("maxOutputTokens") or 0),
                1 if p.get("supportsToolCall") else 0,
                1 if p.get("supportsImages") else 0,
                1 if p.get("supportsReasoning") else 0,
                1 if p.get("useCustomProtocol") else 0,
                reasoning_str,
                1 if p.get("isAuxiliary") else 0,
                p.get("auxiliaryFor"),
                now,
            ),
        )


def delete_provider(provider_id: str) -> bool:
    with transaction() as conn:
        cur = conn.execute("DELETE FROM providers WHERE id = ?", (provider_id,))
        return cur.rowcount > 0
