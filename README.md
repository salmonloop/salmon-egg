# SalmonEgg

基于 Uno Platform 的 Agent Client Protocol (ACP) 客户端应用程序。

## 项目结构

本项目采用四层架构（Clean Architecture）：

```
SalmonEgg/
├── src/
│   ├── SalmonEgg.Domain/          # 领域层 (.NET Standard 2.1)
│   ├── SalmonEgg.Application/     # 应用层 (.NET Standard 2.1)
│   ├── SalmonEgg.Infrastructure/  # 基础设施层 (.NET Standard 2.1)
│   └── SalmonEgg/                 # Uno Platform 主项目
├── tests/
│   ├── SalmonEgg.Domain.Tests/
│   ├── SalmonEgg.Application.Tests/
│   └── SalmonEgg.Infrastructure.Tests/
└── docs/
```

## 技术栈

### 核心框架
- **Uno Platform 6.5+**: 跨平台 UI 框架
- **.NET 9.0**: 目标框架（平台头）
- **.NET Standard 2.1**: 共享库目标框架

### 第三方库

#### Infrastructure 层
- **System.Text.Json 10.0.3**: JSON 序列化
- **Websocket.Client 5.3.0**: WebSocket 客户端
- **Polly 8.6.6**: 弹性和瞬态故障处理
- **Serilog 4.3.1**: 日志记录
- **Serilog.Sinks.File 7.0.0**: 文件日志
- **Serilog.Sinks.Console 6.1.1**: 控制台日志
- **Microsoft.Extensions.DependencyInjection 10.0.3**: 依赖注入

#### Application 层
- **FluentValidation 11.9.2**: 数据验证
- **Microsoft.Extensions.DependencyInjection.Abstractions 10.0.3**: DI 抽象
- **System.Reactive 6.1.0**: 响应式编程

#### Presentation 层
- **CommunityToolkit.Mvvm**: MVVM 框架
- **Microsoft.Extensions.DependencyInjection**: 依赖注入
- **Microsoft.Extensions.Logging**: 日志抽象

## 前置要求

### 开发环境

**所有平台通用**:
- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) 或更高版本
- [Visual Studio 2022](https://visualstudio.microsoft.com/) (17.12+) 或 [Visual Studio Code](https://code.visualstudio.com/)
- [Uno Platform 模板](https://platform.uno/docs/articles/get-started.html)

**Windows 开发**:
- Windows 10 1809 或更高版本
- Visual Studio 2022 with:
  - .NET Desktop Development workload
  - Universal Windows Platform development workload

**WebAssembly 开发**:
- 现代浏览器（Chrome、Firefox、Edge、Safari）
- .NET WebAssembly 构建工具

### 安装 Uno Platform 模板

```bash
# 安装 Uno Platform 项目模板
dotnet new install Uno.Templates

# 验证安装
dotnet new list | grep -i uno
```

## 项目初始化

由于 Uno Platform 6.5+ 需要 .NET 9.0，请确保已安装 .NET 9.0 SDK。

### 创建项目（需要 .NET 9.0）

```bash
# 创建 Uno Platform 应用
dotnet new unoapp -o SalmonEgg

# 进入项目目录
cd SalmonEgg

# 创建解决方案
dotnet new sln -n SalmonEgg -o ..

# 添加项目到解决方案
dotnet sln ../SalmonEgg.sln add SalmonEgg/SalmonEgg.csproj
```

### 创建层项目

```bash
# 创建领域层
dotnet new classlib -n SalmonEgg.Domain -f netstandard2.1 -o ../src/SalmonEgg.Domain
dotnet sln ../SalmonEgg.sln add ../src/SalmonEgg.Domain/SalmonEgg.Domain.csproj

# 创建应用层
dotnet new classlib -n SalmonEgg.Application -f netstandard2.1 -o ../src/SalmonEgg.Application
dotnet sln ../SalmonEgg.sln add ../src/SalmonEgg.Application/SalmonEgg.Application.csproj

# 创建基础设施层
dotnet new classlib -n SalmonEgg.Infrastructure -f netstandard2.1 -o ../src/SalmonEgg.Infrastructure
dotnet sln ../SalmonEgg.sln add ../src/SalmonEgg.Infrastructure/SalmonEgg.Infrastructure.csproj

# 创建测试项目
dotnet new xunit -n SalmonEgg.Domain.Tests -o ../tests/SalmonEgg.Domain.Tests
dotnet sln ../SalmonEgg.sln add ../tests/SalmonEgg.Domain.Tests/SalmonEgg.Domain.Tests.csproj

dotnet new xunit -n SalmonEgg.Infrastructure.Tests -o ../tests/SalmonEgg.Infrastructure.Tests
dotnet sln ../SalmonEgg.sln add ../tests/SalmonEgg.Infrastructure.Tests/SalmonEgg.Infrastructure.Tests.csproj

dotnet new xunit -n SalmonEgg.Application.Tests -o ../tests/SalmonEgg.Application.Tests
dotnet sln ../SalmonEgg.sln add ../tests/SalmonEgg.Application.Tests/SalmonEgg.Application.Tests.csproj
```

### 添加项目引用

```bash
# Application 层引用 Domain 层
dotnet add ../src/SalmonEgg.Application/SalmonEgg.Application.csproj reference ../src/SalmonEgg.Domain/SalmonEgg.Domain.csproj

# Infrastructure 层引用 Domain 层
dotnet add ../src/SalmonEgg.Infrastructure/SalmonEgg.Infrastructure.csproj reference ../src/SalmonEgg.Domain/SalmonEgg.Domain.csproj

# 主项目引用所有层
dotnet add SalmonEgg/SalmonEgg.csproj reference ../src/SalmonEgg.Domain/SalmonEgg.Domain.csproj
dotnet add SalmonEgg/SalmonEgg.csproj reference ../src/SalmonEgg.Application/SalmonEgg.Application.csproj
dotnet add SalmonEgg/SalmonEgg.csproj reference ../src/SalmonEgg.Infrastructure/SalmonEgg.Infrastructure.csproj
```

## 当前状态

✅ 已完成：
- 创建了四层架构项目结构（Domain, Application, Infrastructure）
- 配置了 .NET Standard 2.1 目标框架
- 安装了所有必需的 NuGet 包
- 创建了测试项目结构
- 配置了项目引用关系

⚠️ 待完成（需要 .NET 9.0 SDK）：
- Uno Platform 主项目需要 .NET 9.0 SDK
- 当前 Uno Platform 6.5+ 版本要求 .NET 9.0 或更高版本
- 建议升级到 .NET 9.0 SDK 后继续项目初始化

## 构建项目

```bash
# 恢复依赖
dotnet restore

# 构建解决方案
dotnet build SalmonEgg.sln --configuration Release
```

## 运行测试

```bash
# 运行所有测试
dotnet test

# 运行特定测试项目
dotnet test tests/SalmonEgg.Infrastructure.Tests
```

## 下一步

1. 升级到 .NET 9.0 SDK
2. 完成 Uno Platform 主项目的创建
3. 实现领域模型（AcpMessage, ConnectionState, ServerConfiguration）
4. 实现基础设施层（消息解析器、连接管理器）
5. 实现应用层（用例、服务）
6. 实现表示层（ViewModels、Views）
7. 编写测试

详细的实现步骤请参考 `.kiro/specs/uno-acp-client/tasks.md`。

## 参考文档

- [Uno Platform 文档](https://platform.uno/docs/)
- [ACP 协议规范](https://spec.modelcontextprotocol.io/)
- [Clean Architecture](https://blog.cleancoder.com/uncle-bob/2012/08/13/the-clean-architecture.html)
