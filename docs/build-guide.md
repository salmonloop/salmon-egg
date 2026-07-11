# SalmonEgg 构建和运行指南

> Status: secondary reference. The canonical agent-facing build guide is `../BUILD_GUIDE.md`, which is the file referenced by `AGENTS.md`. Keep this document aligned when it is edited, but prefer `../BUILD_GUIDE.md` for delivery gates and current commands.

## 系统要求

### 开发环境

- **.NET 10.0 SDK**（推荐 10.0.109，允许 patch 前滚）
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
- 当前目标所需的 Xcode 与 .NET workload
- Visual Studio 2022、VS Code/C# Dev Kit 或命令行工具链

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
pwsh -File scripts/dev/stop-stale-wasm-hosts.ps1

dotnet run --project SalmonEgg/SalmonEgg/SalmonEgg.csproj `
  --framework net10.0-browserwasm
```
浏览器会自动打开 http://localhost:5000

## 平台特定构建指南

### Windows Desktop

> 说明：原生 WinUI 3 目标需要 Windows 10/11 SDK + Visual Studio 2022（或 Build Tools 2022，含 MSBuild + C++ 工具链），否则会在 XamlCompiler 步骤失败。
> 首次安装需要在“管理员 PowerShell”运行一次 `run.bat`，以将开发证书写入本机信任存储。
> 证书复用：修复后的 `.tools/run-winui3-msix.ps1` 会复用同一张开发证书，不应再在每次 `run.bat msix` 时重建证书或反复要求安装证书。
> 历史根因：脚本曾使用 PowerShell 中不可靠的 `$Cert.GetRSAPrivateKey()` 调用来判断私钥可用性，导致有效的 RSA 私钥被误判为不可用，进而每次重建新证书；现已改为标准的 `RSACertificateExtensions.GetRSAPrivateKey(...)`。
> 验证口径：不要把 `dotnet build -f net10.0-windows10.0.26100.0` 当作 WinUI 3 / MSIX 的权威门禁；Windows 原生包请以 `build.bat msix` 或 `.tools/run-winui3-msix.ps1 -SkipInstall` 为准。常规 `dotnet build` 主要覆盖 Core / Skia / Wasm。

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
  -o ./publish/desktop
```

如果你怀疑本机仍在反复装证书，可以用下面两条命令核对当前签名证书和本机信任证书是否是同一个 thumbprint：

```bash
Get-ChildItem Cert:\CurrentUser\My | Where-Object Subject -eq 'CN=SalmonEgg'
Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object Subject -eq 'CN=SalmonEgg'
```

### WebAssembly

```bash
# 运行开发服务器
pwsh -File scripts/dev/stop-stale-wasm-hosts.ps1

dotnet run --project SalmonEgg/SalmonEgg/SalmonEgg.csproj `
  --framework net10.0-browserwasm

# 发布为静态网站
dotnet publish SalmonEgg/SalmonEgg/SalmonEgg.csproj \
  -f net10.0-browserwasm \
  -c Release \
  -o ./publish/wasm
```

WASM 调试前必须先清理旧的 `WasmAppHost`。如果端口仍由其它目录的旧构建占用，浏览器可能拿到旧 `package_*` / `dotnet.*.js` 哈希，表现为 `_framework/dotnet.*.js` 404、加载进度条无限停住或 502。若要连当前 worktree 的 WasmHost 也一起清掉，可运行 `pwsh -File scripts/dev/stop-stale-wasm-hosts.ps1 -IncludeCurrentWorktree`。

WASM 持久化与能力边界：

- `net10.0-browserwasm` 启用了 Uno IDBFS，用于 `/local/SalmonEgg` 下的应用数据；
- 当前已确认会持久化浏览器 IndexedDB-backed 文件系统的数据包括：应用设置、ACP profile YAML、其它走应用文件存储抽象的普通配置，以及 plaintext secure storage 中的配置相关凭据；
- WASM 没有 OS-backed secure store，配置相关凭据会以普通应用文件形式持久化；
- 配置云同步包会包含 config 目录和已登记的 ACP token/API key 等凭据，`secrets.json` 为明文内容；
- 云同步当前支持 OneDrive、WebDAV 与 S3-compatible object storage，设置页一次只能连接一个 provider；WebDAV 的文件夹 URL/用户名和 S3 的 endpoint/bucket/region/object key 属于用户配置，WebDAV 同步包默认文件名为 `salmonegg-config.zip`，保存时会在服务端允许的情况下创建缺失 WebDAV collection，S3 object key 前缀不需要目录创建，WebDAV 密码与 S3 access key/secret key 走应用 secure storage；
- OneDrive 应用注册配置通过 GitHub Actions 构建阶段注入：设置 `SALMONEGG_ONEDRIVE_CLIENT_ID`、`SALMONEGG_ONEDRIVE_TENANT_ID`、`SALMONEGG_ONEDRIVE_REDIRECT_URI`、`SALMONEGG_ONEDRIVE_SCOPES` 为 repository secrets 或 variables；workflow 会优先读取 secrets，空值时读取 variables，并写入程序集元数据；
- WASM 不会向 ACP Server 声明 `clientCapabilities.fs`，也不会把 `terminal` 声明为 `true`；
- 设置页中的本地目录打开/导出等桌面入口在 WASM 上必须保持受限。

