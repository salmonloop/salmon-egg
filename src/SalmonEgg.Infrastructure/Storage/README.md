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
  - `secret-tool` 或 Secret Service 不可用时写入凭据 fail-closed
- **DI 注册**: 见 `DependencyInjection.cs` 的 Linux desktop 分支

### MacOSKeychainSecureStorage（macOS Desktop）

- **文件**: `SalmonEgg.Infrastructure.Desktop/Storage/MacOSKeychainSecureStorage.cs`
- **平台**: macOS desktop
- **特点**:
  - 使用 Security.framework Keychain generic password API
  - secret 通过 Keychain data 写入，不作为命令行参数暴露
  - Keychain 不可用时写入凭据 fail-closed
- **DI 注册**: 见 `DependencyInjection.cs` 的 macOS desktop 分支

### AndroidKeyStoreSecureStorage（Android）

- **文件**: `SalmonEgg/Platforms/Android/AndroidKeyStoreSecureStorage.cs`
- **平台**: Android 6.0 / API 23+
- **特点**:
  - 使用 AndroidKeyStore 生成 AES-GCM 密钥
  - 密文和 IV 存入 app 私有 SharedPreferences
  - AndroidKeyStore 不可用或系统版本过低时写入凭据 fail-closed
- **DI 注册**: 见 `DependencyInjection.cs` 的 `__ANDROID__` 分支

### IosKeychainSecureStorage（iOS）

- **文件**: `SalmonEgg/Platforms/iOS/IosKeychainSecureStorage.cs`
- **平台**: iOS
- **特点**:
  - 使用 Keychain generic password item
  - secret 写入 Keychain item data，不进入普通文件
  - Keychain 不可用时写入凭据 fail-closed
- **DI 注册**: 见 `DependencyInjection.cs` 的 `__IOS__` 分支

### VolatileSecureStorage（受限平台）

- **文件**: `VolatileSecureStorage.cs`
- **平台**: WASM、未知 desktop 平台
- **特点**:
  - 仅进程内保存，不持久化到普通文件
  - 防止把敏感凭据降级写入非安全存储

## 安全说明

- Windows：DPAPI 提供系统级加密，只有创建数据的用户可以解密。
- Linux：Secret Service provider 是持久敏感凭据的事实源。
- macOS：Keychain 是持久敏感凭据的事实源。
- Android：AndroidKeyStore 是密钥事实源，SharedPreferences 只保存密文。
- iOS：Keychain 是持久敏感凭据的事实源。
- 受限平台：没有 OS-backed secure store 时只允许 volatile 语义，不得降级到普通文件。
