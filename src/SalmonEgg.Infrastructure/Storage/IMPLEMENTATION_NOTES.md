# 安全存储实现说明

## 实现概述

本任务实现了跨平台安全存储抽象，满足 Requirements 5.4 的要求。

## 已完成的工作

### 1. 接口定义

创建了 `ISecureStorage` 接口，定义了三个核心方法：

```csharp
public interface ISecureStorage
{
    Task SaveAsync(string key, string value);
    Task<string> LoadAsync(string key);
    Task DeleteAsync(string key);
}
```

### 2. 基础实现

在 Infrastructure 项目中实现了 `SecureStorage` 类：

- 使用文件系统存储数据
- 使用 Base64 编码（注意：这不是真正的加密）
- 提供内存缓存以提高性能
- 适用于开发、测试和作为后备实现

### 3. 平台特定实现（参考代码）

提供了以下平台特定实现的参考代码（.txt 文件）：

#### Windows (`WindowsSecureStorage.cs.txt`)
- 使用 DPAPI (Data Protection API)
- 数据与 Windows 用户账户绑定
- 需要在 Windows 平台项目中实现

#### iOS (`iOSSecureStorage.cs.txt`)
- 使用 iOS Keychain
- 系统级安全存储
- 支持生物识别保护
- 需要在 iOS 平台项目中实现

#### Android (`AndroidSecureStorage.cs.txt`)
- 使用 Android KeyStore
- AES-GCM 加密
- 支持硬件密钥存储
- 需要在 Android 平台项目中实现

### 4. WebAssembly 实现

实现了 `WebAssemblySecureStorage` 类：

- 仅内存存储
- 不持久化敏感信息
- 适用于会话期间的临时存储

### 5. 文档

创建了详细的 README.md 文档，包括：

- 各平台实现的说明
- 使用方法和代码示例
- 安全注意事项
- 测试建议

## 架构决策

### 为什么基础实现在 Infrastructure 项目中？

Infrastructure 项目使用 netstandard2.1，这是一个跨平台的目标框架。基础实现：

1. 不依赖平台特定的 API
2. 可以在所有平台上编译和运行
3. 提供了一个可工作的默认实现
4. 便于单元测试

### 为什么平台特定实现是参考代码？

平台特定的实现需要：

1. 平台特定的 API（如 DPAPI、Keychain、KeyStore）
2. 在各自的平台项目中编译（如 SalmonEgg.Windows、SalmonEgg.iOS）
3. 平台特定的依赖注入配置

因此，我们提供了完整的参考实现代码，开发者可以：

1. 复制到相应的平台项目中
2. 根据需要进行调整
3. 在依赖注入中注册正确的实现

## 使用指南

### 开发和测试阶段

使用基础实现：

```csharp
services.AddSingleton<ISecureStorage, SecureStorage>();
```

### 生产环境

在各平台项目中实现并注册平台特定的实现：

**Windows 项目**:
```csharp
services.AddSingleton<ISecureStorage, WindowsSecureStorage>();
```

**iOS 项目**:
```csharp
services.AddSingleton<ISecureStorage, iOSSecureStorage>();
```

**Android 项目**:
```csharp
services.AddSingleton<ISecureStorage>(sp => 
    new AndroidSecureStorage(Android.App.Application.Context));
```

**WebAssembly 项目**:
```csharp
services.AddSingleton<ISecureStorage, WebAssemblySecureStorage>();
```

## 安全考虑

### 基础实现的局限性

基础实现使用 Base64 编码，**不提供真正的加密**：

- ✅ 适用于开发和测试
- ✅ 防止意外的明文暴露
- ❌ 不能防止恶意访问
- ❌ 不应在生产环境中用于敏感数据

### 生产环境建议

在生产环境中，应该：

1. 使用平台特定的安全存储实现
2. 定期审查安全配置
3. 遵循各平台的安全最佳实践
4. 考虑额外的应用层加密（如果需要）

## 测试策略

### 单元测试

为基础实现和 WebAssembly 实现编写单元测试：

```csharp
[Fact]
public async Task SaveAndLoad_ShouldReturnSameValue()
{
    var storage = new SecureStorage();
    await storage.SaveAsync("key", "value");
    var result = await storage.LoadAsync("key");
    Assert.Equal("value", result);
}
```

### 集成测试

在各平台项目中测试平台特定的实现：

- 测试加密和解密的正确性
- 测试错误处理
- 测试边缘情况（如应用重启、权限变化等）

## 后续工作

1. **实现平台特定版本**：
   - 在 Windows 平台项目中实现 WindowsSecureStorage
   - 在 iOS 平台项目中实现 iOSSecureStorage
   - 在 Android 平台项目中实现 AndroidSecureStorage

2. **集成到 ConfigurationManager**：
   - 使用 ISecureStorage 存储敏感配置
   - 实现配置的加密和解密

3. **编写测试**：
   - 为基础实现编写单元测试
   - 为平台特定实现编写集成测试

4. **文档更新**：
   - 更新架构文档
   - 添加安全最佳实践指南

## 相关需求

- **Requirements 5.4**: 配置管理器应加密存储敏感信息（如认证令牌）

## 文件清单

- `ISecureStorage.cs` - 接口定义
- `SecureStorage.cs` - 基础实现（Infrastructure 项目）
- `WebAssemblySecureStorage.cs` - WebAssembly 实现
- `WindowsSecureStorage.cs.txt` - Windows 参考实现
- `iOSSecureStorage.cs.txt` - iOS 参考实现
- `AndroidSecureStorage.cs.txt` - Android 参考实现
- `README.md` - 使用文档
- `IMPLEMENTATION_NOTES.md` - 本文档
