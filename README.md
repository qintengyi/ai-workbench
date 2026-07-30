# AI Workbench — 全场景 AI 办公工作台

> WinUI 3 Windows 原生客户端 + Python 服务端 + iOS SwiftUI 客户端。多提供商 AI 对话 + 文件工作区 + 手机远程控制电脑。

## 目录结构

```
ai-workbench/
├── docs/                 # 设计文档（开发圣经）
│   ├── PROVIDER_SPEC.md  # Provider 配置与发包特征规范 ★必读
│   └── ARCHITECTURE.md   # 三端架构与协议
├── server/               # Python aiohttp 服务端 @10370
├── windows/              # WinUI 3 (C#/.NET 9) 客户端
├── ios/                  # SwiftUI iOS 17+ 客户端
└── .github/workflows/    # GitHub Actions 编译 IPA
```

## 技术栈

| 端 | 技术 | 端口/角色 |
|----|------|----------|
| Windows | WinUI 3 / Windows App SDK / .NET 9 / C# | 本机 AI 工作台 + 被控端 |
| Server | Python 3.13 + aiohttp + SQLite | 10370，中转 broker |
| iOS | SwiftUI iOS 17+ / xcodegen | 远程控制端 |

## 快速开始

见各端 README。核心设计见 `docs/`。

## 规范

遵循工作区 `E:\code\README.md`，不乱放文件。新产物归入对应端目录。
