# AI Workbench — 三端架构

## 拓扑

```
┌─────────────┐   WS(REST)   ┌──────────────┐   WS(agent)   ┌─────────────────┐
│  iOS(SwiftUI)│ ──────────► │  Server       │ ◄──────────► │  Windows(WinUI3) │
│  远程控制端  │              │  aiohttp@10370│              │  本机工作台+被控  │
└─────────────┘              │  SQLite broker│              └─────────────────┘
└──────────────┘              └──────────────┘
```

## 通信协议（借鉴 workbuddy-remote 成熟模式）

- **iOS ↔ Server**: REST `/api/*` + WS `/ws/app?token=<user_token>`
- **Windows ↔ Server**: WS `/ws/agent?token=<AGENT_TOKEN>`（长连）
- **消息格式**: JSON `{type, data, ts}`，统一响应 `{code, msg, data}`
- **鉴权**: HMAC-SHA256 自签 token（非标准 JWT），`{uid, exp, sig}`
- **WS 超时**: nginx `proxy_read_timeout 86400s` 支持 WS 长连

## Server 路由（@10370）

| 方法 | 路径 | 用途 |
|------|------|------|
| POST | /api/auth/login | 用户登录（user/pass → token） |
| GET  | /api/status | 在线 agent + 会话统计 |
| GET/POST | /api/conversations | 会话列表/新建 |
| GET/POST | /api/conversations/{id}/messages | 消息历史/发送 |
| GET/POST | /api/files | 文件树浏览/读取 |
| GET  | /api/providers | provider 配置 |
| PUT  | /api/providers/{id} | 更新 provider |
| WS   | /ws/app | iOS 客户端连接 |
| WS   | /ws/agent | Windows agent 连接 |
| GET  | /health | 健康检查 |

## Windows 端职责

1. 本机 AI 工作台（多 provider 对话，遵循 PROVIDER_SPEC）
2. 文件工作区（浏览 `E:\code`，快速取用，可拖入会话）
3. **被控端**：连 Server `/ws/agent`，接收 iOS 指令执行 AI 调用/文件操作，结果回推
4. 主辅模型图片切换逻辑在此端实现（第 10 条）

## iOS 端职责

1. 登录 Server
2. 远程操作 Windows 端：发会话/消息、浏览文件、取用文件
3. UI 与 Windows 端镜像，体验一致（第 11 条）
4. 不直接调 AI API（由 Windows 端执行，继承特征）

## 部署

- Server → `192.168.1.8:/www/wwwroot/ai-workbench`，systemd，用户手动 nginx 反代
- Windows → 本机运行
- iOS → GitHub Actions 编译未签名 IPA → 全能签重签
