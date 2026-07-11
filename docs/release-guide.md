# SalmonEgg 发布指南

本指南说明如何为不同平台构建和发布 SalmonEgg 应用程序。

## 目录

1. [前置要求](#前置要求)
2. [Windows 发布](#windows-发布)
3. [WebAssembly 发布](#webassembly-发布)
4. [Android 发布](#android-发布)
5. [iOS 发布](#ios-发布)
6. [macOS 发布](#macos-发布)
7. [持续集成发布](#持续集成发布)

---

## 前置要求

### 通用要求

- .NET 10.0 SDK 或更高版本
- Visual Studio 2022 (17.12+) 或 Visual Studio Code
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
- [ ] 版本号已更新
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
