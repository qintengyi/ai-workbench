"""服务端配置：首次启动生成 secret_key + agent_token，持久化到 config.json。"""

from __future__ import annotations

import json
import logging
import os
import secrets
from pathlib import Path
from typing import Any

logger = logging.getLogger(__name__)

BASE_DIR = Path(__file__).resolve().parent
DATA_DIR = BASE_DIR
DB_PATH = DATA_DIR / "data.db"
CONFIG_PATH = DATA_DIR / "config.json"

HOST = "0.0.0.0"
PORT = 10370

DEFAULT_ADMIN_USER = os.environ.get("AIWB_ADMIN_USER", "admin")
DEFAULT_ADMIN_PASSWORD = os.environ.get("AIWB_ADMIN_PASSWORD", "changeme")

# iOS/Win 端配置文件中默认填的占位（agent_token 首次启动后由 server 写出）
SECRET_KEY: str = ""
AGENT_TOKEN: str = ""

# 用户 token 有效期：7 天
TOKEN_TTL_SECONDS = 7 * 24 * 3600

# WS 心跳间隔
WS_PING_INTERVAL = 20.0
WS_MAX_MSG_SIZE = 16 * 1024 * 1024  # 16MB


def _save(cfg: dict[str, Any]) -> None:
    with open(CONFIG_PATH, "w", encoding="utf-8") as f:
        json.dump(cfg, f, indent=2, ensure_ascii=False)


def _load_or_create() -> dict[str, Any]:
    global SECRET_KEY, AGENT_TOKEN
    if CONFIG_PATH.exists():
        with open(CONFIG_PATH, "r", encoding="utf-8") as f:
            cfg = json.load(f)
        SECRET_KEY = cfg.get("secret_key") or secrets.token_urlsafe(32)
        AGENT_TOKEN = cfg.get("agent_token") or secrets.token_urlsafe(32)
        changed = False
        if "secret_key" not in cfg:
            cfg["secret_key"] = SECRET_KEY
            changed = True
        if "agent_token" not in cfg:
            cfg["agent_token"] = AGENT_TOKEN
            changed = True
        if changed:
            _save(cfg)
        return cfg

    SECRET_KEY = secrets.token_urlsafe(32)
    AGENT_TOKEN = secrets.token_urlsafe(32)
    cfg = {
        "secret_key": SECRET_KEY,
        "agent_token": AGENT_TOKEN,
        "host": HOST,
        "port": PORT,
        "default_admin": DEFAULT_ADMIN_USER,
        "created_first_boot": True,
    }
    _save(cfg)
    logger.info("首次启动：已生成 config.json")
    return cfg


def init_config() -> dict[str, Any]:
    return _load_or_create()


def get_secret_key() -> str:
    if not SECRET_KEY:
        init_config()
    return SECRET_KEY


def get_agent_token() -> str:
    if not AGENT_TOKEN:
        init_config()
    return AGENT_TOKEN


def print_boot_info() -> None:
    logger.info("=" * 60)
    logger.info("AI Workbench Server")
    logger.info("  listen       : %s:%s", HOST, PORT)
    logger.info("  db           : %s", DB_PATH)
    logger.info("  AGENT_TOKEN  : %s", get_agent_token())
    logger.info("  admin user   : %s", DEFAULT_ADMIN_USER)
    logger.info("  admin pass   : (set via AIWB_ADMIN_PASSWORD env)")
    logger.info("=" * 60)
    print(f"[BOOT] AGENT_TOKEN={get_agent_token()}", flush=True)
    print(f"[BOOT] admin={DEFAULT_ADMIN_USER} (password from AIWB_ADMIN_PASSWORD env)", flush=True)
    print(f"[BOOT] listening on {HOST}:{PORT}", flush=True)
