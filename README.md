# Salmon Egg

[English](README.en.md)

Salmon Egg 是一款依托 ACP 协议打造的桌面端智能体客户端。

它将智能对话交互、本地实用工具、终端指令操作以及远程智能体服务整合在同一界面中，尽量减少在多个软件和窗口之间来回切换的成本。

你可以连接本地或远程 ACP 服务，新建和恢复会话，查看完整对话记录与工具调用反馈，并在需要时直接在应用内完成终端相关工作流。针对日常使用场景，应用还提供语音输入、个性化设置与诊断支持。

## 适用场景

- 在 Windows 桌面上稳定使用 ACP 智能体能力
- 把智能体交互和本地工具工作流放进同一个工作区
- 统一查看会话、工具调用结果和终端反馈

## 主要能力

- 连接本地或远程 ACP 服务
- 创建、恢复和管理会话
- 展示对话、工具调用与结果反馈
- 支持本地终端与子进程工作流
- 支持语音输入
- 提供设置、日志与诊断能力

## 技术栈

- Uno Platform 6.5+
- .NET 10
- WinUI 3（Windows）
- Clean Architecture + MVVM

## 仓库结构

```text
SalmonEgg/
├── SalmonEgg/SalmonEgg/          # Uno Platform 主项目
├── src/
│   ├── SalmonEgg.Domain/         # 领域层
│   ├── SalmonEgg.Application/    # 应用层
│   ├── SalmonEgg.Infrastructure/ # 基础设施层
│   ├── SalmonEgg.Infrastructure.Desktop/
│   └── SalmonEgg.Presentation.Core/
├── tests/
└── docs/
```

## 快速开始

环境和构建细节请优先参考 [BUILD_GUIDE.md](BUILD_GUIDE.md)。

### 环境要求

- .NET SDK 10.0
- Windows 10 1809+ / Windows 11（WinUI 3 / MSIX）
- Visual Studio 2022 17.12+ 或等效命令行工具链

### 常用命令

```bash
# 恢复依赖
dotnet restore SalmonEgg.sln

# 构建解决方案
dotnet build SalmonEgg.sln --configuration Release

# 运行测试
dotnet test SalmonEgg.sln

# Windows 原生 MSIX 验证
build.bat msix
```

## 文档

- [构建指南](BUILD_GUIDE.md)
- [编码规范](docs/coding-standards.md)
- [会话 / 导航 / 搜索硬约束](docs/hard-constraints-session-navigation-and-search.md)

## 说明

Windows Store / MSIX 提交以仓库中的 WinUI 3 MSIX 打包链为准；纯 `dotnet build` 不是 Windows 原生包的权威验证口径。
