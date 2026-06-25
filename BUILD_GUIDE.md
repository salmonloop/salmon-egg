# SalmonEgg 构建指南

## 快速开始

### Windows 用户
```bash
# 方式 1: 使用构建脚本（Desktop）
build.bat

# 方式 2: 构建 MSIX（不安装）
build.bat msix

# 方式 3: 直接运行 MSIX（默认）
run.bat

# 方式 4: 运行 Skia 桌面版（不走 WinUI3/MSIX）
run.bat desktop
```

### Linux/macOS 用户
```bash
# 方式 1: 使用构建脚本
./build.sh

# 方式 2: 直接运行
./run.sh

# 方式 3: Headless GUI（Xvfb 虚拟屏）
./run-headless.sh
```

## 详细构建步骤

### 1. 环境要求

- **.NET SDK**: 10.0 或更高版本
  - 推荐版本：10.0.202（允许 patch 前滚）
  - 下载地址: https://dotnet.microsoft.com/download/dotnet/10.0
  
- **操作系统**:
  - Windows 10 1809+ (推荐)
  - Windows 11
  - Linux (Ubuntu 20.04+, Debian 11+, 等)
  - macOS 12+

### 2. 检查环境

```bash
# 检查 .NET SDK 版本
dotnet --version

# 应该输出 10.0.2xx（推荐 10.0.202）
```

### 3. 克隆代码（如果还没有）

```bash
git clone <repository-url>
cd salmon-acp
```

### 4. 构建项目

#### 完整构建（推荐）
```bash
# 恢复依赖
dotnet restore SalmonEgg.sln

# 构建项目
dotnet build SalmonEgg.sln --configuration Release

# 运行测试
dotnet test SalmonEgg.sln

# 发布应用
dotnet publish SalmonEgg/SalmonEgg/SalmonEgg.csproj \
  --configuration Release \
  --framework net10.0-desktop \
  --output publish/windows-desktop
```

#### 快速构建（开发时）
```bash
# 构建并运行
dotnet run --project SalmonEgg/SalmonEgg/SalmonEgg.csproj \
  --framework net10.0-desktop
```

### 5. 运行应用

#### Windows Desktop
```bash
run.bat
```

> 说明：Windows 原生 WinUI 3 目标使用 MSIX 方式安装/启动（避免 unpackaged WinUI3 在部分系统上启动即崩溃）。
> 首次安装需要在“管理员 PowerShell”运行一次 `run.bat` 以将开发证书加入本机证书存储。
> 证书复用：`.tools/run-winui3-msix.ps1` 现在会复用同一张开发证书，不应再在每次 `run.bat msix` 时重建证书或反复要求安装证书。
> 历史根因：脚本曾使用 PowerShell 中不可靠的 `$Cert.GetRSAPrivateKey()` 调用来判断私钥可用性，导致有效的 RSA 私钥被误判为不可用，进而每次重建新证书；现已改为标准的 `RSACertificateExtensions.GetRSAPrivateKey(...)`。
> 工具链锁定：Windows SDK 10.0.26100.0，signtool 来自 SDK 10.0.22621.0。
> Workload manifest：CI 应与 `global.json` 中的 .NET SDK patch 保持一致；当前仓库锁定 10.0.202，本地允许最新 patch manifest。
> 验证口径：`dotnet build -f net10.0-windows10.0.26100.0` 不是本仓库的权威 WinUI 3 / MSIX 门禁；Windows 原生包请以 `build.bat msix` 或 `.tools/run-winui3-msix.ps1 -SkipInstall` 为准。`dotnet build` 主要用于 Core/Skia/Desktop/Wasm 验证。

#### Linux Headless Desktop
在没有物理显示器或桌面会话的 Linux 环境中，可以通过 `Xvfb` 提供虚拟 X11 屏幕，再运行 Uno Skia Desktop 目标：

```bash
./run-headless.sh
```

可选环境变量：

- `DISPLAY_NUMBER`：指定虚拟显示编号，默认 `99`
- `XVFB_SCREEN`：指定 `Xvfb` 屏幕参数，默认 `0 1920x1080x24`

如果当前 shell 已经设置了 `DISPLAY`，脚本会复用现有 X server，而不会再次启动 `Xvfb`。

#### Visual Studio 调试（推荐 / 官方）
在 `SalmonEgg.sln` 中将 `SalmonEgg` 设为启动项目，然后在工具栏的启动配置下拉列表中选择目标平台对应的 Launch Profile 即可按 F5 调试：

