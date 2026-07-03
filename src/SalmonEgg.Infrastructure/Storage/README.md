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
  - 支持从旧明文格式迁移（TryDecodeLegacyPlainText）
- **DI 注册**: 见 `DependencyInjection.cs` 的 `#if WINDOWS` 分支

### LinuxSecretServiceSecureStorage（Linux Desktop）

- **文件**: `SalmonEgg.Infrastructure.Desktop/Storage/LinuxSecretServiceSecureStorage.cs`
- **平台**: Linux desktop
- **特点**:
  - 使用 Secret Service provider，通过 `secret-tool` 访问系统密钥环
  - secret 通过 stdin 写入，不作为命令行参数暴露
  - `secret-tool` 或 Secret Service 不可用时写入凭据 fail-closed
- **DI 注册**: 见 `DependencyInjection.cs` 的 Linux desktop 分支

### VolatileSecureStorage（受限平台）

- **文件**: `VolatileSecureStorage.cs`
- **平台**: WASM、Android、iOS、macOS（直到接入平台 keychain）
- **特点**:
  - 仅进程内保存，不持久化到普通文件
  - 防止把敏感凭据降级写入非安全存储

### AppFileStoreSecureStorage（测试/兼容实现）

- **文件**: `AppFileStoreSecureStorage.cs`
- **特点**:
  - 通过 `IAppFileStore` 读写
  - 文件名为 key 的 SHA-256 哈希（不暴露 key 明文）
  - 文件内容为 value 的 Base64 编码；这不是加密
  - 当前不作为生产平台 `ISecureStorage` 注册

## 废弃参考实现

`AndroidSecureStorage.cs.txt`、`iOSSecureStorage.cs.txt`、`WindowsSecureStorage.cs.txt` 是历史参考代码，
未编译进项目，仅供参考。

## 安全说明

- Windows：DPAPI 提供系统级加密，只有创建数据的用户可以解密。
- Linux：Secret Service provider 是持久敏感凭据的事实源。
- 受限平台：没有 OS-backed secure store 时只允许 volatile 语义，不得降级到普通文件。
