"""认证：HMAC-SHA256 自签 token（payload.sig），bcrypt 替代为 stdlib pbkdf2_hmac。

格式：base64url(payload).base64url(signature)
payload: {"uid": <username>, "exp": <unix_ts>}
"""

from __future__ import annotations

import base64
import hashlib
import hmac
import json
import logging
import time
from typing import Any, Optional

from config import (
    DEFAULT_ADMIN_PASSWORD,
    DEFAULT_ADMIN_USER,
    TOKEN_TTL_SECONDS,
    get_agent_token,
    get_secret_key,
)
from db import ensure_admin, get_user_by_username

logger = logging.getLogger(__name__)

# pbkdf2 迭代次数（NIST 推荐 ≥ 600000，2023）
_PBKDF2_ITER = 200_000
_HASH_NAME = "sha256"
_DKLEN = 32


# ─── 密码 hash（stdlib，无 bcrypt 依赖）───


def hash_password(password: str) -> tuple[str, str]:
    """返回 (password_hash, salt)，都用 hex 存储。"""
    salt = hashlib.sha256(time.time().hex().encode() + password.encode()).digest()[:16]
    dk = hashlib.pbkdf2_hmac(_HASH_NAME, password.encode("utf-8"), salt, _PBKDF2_ITER, dklen=_DKLEN)
    return dk.hex(), salt.hex()


def verify_password(password: str, password_hash: str, salt: str) -> bool:
    try:
        dk = hashlib.pbkdf2_hmac(_HASH_NAME, password.encode("utf-8"), bytes.fromhex(salt), _PBKDF2_ITER, dklen=_DKLEN)
        return hmac.compare_digest(dk.hex(), password_hash)
    except Exception:
        return False


def ensure_default_admin() -> None:
    """首次启动创建 admin / CHANGEME。"""
    ensure_admin(DEFAULT_ADMIN_USER, *hash_password(DEFAULT_ADMIN_PASSWORD))


# ─── base64url ───


def _b64url_encode(data: bytes) -> str:
    return base64.urlsafe_b64encode(data).rstrip(b"=").decode("ascii")


def _b64url_decode(data: str) -> bytes:
    pad = "=" * (-len(data) % 4)
    return base64.urlsafe_b64decode(data + pad)


# ─── 用户 token ───


def create_token(username: str, ttl: int = TOKEN_TTL_SECONDS) -> tuple[str, int]:
    exp = int(time.time()) + ttl
    payload = {"uid": username, "exp": exp}
    payload_bytes = json.dumps(payload, separators=(",", ":"), ensure_ascii=False).encode("utf-8")
    payload_b64 = _b64url_encode(payload_bytes)
    sig = hmac.new(
        get_secret_key().encode("utf-8"),
        payload_b64.encode("ascii"),
        hashlib.sha256,
    ).digest()
    token = f"{payload_b64}.{_b64url_encode(sig)}"
    return token, exp


def verify_token(token: str) -> Optional[dict[str, Any]]:
    """成功返回 payload，失败 None。"""
    if not token or "." not in token:
        return None
    try:
        payload_b64, sig_b64 = token.rsplit(".", 1)
        expected = hmac.new(
            get_secret_key().encode("utf-8"),
            payload_b64.encode("ascii"),
            hashlib.sha256,
        ).digest()
        actual = _b64url_decode(sig_b64)
        if not hmac.compare_digest(expected, actual):
            return None
        payload = json.loads(_b64url_decode(payload_b64))
        if int(payload.get("exp", 0)) < int(time.time()):
            return None
        if not payload.get("uid"):
            return None
        return payload
    except Exception as e:
        logger.debug("token 校验失败: %s", e)
        return None


def login(username: str, password: str) -> Optional[dict[str, Any]]:
    user = get_user_by_username(username)
    if not user:
        return None
    if not verify_password(password, user["password_hash"], user["salt"]):
        return None
    token, exp = create_token(username)
    return {"token": token, "expires_at": exp, "username": username}


# ─── Agent token（固定，hmac.compare_digest）───


def verify_agent_token(token: str) -> bool:
    if not token:
        return False
    return hmac.compare_digest(token, get_agent_token())


def extract_bearer(auth_header: Optional[str]) -> Optional[str]:
    if not auth_header:
        return None
    parts = auth_header.strip().split(None, 1)
    if len(parts) != 2 or parts[0].lower() != "bearer":
        return None
    return parts[1].strip() or None
