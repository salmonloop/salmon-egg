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
  - 推荐版本：10.0.302（允许 patch 前滚）
  - 下载地址: https://dotnet.microsoft.com/download/dotnet/10.0
  
- **操作系统**:
  - Windows 10 1809+ (推荐)
  - Windows 11
  - Linux (Ubuntu 20.04+, Debian 11+, 等)
  - macOS 12+

Linux 桌面运行时依赖按能力分层（包名以 Debian/Ubuntu 为例）：

- 基础 Skia Desktop / X11：`libfreetype6`、`fontconfig`、`libfontconfig1`、`libgtk-3-0`、`libx11-6`；
- Headless GUI / XTest input smoke：`xvfb`、`libxtst6`；
- 外部文件/目录打开：`xdg-utils`（提供 `xdg-open`）或等价桌面 opener；
- 本地交互终端 WebView：WebKitGTK / JavaScriptCore（例如 Ubuntu 上的 `libwebkit2gtk-4.1-0` 或发行版对应包）；
- Linux 安全凭据持久化：Secret Service provider 和 `libsecret-tools`（提供 `secret-tool`）。

缺少可选依赖时，对应能力会被平台能力服务关闭或使用受限 fallback。Linux/macOS 桌面安全存储不可用时会降级到应用数据目录下的 plaintext secure storage；WASM 使用浏览器持久化文件系统保存该 plaintext secure storage。

### 2. 检查环境

```bash
# 检查 .NET SDK 版本
dotnet --version

# 应该输出 10.0.302 或兼容的 10.0.3xx patch 版本
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

# 运行测试（global.json 已启用 Microsoft.Testing.Platform）
dotnet test --solution SalmonEgg.sln --configuration Release --timeout 20m --output Normal

# 发布 Linux desktop 应用
dotnet publish SalmonEgg/SalmonEgg/SalmonEgg.csproj \
  --configuration Release \
  --framework net10.0-desktop \
  --output publish/linux-desktop

# 发布 macOS desktop 应用（按目标架构选择 RID）
dotnet publish SalmonEgg/SalmonEgg/SalmonEgg.csproj \
  --configuration Release \
  --framework net10.0-desktop \
  --runtime osx-arm64 \
  --self-contained false \
  --output publish/macos-arm64

dotnet publish SalmonEgg/SalmonEgg/SalmonEgg.csproj \
  --configuration Release \
  --framework net10.0-desktop \
  --runtime osx-x64 \
  --self-contained false \
  --output publish/macos-x64
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
> Workload manifest：CI 应与 `global.json` 中的 .NET SDK patch 保持一致；当前仓库锁定 10.0.302；CI 使用 10.0.x 可前滚到同代最新 patch。
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

Headless 环境没有 EWMH-compliant window manager 或 DBus desktop portal 时，Uno 可能输出窗口状态/主题监听警告；这类警告不等价于应用启动失败。需要验证本地交互终端时，还必须安装 WebKitGTK / JavaScriptCore 运行库。

#### Linux Desktop runtime gate
发布 Linux desktop 产物后，使用当前构建产物做运行时门禁：

```bash
./build.sh desktop
scripts/gates/verify-linux-desktop-runtime.sh publish/linux-desktop
```

该 gate 会检查 `publish/linux-desktop/SalmonEgg`、`xvfb-run`、WebKitGTK、JavaScriptCoreGTK，并在 Xvfb 下启动本次 publish 产物。缺少运行库、Skia/freetype native crash、`DllNotFoundException`、`EntryPointNotFoundException` 或未处理异常都会失败。纯 headless X11 缺少 EWMH window manager 时的窗口状态警告不作为应用启动失败处理。

### .NET 测试 runner

仓库通过 `global.json` 启用 Microsoft.Testing.Platform。测试命令必须显式使用 `--solution` 或 `--project`，不要使用旧的 `dotnet test SalmonEgg.sln` 位置参数，也不要使用 VSTest 的 `--filter "FullyQualifiedName~..."`、`--logger trx`、`--collect:XPlat Code Coverage` 或 `--blame-hang`。xUnit v3 / MTP 的常用替代为：

```bash
# 运行完整 solution
dotnet test --solution SalmonEgg.sln --configuration Release --timeout 20m --output Normal

# 按类过滤
dotnet test --project tests/SalmonEgg.Application.Tests/SalmonEgg.Application.Tests.csproj \
  --filter-class SalmonEgg.Application.Tests.UiConventionsTests \
  --timeout 3m \
  --output Normal