- **SalmonEgg (Desktop)** — Skia Desktop 跨平台渲染
- **SalmonEgg (WebAssembly)** — 浏览器 WASM
- **SalmonEgg (MSIX Script Run)** — WinUI 3 MSIX 打包运行
- **SalmonEgg (MSIX Script Debug Attach)** — WinUI 3 MSIX 附加调试

#### Windows MSIX（仅打包，不安装）
```bash
build.bat msix
```
输出目录：`artifacts/msix/`

#### WebAssembly (浏览器)
```bash
pwsh -File scripts/dev/stop-stale-wasm-hosts.ps1

dotnet run --project SalmonEgg/SalmonEgg/SalmonEgg.csproj \
  --framework net10.0-browserwasm
```

> 说明：WASM 调试前必须先清理旧的 `WasmAppHost`。如果端口仍由其它目录的旧构建占用，浏览器可能拿到旧 `package_*` / `dotnet.*.js` 哈希，表现为 `_framework/dotnet.*.js` 404、加载进度条无限停住或 502。
> 若要连当前 worktree 的 WasmHost 也一起清掉，可运行 `pwsh -File scripts/dev/stop-stale-wasm-hosts.ps1 -IncludeCurrentWorktree`。
> 发布到 Vercel 时，本仓库以 `vercel.json` 为唯一部署配置入口，输出目录固定为 `publish/vercel-wasm/wwwroot`。Vercel 官方文档说明 Deployment Protection 会限制生成部署 URL，保护所有 URL 时生产域和生成 URL 都会受保护；因此浏览器对 `manifest.webmanifest` 和 `service-worker.js` 的自动请求返回 401 属于部署保护策略，不是应用导航状态。验证静态资源可运行 `scripts/gates/verify-wasm-static-assets.sh <deployment-url>`，报告会写入 `artifacts/verification/` 并记录提交与 URL。

#### WebAssembly smoke gate
```bash
scripts/gates/run-wasm-smoke-gates.sh Debug
```

该 gate 会构建当前 `net10.0-browserwasm` 产物、静态托管 `wwwroot`，再用 Playwright/Chromium 执行两条 WASM 浏览器路径：

- 设置页顶部原生 `NavigationView` overflow 导航；
- `ACP / Agent` 与 `数据与存储` 的文件系统可用性 smoke：保存 ACP WebSocket profile、保存 remote directory、刷新后确认配置仍存在、验证 WASM 不声明 `clientCapabilities.fs` / `terminal=true`，并确认受限平台不会暴露桌面文件系统入口；
- WASM ACP 全链路 smoke：用同一 profile 和 remote directory 从 Start 页面创建远端会话，断言 mock ACP Server 收到 `initialize`、`session/new`（`cwd` 为所选 remote path）和 `session/prompt`，并确认 agent reply 投影到 Chat UI。

它补充 Windows self-hosted FlaUI gate，专门覆盖 WASM 浏览器里的原生 Uno 控件行为与当前构建产物的浏览器持久化链路。

#### WebAssembly 持久化策略

Uno 官方 IDBFS 文档要求通过 `<WasmShellEnableIDBFS>true</WasmShellEnableIDBFS>` 显式启用浏览器 IndexedDB-backed 文件系统。本仓库在 `net10.0-browserwasm` 上启用该构建能力，用于 `/local/SalmonEgg` 下的非敏感应用数据。

当前已确认的 WASM 持久化范围：

- 应用设置；
- ACP profile YAML；
- 其它走应用文件存储抽象的非敏感配置数据。

当前不应混淆的边界：

- 浏览器 IndexedDB-backed 文件系统只负责非敏感应用数据持久化；
- 安全存储仍由平台安全存储服务决定；WASM 继续使用 volatile secure storage；
- 因此“WASM 可以持久化 ACP 配置 / 普通设置”与“WASM 没有持久化安全凭据存储”可以同时成立。

ACP / 文件系统能力边界：

- WASM 目标不会向 ACP Server 声明 `clientCapabilities.fs`；
- WASM 目标不会把 `terminal` 宣告为 `true`；
- 设置页里“打开本地目录 / 导出目录”这类桌面文件系统入口在 WASM 上必须保持受限，不得绕过平台能力边界产生本地副作用。

### 6. 发布应用

#### Windows Desktop (独立应用)
```bash
dotnet publish SalmonEgg/SalmonEgg/SalmonEgg.csproj \
  --configuration Release \
  --framework net10.0-desktop \
  --runtime win-x64 \
  --self-contained true \
  --output publish/windows-x64
```

#### WebAssembly (静态网站)
```bash
dotnet publish SalmonEgg/SalmonEgg/SalmonEgg.csproj \
  --configuration Release \
  --framework net10.0-browserwasm \
  --output publish/wasm
```

