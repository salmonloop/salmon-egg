# 安全存储实现说明

## 当前实现

本模块提供 ISecureStorage 的平台分派实现。

### 平台分派（DependencyInjection.cs）

Windows: WindowsDpapiSecureStorage (DPAPI 加密，用户账户绑定)
Linux Desktop: FallbackSecureStorage (Secret Service / secret-tool, fallback 到 plaintext file)
macOS Desktop: FallbackSecureStorage (Security.framework Keychain, fallback 到 plaintext file)
Android: AndroidKeyStoreSecureStorage (AndroidKeyStore AES-GCM + 私有 SharedPreferences 密文)
iOS: IosKeychainSecureStorage (Keychain generic password)
受限平台: PlainTextFileSecureStorage (AppData 普通文件持久化)

### WindowsDpapiSecureStorage

- ProtectedData.Protect/Unprotect（DPAPI，DataProtectionScope.CurrentUser）
- 存储路径：SalmonEggPaths.GetAppDataRootPath() + /SecureStorage/
- 无明文兼容 fallback；无法由 DPAPI 解密的数据直接视为损坏

### LinuxSecretServiceSecureStorage

- 通过 `secret-tool` 使用 Linux Secret Service provider
- secret 通过 stdin 写入，命令行参数只包含哈希后的 key attribute
- 在 Linux desktop DI 中由 `FallbackSecureStorage` 包装；Secret Service 不可用时降级到 plaintext file

### MacOSKeychainSecureStorage

- 通过 Security.framework Keychain generic password API 保存敏感凭据
- secret 作为 Keychain item data 写入，不经过命令行参数
- 在 macOS desktop DI 中由 `FallbackSecureStorage` 包装；Keychain 不可用时降级到 plaintext file

### AndroidKeyStoreSecureStorage

- 通过 AndroidKeyStore 生成不可导出的 AES-GCM 密钥
- 私有 SharedPreferences 只保存 IV + ciphertext，不保存明文 secret
- Android 6.0 / API 23 以下或 AndroidKeyStore 不可用时，保存敏感凭据失败

### IosKeychainSecureStorage

- 通过 iOS Keychain generic password item 保存敏感凭据
- secret 作为 Keychain item data 写入，不经过普通文件系统
- Keychain 不可用时，保存敏感凭据失败，读取 missing item 返回 null

### PlainTextFileSecureStorage

- 用于没有 OS-backed secure store 的受限平台
- 在 AppData 下的 `SecureStoragePlainText/` 目录保存明文 secret
- key 经 SHA-256 转为文件名，value 明文写入普通应用文件

### FallbackSecureStorage

- 优先使用 OS-backed secure store
- 主存储不可用或写入失败时使用 `PlainTextFileSecureStorage`
- 读取时先读主存储，再读 fallback
