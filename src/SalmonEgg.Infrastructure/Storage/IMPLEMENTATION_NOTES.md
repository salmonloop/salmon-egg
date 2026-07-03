# 安全存储实现说明

## 当前实现

本模块提供 ISecureStorage 的平台分派实现。

### 平台分派（DependencyInjection.cs）

Windows: WindowsDpapiSecureStorage (DPAPI 加密，用户账户绑定)
Linux Desktop: LinuxSecretServiceSecureStorage (Secret Service / secret-tool)
macOS Desktop: MacOSKeychainSecureStorage (Security.framework Keychain)
Android: AndroidKeyStoreSecureStorage (AndroidKeyStore AES-GCM + 私有 SharedPreferences 密文)
iOS: IosKeychainSecureStorage (Keychain generic password)
受限平台: VolatileSecureStorage (进程内、不持久化)

### WindowsDpapiSecureStorage

- ProtectedData.Protect/Unprotect（DPAPI，DataProtectionScope.CurrentUser）
- 存储路径：SalmonEggPaths.GetAppDataRootPath() + /SecureStorage/
- 无明文兼容 fallback；无法由 DPAPI 解密的数据直接视为损坏

### LinuxSecretServiceSecureStorage

- 通过 `secret-tool` 使用 Linux Secret Service provider
- secret 通过 stdin 写入，命令行参数只包含哈希后的 key attribute
- Secret Service 不可用时，保存敏感凭据失败，读取返回 null

### MacOSKeychainSecureStorage

- 通过 Security.framework Keychain generic password API 保存敏感凭据
- secret 作为 Keychain item data 写入，不经过命令行参数
- Keychain 不可用时，保存敏感凭据失败，读取 missing item 返回 null

### AndroidKeyStoreSecureStorage

- 通过 AndroidKeyStore 生成不可导出的 AES-GCM 密钥
- 私有 SharedPreferences 只保存 IV + ciphertext，不保存明文 secret
- Android 6.0 / API 23 以下或 AndroidKeyStore 不可用时，保存敏感凭据失败

### IosKeychainSecureStorage

- 通过 iOS Keychain generic password item 保存敏感凭据
- secret 作为 Keychain item data 写入，不经过普通文件系统
- Keychain 不可用时，保存敏感凭据失败，读取 missing item 返回 null

### VolatileSecureStorage

- 用于没有 OS-backed secure store 的受限平台
- 不写入文件系统，不跨进程持久化
