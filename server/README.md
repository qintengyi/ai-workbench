# AI Workbench Server

iOS↔Windows 中转 broker。aiohttp + SQLite，监听 10370。

## 文件

| 文件 | 职责 |
|------|------|
| `main.py` | aiohttp 入口，注册路由 + CORS，监听 10370 |
| `config.py` | 首次启动生成 `secret_key` + `agent_token`，默认 `admin`/`CHANGEME` |
| `auth.py` | HMAC-SHA256 自签 token（`payload.sig`），pbkdf2 密码 |
| `db.py` | SQLite WAL + BEGIN IMMEDIATE 短事务；表 `users/conversations/messages/providers` |
| `broker.py` | agent 注册表 + iOS→agent 消息中转 |
| `websocket.py` | `/ws/app`(iOS) + `/ws/agent`(Win) 双 WS，`max_msg_size=16MB`，20s ping |
| `api_rest.py` | REST 路由（见下表） |
| `requirements.txt` | `aiohttp` + `aiohttp-cors` |

## 路由

| 方法 | 路径 | 说明 |
|------|------|------|
| POST | `/api/auth/login` | user/pass → token |
| GET | `/api/status` | 在线 agent + 会话/Provider 计数 |
| GET / POST | `/api/conversations` | 列表 / 新建 |
| GET / POST | `/api/conversations/{id}/messages` | 消息历史 / 发送 |
| GET / POST | `/api/files` | 文件树（转发 agent） / 读文件 |
| GET | `/api/providers` | provider 配置 |
| PUT | `/api/providers/{id}` | 更新/创建 provider |
| DELETE | `/api/providers/{id}` | 删除 provider |
| WS | `/ws/app?token=<user_token>` | iOS 客户端连接 |
| WS | `/ws/agent?token=<AGENT_TOKEN>` | Windows agent 连接 |
| GET | `/health` | 健康检查 |

## 响应格式

统一 `{code, msg, data}`，成功 `code=0`。

## 鉴权

- **用户 token**：HMAC-SHA256 自签 `base64url(payload).base64url(sig)`，payload `{uid, exp}`，7 天有效
- **agent token**：首次启动生成的固定字符串，`hmac.compare_digest` 校验
- 密码：stdlib `pbkdf2_hmac`（无 bcrypt 依赖）

## 启动

```bash
cd server
pip install -r requirements.txt
python main.py
```

首次启动会在 `server/config.json` 写出 `secret_key` 与 `agent_token`，并把 `agent_token` 打印到 stdout。Windows agent 端需要把它填入本地配置才能连 `/ws/agent`。

## 部署

```bash
python deploy/deploy_server.py
```

会把 `server/` 上传到 `192.168.1.8:/www/wwwroot/ai-workbench/server/`，建 venv，安装 systemd 服务 `ai-workbench.service`。

nginx 反代需 `proxy_read_timeout 86400s;` 以支持 WS 长连。

## 消息流

```
iOS 发消息 → POST /api/conversations/{id}/messages
            → server 落库 user 消息
            → 透传给 agent (WS /ws/agent, type=send_message)
            → Windows 执行 AI 调用
            → agent 回推 type=new_message
            → server 落库 assistant 消息
            → broadcast_to_apps → 所有 iOS WS 收到 new_message
```
