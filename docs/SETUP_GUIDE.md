# SalmonEgg 项目设置指南

## 概述

本文档提供了 SalmonEgg 项目的详细设置说明。该项目采用四层架构（Clean Architecture），使用 Uno Platform 构建跨平台原生应用。

## 系统要求

### 必需软件

1. **.NET 10.0 SDK** 或更高版本
   - 下载地址: https://dotnet.microsoft.com/download/dotnet/10.0
   - 验证安装: `dotnet --version`

2. **Visual Studio 2022** (17.12+) 或 **Visual Studio Code**
   - Visual Studio 2022: https://visualstudio.microsoft.com/
   - VS Code: https://code.visualstudio.com/

3. **Uno Platform 模板**
   ```bash
   dotnet new install Uno.Templates
   ```

### 可选软件（根据目标平台）

- **Windows 开发**: Windows 10 1809+, UWP workload
- **Android 开发**: Android SDK (API Level 21+), Android Emulator
- **iOS 开发**: macOS 12.0+, Xcode 14.0+
- **WebAssembly**: 现代浏览器

## 项目初始化步骤

### 1. 验证环境

```bash
# 检查 .NET 版本（需要 10.0+）
dotnet --version

# 检查 Uno Platform 模板
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
dotnet new classlib -n SalmonEgg.Domain -f netstandard2.1 -o src/SalmonEgg.Domain
dotnet sln add src/SalmonEgg.Domain/SalmonEgg.Domain.csproj

# 创建目录结构
mkdir -p src/SalmonEgg.Domain/Models
mkdir -p src/SalmonEgg.Domain/Services
mkdir -p src/SalmonEgg.Domain/Exceptions
```

#### Application 层（应用层）

```bash
dotnet new classlib -n SalmonEgg.Application -f netstandard2.1 -o src/SalmonEgg.Application
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
dotnet new classlib -n SalmonEgg.Infrastructure -f netstandard2.1 -o src/SalmonEgg.Infrastructure
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

```bash
cd src/SalmonEgg.Infrastructure
dotnet add package System.Text.Json --version 10.0.3
dotnet add package Websocket.Client --version 5.3.0
dotnet add package Polly --version 8.6.6
dotnet add package Serilog --version 4.3.1
dotnet add package Serilog.Sinks.File --version 7.0.0
dotnet add package Serilog.Sinks.Console --version 6.1.1
dotnet add package Microsoft.Extensions.DependencyInjection --version 10.0.3
cd ../..
```

#### Application 层

```bash
cd src/SalmonEgg.Application
dotnet add package FluentValidation --version 11.9.2
dotnet add package Microsoft.Extensions.DependencyInjection.Abstractions --version 10.0.3
dotnet add package System.Reactive --version 6.1.0
cd ../..
```

#### Presentation 层

```bash
cd SalmonEgg/SalmonEgg
dotnet add package CommunityToolkit.Mvvm
dotnet add package Microsoft.Extensions.DependencyInjection
dotnet add package Microsoft.Extensions.Logging
cd ../..
```

#### 测试项目

```bash
# 为每个测试项目添加测试包
cd tests/SalmonEgg.Domain.Tests
dotnet add package xunit
dotnet add package xunit.runner.visualstudio
dotnet add package FsCheck
dotnet add package FsCheck.Xunit
dotnet add package Moq
dotnet add package FluentAssertions
cd ../..

# 对其他测试项目重复相同操作
cd tests/SalmonEgg.Infrastructure.Tests
dotnet add package xunit
dotnet add package xunit.runner.visualstudio
dotnet add package FsCheck
dotnet add package FsCheck.Xunit
dotnet add package Moq
dotnet add package FluentAssertions
cd ../..

cd tests/SalmonEgg.Application.Tests
dotnet add package xunit
dotnet add package xunit.runner.visualstudio
dotnet add package FsCheck
dotnet add package FsCheck.Xunit
dotnet add package Moq
dotnet add package FluentAssertions
cd ../..
```

### 8. 验证设置

```bash
# 恢复所有依赖
dotnet restore

# 构建解决方案
dotnet build

# 运行测试
dotnet test
```

## 常见问题

### 问题 1: Uno Platform 版本不兼容

**症状**: 错误提示包不兼容 net8.0

**解决方案**: 
- 确保安装了 .NET 10.0 SDK
- 更新 Uno Platform 模板: `dotnet new install Uno.Templates --force`

### 问题 2: Websocket.Client 不兼容 netstandard2.0

**症状**: NU1202 错误，Websocket.Client 需要 netstandard2.1

**解决方案**:
- 将 Infrastructure 层目标框架改为 netstandard2.1
- 同时更新 Domain 和 Application 层为 netstandard2.1

### 问题 3: 工作负载未安装

**症状**: NETSDK1147 错误，需要安装 Android/iOS 工作负载

**解决方案**:
```bash
# 安装所需工作负载
dotnet workload install android ios wasm-tools

# 或者只安装需要的平台
dotnet workload install wasm-tools  # 仅 WebAssembly
```

### 问题 4: FluentValidation 版本不兼容

**症状**: FluentValidation 12.x 需要 net8.0

**解决方案**:
- 使用 FluentValidation 11.9.2 版本（支持 netstandard2.1）
- 或者将 Application 层升级到 net8.0

## 开发工作流

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

## 下一步

完成项目初始化后，请参考以下文档继续开发：

1. **架构文档**: `.kiro/specs/uno-acp-client/design.md`
2. **需求文档**: `.kiro/specs/uno-acp-client/requirements.md`
3. **任务列表**: `.kiro/specs/uno-acp-client/tasks.md`
4. **构建指南**: `BUILD_GUIDE.md`

## 参考资源

- [Uno Platform 官方文档](https://platform.uno/docs/)
- [.NET 10.0 文档](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10)
- [Clean Architecture](https://blog.cleancoder.com/uncle-bob/2012/08/13/the-clean-architecture.html)
- [MVVM Pattern](https://learn.microsoft.com/en-us/dotnet/architecture/maui/mvvm)
