# 安全存储实现说明

## 当前实现

本模块提供 ISecureStorage 的跨平台持久化实现。

### 平台分派（DependencyInjection.cs）

Windows: WindowsDpapiSecureStorage (DPAPI 加密，用户账户绑定)
非 Windows: AppFileStoreSecureStorage (IAppFileStore 文件存储，WASM 下自动接入 IDBFS)

### AppFileStoreSecureStorage

- key 经 SHA-256 哈希后作为文件名（key 不落盘）
- value 以 Base64 编码写入文件内容（不以明文存储）
- 写入路径：IAppFileStore.WriteAllTextAsync → AtomicFile → IFileSystemPersistence.FlushAsync
- WASM 下 IFileSystemPersistence = WasmFileSystemPersistence，flush 触发 IDBFS sync

### WindowsDpapiSecureStorage

- ProtectedData.Protect/Unprotect（DPAPI，DataProtectionScope.CurrentUser）
- 存储路径：SalmonEggPaths.GetAppDataRootPath() + /SecureStorage/
- 支持旧明文格式迁移（TryDecodeLegacyPlainText）

## 废弃的历史文件

以下文件保留仅供参考，不编译进任何目标：
- AndroidSecureStorage.cs.txt
- iOSSecureStorage.cs.txt
- WindowsSecureStorage.cs.txt
