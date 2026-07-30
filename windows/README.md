# AiWorkbench — Windows 客户端 (WinUI 3 / .NET 9)

Windows 端：本机 AI 工作台 + 被控端（连服务端接收 iOS 远程指令）。

## 工程结构

```
windows/
├── AiWorkbench.sln
└── src/AiWorkbench/
    ├── AiWorkbench.csproj         (Windows App SDK, net9.0-windows10.0.19041, WinUI3)
    ├── app.manifest               (DPI/权限清单)
    ├── App.xaml(.cs)
    ├── MainWindow.xaml(.cs)       (Mica 背景, Fluent 导航)
    ├── Pages/
    │   ├── ChatPage.xaml(.cs)     对话主界面（含图片拖入）
    │   ├── FilesPage.xaml(.cs)    E:\code 文件工作区
    │   └── SettingsPage.xaml(.cs) provider 全字段配置 UI
    ├── Models/
    │   ├── Provider.cs            对应 models.json
    │   ├── Conversation.cs
    │   └── Message.cs             含 auxiliaryTrace
    └── Services/
        ├── AiClient.cs            OpenAI 兼容, UA=CodeBuddy-Code/5.3.5, SSE 流式
        ├── ProviderStore.cs       providers.json CRUD
        ├── AgentClient.cs         连 Server /ws/agent，被控执行
        ├── ImageRouter.cs         ★ 第 10 条主辅模型图片自动切换
        └── FileWorkspace.cs       浏览 E:\code, 读文件
```

## 第 10 条主辅模型图片切换（核心）

`ImageRouter.PrepareForPrimaryAsync`：

1. 用户发图 + 主模型 `supportsImages=false`
2. 查辅助 provider（`isAuxiliary=true` 且 `auxiliaryFor=主模型id`；无匹配则取任意 `supportsImages=true`）
3. 调辅助模型识别图片 → 文字描述
4. 把描述作为 user message 注入主模型上下文，移除原始图片
5. 切回主模型完成任务
6. UI 仅显示主模型回复，辅助步骤标 "图片已识别"

主模型 `supportsImages=true` 时直接发图片，不切换。完整审计记录在 `Message.AuxiliaryTrace`。

## 发包特征（遵循 PROVIDER_SPEC 第 2 节）

- **UA**: `CodeBuddy-Code/5.3.5`
- **协议**: OpenAI `POST {url}/chat/completions`
- **鉴权**: `Authorization: Bearer {apiKey}`
- **流式**: `stream:true`, SSE `data: {chunk}`
- **思考**: `reasoning_effort`（值取自 `supportedEfforts`）
- **图片**: `content` 数组 `{type:"image_url",image_url:{url:"data:image/png;base64,..."}}`

## UI 规范

- WinUI 3 + Mica 材质
- Fluent 按钮/导航
- 深色主题为主
- 中文字体 Microsoft YaHei UI

## 编译

本机无 .NET SDK，走 GitHub Actions `windows-latest`：

```bash
# 本地预检（如有 SDK）
dotnet workload install wasdk
msbuild AiWorkbench.sln /p:Configuration=Release /p:Platform=x64 /p:SelfContained=true
```

CI: `.github/workflows/build-windows.yml`，产 self-contained win-x64 工件。

## 被控端

`AgentClient` 连 Server `/ws/agent?token=AGENT_TOKEN`（长连，自动重连）。
接收 iOS 指令 → 本地执行 AI 调用/文件操作 → 回推结果。命令：
- `ping`
- `list_providers`
- `list_files` / `read_file`
- `send_message`（含主辅图片切换）

## 配置存储

- `%LOCALAPPDATA%\AiWorkbench\providers.json` — provider 列表
- 首次启动自动种子 GLM-5.2（取自 PROVIDER_SPEC.md 第 1 节示例）
