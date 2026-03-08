# 安全存储实现

本目录包含跨平台安全存储的接口和实现。

## 接口

### ISecureStorage

定义了安全存储的核心接口：

- `SaveAsync(string key, string value)` - 安全保存数据
- `LoadAsync(string key)` - 加载安全存储的数据
- `DeleteAsync(string key)` - 删除安全存储的数据

## 实现

### 1. SecureStorage (基础实现)

- **文件**: `SecureStorage.cs`
- **平台**: 所有平台（默认实现）
- **加密方式**: Base64 编码（注意：这不是真正的加密）
- **存储位置**: 应用数据目录
- **用途**: 开发和测试，或作为平台特定实现的后备方案
- **实现位置**: Infrastructure 项目中

### 2. WindowsSecureStorage

- **文件**: `WindowsSecureStorage.cs.txt` (参考实现)
- **平台**: Windows
- **加密方式**: DPAPI (Data Protection API)
- **存储位置**: `%LOCALAPPDATA%\SalmonEgg\SecureStorage`
- **特点**: 使用 Windows 用户凭据加密，只有当前用户可以解密
- **实现位置**: 应在 Windows 平台项目中实现

### 3. iOSSecureStorage

- **文件**: `iOSSecureStorage.cs.txt` (参考实现)
- **平台**: iOS
- **加密方式**: iOS Keychain
- **存储位置**: 系统 Keychain
- **特点**: 使用 iOS 系统级安全存储，支持 Touch ID/Face ID 保护
- **实现位置**: 应在 iOS 平台项目中实现

### 4. AndroidSecureStorage

- **文件**: `AndroidSecureStorage.cs.txt` (参考实现)
- **平台**: Android
- **加密方式**: Android KeyStore + AES-GCM
- **存储位置**: SharedPreferences (加密后的数据)
- **特点**: 使用硬件支持的密钥存储（如果可用）
- **实现位置**: 应在 Android 平台项目中实现

### 5. WebAssemblySecureStorage

- **文件**: `WebAssemblySecureStorage.cs`
- **平台**: WebAssembly
- **加密方式**: 无（仅内存存储）
- **存储位置**: 内存
- **特点**: 不持久化敏感信息，仅在会话期间有效

## 使用方法

### 依赖注入配置

在各平台项目中注册适当的实现：

#### Windows 项目

```csharp
services.AddSingleton<ISecureStorage, WindowsSecureStorage>();
```

#### iOS 项目

```csharp
services.AddSingleton<ISecureStorage, iOSSecureStorage>();
```

#### Android 项目

```csharp
services.AddSingleton<ISecureStorage>(sp => 
    new AndroidSecureStorage(Android.App.Application.Context));
```

#### WebAssembly 项目

```csharp
services.AddSingleton<ISecureStorage, WebAssemblySecureStorage>();
```

#### 默认/测试

```csharp
services.AddSingleton<ISecureStorage, SecureStorage>();
```

### 代码示例

```csharp
public class ConfigurationManager
{
    private readonly ISecureStorage _secureStorage;

    public ConfigurationManager(ISecureStorage secureStorage)
    {
        _secureStorage = secureStorage;
    }

    public async Task SaveAuthTokenAsync(string token)
    {
        await _secureStorage.SaveAsync("auth_token", token);
    }

    public async Task<string> LoadAuthTokenAsync()
    {
        return await _secureStorage.LoadAsync("auth_token");
    }

    public async Task DeleteAuthTokenAsync()
    {
        await _secureStorage.DeleteAsync("auth_token");
    }
}
```

## 安全注意事项

1. **Windows DPAPI**: 
   - 数据与 Windows 用户账户绑定
   - 如果用户密码重置，可能无法解密数据
   - 不适合跨用户共享数据

2. **iOS Keychain**:
   - 数据在应用卸载后会被删除（除非配置了 iCloud Keychain）
   - 可以配置访问控制（如需要生物识别）

3. **Android KeyStore**:
   - 密钥存储在硬件安全模块中（如果设备支持）
   - 应用卸载后数据会被删除
   - 需要 API Level 23+ 才能使用完整功能

4. **WebAssembly**:
   - 不应存储敏感信息
   - 仅用于会话期间的临时存储
   - 页面刷新后数据丢失

## 测试

建议为每个平台实现编写单元测试：

```csharp
[Fact]
public async Task SaveAndLoad_ShouldReturnSameValue()
{
    // Arrange
    var storage = new WindowsSecureStorage();
    var key = "test_key";
    var value = "test_value";

    // Act
    await storage.SaveAsync(key, value);
    var loaded = await storage.LoadAsync(key);

    // Assert
    Assert.Equal(value, loaded);
}

[Fact]
public async Task LoadNonExistentKey_ShouldReturnNull()
{
    // Arrange
    var storage = new WindowsSecureStorage();

    // Act
    var loaded = await storage.LoadAsync("non_existent_key");

    // Assert
    Assert.Null(loaded);
}

[Fact]
public async Task Delete_ShouldRemoveValue()
{
    // Arrange
    var storage = new WindowsSecureStorage();
    var key = "test_key";
    await storage.SaveAsync(key, "test_value");

    // Act
    await storage.DeleteAsync(key);
    var loaded = await storage.LoadAsync(key);

    // Assert
    Assert.Null(loaded);
}
```

## 相关需求

- **Requirements 5.4**: 配置管理器应加密存储敏感信息（如认证令牌）