# 生成 TRX 与 Cobertura 覆盖率
dotnet test --solution SalmonEgg.sln \
  --configuration Release \
  --results-directory TestResults \
  --report-xunit-trx \
  --coverlet \
  --coverlet-output-format cobertura \
  --timeout 20m \
  --output Normal
```

`tests/SalmonEgg.Presentation.Core.Tests/testconfig.json` 是 Presentation.Core 测试在 MTP 下的并行度事实源；不要再通过 `RunConfiguration.DisableParallelization` 或 VSTest runsettings 参数补偿。

#### Skia Desktop GUI smoke gate
Skia Desktop 的跨平台 GUI smoke 使用真实 `net10.0-desktop` 构建产物。Linux 下通过 Xvfb 启动并用轻量 X11 probe 验证窗口已映射、像素非空、可成为 X input focus，且 XTest 键盘事件可投递到目标窗口；macOS 下需要当前会话具备可用 GUI。该 gate 使用 Debug 构建中的 `boot.log` readiness probe 验证：

1. XAML 主窗口已经完成 shell 初始内容激活；
2. 通过 portable AppData seed（`SalmonEgg.TestSupport.SkiaDesktopGuiSeedWriter`）恢复的混排 transcript（markdown + tool_call + mode_change + image）已被权威 projection 写入 `MessageHistory`。

种子只写真实生产文件（`conversations/conversations.v1.json` + `config/app.yaml`），不引入 AT-SPI 或 UI test hook；探针不进入 Release：

```bash
scripts/gates/run-skia-desktop-gui-smoke-gates.sh Debug
```

Linux Skia Desktop 当前使用 Uno X11 host。该 host-window smoke 不声明 AT-SPI、AutomationId 或控件语义树覆盖；本机 `dbus-run-session` + Xvfb + `org.a11y.Bus` 探测显示 SalmonEgg 进程未注册到 AT-SPI bus，强制 `GTK_MODULES=atk-bridge` 也不会产生语义 provider。若后续 Uno/Skia host 暴露稳定 AT-SPI provider，应新增独立 Linux semantic GUI gate；在此之前禁止用 X11 window 属性、截图内容或应用内 test hook 冒充语义自动化。

该 gate 与 Windows FlaUI / WASM Playwright gate 分工不同：

- Windows WinUI 3 / MSIX GUI 行为：`scripts/gates/run-gui-smoke-gates.ps1`，使用 FlaUI/UIA3；
- BrowserWasm GUI 行为：`scripts/gates/run-wasm-smoke-gates.sh Debug`，使用 Playwright/Chromium；
- Skia Desktop GUI readiness + seeded transcript projection：`scripts/gates/run-skia-desktop-gui-smoke-gates.sh Debug`，验证跨平台 desktop shell 在真实 GUI host 中到达主窗口 readiness，并投影混排 transcript；Linux 还验证 X11 窗口映射、非空像素、host-window focus 和 XTest 键盘输入边界。

#### Mobile target contract gate
移动端目标默认不进入常规构建，但 target graph 和平台安全存储源码必须保持可验证：

```bash
scripts/gates/verify-mobile-target-contracts.sh
```

该 gate 会验证默认构建不包含移动 TFM、Android/iOS opt-in TFM 展开符合 `SalmonEgg.csproj` 的单一事实源；当本机安装了 Android ref pack 时，还会对 `AndroidKeyStoreSecureStorage` 做 Android 引用级 C# 编译检查。完整 Android 打包仍以 x64 Linux/macOS/Windows Android toolchain 或 CI 为准；iOS 打包仍需要 macOS/Xcode。

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

该 gate 会构建当前 `net10.0-browserwasm` 产物、静态托管本次构建输出的 `wwwroot`，再用 Playwright/Chromium 执行 7 条 WASM 浏览器行为路径：

- 设置页顶部原生 `NavigationView` overflow 导航；
- Start 页推荐卡片可见性：确认推荐标题和说明文本进入可见 DOM，且辅助文本不会解析为透明；
- Diagnostics 页焦点边界：确认焦点不会落到隐藏、stale 或 body-only 状态；
- 设置持久化：通过 UI 切换应用语言并修改外观、数据与存储、快捷键、ACP 和 MCP 服务器状态，验证 shell 重载后的 `x:Uid` 与 singleton ViewModel 文案，并在刷新后从可见设置页确认状态仍存在；
- ACP / 平台能力边界：保存 ACP WebSocket profile，验证 WASM 不声明 `clientCapabilities.fs` / `terminal=true`，并确认受限平台不会暴露桌面文件系统入口；
- Gamepad 能力边界：确认 BrowserWasm 通过浏览器 Gamepad API 投影标准手柄读数，并验证 DPad / A 键经平台 bridge 进入 Uno 原生焦点与控件激活路径；
- WASM ACP 全链路：用同一 profile 和 remote directory 从 Start 页面创建远端会话，断言 mock ACP Server 收到 `initialize`、`session/new`（`cwd` 为所选 remote path）和 `session/prompt`，并确认 agent reply 投影到 Chat UI。

它补充 Windows self-hosted FlaUI gate，专门覆盖 WASM 浏览器里的原生 Uno 控件行为与当前构建产物的浏览器持久化链路。

#### Windows native gamepad bridge

Windows-only native gamepad validation uses `tests/SalmonEgg.GamepadBridge.Windows` with HIDMaestro. The bridge resolves `HIDMaestro.Core.dll` from `SALMONEGG_HIDMAESTRO_CORE_PATH` or from a DLL placed beside the bridge executable.

By default it creates the `xbox-360-wired` HIDMaestro profile. To validate another installed HIDMaestro controller profile, set `SALMONEGG_HIDMAESTRO_PROFILE_ID` before starting the bridge:

```powershell
$env:SALMONEGG_HIDMAESTRO_CORE_PATH = "C:\Path\To\HIDMaestro.Core.dll"
$env:SALMONEGG_HIDMAESTRO_PROFILE_ID = "xbox-360-wired"
dotnet run --project tests/SalmonEgg.GamepadBridge.Windows/SalmonEgg.GamepadBridge.Windows.csproj -- serve
```

The bridge protocol accepts `create`, `dispose`, and `press <input>`. Supported inputs are `dpad-up`, `dpad-down`, `dpad-left`, `dpad-right`, `a`, `b`, `x`, and `y`. Face-button validation must record which physical button each profile maps to these commands so Activate, Back, Voice Toggle, and the west-face no-op boundary are all covered.

Do not guess profile ids in automation. Confirm the installed HIDMaestro profile id first, then record the controller family, transport, profile id, and app diagnostics output in the validation notes.

Real-device gamepad validation must use the current MSIX install and the Diagnostics > Gamepad monitor. For every controller family and transport under test, capture:

- When `Input source` is `Gamepad`, the standard-details line that includes controller identity when available (`DisplayName`, `VID`, `PID`), resolved face `layout`, physical `labels` from `Gamepad.GetButtonLabel`, `pressed`, `semantic`, and `reading`.
- Confirm that standard-path face semantics follow physical labels (Xbox/PS glyphs or Nintendo `Letter*`), not a second brand-specific UI/shell path.
- The raw-details line that includes `VID`, `PID`, `layout`, `pressed`, `semantic`, and `reading` whenever raw controllers are present.
- Whether Windows exposes the device on the standard `Gamepad` path, the `RawGameController` path, or both; do not change path priority from diagnostics alone.

Minimum Windows validation matrix:

| Controller | Transport | Required diagnostics evidence |
| --- | --- | --- |
| Xbox controller | USB or official wireless path available to Windows | Standard `Gamepad` path if Windows exposes it, or `RawGameController` fallback with `layout Standard`; D-pad directions project to `Move*`; A projects to `Activate`; B projects to `Back`; Y projects to `ToggleVoiceInput`; X produces no app semantic action. |
| DualShock / DualSense | USB and Bluetooth when available | `RawGameController` or `Gamepad` path is acceptable only when diagnostics show PS labels or standard semantics; Cross projects to `Activate`; Circle projects to `Back`; Triangle projects to `ToggleVoiceInput`; Square produces no app semantic action. |
| Switch Pro / Joy-Con | USB and Bluetooth when available | Prefer recording both paths when dual-exposed. `RawGameController` details show Nintendo identity, `VID 057E` or Nintendo/Switch/Joy-Con display name, and `layout Nintendo`. On the standard `Gamepad` path, `labels` must show physical `Letter*` (or Nintendo glyphs) and semantic mapping must follow physical position: physical B/`LetterB` -> `Activate`, physical A/`LetterA` -> `Back`, physical X/`LetterX` -> `ToggleVoiceInput`, physical Y/`LetterY` -> no app action. |

When raw-only controllers report `pressed B0` / `B1` / `B2` / `B3` with no label suffix, known full Xbox (`045E`), Sony (`054C`), and Nintendo (`057E`) families—or matching display-name tokens such as `Xbox`, `DualSense`/`DualShock`, `PS5`/`PS4`, `Nintendo`, `Switch Pro`, and non-single Joy-Con pair/grip/dual names—may still project face semantics from those physical face indexes. Unlabeled digital trigger indexes `B6` / `B7` project to left/right trigger (`PageUp` / `PageDown`) on the same full-gamepad map. Diagnostics raw lines record `unlabeled-index-fallback on|off` for that gate. Single Joy-Con presentations (`Joy-Con (L/R)`, `JoyCon (L/R)`) are excluded because their HID index map is not the full-gamepad face/trigger layout; pair/grip/dual Joy-Con presentations remain eligible when identity otherwise matches. Prefer labeled evidence (`B0:Cross`, `B0:LetterB`, etc.) when Windows provides `GetButtonLabel` values.

For each run, also verify disconnect/reconnect updates counts, triggers project to `PageUp` / `PageDown` when exposed, thumbstick movement changes `reading X/Y` without stale values, and inactive controllers do not hide an active raw fallback behind an idle standard gamepad.

#### WebAssembly 持久化策略

Uno 官方 IDBFS 文档要求通过 `<WasmShellEnableIDBFS>true</WasmShellEnableIDBFS>` 显式启用浏览器 IndexedDB-backed 文件系统。本仓库在 `net10.0-browserwasm` 上启用该构建能力，用于 `/local/SalmonEgg` 下的应用数据。

当前已确认的 WASM 持久化范围：

- 应用设置；
- ACP profile YAML；
- 其它走应用文件存储抽象的普通配置；
- plaintext secure storage 中的 ACP token/API key 等配置相关凭据。

当前不应混淆的边界：

- 浏览器 IndexedDB-backed 文件系统负责 WASM 应用数据持久化，包括受限平台 fallback 使用的 plaintext secure storage；
- WASM 没有 OS-backed secure store，配置相关凭据会以普通应用文件形式持久化；
- 配置云同步包会包含 config 目录和已登记的 ACP token/API key 等凭据，`secrets.json` 为明文内容。
- 云同步当前支持 OneDrive、WebDAV 与 S3-compatible object storage，设置页一次只能连接一个 provider；WebDAV 的 URL/用户名和 S3 的 endpoint/bucket/region/object key 属于用户配置，WebDAV 密码与 S3 access key/secret key 走应用 secure storage。
- OneDrive 应用注册配置通过 GitHub Actions 构建阶段注入：设置 `SALMONEGG_ONEDRIVE_CLIENT_ID`、`SALMONEGG_ONEDRIVE_TENANT_ID`、`SALMONEGG_ONEDRIVE_REDIRECT_URI`、`SALMONEGG_ONEDRIVE_SCOPES` 为 repository secrets 或 variables；workflow 会优先读取 secrets，空值时读取 variables，并写入程序集元数据。

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

### 问题 6: `run.bat desktop` 提示 side-by-side 配置不正确

**现象**: Windows 上运行 `run.bat desktop` 时，构建完成后启动 `net10.0-desktop` 可执行文件失败，并提示“应用程序的并行配置不正确”或 `side-by-side configuration is incorrect`。

**原因**: `run.bat desktop` 走 Uno Skia Desktop 的 unpackaged 运行路径，不是 Windows 原生 WinUI 3 / MSIX 路径。该路径依赖本机 Visual C++ x64 运行时；缺失时 Windows 会在进程启动阶段直接报 side-by-side 错误。

**解决方案**:
1. Windows 原生开发优先运行默认 MSIX 路径：
   ```bash
   run.bat
   ```
2. 如确实需要 Skia Desktop 路径，先安装 Microsoft Visual C++ Redistributable 2015-2022 x64：
   ```text
   https://aka.ms/vs/17/release/vc_redist.x64.exe
   ```
3. 最新 `run.bat desktop` 会在 Windows 上提前检查该运行时，缺失时直接给出上述安装提示，避免构建后才出现系统级 side-by-side 崩溃。

## 构建输出

构建成功后，您会在以下目录找到输出：

- **Linux Desktop**: `publish/linux-desktop/SalmonEgg`
- **macOS Desktop**: `publish/macos-arm64/SalmonEgg` 或 `publish/macos-x64/SalmonEgg`
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
dotnet test --solution SalmonEgg.sln --configuration Release --timeout 20m --output Normal

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
