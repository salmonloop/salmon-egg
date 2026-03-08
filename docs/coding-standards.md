# SalmonEgg 代码规范文档

## 概述

本文档定义了 SalmonEgg 项目的代码规范和最佳实践。遵循这些规范可以确保代码的一致性、可读性和可维护性。

## 命名约定

### 类和接口

- **类名**: 使用 PascalCase 命名法
  ```csharp
  public class AcpMessage { }
  public class ConnectionManager { }
  ```

- **接口名**: 使用 PascalCase 命名法，以 "I" 开头
  ```csharp
  public interface IConnectionService { }
  public interface IAcpProtocolService { }
  ```

- **抽象类**: 使用 PascalCase 命名法，可以 "Abstract" 或 "Base" 结尾（可选）
  ```csharp
  public abstract class ViewModelBase { }
  ```

### 方法和属性

- **方法名**: 使用 PascalCase 命名法，使用动词描述操作
  ```csharp
  public async Task ConnectAsync() { }
  public void SendMessage() { }
  ```

- **属性名**: 使用 PascalCase 命名法，使用名词或形容词
  ```csharp
  public string ServerUrl { get; set; }
  public bool IsConnected { get; set; }
  ```

- **私有字段**: 使用 camelCase 命名法，以下划线前缀（可选）
  ```csharp
  private readonly ILogger _logger;
  private ConnectionState _currentState;
  ```

### 参数和局部变量

- **参数名**: 使用 camelCase 命名法
  ```csharp
  public void Connect(string serverUrl, int timeout) { }
  ```

- **局部变量**: 使用 camelCase 命名法
  ```csharp
  var connectionState = new ConnectionState();
  var retryCount = 0;
  ```

## 代码组织

### 文件结构

每个文件应该只包含一个主要的公共类型（类、接口、枚举等）。

```
src/
├── Domain/
│   ├── Models/
│   ├── Services/
│   └── Exceptions/
├── Application/
│   ├── Services/
│   ├── UseCases/
│   └── Common/
└── Infrastructure/
    ├── Network/
    ├── Serialization/
    └── Storage/
```

### 类成员排序

在类中，成员应该按以下顺序排列：

1. 常量 (Constants)
2. 静态字段 (Static fields)
3. 实例字段 (Instance fields)
4. 构造函数 (Constructors)
5. 属性 (Properties)
6. 事件 (Events)
7. 方法 (Methods)

```csharp
public class Example
{
    // 1. Constants
    private const int MaxRetryCount = 3;

    // 2. Static fields
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    // 3. Instance fields
    private readonly ILogger _logger;
    private readonly IConnectionManager _connectionManager;
    private bool _isDisposed;

    // 4. Constructors
    public Example(ILogger logger, IConnectionManager connectionManager)
    {
        _logger = logger;
        _connectionManager = connectionManager;
    }

    // 5. Properties
    public ConnectionState CurrentState { get; private set; }

    // 6. Events
    public event EventHandler<ConnectionState>? StateChanged;

    // 7. Methods
    public async Task ConnectAsync() { }
    public void Disconnect() { }
    
    // Private helper methods
    private void UpdateState(ConnectionState newState) { }
}
```

## 异步编程规范

### 使用 Async 后缀

异步方法应该以 "Async" 结尾：

```csharp
// 正确
public async Task ConnectAsync() { }
public async Task<string> GetMessageAsync() { }

// 错误
public async Task Connect() { }  // 缺少 Async 后缀
```

### 避免 async void

除非是事件处理器，否则避免使用 `async void`：

```csharp
// 错误 - 异步方法应该返回 Task
public async void DoWork() { }

// 正确
public async Task DoWorkAsync() { }

// 正确 - 事件处理器可以使用 async void
private async void Button_Click(object sender, RoutedEventArgs e) { }
```

### 使用 CancellationToken

对于长时间运行的操作，应该接受 CancellationToken 参数：

```csharp
public async Task ConnectAsync(CancellationToken cancellationToken = default)
{
    await _transport.ConnectAsync(_serverUrl, cancellationToken);
}
```

## 异常处理

### 使用 Result 模式

对于业务逻辑，优先使用 `Result<T>` 模式而不是异常：

```csharp
public async Task<Result<ConnectionState>> ConnectAsync(string configId)
{
    if (string.IsNullOrWhiteSpace(configId))
    {
        return Result<ConnectionState>.Failure("配置 ID 不能为空");
    }

    var result = await _connectionManager.ConnectAsync(configId);
    return result.IsSuccess
        ? Result<ConnectionState>.Success(result.Value)
        : Result<ConnectionState>.Failure(result.Error);
}
```

### 抛出异常的时机

只在以下情况抛出异常：

1. 参数验证失败（使用 `ArgumentException` 及其子类）
2. 未预期的系统级错误
3. 契约违反

