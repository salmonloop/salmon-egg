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
dotnet run --project SalmonEgg/SalmonEgg/SalmonEgg.csproj \
  --framework net10.0-browserwasm
```

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
