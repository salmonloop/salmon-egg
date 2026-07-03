# 安全存储实现说明

## 当前实现

本模块提供 ISecureStorage 的平台分派实现。

### 平台分派（DependencyInjection.cs）

Windows: WindowsDpapiSecureStorage (DPAPI 加密，用户账户绑定)
Linux Desktop: LinuxSecretServiceSecureStorage (Secret Service / secret-tool)
受限平台: VolatileSecureStorage (进程内、不持久化)

### AppFileStoreSecureStorage

- key 经 SHA-256 哈希后作为文件名（key 不落盘）
- value 以 Base64 编码写入文件内容；这不是加密
- 写入路径：IAppFileStore.WriteAllTextAsync → AtomicFile → IFileSystemPersistence.FlushAsync
- 当前仅用于测试/兼容场景，不作为生产平台 ISecureStorage 注册

### WindowsDpapiSecureStorage

- ProtectedData.Protect/Unprotect（DPAPI，DataProtectionScope.CurrentUser）
- 存储路径：SalmonEggPaths.GetAppDataRootPath() + /SecureStorage/
- 支持旧明文格式迁移（TryDecodeLegacyPlainText）

### LinuxSecretServiceSecureStorage

- 通过 `secret-tool` 使用 Linux Secret Service provider
- secret 通过 stdin 写入，命令行参数只包含哈希后的 key attribute
- Secret Service 不可用时，保存敏感凭据失败，读取返回 null

### VolatileSecureStorage

- 用于没有 OS-backed secure store 的受限平台
- 不写入文件系统，不跨进程持久化

## 废弃的历史文件

以下文件保留仅供参考，不编译进任何目标：
- AndroidSecureStorage.cs.txt
- iOSSecureStorage.cs.txt
- WindowsSecureStorage.cs.txt