## 常见问题

### 问题 1: 找不到 .NET SDK
**错误**: `dotnet: command not found` 或 `dotnet` 不是内部或外部命令

**解决**: 
1. 从 https://dotnet.microsoft.com/download/dotnet/10.0 下载并安装 .NET 10.0 SDK
2. 重启终端或命令提示符
3. 运行 `dotnet --version` 验证安装

### 问题 2: 版本不兼容
**错误**: `The current .NET SDK does not support targeting .NET 10.0`

**解决**: 
升级 .NET SDK 到 10.0 或更高版本

### 问题 3: 依赖还原失败
**错误**: `Unable to resolve package`

**解决**: 
```bash
# 清理 NuGet 缓存
dotnet nuget locals all --clear

# 重新还原
dotnet restore --force
```

### 问题 4: 构建失败
**解决**: 
```bash
# 清理构建输出
dotnet clean

# 删除 obj 和 bin 目录
rm -rf */obj */bin

# 重新构建
dotnet build
```

### 问题 5: `run.bat msix` 每次都重新安装开发证书
**现象**: 每次在管理员 PowerShell 中运行 `run.bat msix` 都重新生成证书，或 Windows 再次提示安装开发证书

**原因**:
旧脚本对证书私钥的可用性判断有误，把可复用的 RSA 证书误判成“没有私钥”，从而每次重建新证书；一旦签名证书 thumbprint 变化，Windows 就会把它视为新的签名者。

**当前预期**:
修复后，同一开发证书会被复用；连续执行 `run.bat msix` 时，不应再出现 `Existing dev certs are missing an RSA private key; recreating.`。

**排查**:
```bash
# 查看当前用户的开发证书
Get-ChildItem Cert:\CurrentUser\My | Where-Object Subject -eq 'CN=SalmonEgg'

# 查看本机信任的开发证书
Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object Subject -eq 'CN=SalmonEgg'
```

若 `CurrentUser\My` 与 `LocalMachine\TrustedPeople` 的 thumbprint 不一致，请先使用最新脚本再次执行一次管理员 `run.bat msix`，让脚本重新同步信任存储。

## 构建输出

构建成功后，您会在以下目录找到输出：

- **Windows Desktop**: `publish/windows-desktop/SalmonEgg.exe`
- **WebAssembly**: `publish/wasm/wwwroot/`

## 开发工作流

### 日常开发
```bash
# 1. 拉取最新代码
git pull

# 2. 恢复依赖（如果csproj有变化）
dotnet restore

# 3. 运行应用
./run.bat  # Windows（MSIX）
./run.sh   # Linux/macOS

# 或直接
dotnet run --project SalmonEgg/SalmonEgg/SalmonEgg.csproj \
  --framework net10.0-desktop
```

### 发布前检查
```bash
# 1. 运行所有测试
dotnet test

# 2. 构建 Core / Skia / Wasm 验证目标
dotnet build --configuration Release

# 3. 验证 Windows 原生 MSIX 打包链路
build.bat msix

# 4. 发布应用
./build.bat        # Windows desktop
./build.bat msix   # Windows MSIX
./build.sh   # Linux/macOS
```

## 性能优化

### 启用 AOT 编译 (WebAssembly)
```bash
dotnet publish \
  --configuration Release \
  --framework net10.0-browserwasm \
  -p:PublishTrimmed=true \
  -p:TrimMode=link
```

> 注意：上面的命令启用的是 trimming/linker，并不等同于完整 AOT。当前 WASM 发布优化前，需先确认 browserwasm 依赖图没有拉入桌面/PTY/PInvoke 链路，否则发布阶段可能在 P/Invoke 扫描或裁剪阶段失败。

### 减小发布体积
```bash
dotnet publish \
  --configuration Release \
  --self-contained false \
  --runtime win-x64
```

## 持续集成

项目已配置 GitHub Actions CI/CD，每次推送代码时会自动：
1. 恢复依赖
2. 构建项目
3. 运行测试
4. 打包应用

查看 `.github/workflows/ci.yml` 了解详情。

## 相关文档

- [用户指南](docs/USER_GUIDE.md) - 如何使用应用
- [架构文档](docs/architecture.md) - 项目架构说明
- [发布指南](docs/release-guide.md) - 各平台发布说明

## 获取帮助

如果遇到问题：
1. 查看本文档的"常见问题"部分
2. 检查日志文件: `%LOCALAPPDATA%\SalmonEgg\logs\`
3. 提交 Issue: [GitHub Issues]

---

**祝您使用愉快！** 🎉
