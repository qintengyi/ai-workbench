# AiWorkbench iOS

全场景 AI 办公工作台 — iOS 远程控制端（SwiftUI / iOS 17+ / 零三方依赖）。

## 角色

iOS 端是**远程控制端**，不直接调 AI API：

```
iOS  ──REST /api/*──►  Server(aiohttp@10370)  ◄──WS /ws/agent──  Windows(WinUI3)
   └──WS /ws/app──►                                      └─本机 AI 工作台 + 被控
```

- iOS 发会话/消息 → Server 中转 → Windows 端执行 AI 调用 → 结果回推 iOS
- 文件工作区：浏览 Windows `E:\code` 树、读文件
- UI 与 Windows 端镜像，体验一致

## 功能

1. 登录服务端拿 token（HMAC-SHA256 自签）
2. REST：列会话 / 发消息 / 列消息 / 浏览文件 / 读文件
3. WS：实时接收 Windows 端经 server 中转的 AI 回复流（`stream_chunk` 事件）
4. UI 镜像 Windows 端布局，深色主题，中文字体

## 项目结构

```
ios/
├── project.yml                         # xcodegen 配置（iOS 17, SwiftUI, 零依赖, 未签名）
├── README.md
├── .github/workflows/build-ipa.yml     # CI: macos-15 + xcodegen + xcodebuild unsigned + zip IPA
└── Sources/AiWorkbench/
    ├── AiWorkbenchApp.swift            # 入口 @main + AppStateManager + RootView
    ├── Info.plist
    ├── Resources/Assets.xcassets/      # AppIcon / AccentColor / LaunchBackgroundColor
    ├── Models/
    │   ├── APIResponse.swift           # 通用响应 + 宽松 JSON 解码 + AnyDecodable
    │   ├── ServerConfig.swift          # 本地服务端配置
    │   ├── Conversation.swift          # 会话（对齐 PROVIDER_SPEC §4）
    │   ├── Message.swift               # 消息 + AuxiliaryTrace + SendMessage 请求/响应
    │   └── FileNode.swift              # 文件树节点 + 文件内容
    ├── Network/
    │   ├── APIClient.swift             # 单例 REST 客户端（URLSession, /api/*）
    │   └── WSClient.swift              # @Observable WS 客户端（代次防 stale, 20s ping, 指数退避）
    ├── Stores/
    │   ├── AuthStore.swift             # @Observable token + serverURL
    │   └── SettingsStore.swift         # UserDefaults 持久化
    └── Views/
        ├── LoginView.swift             # 登录服务端
        ├── MainView.swift              # TabView: 会话/文件/设置
        ├── ConversationsView.swift     # 会话列表
        ├── ConversationDetailView.swift # 对话 + 流式增量气泡
        ├── FilesView.swift             # 浏览 E:\code + 读文件
        └── SettingsView.swift          # 服务端地址 / Token / WS 状态 / 登出
```

## 关键设计

### WS 代次防 stale（借鉴 workbuddy-remote）

每次 `connect()` 自增 `generation`，所有异步回调（`receiveLoop`、`reconnect`）携带代次，
回调返回时若 `generation != 当前代次`，跳过 — 防止旧连接的 stale 数据污染新连接。

### 流式增量

`WSClient.onStreamChunk` 回调 `WSStreamChunk { conversationId, delta, reasoningDelta, finished }`：
- iOS 发消息后乐观插入 user 气泡，开始流式接收 assistant 增量
- `delta` 累积到 `streamingText`，`finished=true` 时落盘为 assistant 气泡
- 等 `new_message` 事件刷新真实消息列表

## 本地构建

```bash
# 安装 xcodegen
brew install xcodegen

# 生成 Xcode 项目
cd ios
xcodegen generate

# 编译（未签名）
xcodebuild \
  -project AiWorkbench.xcodeproj \
  -scheme AiWorkbench \
  -configuration Release \
  -sdk iphoneos \
  -destination 'generic/platform=iOS' \
  CODE_SIGNING_ALLOWED=NO \
  DEVELOPMENT_TEAM="" \
  build

# 打包 IPA
APP_PATH=$(find build/Build/Products/Release-iphoneos -name "AiWorkbench.app" -type d | head -1)
rm -rf Payload && mkdir -p Payload
cp -R "$APP_PATH" Payload/
zip -qr AiWorkbench.ipa Payload
```

## CI

推送到 `main`/`master` 或手动触发 `Build IPA` workflow：
- `macos-15` + 最新 Xcode 16.x
- `xcodegen generate` 生成项目
- `xcodebuild` 编译未签名 `.app`
- `zip` 打包 `Payload/` → `AiWorkbench.ipa`
- `upload-artifact` 上传，保留 30 天

## 部署

IPA 未签名，需用 [全能签](https://sign.ipa.tools/) 等工具重签后安装到非越狱设备。

## 依赖

- iOS 17.0+
- SwiftUI / Observation
- 零第三方依赖（仅 Apple SDK）