WASM smoke gate：

```bash
scripts/gates/run-wasm-smoke-gates.sh Debug
```

该 gate 会构建当前 `net10.0-browserwasm` 产物、静态托管 `wwwroot`，然后用 Playwright/Chromium 覆盖：

- 设置页顶部原生 `NavigationView` overflow；
- `ACP / Agent` profile 与 remote directory 保存并刷新后仍可见；
- ACP `initialize` 不宣告 `fs` / `terminal=true`；
- 从 Start 页面按所选 remote directory 真正创建 ACP 会话，断言 `session/new.cwd` 等于 remote path，随后发送 `session/prompt` 并在 Chat UI 看到 agent reply；
- `数据与存储` 页面上的受限桌面文件系统入口不会越过平台能力边界。

发布后的文件可以部署到任何静态网站托管服务（如 Azure Static Web Apps、GitHub Pages、Netlify 等）。

### Android

```bash
# 安装 Android 工作负载（首次需要）
dotnet workload install android

# CI manifest 应与 `global.json` 中的 SDK patch 保持一致（当前为 10.0.109）

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

> 说明：`net10.0-ios` 需要 macOS/Xcode/iOS workload，不进入默认构建。需要 iOS 时通过 `EnableMobileTargets=true` + `EnableIosTarget=true` 启用，不手改 `TargetFrameworks`。

```bash
# 安装 iOS 工作负载（首次需要）
dotnet workload install ios

# 运行在 iOS 模拟器
dotnet run --project SalmonEgg/SalmonEgg/SalmonEgg.csproj \
  -f net10.0-ios \
  -p:EnableMobileTargets=true \
  -p:EnableIosTarget=true \
  -t:RunSimulator
```

### Mobile target contract gate

```bash
scripts/gates/verify-mobile-target-contracts.sh
```

该 gate 验证默认构建不包含移动 TFM、Android/iOS opt-in TFM 展开符合项目文件；当本机安装 Android ref pack 时，还会对 `AndroidKeyStoreSecureStorage` 做 Android 引用级 C# 编译检查。完整 Android 打包仍需要可运行的 Android build-tools；iOS 打包仍需要 macOS/Xcode。

## 测试

### 运行所有测试

```bash
dotnet test --solution SalmonEgg.sln --configuration Release --timeout 20m --output Normal
```

### 运行特定项目测试

```bash
# 基础设施测试
dotnet test --project tests/SalmonEgg.Infrastructure.Tests/SalmonEgg.Infrastructure.Tests.csproj --output Normal

# 应用层测试
dotnet test --project tests/SalmonEgg.Application.Tests/SalmonEgg.Application.Tests.csproj --output Normal

# 领域层测试
dotnet test --project tests/SalmonEgg.Domain.Tests/SalmonEgg.Domain.Tests.csproj --output Normal
```

### 运行特定测试

```bash
# 使用 xUnit v3 / Microsoft.Testing.Platform 类过滤器
dotnet test --project tests/SalmonEgg.Application.Tests/SalmonEgg.Application.Tests.csproj \
  --filter-class SalmonEgg.Application.Tests.UiConventionsTests \
  --output Normal

# 显示详细输出
dotnet test --solution SalmonEgg.sln --output Normal
```

### 代码覆盖率

```bash
# 生成 TRX 与 Cobertura 覆盖率
dotnet test --solution SalmonEgg.sln \
  --results-directory TestResults \
  --report-xunit-trx \
  --coverlet \
  --coverlet-output-format cobertura \
  --output Normal
```

## 调试

### Visual Studio 2022

1. 打开 `SalmonEgg.sln`
2. 设置启动项目为 `SalmonEgg`
3. 对 Windows 原生运行优先选择 `SalmonEgg (MSIX Script Run)` 或 `SalmonEgg (MSIX Script Debug Attach)` Launch Profile
4. 对 Skia / Wasm 再按目标框架选择对应 profile 并开始调试

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

### GitHub Actions

仓库现有 GitHub Actions 是唯一 CI 配置来源：`ci-core.yml`、`code-quality.yml`、`gui-smoke-gates.yml`、`wasm-smoke-gates.yml` 和 `release-packaging.yml`。不要从文档复制一套平行 CI；门禁变更时直接修改对应 workflow。

## 参考资源

- [Uno Platform 官方文档](https://platform.uno/docs/)
- [.NET 10.0 文档](https://learn.microsoft.com/dotnet/core/whats-new/dotnet-10)
- [CommunityToolkit.Mvvm](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/)
- [Serilog 文档](https://serilog.net/)
- [xUnit 测试框架](https://xunit.net/)
