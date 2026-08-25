# SalmonEgg 发布指南

本指南说明如何为不同平台构建和发布 SalmonEgg 应用程序。

## 目录

1. [前置要求](#前置要求)
2. [Windows 发布](#windows-发布)
3. [WebAssembly 发布](#webassembly-发布)
4. [Android 发布](#android-发布)
5. [iOS 发布](#ios-发布)
6. [macOS 发布](#macos-发布)
7. [CLI 发布](#cli-发布)
8. [持续集成发布](#持续集成发布)

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

### 创建 DMG 安装包

当前工程的 macOS desktop 产物来自 Uno `net10.0-desktop`，publish 输出为可执行文件目录；如需 `.app`/DMG，需要先在 macOS 上完成 `.app` bundle、签名和公证流程，再打包 DMG。

```bash
# 使用 create-dmg（需要安装）
cd publish/macos-x64
create-dmg \
  --volname "SalmonEgg" \
  --window-pos 200,120 \
  --window-size 600,400 \
  --icon-size 100 \
  --app-drop-link 400,200 \
  "SalmonEgg.dmg" \
  "SalmonEgg.app"
```

---

## CLI 发布

### 正式支持矩阵

CLI 的支持矩阵事实源是 `src/SalmonEgg.Cli/SalmonEgg.Cli.csproj` 中的 `SalmonEggCliSupportedRuntimeIdentifiers`；发布脚本与工作流都从该属性读取，不另行维护列表。

| Runtime identifier | 平台 | 安装包 | PATH 注册方式 |
|---|---|---|---|
| `linux-x64` | Linux x64 | `tar.gz` + `.deb` | `.deb` 安装到 `/usr/bin/salmon-egg`，由 dpkg 拥有 |
| `win-x64` | Windows x64 | `zip` + per-user `.msi` | MSI 的 `Environment` 元素追加安装目录到**用户** PATH，卸载时自动移除 |
| `osx-arm64` | macOS Apple Silicon | `tar.gz` + Homebrew formula | `brew install` 链接到 Homebrew `bin`，已在 PATH 上 |

矩阵之外的 RID（`win-arm64`、`linux-arm64`、`osx-x64` 等）不进入正式支持：能交叉编译不等于有真实运行验证。`--allow-unsupported-rid` 仅供本机验证，产物不得发布。

产物为 self-contained 单文件，用户无需预装 .NET；`scripts/gates/run-cli-release-artifact-smoke.sh` 会在移除可用 .NET 的环境下运行刚构建出的可执行文件来验证这一点。

### 构建 CLI 产物

```bash
# 单个 RID：发布、打包、生成 SHA-256
scripts/release/build-cli-artifacts.sh --rid linux-x64

# Linux 安装包
scripts/release/build-cli-deb.sh \
  --executable artifacts/cli-publish/linux-x64/salmon-egg \
  --architecture amd64

# Windows 安装包（需要 WiX Toolset）
./scripts/release/build-cli-msi.ps1 -Executable artifacts/cli-publish/win-x64/salmon-egg.exe
```

产物命名：

```text
salmon-egg-cli-1.0.5-win-x64.zip
salmon-egg-cli-1.0.5-linux-x64.tar.gz
salmon-egg-cli-1.0.5-osx-arm64.tar.gz
salmon-egg-cli_1.0.5_amd64.deb
salmon-egg-cli-1.0.5-win-x64.msi
```

Homebrew formula 在 release 聚合阶段由 `scripts/release/build-cli-homebrew-formula.sh` 依据本次产物的 `.sha256` 旁文件生成，不手工维护校验和。

### 发布前门禁

```bash
# 真实产物行为门禁（退出码契约、凭据边界、事务残留、无需 .NET 运行时）
scripts/gates/run-cli-release-artifact-smoke.sh artifacts/cli-publish/linux-x64/salmon-egg

# 安装 / PATH / 卸载门禁（需要 root 或免密 sudo）
scripts/gates/run-cli-linux-package-smoke.sh artifacts/cli/salmon-egg-cli-1.0.5_amd64.deb

# Windows MSI PATH 注册规则的正反例门禁（纯字符串逻辑，任意平台可跑，无需 WiX）
pwsh -NoProfile -File scripts/gates/run-cli-msi-path-contract-gate.ps1
```

前两个门禁必须使用**本次构建产出**的产物；`dotnet run` 不是有效验证口径。第三个门禁验证的是规则本身而非产物，因此不受此限制。

### PATH 与凭据约定

- GUI 安装包不修改 PATH。全局 `salmon-egg` 命令只来自 CLI 安装包。
- 不允许用脚本编辑 `.bashrc`、`.zshrc` 或用户 PATH 字符串：安装、升级、卸载必须由同一个包管理器拥有。
- CLI 凭据写入默认 fail-closed。平台安全存储不可用时写入失败而非降级为明文；需要明文降级必须显式传 `--allow-insecure-storage`。非凭据配置操作不受影响。该策略在 Linux（Secret Service）与 macOS（Keychain）上生效；Windows DPAPI 不依赖 keyring 守护进程、始终可用，因此该 flag 在 Windows 上无实际作用。
- Windows MSI 的 PATH 注册由 `build-cli-msi.ps1` 直接读取本次构建产出的 MSI 的 `Environment` 表验证：表中必须恰好一行，且该行必须满足下表全部断言。

  | 断言 | 违规后果 |
  |---|---|
  | 变量为 `PATH` | 命令不会变得可发现 |
  | 前缀含 `=`、不含 `!` | 安装时不写入，或安装时就把变量删掉 |
  | 前缀含 `-` | 卸载后安装目录永久留在 PATH 上 |
  | 前缀不含 `*` | 写的是机器环境而非当前用户 |
  | 值以 `[~]` + 分隔符开头 | 缺 `[~]` 会整体覆盖 PATH（MSI 文档明确警告可能导致机器无法启动）；`[~]` 出现在结尾则是前置插入，会遮蔽用户原有工具 |
  | 值引用 `[INSTALLFOLDER]` | 加进 PATH 的不是本包的安装目录 |

  规则本身在 `scripts/release/CliMsiPathContract.ps1`，与读取 MSI 的 COM 代码分离，因此 `scripts/gates/run-cli-msi-path-contract-gate.ps1` 能在任意平台（含 Linux）直接用正反例跑这条规则；该门禁在 `ci-core.yml` 的每次 push / PR 上执行，不必等到打 tag 才发现规则被削弱。
- 上述断言只覆盖包内表结构。真实安装 / 卸载需要交互式 Windows 会话，不在 CI 覆盖范围：发布 Windows 安装包前仍需手工确认安装后新开终端 `where salmon-egg` 命中安装目录、卸载后命令消失、且用户原有 PATH 条目完好无残留重复项。

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
- [ ] 版本号已更新（只改根 `Directory.Build.props` 的 `SalmonEggDisplayVersion`）
- [ ] CLI 三个支持 RID 的产物均已构建
- [ ] CLI 真实产物 smoke 与安装 / PATH 门禁通过
- [ ] 确认 GitHub 自动生成的 release notes 或维护中的变更记录已覆盖本版本
- [ ] 更新 README.md（如需要）
- [ ] 构建发布版本
- [ ] 在测试环境验证
- [ ] 创建 Git tag
- [ ] 推送到远程仓库
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
| GUI / CLI | `v*` | 根 `Directory.Build.props` 的 `SalmonEggDisplayVersion` | `release-packaging.yml` |
| ACP SDK | `acp-sdk-v*` | `src/SalmonEgg.Acp/SalmonEgg.Acp.csproj` 的 `<Version>` | `release-acp-sdk.yml` |

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
# 1. 更新 SDK 版本（首发之后必须同时提供上一个已发布版本作为兼容性基线）
#    src/SalmonEgg.Acp/SalmonEgg.Acp.csproj: <Version>1.0.1</Version>

# 2. 本地跑完整门禁 + 真实包消费验证
./scripts/gates/run-acp-sdk-gates.sh Release artifacts/acp-sdk-pack
./scripts/gates/run-acp-sdk-tag-version-gate-selftest.sh
./scripts/gates/run-acp-sdk-tag-version-gate.sh acp-sdk-v1.0.1 artifacts/acp-sdk-pack
./scripts/gates/run-acp-consumer-package-smoke.sh artifacts/acp-sdk-pack Release

# 3. 打 tag 推送，workflow 接管 pack -> consumer smoke -> nuget push
git tag acp-sdk-v1.0.1
git push origin acp-sdk-v1.0.1
```

### 兼容性基线

`EnablePackageValidation` 需要一个已发布版本作为对比基线才有意义。首发版本（`AcpPackageFirstReleasedVersion`，当前 `1.0.0`）在 nuget.org 上没有前身，写死基线会导致 restore 报 `NU1101`，因此该版本不启用基线比对。

`<Version>` 一旦超过首发版本，构建就要求显式传入基线，否则 `AcpBaselineRequired` 直接报错：

```bash
dotnet pack src/SalmonEgg.Acp/SalmonEgg.Acp.csproj -c Release -p:AcpPackageBaselineVersion=1.0.0
```

这条门禁是 fail-closed 的：忘记传基线会构建失败，而不是让包验证退化成"什么都不比"的假绿。

### 不可逆性

nuget.org 的推送无法撤回，也无法覆盖同版本号。因此：

- `run-acp-sdk-tag-version-gate.sh` 在推送前校验 tag 版本与产物版本一致、`.nupkg` 与 `.snupkg` 各恰好一个；
- `concurrency` 不取消进行中的发布，避免出现"包已推、符号未推"的半成品；
- `.snupkg` 与 `.nupkg` 分别显式推送，不用通配符（glob 会连带匹配 `.snupkg`）。

### SDK 发布清单

- [ ] `<Version>` 已更新，且非首发版本时已确定 `AcpPackageBaselineVersion`
- [ ] `run-acp-sdk-gates.sh` 全绿（format / analyzer / 契约测试 / pack）
- [ ] `run-acp-sdk-tag-version-gate-selftest.sh` 通过（门禁规则本身的正反例）
- [ ] `run-acp-sdk-tag-version-gate.sh` 通过
- [ ] `run-acp-consumer-package-smoke.sh` 通过（真实 nupkg restore + build + run）
- [ ] tag 使用 `acp-sdk-v` 前缀，且与 `<Version>` 一致
- [ ] nuget.org 受信任发布策略、`NUGET_USER` 变量与 `nuget-publish` environment 均已就绪
- [ ] 发布后在 nuget.org 确认包与符号均已上架
