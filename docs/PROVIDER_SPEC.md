# AI Workbench — Provider 与发包特征规范

> 三端（Windows / Server / iOS）AI 调用层统一遵循本规范。基于 WorkBuddy `~/.workbuddy/models.json` 逆向 + 实测。

## 1. Provider 配置格式（第 8 条自定义字段）

每个 provider 是一个 JSON 对象，存于本地配置，UI 允许增删改：

```json
{
  "id": "GLM-5.2",
  "name": "GLM-5.2",
  "vendor": "Buddy",
  "apiKey": "sk-xxxx",
  "url": "https://api.xiaoyyua.top/v1",
  "maxInputTokens": 1000000,
  "maxOutputTokens": 8192,
  "supportsToolCall": true,
  "supportsImages": true,
  "supportsReasoning": true,
  "useCustomProtocol": false,
  "reasoning": { "supportedEfforts": ["xhigh","high","medium","low"], "defaultEffort": "medium" },
  "isAuxiliary": false,
  "auxiliaryFor": null
}
```

字段说明：
- `useCustomProtocol: false` → 走标准 OpenAI `/v1/chat/completions`
- `supportsImages` → 决定第 10 条主辅切换
- `isAuxiliary: true` + `auxiliaryFor: "<主模型id>"` → 标记为某主模型的视觉辅助
- `reasoning.defaultEffort` → UI 默认思考强度

## 2. 发包特征（第 9 条复刻 WorkBuddy）

实测：`api.xiaoyyua.top` **不校验 UA**，裸 `Authorization: Bearer <apiKey>` 即可调通。

继承 WorkBuddy 特征（兜底，防中转校验）：
- **UA**: `CodeBuddy-Code/5.3.5`
- **协议**: OpenAI 兼容 `POST {url}/chat/completions`
- **鉴权**: `Authorization: Bearer {apiKey}`
- **流式**: `stream: true`，SSE `data: {chunk}\n\n`
- **思考**: `reasoning_effort` 字段（值取自 `supportedEfforts`）
- **图片**: `content` 数组含 `{type:"image_url",image_url:{url:"data:image/png;base64,..."}}`

三端 HTTP 客户端统一设上述 UA + headers。

## 3. 主辅模型图片自动切换（第 10 条 — 最核心）

```
用户发图片 + 当前主模型 supportsImages=false
  → 后台拦截，切辅助模型（auxiliaryFor=当前主模型id 的 isAuxiliary provider）
  → 辅助模型识别图片 → 返回文字描述
  → 将描述作为 user message 注入主模型上下文
  → 切回主模型继续完成任务
  → 对用户透明（UI 仅显示主模型回复，辅助步骤标记为"图片已识别"）
```

若主模型 `supportsImages=true`，直接发图片给主模型，不切换。
辅助模型由配置里 `isAuxiliary:true` 且 `auxiliaryFor` 匹配决定；无匹配则取任意 `supportsImages:true` 的 provider。

## 4. 会话/消息数据结构

- `conversation`: `{id, title, providerId, modelId, createdAt, updatedAt}`
- `message`: `{id, conversationId, role(user/assistant/system), content, images[], reasoningContent, effort, ts, auxiliaryTrace[]}`
- `auxiliaryTrace`: 记录后台辅助调用的模型 id + 识别结果，便于审计

## 5. iOS 远程控制（第 11 条体验一致）

iOS 端 ≠ 独立 AI 客户端，而是**远程操作 Windows 端**的会话与文件：
- iOS 发起会话/消息 → Server 中转 → Windows 端执行实际 AI 调用 → 结果回传 iOS
- 文件工作区：iOS 浏览 Windows `E:\code` 树、读文件、触发取用
- 体验一致：iOS UI 布局/交互与 Windows 端镜像，延迟由 Server WS 实时推送掩盖
