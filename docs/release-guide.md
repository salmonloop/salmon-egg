# Uno ACP Client 发布指南

本指南说明如何为不同平台构建和发布 Uno ACP Client 应用程序。

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

- .NET 9.0 SDK 或更高版本
- Visual Studio 2022 (17.12+) 或 Visual Studio Code
- Git

### 平台特定要求

| 平台 | 额外要求 |
|------|---------|
| Windows | Windows 10 1809+ |
| Android | Android SDK, Java JDK 17+ |
| iOS | macOS, Xcode 15+, Visual Studio for Mac |
| macOS | macOS 12+, Xcode 15+ |
| WebAssembly | 无额外要求 |

---

## Windows 发布

### 发布为独立应用

```bash
cd UnoAcpClient/UnoAcpClient

# 发布为 Windows 独立应用（包含 .NET 运行时）
dotnet publish -f net9.0-windows10.0.19041.0 -c Release \
  --self-contained true \
  -r win-x64 \
  -o ../../publish/windows-x64

# 或使用依赖框架的发布（需要用户已安装 .NET）
dotnet publish -f net10.0-windows10.0.19041.0 -c Release \
  --self-contained false \
  -o ../../publish/windows-desktop
```

### 创建安装包（可选）

使用 WiX Toolset 或 Inno Setup 创建安装程序：

```bash
# 使用 WiX Toolset（需要 .wixproj）
dotnet publish -c Release -p:PublishProfile=Properties/PublishProfiles/WinExe.pubxml

# 或使用 Inno Setup
iscc windows-installer.iss
```

### 验证发布

```bash
# 运行发布的应用
./publish/windows-x64/UnoAcpClient.exe
```

---

## WebAssembly 发布

### 发布 WebAssembly 应用

```bash
cd UnoAcpClient/UnoAcpClient

# 发布为 WebAssembly（优化后）
dotnet publish -f net9.0-browserwasm -c Release \
  --no-build \
  -o ../../publish/wasm

# 或使用 AOT 编译（更快但包更大）
dotnet publish -f net9.0-browserwasm -c Release \
  -p:PublishTrimmed=true \
  -p:TrimMode=link \
  -o ../../publish/wasm-aot
```

### 部署到 Web 服务器

#### 部署到静态网站托管

```bash
# 复制到 Web 服务器
cp -r publish/wasm/wwwroot/* /var/www/unacpclient/

# 或使用 Azure Static Web Apps
az staticwebapp create \
  --name unacpclient \
  --source publish/wasm/wwwroot \
  --branch main
```

#### Nginx 配置示例

```nginx
server {
    listen 80;
    server_name unacpclient.example.com;
    root /var/www/unacpclient;
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

在浏览器中打开 `publish/wasm/wwwroot/index.html` 或使用本地服务器：

```bash
# 使用 .NET HTTP 服务器
cd publish/wasm/wwwroot
dotnet run --project ../../../UnoAcpClient/UnoAcpClient/UnoAcpClient.csproj \
  -f net9.0-browserwasm

# 或使用任意 HTTP 服务器
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
keytool -genkey -v -keystore uno-acp-client.keystore -alias uno-acp -keyalg RSA -keysize 2048 -validity 10000
```

### 发布 APK

```bash
cd UnoAcpClient/UnoAcpClient

# 发布未签名的 APK
dotnet publish -f net9.0-android -c Release \
  -p:AndroidPackageFormat=apk \
  -o ../../publish/android

# 发布签名后的 APK
dotnet publish -f net9.0-android -c Release \
  -p:AndroidPackageFormat=apk \
  -p:AndroidKeyStore=true \
  -p:AndroidSigningKeyStore=uno-acp-client.keystore \
  -p:AndroidSigningKeyAlias=uno-acp \
  -p:AndroidSigningKeyPass=YOUR_KEY_PASS \
  -p:AndroidSigningStorePass=YOUR_STORE_PASS \
  -o ../../publish/android-signed
```

### 发布 AAB（Google Play 要求）

```bash
dotnet publish -f net9.0-android -c Release \
  -p:AndroidPackageFormat=aab \
  -p:AndroidKeyStore=true \
  -p:AndroidSigningKeyStore=uno-acp-client.keystore \
  -p:AndroidSigningKeyAlias=uno-acp \
  -o ../../publish/android-aab
```

### 验证发布

```bash
# 在模拟器上安装
adb install publish/android/UnoAcpClient.apk

# 或在设备上调试
dotnet build -t:Run -f net9.0-android -c Release
```

---

## iOS 发布

### 前置配置

1. macOS 12+ 和 Xcode 15+
2. Apple Developer 账号
3. 配置代码签名证书和配置文件

### 创建 IPA

```bash
cd UnoAcpClient/UnoAcpClient

# 发布为 IPA
dotnet publish -f net9.0-ios -c Release \
  -p:ArchiveOnBuild=true \
  -p:CreateIpa=true \
  -p:CodesignKey="iPhone Distribution: Your Company" \
  -p:IpaPackageName=UnoAcpClient \
  -o ../../publish/ios
```

### 提交到 App Store Connect

```bash
# 使用 Xcode 上传
cd publish/ios
xcrun altool --upload-app --type ios -f UnoAcpClient.ipa \
  -u your.apple.id@apple.com -p your-app-specific-password
```

---

## macOS 发布

### 发布为独立应用

```bash
cd UnoAcpClient/UnoAcpClient

# 发布 macOS 应用
dotnet publish -f net9.0-maccatalyst -c Release \
  --self-contained true \
  -r osx-x64 \
  -o ../../publish/macos-x64

# 或 Apple Silicon (M1/M2)
dotnet publish -f net9.0-maccatalyst -c Release \
  --self-contained true \
  -r osx-arm64 \
  -o ../../publish/macos-arm64
```

### 创建 DMG 安装包

```bash
# 使用 create-dmg（需要安装）
cd publish/macos-x64
create-dmg \
  --volname "Uno ACP Client" \
  --window-pos 200,120 \
  --window-size 600,400 \
  --icon-size 100 \
  --app-drop-link 400,200 \
  "UnoAcpClient.dmg" \
  "UnoAcpClient.app"
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

### Azure DevOps 配置

```yaml
# azure-pipelines.yml
trigger:
  branches:
    include: [main, develop]
  tags:
    include: ['v*']

pool:
  vmImage: 'windows-latest'

steps:
- task: UseDotNet@2
  inputs:
    packageType: 'sdk'
    version: '9.0.x'

- script: dotnet build -c Release
  displayName: 'Build'

- script: dotnet test -c Release --no-build
  displayName: 'Test'

- script: |
    cd UnoAcpClient/UnoAcpClient
    dotnet publish -f net9.0-browserwasm -c Release -o ../../publish/wasm
  displayName: 'Publish WebAssembly'

- task: PublishBuildArtifacts@1
  inputs:
    PathtoPublish: 'publish/wasm'
    ArtifactName: 'wasm-drop'
```

---

## 发布清单

发布前检查清单：

- [ ] 所有测试通过
- [ ] 版本号已更新
- [ ] 更新 CHANGELOG.md
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
# 启用 AOT 编译
dotnet publish -p:PublishTrimmed=true -p:TrimMode=link

# 优化资源加载
# 使用 gzip 压缩
# 启用浏览器缓存
```

### 获取帮助

- [Uno Platform 文档](https://platform.uno/docs/)
- [.NET 发布指南](https://docs.microsoft.com/dotnet/core/deploying/)
- [GitHub Issues](https://github.com/your-org/UnoAcpClient/issues)
