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

- Uno Platform 6.6+（仓库锁定 `Uno.Sdk` 6.6.29）
- .NET 10（仓库锁定 SDK 10.0.302，允许 10.0.3xx patch 前滚）
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
│   ├── SalmonEgg.Presentation.Core/
│   ├── SalmonEgg.Acp/             # 独立 ACP 协议 SDK
│   └── SalmonEgg.Cli/             # 配置管理 CLI
├── tests/
└── docs/
```

## 快速开始

环境和构建细节请优先参考 [BUILD_GUIDE.md](BUILD_GUIDE.md)。

### 环境要求

- .NET SDK **10.0.302** 或兼容的 **10.0.3xx** patch（见 `global.json`）
- Windows 10 1809+ / Windows 11（WinUI 3 / MSIX）；Windows 建议 Visual Studio **18.8+**
- 或等效命令行工具链（Linux/macOS 可构建 Desktop / WASM）

### 常用命令

```bash
# 恢复依赖
dotnet restore SalmonEgg.sln

# 构建解决方案
dotnet build SalmonEgg.sln --configuration Release

# 运行测试
dotnet test --solution SalmonEgg.sln

# Windows 原生 MSIX 验证
build.bat msix
```

### 配置管理 CLI

仓库包含一个跨平台桌面 CLI，用于管理服务器配置与凭据。它随主程序一起分发：**安装 SalmonEgg 就会注册 `salmon-egg` 命令**，无需单独安装，也不必预装 .NET（产物是 self-contained 单文件）。

| 安装包 | 命令注册方式 |
|---|---|
| Windows MSIX | 包内声明 app execution alias，Windows 在 `%LOCALAPPDATA%\Microsoft\WindowsApps` 生成入口（该目录默认在用户 PATH 上） |
| Windows MSI（Skia Desktop） | MSI 的 `Environment` 表把安装目录下的 `cli` 追加到用户 PATH，卸载时移除 |
| Linux `.deb` | dpkg 安装 `/usr/bin/salmon-egg` 符号链接，purge 时移除 |
| macOS `.pkg` | 安装脚本把命令链接到 `/usr/local/bin`（macOS 默认 PATH），删除时手工 `rm /usr/local/bin/salmon-egg` |
| macOS `.dmg` | 命令在 `SalmonEgg.app` 内，但拖拽安装没有安装钩子，需要自行链接或改用 `.pkg` |

应用内可在 设置 → **命令行工具** 查看命令是否可用、PATH 命中哪一份、版本是否与当前应用一致；macOS 还可在该页一键链接或移除 `/usr/local/bin` 里的入口。


`win-arm64`、`linux-arm64`、`osx-x64` 等不属于正式支持范围：可交叉编译，但没有真实机器验证。

```bash
salmon-egg --help
salmon-egg config server list

# 凭据写入默认 fail-closed：平台安全存储不可用时写入失败，而非静默降级为明文
printf '%s\n' "$AGENT_TOKEN" | salmon-egg set-credential <server-id> --token-stdin

# 需要明文降级时必须显式声明
printf '%s\n' "$AGENT_TOKEN" | salmon-egg --allow-insecure-storage set-credential <server-id> --token-stdin
```

凭据值只从 stdin 读取，不会进入进程参数、YAML 或 `has-credential` 输出。非凭据配置操作不受该策略影响。该策略针对 Linux Secret Service 与 macOS Keychain；Windows DPAPI 始终可用，该 flag 在 Windows 上无实际作用。完整命令示例见 [README.en.md](README.en.md#cli-configuration-management)，发布与安装细节见 [发布指南](docs/release-guide.md#cli-发布)。

## 文档

- [文档导航](docs/README.md)
- [构建指南](BUILD_GUIDE.md)
- [编码规范](docs/coding-standards.md)
- [发布指南](docs/release-guide.md)
- [会话 / 导航 / 搜索硬约束](docs/hard-constraints-session-navigation-and-search.md)

## 说明

Windows Store / MSIX 提交以仓库中的 WinUI 3 MSIX 打包链为准；纯 `dotnet build` 不是 Windows 原生包的权威验证口径。
