# SalmonEgg 发布指南

本指南说明如何为不同平台构建和发布 SalmonEgg 应用程序。

## 目录

1. [前置要求](#前置要求)
2. [Windows 发布](#windows-发布)
3. [WebAssembly 发布](#webassembly-发布)
4. [Android 发布](#android-发布)
5. [iOS 发布](#ios-发布)
6. [macOS 发布](#macos-发布)
7. [Linux 发布](#linux-发布)
8. [命令行工具（随主程序分发）](#命令行工具随主程序分发)
9. [持续集成发布](#持续集成发布)

---

## 前置要求

### 通用要求

- .NET 10.0 SDK 或更高版本
- Visual Studio 18.8+ 或 Visual Studio Code
- Git

### 平台特定要求

| 平台 | 额外要求 |
|------|---------|
| Windows | Windows 10 1809+ |
| Android | Android SDK, Java JDK 17+ |
| iOS | macOS、当前目标所需的 Xcode 与 iOS workload |
| macOS | macOS 12+、当前目标所需的 Xcode/.NET 工具链 |
| WebAssembly | 无额外要求 |

---

## Windows 发布

### MSIX（WinUI 3，推荐）

```bash
# 生成 MSIX（不安装）
build.bat msix
```

输出目录：`artifacts/msix/`

### 发布为独立应用（Skia Desktop）

```bash
cd SalmonEgg/SalmonEgg

# 发布为 Windows 独立应用（包含 .NET 运行时）
dotnet publish -f net10.0-desktop -c Release \
  --self-contained true \
  -r win-x64 \
  -o ../../publish/windows-x64

# 或使用依赖框架的发布（需要用户已安装 .NET）
dotnet publish -f net10.0-desktop -c Release \
  --self-contained false \
  -o ../../publish/windows-desktop
```

仓库当前的 Windows 发布事实源是 `.github/workflows/release-packaging.yml` 和 `.tools/run-winui3-msix.ps1`。不要引用仓库中不存在的 WiX、Inno Setup 或 publish profile。

### 验证发布

```bash
# 运行发布的应用
./publish/windows-x64/SalmonEgg.exe
```

---

## WebAssembly 发布

### 发布 WebAssembly 应用

```bash
cd SalmonEgg/SalmonEgg

# 发布为 WebAssembly（优化后）
dotnet publish -f net10.0-browserwasm -c Release \
  -o ../../publish/wasm

# Vercel 使用仓库脚本与固定输出目录
cd ../..
scripts/vercel-build.sh
```

### 部署到 Web 服务器

#### 部署到静态网站托管

```bash
# 复制到 Web 服务器
cp -r publish/wasm/wwwroot/* /var/www/salmonegg/
```

#### Nginx 配置示例

```nginx
server {
    listen 80;
    server_name salmonegg.example.com;
    root /var/www/salmonegg;
    index index.html;

    # 启用 gzip 压缩
    gzip on;
    gzip_types application/javascript application/wasm text/plain;

    # 处理 SPA 路由
    location / {
        try_files $uri $uri/ /index.html;
    }

    # 缓存策略
    location ~* \.(wasm|js|css|html)$ {
        expires 30d;
        add_header Cache-Control "public, immutable";
    }
}
```

### 验证发布

WASM 产物必须通过 HTTP 服务器验证，不能直接打开 `index.html`：

```bash
cd publish/wasm/wwwroot
python -m http.server 8080
```

---

## Android 发布

### 前置配置

1. 安装 Android SDK（通过 Visual Studio Installer 或 Android Studio）
2. 配置 Java JDK 17+
3. 设置签名密钥：

```bash
# 创建密钥库（首次发布）
keytool -genkeypair -v -keystore salmonegg.keystore -alias salmonegg -keyalg RSA -keysize 2048 -validity 10000
```

### 发布 APK

```bash
cd SalmonEgg/SalmonEgg

# 发布未签名的 APK
dotnet publish -f net10.0-android36.0 -c Release \
  -p:AndroidPackageFormat=apk \
  -o ../../publish/android

# 发布签名后的 APK
dotnet publish -f net10.0-android36.0 -c Release \
  -p:AndroidPackageFormat=apk \
  -p:AndroidKeyStore=true \
  -p:AndroidSigningKeyStore=salmonegg.keystore \
  -p:AndroidSigningKeyAlias=salmonegg \
  -p:AndroidSigningKeyPass=YOUR_KEY_PASS \
  -p:AndroidSigningStorePass=YOUR_STORE_PASS \
  -o ../../publish/android-signed
```

### 发布 AAB（Google Play 要求）

```bash
dotnet publish -f net10.0-android36.0 -c Release \
  -p:AndroidPackageFormat=aab \
  -p:AndroidKeyStore=true \
  -p:AndroidSigningKeyStore=salmonegg.keystore \
  -p:AndroidSigningKeyAlias=salmonegg \
  -o ../../publish/android-aab
```

### 验证发布

```bash
# 在模拟器上安装
adb install publish/android/SalmonEgg.apk

# 或在设备上调试
dotnet build -t:Run -f net10.0-android36.0 -c Release
```

---

## iOS 发布

### 前置配置

1. macOS 12+ 和 Xcode 15+
2. Apple Developer 账号
3. 配置代码签名证书和配置文件

### 创建 IPA

> 说明：`net10.0-ios` 需要 macOS/Xcode/iOS workload，不进入默认构建。需要 iOS 时通过 `EnableMobileTargets=true` + `EnableIosTarget=true` 启用，不手改 `TargetFrameworks`。

```bash
cd SalmonEgg/SalmonEgg

# 发布为 IPA
dotnet publish -f net10.0-ios -c Release \
  -p:EnableMobileTargets=true \
  -p:EnableIosTarget=true \
  -p:ArchiveOnBuild=true \
  -p:CreateIpa=true \
  -p:CodesignKey="iPhone Distribution: Your Company" \
  -p:IpaPackageName=SalmonEgg \
  -o ../../publish/ios
```

### 提交到 App Store Connect

使用当前 Xcode Organizer 或 App Store Connect 支持的 Transporter 流程上传并验证归档；不要再使用已淘汰的 `xcrun altool` 上传命令。

---

## macOS 发布

### 发布为独立应用

```bash
cd SalmonEgg/SalmonEgg

# 发布 macOS desktop 应用（Intel）
dotnet publish -f net10.0-desktop -c Release \
  --self-contained false \
  -r osx-x64 \
  -o ../../publish/macos-x64

# 发布 macOS desktop 应用（Apple Silicon）
dotnet publish -f net10.0-desktop -c Release \
  --self-contained false \
  -r osx-arm64 \
  -o ../../publish/macos-arm64
```

### 打包 `.app` / `.dmg` / `.pkg`

`.app` bundle 与 `.dmg` 由 Uno 的打包目标产出（`PackageFormat=app` / `dmg`），签名与公证参数来自 workflow 的 secrets。**bundle 必须携带 CLI**，因此 publish 时要传 `-p:SalmonEggBundledCliExecutable=<CLI 路径>`：

```bash
scripts/release/publish-cli-binary.sh --rid osx-arm64

cd SalmonEgg/SalmonEgg
dotnet publish -f net10.0-desktop -c Release -r osx-arm64 \
  -p:PackageFormat=app \
  -p:RuntimeIdentifiers=osx-arm64 \
  -p:SalmonEggBundledCliExecutable=../../artifacts/cli-bin/osx-arm64/salmon-egg
```

`.pkg` 不走 Uno：它的 `PackageAppBundle` 任务不接受 scripts 参数，而 `.pkg` 存在的唯一理由就是那个 postinstall——把 bundle 内的命令链接进 `/usr/local/bin`。所以由仓库脚本直接调 `pkgbuild`：

```bash
scripts/release/build-macos-pkg.sh \
  --app-bundle publish/macos-bundle/SalmonEgg.app \
  --signing-key "Developer ID Installer: ..."   # 可选，省略则产出未签名包
```

`--signing-key` 需要 **Developer ID Installer** 证书（与 app、dmg 的签名证书不是同一张）；workflow 通过 `MACOS_PKG_CODESIGN_KEY` 提供，缺失时仍产出未签名包。

命令在 bundle 内的位置由 Uno 决定：实测 v1.4.2 的已发布 bundle，`GenerateAppBundle` 把 apphost、`deps.json`/`runtimeconfig.json` 与全部 `.dylib` 放进 `Contents/MacOS`（19 个文件），托管程序集、卫星资源与 `Assets/` 子目录放进 `Contents/Resources`（589 个文件，保留相对路径）。`cli/` 子目录里一个无扩展名的 Mach-O 不精确匹配任何一侧，且该切分不是文档化契约，因此 postinstall、`build-macos-pkg.sh` 与产物契约门禁**都同时探测两个位置并报告命中的那个**——第一次 tag 构建的日志即可定论。

---

## Linux 发布

Linux 只有一种安装物：同时包含 GUI 与 `salmon-egg` 命令的 `.deb`。

```bash
scripts/release/publish-cli-binary.sh --rid linux-x64

dotnet publish SalmonEgg/SalmonEgg/SalmonEgg.csproj \
  -c Release -f net10.0-desktop -r linux-x64 --self-contained true \
  -p:SalmonEggBundledCliExecutable=$PWD/artifacts/cli-bin/linux-x64/salmon-egg \
  -o publish/linux-desktop

scripts/release/build-desktop-deb.sh --publish-dir publish/linux-desktop --architecture amd64

# 安装 / PATH / .desktop / 卸载全链门禁（需要 root 或免密 sudo）
scripts/gates/run-desktop-linux-package-smoke.sh artifacts/desktop/salmon-egg_<版本>_amd64.deb
```

包布局与理由：

| 路径 | 内容 | 说明 |
|---|---|---|
| `/opt/salmon-egg/` | self-contained publish 输出 | FHS 中 `/opt` 用于自带运行时的附加应用 |
| `/opt/salmon-egg/cli/salmon-egg` | CLI 二进制 | 由 publish 目录的 `cli/` 子目录带入 |
| `/usr/bin/salmon-egg` | 指向上一行的相对符号链接（`../../opt/...`） | `/usr/bin` 已在所有登录 PATH 上；链接由 dpkg 拥有，purge 时移除。少一级会解析成 `/usr/opt` 并悬空，而包的文件列表只显示链接文本、看不出这一点 |
| `/usr/share/applications/salmon-egg.desktop` | 桌面项 | `Exec` 指向 GUI，`MimeType` 注册与 Windows 侧一致的 `s8p` scheme |
| `/usr/share/icons/hicolor/{16,24,32,48,256}/apps/` | 应用图标 | 取自 Resizetizer 本次构建生成的标准尺寸；源图 200×200 不是 hicolor 尺寸，直接放会被二次缩放或忽略 |

依赖列表无法从构建推导：`dpkg-shlibdeps` 与 `readelf -d` 只看链接期 NEEDED，而 X11、GL、GLib、GStreamer、ICU 都是运行时 `dlopen` 的。当前列表来自实测——headless 跑起已 publish 的应用，从 `/proc/<pid>/maps` 读出真实映射的库。写成备选依赖（`libicu76 | libicu74 | ...`）而非钉死版本：Microsoft 自家 .NET deb 敢钉死是因为每个发行版单独构建，而这个包只发一份。

`.deb` 声明 `Conflicts`/`Replaces: salmon-egg-cli`，因为 `/usr/bin/salmon-egg` 现在归它所有。

> **已知缺口**：GUI 依赖会把 X11/GL/WebKit 拉进依赖图，因此无 GUI 的服务器无法只装命令。若要恢复该场景，需要把 deb 拆成 CLI 子包与依赖它的 GUI 主包。

---

## 命令行工具（随主程序分发）

`salmon-egg` 不再单独发布。**装了 SalmonEgg 就有这个命令**，四个安装包各用本平台的机制注册它：

| 安装包 | 注册机制 | 由谁验证 |
|---|---|---|
| Windows MSIX | 清单里的 `windows.appExecutionAlias`；Windows 在 `%LOCALAPPDATA%\Microsoft\WindowsApps` 生成入口（该目录默认在用户 PATH 上）。打包应用无法改 PATH，这是系统提供的唯一途径 | `run-msix-package-contract-gate.ps1` 读真实包 |
| Windows MSI（Skia Desktop） | WiX `Environment` 行把 `[CLIFOLDER]`（安装目录下的 `cli`）追加到用户 PATH，卸载时移除 | `DesktopMsiContract.ps1` 读真实包的 `File` 与 `Environment` 表 |
| Linux `.deb` | dpkg 拥有的 `/usr/bin/salmon-egg` 符号链接 | `run-desktop-linux-package-smoke.sh` 真装真卸 |
| macOS `.pkg` | postinstall 把命令链接进 `/usr/local/bin`（macOS 默认 PATH） | `run-macos-pkg-contract-gate.sh` 用假根跑真脚本 |

`.dmg` 里也带命令（在 `SalmonEgg.app` 内），但拖拽安装没有安装钩子，用户需自行链接或改用 `.pkg`。

### 支持矩阵

事实源是 `src/SalmonEgg.Cli/SalmonEgg.Cli.csproj` 的 `SalmonEggCliSupportedRuntimeIdentifiers`；`publish-cli-binary.sh` 从该属性读回，不另行维护列表。

| Runtime identifier | 消费它的安装包 |
|---|---|
| `win-x64` | MSIX、Skia Desktop MSI |
| `linux-x64` | `.deb` |
| `osx-arm64` | `.app`、`.dmg`、`.pkg` |

矩阵之外的 RID（`win-arm64`、`linux-arm64`、`osx-x64` 等）不属于正式支持：能交叉编译不等于有真实运行验证，而且没有任何安装包会嵌入它。`--allow-unsupported-rid` 仅供本机验证，产物不得发布。

产物为 self-contained 单文件，用户无需预装 .NET。

### 构建 CLI 二进制

```bash
# 各打包链都调这一个脚本，所以四个安装包里的命令是同一个二进制
scripts/release/publish-cli-binary.sh --rid linux-x64
# 输出：artifacts/cli-bin/<rid>/salmon-egg[.exe]
```

在 Windows runner 上该脚本跑在 Git Bash 里，因此除 POSIX 路径外还输出 `executable-path-native`（`cygpath -w` 转换）：MSBuild 与 WiX 都是原生进程，打不开 `/d/a/...` 形式的路径。

主程序侧通过 `-p:SalmonEggBundledCliExecutable=<路径>` 消费它（MSIX、macOS bundle、Linux deb 走这条），Windows Skia MSI 例外：它的 WiX 作者需要显式命名命令所在目录，而 heat harvest 生成的目录 ID 不稳定，所以 CLI 由 `Product.wxs` 直接声明、且发布目录里**不得**出现 `cli/`。

### 发布前门禁

```bash
# 真实产物行为门禁（退出码契约、凭据边界、事务残留、无需 .NET 运行时）
scripts/gates/run-cli-release-artifact-smoke.sh artifacts/cli-bin/linux-x64/salmon-egg

# Linux 安装 / PATH / .desktop / 卸载全链门禁（需要 root 或免密 sudo）
scripts/gates/run-desktop-linux-package-smoke.sh artifacts/desktop/salmon-egg_<版本>_amd64.deb

# macOS 安装脚本的 PATH 注册门禁（用假根跑真 postinstall，任意平台可跑）
scripts/gates/run-macos-pkg-contract-gate.sh

# MSI PATH 注册规则的正反例门禁（纯字符串逻辑，任意平台可跑，无需 WiX）
pwsh -NoProfile -File scripts/gates/run-msi-path-contract-gate.ps1

# MSIX 包契约（含 alias 与 CLI payload）的自检
pwsh -NoProfile -File scripts/gates/run-msix-package-contract-gate.ps1 -SelfTest
```

前两个门禁必须使用**本次构建产出**的产物；`dotnet run` 不是有效验证口径。后三个验证的是规则本身而非产物，因此不受此限制，也因此能在每次 push 上跑。

### PATH 与凭据约定

- **GUI 安装包负责注册 PATH。** 这与 v1.4.x 之前相反：那时全局命令只来自独立 CLI 安装包，GUI 安装包不碰 PATH。
- 不允许用脚本编辑 `.bashrc`、`.zshrc` 或用户 PATH 字符串：安装、升级、卸载必须由同一个安装器/包管理器拥有。macOS 是唯一例外——它的包格式没有卸载阶段，`/usr/local/bin/salmon-egg` 需要用户手工 `rm`。
- CLI 凭据写入默认 fail-closed。平台安全存储不可用时写入失败而非降级为明文；需要明文降级必须显式传 `--allow-insecure-storage`。非凭据配置操作不受影响。该策略在 Linux（Secret Service）与 macOS（Keychain）上生效；Windows DPAPI 不依赖 keyring 守护进程、始终可用，因此该 flag 在 Windows 上无实际作用。
- Windows MSI 的 PATH 注册由 `DesktopMsiContract.ps1` 读取本次构建产出的 MSI 的 `Environment` 表验证：表中必须恰好一行，且该行必须满足下表全部断言。

  | 断言 | 违规后果 |
  |---|---|
  | 变量为 `PATH` | 命令不会变得可发现 |
  | 前缀含 `=`、不含 `!` | 安装时不写入，或安装时就把变量删掉 |
  | 前缀含 `-` | 卸载后安装目录永久留在 PATH 上 |
  | 前缀不含 `*` | 写的是机器环境而非当前用户 |
  | 值以 `[~]` + 分隔符开头 | 缺 `[~]` 会整体覆盖 PATH（MSI 文档明确警告可能导致机器无法启动）；`[~]` 出现在结尾则是前置插入，会遮蔽用户原有工具 |
  | 值引用 `[CLIFOLDER]` | 加进 PATH 的不是命令所在目录（若写成 `[INSTALLFOLDER]`，等于把应用旁边所有 DLL 一起暴露，且命令仍然不可解析） |

  规则本身在 `scripts/release/MsiPathContract.ps1`，与读取 MSI 的 COM 代码分离，目录 token 是参数，因此 `scripts/gates/run-msi-path-contract-gate.ps1` 能在任意平台（含 Linux）直接用正反例跑这条规则；该门禁在 `ci-core.yml` 的每次 push / PR 上执行，不必等到打 tag 才发现规则被削弱。
- 上述断言只覆盖包内表结构。真实安装 / 卸载需要交互式会话，不在 CI 覆盖范围。发布前仍需手工确认：
  - **Windows MSIX**：安装后新开终端 `where salmon-egg` 命中 `WindowsApps` 下的 alias，且命令继承控制台、退出码正确；卸载后 alias 消失。
  - **Windows MSI**：安装后新开终端 `where salmon-egg` 命中 `...\SalmonEgg\cli`；卸载后命令消失、用户原有 PATH 条目完好无残留重复项。
  - **macOS**：`.pkg` 安装后新登录 shell 里 `which salmon-egg` 命中 `/usr/local/bin`；且带 CLI 的 bundle 能通过公证（Mach-O 落在 Uno 选定的目录里，是否被签名覆盖需实测）。

---

## 持续集成发布

### GitHub Actions 配置

项目已配置自动发布流程。当创建 Git tag 时（格式：`v*`），会自动构建并发布到 GitHub Releases。

```bash
# 创建版本标签
git tag v1.0.0
git push origin v1.0.0
```

发布自动化由 `.github/workflows/release-packaging.yml` 维护。不要在文档中维护另一套 Azure DevOps 示例，以免包格式、签名和产物名称偏离实际 workflow。

---

## 发布清单

发布前检查清单：

- [ ] 所有测试通过
- [ ] 四个安装包都嵌入了本次构建的 CLI（MSIX、Skia MSI、`.deb`、`.app`/`.pkg`）
- [ ] CLI 真实产物 smoke 通过；Linux `.deb` 的安装 / PATH / 卸载门禁通过
- [ ] 手工确认 Windows 与 macOS 安装后 `salmon-egg` 可解析（见「PATH 与凭据约定」末尾清单）
- [ ] 确认 GitHub 自动生成的 release notes 或维护中的变更记录已覆盖本版本
- [ ] 更新 README.md（如需要）
- [ ] 构建发布版本
- [ ] 在测试环境验证
- [ ] 创建 Git tag 并推送到远程仓库（版本号由 MinVer 从 tag 推导，无需改任何文件）
- [ ] 验证 CI/CD 流程
- [ ] 检查发布产物

---

## 故障排除

### 常见问题

#### Android 发布失败

```bash
# 问题：找不到 Android SDK
# 解决：设置 ANDROID_HOME 环境变量
export ANDROID_HOME=/path/to/android/sdk

# 问题：签名失败
# 解决：检查密钥库路径和密码是否正确
```

#### iOS 发布失败

```bash
# 问题：代码签名错误
# 解决：在 Xcode 中验证签名证书和配置文件

# 问题：设备不兼容
# 解决：检查 Deployment Target 设置
```

#### WebAssembly 加载慢

```bash
# 使用当前仓库发布命令重新生成产物，并通过真实 HTTP host 验证
dotnet publish SalmonEgg/SalmonEgg/SalmonEgg.csproj -f net10.0-browserwasm -c Release -o publish/wasm
```

### 获取帮助

- [Uno Platform 文档](https://platform.uno/docs/)
- [.NET 发布指南](https://docs.microsoft.com/dotnet/core/deploying/)
- [GitHub Issues](https://github.com/salmonloop/salmon-egg/issues)

---

## ACP SDK（`SalmonEgg.Acp`）发布

SDK 与应用是两条独立的发布线，各有自己的 tag 命名空间与版本号：

| | tag | 版本来源 | workflow |
|---|---|---|---|
| GUI / CLI | `v*` | git tag（MinVer 从 tag 推导，根 `Directory.Build.props` 派生 `SalmonEggDisplayVersion`） | `release-packaging.yml` |
| ACP SDK | `acp-sdk-v*` | git tag（MinVer 以 `MinVerTagPrefix=acp-sdk-v` 推导） | `release-acp-sdk.yml` |

两者必须保持分离：共用 `v*` 会让每次 SDK 发布都重建桌面安装包，也会让应用发版误触发 NuGet 推送。

### 一次性准备（受信任发布 / Trusted Publishing）

通道不存长期 API key。`publish-nuget` job 用 GitHub OIDC 向 nuget.org 换一枚有效期 1 小时的临时 key，用完即弃。

1. 登录 nuget.org → 点用户名 → **Trusted Publishing** → 新建策略：

    | 字段 | 值 |
    |---|---|
    | Package owner | `Salmon` |
    | Scopes | Push new packages and package versions |
    | Glob Patterns and Packages | `SalmonEgg.Acp` |
    | Repository Owner | `salmonloop` |
    | Repository | `salmon-egg` |
    | Workflow File | `release-acp-sdk.yml` |
    | Environment | `nuget-publish` |

    Workflow File 只填文件名，**不要带** `.github/workflows/` 路径。Environment 必须与 workflow 里的 `environment:` 完全一致，否则令牌交换会被拒。Scopes 必须包含 "new packages"：首发时包还不存在，只给 "new versions" 会推不上去。

    填表时请关闭浏览器页面翻译。`salmon-egg` 被译成中文后写入策略，与 GitHub 令牌里的原始仓库名不匹配，交换同样会失败。

2. 策略的 **Package owner** 决定它归谁名下，并对该 owner 名下**所有**包生效，因此不要求 `SalmonEgg.Acp` 事先存在。选个人比选组织稳：策略归组织名下时，创建者若离开该组织，策略会失效。

3. 创建名为 `nuget-publish` 的 GitHub Environment；`publish-nuget` job 绑定该环境，可在此挂必要的审批人。策略若限定了 environment，该环境就是鉴权边界的一部分，不是装饰。

4. 在 `Settings → Secrets and variables → Actions → Variables` 添加变量 `NUGET_USER`，值为 nuget.org 的**用户名（profile name），不是邮箱**。它不是机密，但也不该硬编码进 workflow：fork 会连同用户名一起继承。

新建策略会先带一个 7 天临时有效期，即使仓库是公开的也会看到。nuget.org 需要 GitHub 仓库与所有者的**数字 ID** 才能把策略永久锁定到本仓库（防止有人删库、重建同名仓库后冒名发布），而这些 ID 只随一次成功发布的 OIDC 令牌送达。7 天内未发布则转为 inactive，可随时重启该窗口。

OIDC 交换未产出 key 时 `publish-nuget` 会显式失败而非静默跳过 —— 一次"绿灯但什么都没发布"的运行比失败更难发现。

### 发布步骤

```bash
# 1. 本地跑完整门禁 + 真实包消费验证（SDK 版本由 MinVer 从 tag 历史推导；
#    非首发版本必须同时提供上一个已发布版本作为兼容性基线）
ACP_PACKAGE_BASELINE_VERSION=1.0.0 ./scripts/gates/run-acp-sdk-gates.sh Release artifacts/acp-sdk-pack
./scripts/gates/run-acp-sdk-tag-version-gate-selftest.sh
./scripts/gates/run-acp-sdk-tag-version-gate.sh acp-sdk-v1.0.1 artifacts/acp-sdk-pack
./scripts/gates/run-acp-consumer-package-smoke.sh artifacts/acp-sdk-pack Release

# 2. 打 tag 推送，workflow 接管 pack -> consumer smoke -> nuget push；
#    版本号即 tag 上的数字，MinVer 负责把同样的版本打进包里
git tag acp-sdk-v1.0.1
git push origin acp-sdk-v1.0.1
```

### 兼容性基线

`EnablePackageValidation` 需要一个已发布版本作为对比基线才有意义。首发版本（`AcpPackageFirstReleasedVersion`，当前 `1.0.0`）在 nuget.org 上没有前身，写死基线会导致 restore 报 `NU1101`，因此该版本不启用基线比对。

版本一旦超过首发版本，pack 就要求显式传入基线，否则 `AcpBaselineRequired` 直接报错（普通开发构建与测试不受影响，报错只在产出包的 pack 管线触发）：

```bash
dotnet pack src/SalmonEgg.Acp/SalmonEgg.Acp.csproj -c Release -p:AcpPackageBaselineVersion=1.0.0
```

CI 侧由仓库变量 `ACP_PACKAGE_BASELINE_VERSION` 提供基线：发下一个 SDK 版本前，把它设为当前已发布的版本号。`run-acp-sdk-gates.sh` 会在带该属性的 restore 中预先下载基线包，pack 时 ApiCompat 才能找到它做比对。

这条门禁是 fail-closed 的：忘记传基线会构建失败，而不是让包验证退化成"什么都不比"的假绿。

### 不可逆性

nuget.org 的推送无法撤回，也无法覆盖同版本号。因此：

- `run-acp-sdk-tag-version-gate.sh` 在推送前校验 tag 版本与产物版本一致、`.nupkg` 与 `.snupkg` 各恰好一个；
- `concurrency` 不取消进行中的发布，避免出现"包已推、符号未推"的半成品；
- `.snupkg` 与 `.nupkg` 分别显式推送，不用通配符（glob 会连带匹配 `.snupkg`）。

### SDK 发布清单

- [ ] 版本号由 `acp-sdk-v*` tag 决定，无需改文件；非首发版本时已设置仓库变量 `ACP_PACKAGE_BASELINE_VERSION`
- [ ] `run-acp-sdk-gates.sh` 全绿（format / analyzer / 契约测试 / pack）
- [ ] `run-acp-sdk-tag-version-gate-selftest.sh` 通过（门禁规则本身的正反例）
- [ ] `run-acp-sdk-tag-version-gate.sh` 通过
- [ ] `run-acp-consumer-package-smoke.sh` 通过（真实 nupkg restore + build + run）
- [ ] tag 使用 `acp-sdk-v` 前缀（包版本即 tag 上的数字，由 MinVer 推导）
- [ ] nuget.org 受信任发布策略、`NUGET_USER` 变量与 `nuget-publish` environment 均已就绪
- [ ] 发布后在 nuget.org 确认包与符号均已上架
