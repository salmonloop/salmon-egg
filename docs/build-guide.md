# SalmonEgg 构建和运行指南

## 系统要求

### 开发环境

- **.NET 10.0 SDK**（推荐 10.0.200，允许 patch 前滚）
  - 下载地址：https://dotnet.microsoft.com/download/dotnet/10.0
  - 验证安装：`dotnet --version`

- **Visual Studio 2022** (17.12+) 或 **Visual Studio Code**
  - Visual Studio 2022：https://visualstudio.microsoft.com/
  - VS Code：https://code.visualstudio.com/

- **Uno Platform 模板**
  ```bash
  dotnet new install Uno.Templates
  ```

### 平台特定要求

#### Windows
- Windows 10 1809 或更高版本
- Visual Studio 2022 with:
  - .NET Desktop Development workload
  - Universal Windows Platform development workload (可选)
  - Windows SDK 10.0.26100.0
  - Windows SDK 10.0.22621.0（signtool）

#### WebAssembly
- 现代浏览器（Chrome、Firefox、Edge、Safari）
- 无需额外安装

#### Android (可选)
- Android SDK (API Level 21+)
- 或 Visual Studio 2022 with .NET Multi-platform App UI development

#### iOS/macOS (可选)
- macOS 12.0+
- Xcode 14.0+
- Visual Studio for Mac

## 快速开始

### 1. 克隆或创建项目

```bash
# 如果从现有仓库克隆
git clone <repository-url>
cd salmon-acp
```

### 2. 还原依赖

```bash
# 还原所有 NuGet 包
dotnet restore SalmonEgg.sln
```

### 3. 构建项目

```bash
# 构建整个解决方案
dotnet build SalmonEgg.sln --configuration Release
```

### 4. 运行应用

#### Windows (Desktop)
```bash
run.bat
```

#### WebAssembly
```bash
cd SalmonEgg/SalmonEgg
dotnet run --framework net10.0-browserwasm
```
浏览器会自动打开 http://localhost:5000

## 平台特定构建指南

### Windows Desktop

> 说明：原生 WinUI 3 目标需要 Windows 10/11 SDK + Visual Studio 2022（或 Build Tools 2022，含 MSBuild + C++ 工具链），否则会在 XamlCompiler 步骤失败。
> 首次安装需要在“管理员 PowerShell”运行一次 `run.bat`，以将开发证书写入本机信任存储。
> 证书复用：修复后的 `.tools/run-winui3-msix.ps1` 会复用同一张开发证书，不应再在每次 `run.bat msix` 时重建证书或反复要求安装证书。
> 历史根因：脚本曾使用 PowerShell 中不可靠的 `$Cert.GetRSAPrivateKey()` 调用来判断私钥可用性，导致有效的 RSA 私钥被误判为不可用，进而每次重建新证书；现已改为标准的 `RSACertificateExtensions.GetRSAPrivateKey(...)`。

```bash
# 运行（MSIX）
run.bat

# 仅打包 MSIX（不安装）
build.bat msix

# Skia Desktop（跨平台）
dotnet run --project SalmonEgg/SalmonEgg/SalmonEgg.csproj \
  -f net10.0-desktop

# 发布 Skia Desktop
dotnet publish SalmonEgg/SalmonEgg/SalmonEgg.csproj \
  -f net10.0-desktop \
  -c Release \
  -o ./publish/windows-desktop
```

如果你怀疑本机仍在反复装证书，可以用下面两条命令核对当前签名证书和本机信任证书是否是同一个 thumbprint：

```bash
Get-ChildItem Cert:\CurrentUser\My | Where-Object Subject -eq 'CN=SalmonEgg'
Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object Subject -eq 'CN=SalmonEgg'
```

### WebAssembly

```bash
# 运行开发服务器
cd SalmonEgg/SalmonEgg
dotnet run --framework net10.0-browserwasm

# 发布为静态网站
dotnet publish SalmonEgg/SalmonEgg/SalmonEgg.csproj \
  -f net10.0-browserwasm \
  -c Release \
  -o ./publish/wasm
```

发布后的文件可以部署到任何静态网站托管服务（如 Azure Static Web Apps、GitHub Pages、Netlify 等）。

### Android

```bash
# 安装 Android 工作负载（首次需要）
dotnet workload install android

# CI 固定的 manifest 版本（本地可不必）
# dotnet workload install android --version 10.0.200-manifests.34a88a22

# 运行在 Android 模拟器
dotnet run --project SalmonEgg/SalmonEgg/SalmonEgg.csproj \
  -f net10.0-android36.0

# 发布 APK
dotnet publish SalmonEgg/SalmonEgg/SalmonEgg.csproj \
  -f net10.0-android36.0 \
  -c Release \
  -o ./publish/android
```

### iOS (需要 macOS，可选)

