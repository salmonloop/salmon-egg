# SalmonEgg 项目设置指南

> Status: **historical bootstrap record** (早期脚手架笔记)。  
> **不要**把本文中的 `dotnet new` / 手装包 / 旧 TFM 故障排除当作当前仓库命令执行。  
> 现行事实源：
>
> | 用途 | 文件 |
> |------|------|
> | 构建与运行 | [`../BUILD_GUIDE.md`](../BUILD_GUIDE.md) |
> | Agent 规则 | [`../AGENTS.md`](../AGENTS.md) |
> | 项目状态 | [`PROJECT_STATUS.md`](PROJECT_STATUS.md) |
> | .NET SDK / Uno.Sdk pin | [`../global.json`](../global.json)、[`../SalmonEgg/global.json`](../SalmonEgg/global.json) |
> | 集中包版本 | [`../SalmonEgg/Directory.Packages.props`](../SalmonEgg/Directory.Packages.props) |
>
> **当前仓库锁定（2026-07 起）**：.NET SDK **10.0.302**（`rollForward: latestMinor`）、Uno.Sdk **6.6.29**、`Microsoft.Extensions.*` / `System.Text.Json` **10.0.10**。  
> 下文若出现与上表冲突的版本号，以 `global.json` / `Directory.Packages.props` 为准；正文示例已尽量改成现行 pin，仅保留历史脚手架步骤形态。

## 概述

本文档记录 SalmonEgg **早期**从零搭 Clean Architecture + Uno 工程的步骤。现行开发请直接克隆仓库并按 `BUILD_GUIDE.md` 构建。

## 系统要求

### 必需软件

1. **.NET 10.0 SDK 10.0.302**（或兼容的 10.0.3xx patch）
   - 下载地址: https://dotnet.microsoft.com/download/dotnet/10.0
   - 验证安装: `dotnet --version`（应输出 `10.0.302` 或同代 3xx）
   - 事实源: 仓库根 `global.json`

2. **Visual Studio 18.8+**（Windows 原生 WinUI / MSIX）或 **VS Code / CLI**
   - Visual Studio: https://visualstudio.microsoft.com/
   - VS Code: https://code.visualstudio.com/

3. **Uno Platform 模板**（仅历史脚手架需要；当前仓库已存在，无需再 `dotnet new`）
   ```bash
   dotnet new install Uno.Templates
   ```

### 可选软件（根据目标平台）

- **Windows 开发**: Windows 10 1809+, Windows SDK 10.0.26100.0，signtool 用 SDK 10.0.22621.0
- **Android 开发**: Android SDK / workload（`EnableMobileTargets=true` 时）
- **iOS 开发**: macOS + Xcode + iOS workload（`EnableIosTarget=true` 时）
- **WebAssembly**: 现代浏览器 + `wasm-tools` workload
- **Linux Skia Desktop**: Xvfb / libX11 / libXtst 等（见 `BUILD_GUIDE.md`）

## 项目初始化步骤

### 1. 验证环境

```bash
# 检查 .NET SDK（应对齐 global.json：10.0.302 / 10.0.3xx）
dotnet --version

# 检查 Uno Platform 模板（仅脚手架场景）
dotnet new list | grep -i uno
```

### 2. 克隆或创建项目

如果从现有仓库克隆：
```bash
git clone <repository-url>
cd SalmonEgg
```

如果从头创建，请按照以下步骤操作。

### 3. 创建解决方案结构

```bash
# 创建根目录
mkdir SalmonEgg
cd SalmonEgg

# 创建解决方案文件
dotnet new sln -n SalmonEgg
```

### 4. 创建 Uno Platform 主项目

```bash
# 创建 Uno Platform 应用（需要 .NET 10.0）
dotnet new unoapp -o SalmonEgg

# 添加到解决方案
dotnet sln add SalmonEgg/SalmonEgg/SalmonEgg.csproj
```

### 5. 创建层项目

#### Domain 层（领域层）

```bash
# 现行仓库目标为 net10.0（历史笔记曾写 netstandard2.1，勿再回退）
dotnet new classlib -n SalmonEgg.Domain -f net10.0 -o src/SalmonEgg.Domain
dotnet sln add src/SalmonEgg.Domain/SalmonEgg.Domain.csproj

# 创建目录结构
mkdir -p src/SalmonEgg.Domain/Models
mkdir -p src/SalmonEgg.Domain/Services
mkdir -p src/SalmonEgg.Domain/Exceptions
```

#### Application 层（应用层）

```bash
dotnet new classlib -n SalmonEgg.Application -f net10.0 -o src/SalmonEgg.Application
dotnet sln add src/SalmonEgg.Application/SalmonEgg.Application.csproj

# 创建目录结构
mkdir -p src/SalmonEgg.Application/Services
mkdir -p src/SalmonEgg.Application/UseCases
mkdir -p src/SalmonEgg.Application/Common

# 添加项目引用
dotnet add src/SalmonEgg.Application/SalmonEgg.Application.csproj reference src/SalmonEgg.Domain/SalmonEgg.Domain.csproj
```