```csharp
public void SaveConfiguration(ServerConfiguration config)
{
    if (config == null)
        throw new ArgumentNullException(nameof(config));
    
    if (string.IsNullOrWhiteSpace(config.Id))
        throw new ArgumentException("配置 ID 不能为空", nameof(config));

    // 业务逻辑...
}
```

### 异常处理最佳实践

```csharp
try
{
    await DoWorkAsync();
}
catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
{
    // 优雅处理取消
    _logger.LogInformation("操作已取消");
    throw; // 或记录后重新抛出
}
catch (Exception ex)
{
    _logger.LogError(ex, "操作失败");
    return Result.Failure("操作失败：" + ex.Message);
}
```

## 日志记录

### 使用结构化日志

```csharp
// 正确 - 结构化日志
_logger.Information("连接到服务器 {ServerUrl}，配置 ID: {ConfigId}", serverUrl, configId);

// 错误 - 字符串插值
_logger.Information($"连接到服务器 {serverUrl}，配置 ID: {configId}");
```

### 日志级别使用

- **Debug**: 详细的调试信息，仅开发时使用
- **Information**: 正常的业务流程信息
- **Warning**: 可恢复的异常情况
- **Error**: 不可恢复的错误
- **Critical**: 系统级严重错误

```csharp
_logger.Debug("开始连接到 {Url}", url);
_logger.Information("连接成功");
_logger.Warning("连接超时，正在重试 ({RetryCount})", retryCount);
_logger.Error(ex, "连接失败");
_logger.Critical(ex, "系统资源耗尽");
```

## 依赖注入

### 构造函数注入

优先使用构造函数注入：

```csharp
public class ConnectionService : IConnectionService
{
    private readonly IConnectionManager _connectionManager;
    private readonly ILogger _logger;

    public ConnectionService(
        IConnectionManager connectionManager,
        ILogger logger)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
}
```

### 生命周期选择

- **Singleton**: 无状态服务，如协议解析器、配置服务
- **Scoped**: 每个请求/会话唯一的服务
- **Transient**: 轻量级、有状态的服务，如 ViewModel

## 测试规范

### 命名约定

```csharp
[Fact]
public void MethodName_Scenario_ExpectedBehavior()
{
    // 示例
    public void ConnectAsync_WithValidConfiguration_ShouldSucceed()
    public void SendMessageAsync_WhenNotConnected_ShouldReturnFailure()
}
```

### 测试结构 (AAA)

```csharp
[Fact]
public async Task ConnectAsync_WithValidUrl_ShouldSucceed()
{
    // Arrange (准备)
    var config = new ServerConfiguration 
    { 
        ServerUrl = "ws://localhost:8080",
        Transport = TransportType.WebSocket
    };
    var mockTransport = new Mock<ITransport>();
    
    // Act (执行)
    var result = await _sut.ConnectAsync(config, CancellationToken.None);
    
    // Assert (断言)
    result.IsSuccess.Should().BeTrue();
    mockTransport.Verify(t => t.ConnectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
}
```

### 属性测试

对于关键逻辑，使用属性测试（FsCheck）：

```csharp
[Property]
public Property RoundTripSerialization(int id, string method, string content)
{
    var message = new AcpMessage 
    { 
        Id = id.ToString(), 
        Method = method,
        Content = content 
    };
    
    var json = _parser.SerializeMessage(message);
    var deserialized = _parser.ParseMessage(json);
    
    return deserialized.Id == message.Id 
        && deserialized.Method == message.Method
        && deserialized.Content == message.Content;
}
```

## 代码审查清单

在提交代码前，确保：

- [ ] 代码符合命名约定
- [ ] 方法短小精悍（最好少于 50 行）
- [ ] 每个方法只做一件事
- [ ] 使用了适当的日志记录
- [ ] 处理了所有可能的错误情况
- [ ] 编写了相应的单元测试
- [ ] 没有硬编码的字符串或数字（使用常量或配置）
- [ ] 注释解释了"为什么"而不是"是什么"
- [ ] 移除了调试代码和 TODO 注释

## 版本控制

### 提交信息格式

```
<type>(<scope>): <subject>

<body>

<footer>
```

示例：

```
feat(connection): 添加自动重连机制

- 实现基于 Polly 的重试策略
- 配置最多重试 3 次，指数退避
- 在连接断开时自动触发重连

Closes #123
```

### 提交类型

- **feat**: 新功能
- **fix**: 修复 bug
- **docs**: 文档更新
- **style**: 代码格式调整
- **refactor**: 代码重构（不改变行为）
- **test**: 添加测试
- **chore**: 构建、依赖、配置等

## 参考资料

- [Microsoft C# 编码约定](https://learn.microsoft.com/dotnet/csharp/fundamentals/coding-style/coding-conventions)
- [CommunityToolkit.Mvvm 指南](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/)
- [Clean Code by Robert C. Martin](https://blog.cleancoder.com/uncle-bob/2010/03/22/Code-Smells.html)
- [xUnit 测试最佳实践](https://xunit.net/)