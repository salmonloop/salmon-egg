# 安全存储实现

本目录包含跨平台安全存储的接口和实现。

## 接口

### ISecureStorage

定义了安全存储的核心接口：

- `SaveAsync(string key, string value)` — 安全保存数据
- `LoadAsync(string key)` — 加载安全存储的数据，不存在返回 null
- `DeleteAsync(string key)` — 删除安全存储的数据

## 实现

### AppFileStoreSecureStorage（非 Windows 平台）

- **文件**: `AppFileStoreSecureStorage.cs`
- **平台**: WASM、Android、iOS 及其他非 Windows 平台
- **存储位置**: `<AppDataRoot>/SecureStorage/`（由 `IAppDataService.AppDataRootPath` 决定）
- **特点**:
  - 通过 `IAppFileStore` 读写，自动复用 `IFileSystemPersistence`（WASM 下接入 IndexedDB）
  - 文件名为 key 的 SHA-256 哈希（不暴露 key 明文）
  - 文件内容为 value 的 Base64 编码（不以明文存储）
  - 构造函数不触盘，首次写入时自动创建目录
- **DI 注册**: 见 `DependencyInjection.cs` 的 `#else` 分支（非 Windows）

### WindowsDpapiSecureStorage（Windows 平台）

- **文件**: `SalmonEgg/Platforms/Windows/WindowsDpapiSecureStorage.cs`
- **平台**: Windows
- **存储位置**: `%LOCALAPPDATA%\SalmonEgg\SecureStorage\`
- **特点**:
  - 使用 DPAPI (`ProtectedData`) 加密，与 Windows 当前用户账户绑定
  - 构造函数不触盘，首次写入时创建目录
  - 支持从旧明文格式迁移（TryDecodeLegacyPlainText）
- **DI 注册**: 见 `DependencyInjection.cs` 的 `#if WINDOWS` 分支

## 废弃参考实现

`AndroidSecureStorage.cs.txt`、`iOSSecureStorage.cs.txt`、`WindowsSecureStorage.cs.txt` 是历史参考代码，
未编译进项目，仅供参考。当前生产路径走 `AppFileStoreSecureStorage`（Android/iOS）和
`WindowsDpapiSecureStorage`（Windows）。

## 安全说明

- Windows：DPAPI 提供系统级加密，只有创建数据的用户可以解密。
- 非 Windows：base64 编码防止明文存储，但不是加密。WASM 环境无原生 keychain，
  IndexedDB 存储的安全性由浏览器沙盒提供。