#### Infrastructure 层（基础设施层）

```bash
dotnet new classlib -n SalmonEgg.Infrastructure -f net10.0 -o src/SalmonEgg.Infrastructure
dotnet sln add src/SalmonEgg.Infrastructure/SalmonEgg.Infrastructure.csproj

# 创建目录结构
mkdir -p src/SalmonEgg.Infrastructure/Network
mkdir -p src/SalmonEgg.Infrastructure/Serialization
mkdir -p src/SalmonEgg.Infrastructure/Storage
mkdir -p src/SalmonEgg.Infrastructure/Logging

# 添加项目引用
dotnet add src/SalmonEgg.Infrastructure/SalmonEgg.Infrastructure.csproj reference src/SalmonEgg.Domain/SalmonEgg.Domain.csproj
```

#### Presentation 层（主项目）

```bash
# 添加项目引用
dotnet add SalmonEgg/SalmonEgg/SalmonEgg.csproj reference src/SalmonEgg.Domain/SalmonEgg.Domain.csproj
dotnet add SalmonEgg/SalmonEgg/SalmonEgg.csproj reference src/SalmonEgg.Application/SalmonEgg.Application.csproj
dotnet add SalmonEgg/SalmonEgg/SalmonEgg.csproj reference src/SalmonEgg.Infrastructure/SalmonEgg.Infrastructure.csproj
```

### 6. 创建测试项目

```bash
# Domain 测试
dotnet new xunit -n SalmonEgg.Domain.Tests -o tests/SalmonEgg.Domain.Tests
dotnet sln add tests/SalmonEgg.Domain.Tests/SalmonEgg.Domain.Tests.csproj
dotnet add tests/SalmonEgg.Domain.Tests/SalmonEgg.Domain.Tests.csproj reference src/SalmonEgg.Domain/SalmonEgg.Domain.csproj

# Infrastructure 测试
dotnet new xunit -n SalmonEgg.Infrastructure.Tests -o tests/SalmonEgg.Infrastructure.Tests
dotnet sln add tests/SalmonEgg.Infrastructure.Tests/SalmonEgg.Infrastructure.Tests.csproj
dotnet add tests/SalmonEgg.Infrastructure.Tests/SalmonEgg.Infrastructure.Tests.csproj reference src/SalmonEgg.Infrastructure/SalmonEgg.Infrastructure.csproj

# Application 测试
dotnet new xunit -n SalmonEgg.Application.Tests -o tests/SalmonEgg.Application.Tests
dotnet sln add tests/SalmonEgg.Application.Tests/SalmonEgg.Application.Tests.csproj
dotnet add tests/SalmonEgg.Application.Tests/SalmonEgg.Application.Tests.csproj reference src/SalmonEgg.Application/SalmonEgg.Application.csproj
```

### 7. 安装 NuGet 包

#### Infrastructure 层

> 现行仓库已通过各层 `.csproj` 与 `SalmonEgg/Directory.Packages.props` 管理版本；以下仅作**历史脚手架示例**，版本号已改为当前 pin。

```bash
cd src/SalmonEgg.Infrastructure
dotnet add package Websocket.Client --version 5.5.0
dotnet add package Polly --version 8.7.0
dotnet add package Serilog --version 4.4.0
dotnet add package Serilog.Sinks.File --version 7.0.0
dotnet add package Serilog.Sinks.Console --version 6.1.1
dotnet add package Microsoft.Extensions.DependencyInjection --version 10.0.10
dotnet add package System.Reactive --version 6.1.0
dotnet add package YamlDotNet --version 18.1.0
cd ../..
```

#### Application 层

```bash
cd src/SalmonEgg.Application
dotnet add package FluentValidation --version 12.1.1
dotnet add package Microsoft.Extensions.DependencyInjection.Abstractions --version 10.0.10
dotnet add package System.Reactive --version 6.1.0
cd ../..
```

#### ACP / Presentation.Core

```bash
# ACP（netstandard2.1 目标需要显式 System.Text.Json）
cd src/SalmonEgg.Acp
dotnet add package System.Text.Json --version 10.0.10
cd ../..

# Presentation.Core（集中 MVVM / Reactive）
cd src/SalmonEgg.Presentation.Core
dotnet add package CommunityToolkit.Mvvm --version 8.4.2
dotnet add package Microsoft.Extensions.Localization.Abstractions --version 10.0.10
dotnet add package Microsoft.Extensions.Logging.Abstractions --version 10.0.10
dotnet add package Uno.Extensions.Reactive --version 7.2.3
dotnet add package Uno.Extensions.Reactive.Messaging --version 7.2.3
cd ../..
```

#### Presentation 宿主（Uno 应用）

```bash
# 主应用使用 Uno.Sdk + Directory.Packages.props；不要在脚手架阶段手写与 global.json 冲突的 Uno 版本。
cd SalmonEgg/SalmonEgg
# 参考 SalmonEgg/Directory.Packages.props：
# CommunityToolkit.Mvvm 8.4.2, Microsoft.Extensions.* 10.0.10,
# MSAL 4.86.1, Uno.Extensions.Reactive 7.2.3, Uno.WinUI.Lottie 6.6.166 等
cd ../..
```

