# 安全存储实现

本目录包含跨平台安全存储的接口和实现。

## 接口

### ISecureStorage

定义了安全存储的核心接口：

- `SaveAsync(string key, string value)` — 安全保存数据
- `LoadAsync(string key)` — 加载安全存储的数据，不存在返回 null
- `DeleteAsync(string key)` — 删除安全存储的数据

## 实现

### WindowsDpapiSecureStorage（Windows 平台）

- **文件**: `SalmonEgg/Platforms/Windows/WindowsDpapiSecureStorage.cs`
- **平台**: Windows
- **存储位置**: `%LOCALAPPDATA%\SalmonEgg\SecureStorage\`
- **特点**:
  - 使用 DPAPI (`ProtectedData`) 加密，与 Windows 当前用户账户绑定
  - 构造函数不触盘，首次写入时创建目录
  - 无明文兼容 fallback；无法由 DPAPI 解密的数据直接视为损坏
- **DI 注册**: 见 `DependencyInjection.cs` 的 `#if WINDOWS` 分支

### LinuxSecretServiceSecureStorage（Linux Desktop）

- **文件**: `SalmonEgg.Infrastructure.Desktop/Storage/LinuxSecretServiceSecureStorage.cs`
- **平台**: Linux desktop
- **特点**:
  - 使用 Secret Service provider，通过 `secret-tool` 访问系统密钥环
  - secret 通过 stdin 写入，不作为命令行参数暴露
  - 与 `FallbackSecureStorage` 组合注册时，`secret-tool` 或 Secret Service 不可用会降级到 plaintext secure storage
- **DI 注册**: 见 `DependencyInjection.cs` 的 Linux desktop 分支

### MacOSKeychainSecureStorage（macOS Desktop）

- **文件**: `SalmonEgg.Infrastructure.Desktop/Storage/MacOSKeychainSecureStorage.cs`
- **平台**: macOS desktop
- **特点**:
  - 使用 Security.framework Keychain generic password API
  - secret 通过 Keychain data 写入，不作为命令行参数暴露
  - 与 `FallbackSecureStorage` 组合注册时，Keychain 不可用会降级到 plaintext secure storage
- **DI 注册**: 见 `DependencyInjection.cs` 的 macOS desktop 分支

### AndroidKeyStoreSecureStorage（Android）

- **文件**: `SalmonEgg/Platforms/Android/AndroidKeyStoreSecureStorage.cs`
- **平台**: Android 6.0 / API 23+
- **特点**:
  - 使用 AndroidKeyStore 生成 AES-GCM 密钥
  - 密文和 IV 存入 app 私有 SharedPreferences
  - AndroidKeyStore 不可用或系统版本过低时写入凭据会失败
- **DI 注册**: 见 `DependencyInjection.cs` 的 `__ANDROID__` 分支

### IosKeychainSecureStorage（iOS）

- **文件**: `SalmonEgg/Platforms/iOS/IosKeychainSecureStorage.cs`
- **平台**: iOS
- **特点**:
  - 使用 Keychain generic password item
  - secret 写入 Keychain item data，不进入普通文件
  - Keychain 不可用时写入凭据会失败
- **DI 注册**: 见 `DependencyInjection.cs` 的 `__IOS__` 分支

### PlainTextFileSecureStorage（受限平台 fallback）

- **文件**: `PlainTextFileSecureStorage.cs`
- **平台**: WASM、未知 desktop 平台、Linux/macOS OS-backed secure store 不可用时的 fallback
- **特点**:
  - 保存到 AppData 下的 `SecureStoragePlainText/`
  - key 经 SHA-256 转为文件名，value 以明文写入普通应用文件
  - 构造函数不触盘，首次写入时创建目录

### FallbackSecureStorage

- **文件**: `FallbackSecureStorage.cs`
- **平台**: 当前用于 Linux/macOS desktop
- **特点**:
  - 优先写入和读取 OS-backed secure store
  - 主存储不可用或写入失败时使用 `PlainTextFileSecureStorage`

## 安全说明

- Windows：DPAPI 提供系统级加密，只有创建数据的用户可以解密。
- Linux：优先使用 Secret Service provider；不可用时使用 plaintext secure storage。
- macOS：优先使用 Keychain；不可用时使用 plaintext secure storage。
- Android：AndroidKeyStore 是密钥事实源，SharedPreferences 只保存密文。
- iOS：Keychain 是持久敏感凭据的事实源。
- 受限平台：没有 OS-backed secure store 时使用 plaintext secure storage。配置云同步启用且包含凭据时，同步包的 `secrets.json` 也会明文包含已登记的配置相关凭据。