> 说明：当前仓库默认只启用 `net10.0-android36.0`（见 `SalmonEgg/SalmonEgg/SalmonEgg.csproj`）。
> 如需 iOS/macOS，请先将对应 TFM 加入 `TargetFrameworks`。

```bash
# 安装 iOS 工作负载（首次需要）
dotnet workload install ios

# 运行在 iOS 模拟器
dotnet run --project SalmonEgg/SalmonEgg/SalmonEgg.csproj \
  -f net10.0-ios \
  -t:RunSimulator
```

## 测试

### 运行所有测试

```bash
dotnet test SalmonEgg.sln
```

### 运行特定项目测试

```bash
# 基础设施测试
dotnet test tests/SalmonEgg.Infrastructure.Tests

# 应用层测试
dotnet test tests/SalmonEgg.Application.Tests

# 领域层测试
dotnet test tests/SalmonEgg.Domain.Tests
```

### 运行特定测试

```bash
# 使用过滤器
dotnet test --filter "FullyQualifiedName~ConnectionManager"

# 显示详细输出
dotnet test --verbosity normal
```

### 代码覆盖率

```bash
# 生成覆盖率报告（需要 coverlet.collector）
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=cobertura
```

## 调试

### Visual Studio 2022

1. 打开 `SalmonEgg.sln`
2. 设置启动项目为 `SalmonEgg`
3. 选择目标框架（如 `net10.0-windows10.0.26100.0`）
4. 按 F5 开始调试

### Visual Studio Code

1. 安装 C# Dev Kit 扩展
2. 打开项目文件夹
3. 选择 .NET 10.0 作为目标框架
4. 按 F5 开始调试

### 调试日志

应用使用 Serilog 记录日志：
- **调试模式**：日志级别为 Debug，输出到控制台和文件
- **发布模式**：日志级别为 Information

日志文件位置：
- **Windows**: `%LOCALAPPDATA%\SalmonEgg\logs\`
- **WebAssembly**: 浏览器开发者工具 Console
- **macOS/Linux**: `~/.local/share/SalmonEgg/logs/`

## 常见问题

### 问题 1: "SalmonEgg" 项目无法构建

**症状**: 编译错误提到找不到类型或命名空间

**解决方案**:
1. 确保已安装 .NET 10.0 SDK
2. 运行 `dotnet restore`
3. 清理并重新构建：
   ```bash
   dotnet clean
   dotnet build
   ```

### 问题 2: Android/iOS 工作负载未安装

**症状**: `NETSDK1147: To build this project, the following workloads must be installed`

**解决方案**:
```bash
# 安装 Android 工作负载
dotnet workload install android

# 安装 iOS 工作负载（需要 macOS）
dotnet workload install ios
```

### 问题 3: WebAssembly 构建失败

**症状**: 构建时提示缺少 wasm-tools

**解决方案**:
```bash
# 安装 WebAssembly 工具
dotnet workload install wasm-tools

# 重新构建
dotnet build --framework net10.0-browserwasm
```

### 问题 4: XAML 编译错误

**症状**: `UXAML0001: Processing failed for an unknown reason`

**解决方案**:
1. 检查 XAML 语法错误
2. 确保使用了正确的命名空间前缀
3. 避免在 Uno Platform 中使用 WinUI 专有特性
4. 清理并重新构建：
   ```bash
   dotnet clean
   dotnet build
   ```

### 问题 5: 依赖注入服务未注册

**症状**: `InvalidOperationException: Unable to resolve service for type...`

**解决方案**:
1. 检查 `DependencyInjection.cs` 中是否注册了该服务
2. 确保服务注册在使用之前执行
3. 验证服务生命周期（Singleton/Scoped/Transient）是否正确

## 性能优化

### 发布版本优化

```bash
# 发布优化的 Release 版本
dotnet publish -c Release -r win-x64 --self-contained true
```

### 减少构建时间

1. 使用增量构建（默认启用）
2. 仅构建需要的目标框架
3. 使用预编译头（如果适用）

## 持续集成

### GitHub Actions 示例

创建 `.github/workflows/ci.yml`:

```yaml
name: CI

on:
  push:
    branches: [ main ]
  pull_request:
    branches: [ main ]

jobs:
  build:
    runs-on: windows-latest
    
    steps:
    - uses: actions/checkout@v3
    
    - name: Setup .NET
      uses: actions/setup-dotnet@v3
      with:
        dotnet-version: '10.0.x'
    
    - name: Restore dependencies
      run: dotnet restore
    
    - name: Build
      run: dotnet build --no-restore --configuration Release
    
    - name: Test
      run: dotnet test --no-build --configuration Release --verbosity normal
```

## 参考资源

- [Uno Platform 官方文档](https://platform.uno/docs/)
- [.NET 10.0 文档](https://learn.microsoft.com/dotnet/core/whats-new/dotnet-10)
- [CommunityToolkit.Mvvm](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/)
- [Serilog 文档](https://serilog.net/)
- [xUnit 测试框架](https://xunit.net/)