#### 测试项目

```bash
# 现行测试使用 xunit.v3 + Microsoft.Testing.Platform（见各测试 csproj）
cd tests/SalmonEgg.Domain.Tests
dotnet add package xunit.v3.mtp-v2 --version 3.2.2
dotnet add package coverlet.MTP --version 10.0.1
dotnet add package FsCheck --version 3.3.3
cd ../..

cd tests/SalmonEgg.Infrastructure.Tests
dotnet add package xunit.v3.mtp-v2 --version 3.2.2
dotnet add package coverlet.MTP --version 10.0.1
dotnet add package FsCheck --version 3.3.3
dotnet add package Moq --version 4.20.72
cd ../..

cd tests/SalmonEgg.Application.Tests
dotnet add package xunit.v3.mtp-v2 --version 3.2.2
dotnet add package coverlet.MTP --version 10.0.1
dotnet add package Moq --version 4.20.72
cd ../..

cd tests/SalmonEgg.Presentation.Core.Tests
dotnet add package xunit.v3.mtp-v2 --version 3.2.2
dotnet add package coverlet.MTP --version 10.0.1
dotnet add package Moq --version 4.20.72
cd ../..
```

### 8. 验证设置

```bash
# 恢复所有依赖
dotnet restore SalmonEgg.sln

# 构建解决方案
dotnet build SalmonEgg.sln --configuration Release

# 运行测试（MTP：必须 --solution / --project）
dotnet test --solution SalmonEgg.sln --configuration Release --timeout 20m --output Normal
```

## 常见问题

### 问题 1: Uno Platform / Uno.Sdk 版本不兼容

**症状**: 包还原失败、隐式 Uno 包与 `global.json` 不一致

**解决方案**: 
- 确保安装 **.NET SDK 10.0.302**（或同代 3xx）
- 根目录与 `SalmonEgg/global.json` 中 **Uno.Sdk 均为 6.6.29**
- 不要单独升级/降级 `Uno.WinUI.*` 与 `Uno.Sdk` 错代

### 问题 2: 层目标框架与历史 netstandard 笔记冲突

**症状**: 文档仍写 Domain/Application 为 netstandard2.1，但当前仓库多为 **net10.0**

**解决方案**:
- 以各层 `.csproj` 的 `TargetFramework(s)` 为准（现行 Domain/Application/Infrastructure/Presentation.Core 为 `net10.0`；ACP 为 `netstandard2.1;net10.0`）
- 历史脚手架中的 netstandard 指令仅供考古，不要反向改回

### 问题 3: 工作负载未安装

**症状**: NETSDK1147 / UNOWA0001，需要 Android/iOS/wasm-tools

**解决方案**:
```bash
# 在已安装的 SDK 10.0.302 上安装 workload
dotnet workload install wasm-tools
# 移动端按需：
# dotnet workload install android
# dotnet workload install ios   # 需 macOS

# CI manifest / 本机 SDK patch 应与 global.json（当前 10.0.302）一致
```

### 问题 4: FluentValidation / 包版本过时

**症状**: 仍按 11.x + netstandard 笔记安装，与现行 Application 层冲突

**解决方案**:
- 现行 Application 使用 **FluentValidation 12.1.1**、目标 **net10.0**
- 一律以 `Directory.Packages.props` 与各层 `.csproj` 为准，勿再降回 11.9.2
## 历史开发工作流（不可作为当前命令参考）

### 日常开发

```bash
# 1. 拉取最新代码
git pull

# 2. 恢复依赖
dotnet restore

# 3. 构建项目
dotnet build

# 4. 运行测试
dotnet test

# 5. 运行应用（Windows）
cd SalmonEgg/SalmonEgg
dotnet run
```

### 添加新功能

1. 在 Domain 层定义领域模型和接口
2. 在 Infrastructure 层实现接口
3. 在 Application 层创建用例
4. 在 Presentation 层创建 ViewModel 和 View
5. 编写单元测试和属性测试

### 运行特定平台

```bash
# Windows
dotnet run --project SalmonEgg/SalmonEgg/SalmonEgg.csproj

# WebAssembly
cd SalmonEgg/SalmonEgg
dotnet run
# 浏览器会自动打开 http://localhost:5000
```

## 当前文档入口

当前开发请参考：

1. **仓库规则**: `../AGENTS.md`
2. **构建指南**: `../BUILD_GUIDE.md`
3. **架构文档**: `architecture.md`
4. **项目状态**: `PROJECT_STATUS.md`

## 参考资源

- [Uno Platform 官方文档](https://platform.uno/docs/)
- [.NET 10.0 文档](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10)
- [Clean Architecture](https://blog.cleancoder.com/uncle-bob/2012/08/13/the-clean-architecture.html)
- [MVVM Pattern](https://learn.microsoft.com/en-us/dotnet/architecture/maui/mvvm)
