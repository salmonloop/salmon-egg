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

# 应该输出 10.0.x 或更高
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

# 2. 构建发布版本
dotnet build --configuration Release

# 3. 发布应用
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
