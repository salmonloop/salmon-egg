using System;
using System.Threading.Tasks;
using Moq;
using Xunit;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Services;
using SalmonEgg.Domain.Services.Security;
using SalmonEgg.Domain.Models.Protocol;

namespace SalmonEgg.Application.Tests.Services.Chat;

public sealed class ErrorRecoveryServiceTests
{
    private readonly Mock<IChatService> _mockChatService;
    private readonly Mock<IPathValidator> _mockPathValidator;
    private readonly Mock<IErrorLogger> _mockErrorLogger;
    private readonly ErrorRecoveryService _service;

    public ErrorRecoveryServiceTests()
    {
        _mockChatService = new Mock<IChatService>(MockBehavior.Strict);
        _mockPathValidator = new Mock<IPathValidator>(MockBehavior.Strict);
        _mockErrorLogger = new Mock<IErrorLogger>(MockBehavior.Loose);

        _service = new ErrorRecoveryService(
            _mockChatService.Object,
            _mockPathValidator.Object,
            _mockErrorLogger.Object);
    }

    [Fact]
    public async Task RecoverFromProtocolVersionErrorAsync_WhenShowWarningIsFalse_ReturnsSuccess()
    {
        // Arrange
        var config = _service.GetConfig();
        config.ShowProtocolVersionWarning = false;
        _service.SetConfig(config);

        // Act
        var result = await _service.RecoverFromProtocolVersionErrorAsync(1, 2);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task RecoverFromProtocolVersionErrorAsync_WhenShowWarningIsTrue_ReturnsFailureWithMessage()
    {
        // Arrange
        var config = _service.GetConfig();
        config.ShowProtocolVersionWarning = true;
        _service.SetConfig(config);

        int expectedVersion = 1;
        int actualVersion = 2;
        string expectedError = $"Protocol version incompatible: Client expects v{expectedVersion}, but Agent returned v{actualVersion}. Please ensure the Agent is updated to the latest version.";

        // Act
        var result = await _service.RecoverFromProtocolVersionErrorAsync(expectedVersion, actualVersion);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(expectedError, result.Error);
    }

    [Fact]
    public async Task RecoverFromConnectionErrorAsync_WhenAutoReconnectEnabled_ReturnsSuccessOnFirstAttempt()
    {
        // Arrange
        _service.SetConfig(new ErrorRecoveryConfig
        {
            EnableAutoReconnect = true,
            MaxRetries = 3,
            InitialDelayMs = 1 // Use small delay for faster tests
        });

        // Act
        var result = await _service.RecoverFromConnectionErrorAsync("test error");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(1, _service.GetCurrentRetryCount());
        _mockErrorLogger.Verify(x => x.LogError(It.IsAny<ErrorLogEntry>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task RecoverFromConnectionErrorAsync_WhenAutoReconnectDisabled_FailsAfterMaxRetries()
    {
        // Arrange
        _service.SetConfig(new ErrorRecoveryConfig
        {
            EnableAutoReconnect = false
        });
        int maxRetries = 2;
        int initialDelayMs = 1;

        // Act
        var result = await _service.RecoverFromConnectionErrorAsync("test error", maxRetries, initialDelayMs);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("reached max retries", result.Error);
        Assert.Equal(2, _service.GetCurrentRetryCount());
    }

    [Fact]
    public async Task ConcurrentRecovery_ShouldBeProtectedByLock()
    {
        // Arrange
        _service.SetConfig(new ErrorRecoveryConfig
        {
            EnableAutoReconnect = true,
            InitialDelayMs = 50,
            MaxRetries = 3
        });

        // Act
        var task1 = _service.RecoverFromConnectionErrorAsync("error 1");
        var task2 = _service.RecoverFromConnectionErrorAsync("error 2");

        var results = await Task.WhenAll(task1, task2);

        // Assert
        Assert.True(results[0].IsSuccess);
        Assert.True(results[1].IsSuccess);

        // Due to the lock, the retry count is reset on entry.
        // So after two sequential successful reconnect attempts, it should be 1.
        Assert.Equal(1, _service.GetCurrentRetryCount());
    }

    [Fact]
    public async Task RecoverFromSessionErrorAsync_WhenDisabled_ReturnsFailure()
    {
        // Arrange
        _service.SetConfig(new ErrorRecoveryConfig { EnableSessionAutoRecovery = false });

        // Act
        var result = await _service.RecoverFromSessionErrorAsync("session-1", "test error");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("Session auto-recovery is disabled", result.Error);
    }

    [Fact]
    public async Task RecoverFromSessionErrorAsync_WhenEnabledAndCreationSucceeds_ReturnsNewSessionId()
    {
        // Arrange
        _service.SetConfig(new ErrorRecoveryConfig { EnableSessionAutoRecovery = true });

        _mockChatService.Setup(x => x.CreateSessionAsync(It.IsAny<SessionNewParams>()))
            .ReturnsAsync(new SessionNewResponse { SessionId = "new-session" });

        // Act
        var result = await _service.RecoverFromSessionErrorAsync("old-session", "test error");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("new-session", result.Value);
        _mockChatService.Verify(x => x.CreateSessionAsync(It.IsAny<SessionNewParams>()), Times.Once);
    }

    [Fact]
    public async Task RecoverFromSessionErrorAsync_WhenEnabledAndCreationThrows_ReturnsFailure()
    {
        // Arrange
        _service.SetConfig(new ErrorRecoveryConfig { EnableSessionAutoRecovery = true });

        _mockChatService.Setup(x => x.CreateSessionAsync(It.IsAny<SessionNewParams>()))
            .ThrowsAsync(new Exception("Creation failed"));

        // Act
        var result = await _service.RecoverFromSessionErrorAsync("old-session", "test error");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("Creation failed", result.Error);
    }

    [Fact]
    public async Task RecoverFromFileSystemErrorAsync_WhenDisabled_ReturnsFailure()
    {
        // Arrange
        _service.SetConfig(new ErrorRecoveryConfig { EnableFileSystemRecovery = false });

        // Act
        var result = await _service.RecoverFromFileSystemErrorAsync("read", "/test/path", "test error");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("File system error recovery is disabled", result.Error);
    }

    [Fact]
    public async Task RecoverFromFileSystemErrorAsync_WhenEnabledAndPathIsValid_ReturnsSuccess()
    {
        // Arrange
        _service.SetConfig(new ErrorRecoveryConfig { EnableFileSystemRecovery = true });
        _mockPathValidator.Setup(x => x.ValidatePath("/test/path")).Returns(true);

        // Act
        var result = await _service.RecoverFromFileSystemErrorAsync("read", "/test/path", "test error");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact]
    public async Task RecoverFromFileSystemErrorAsync_WhenEnabledAndPathIsInvalid_ReturnsFailure()
    {
        // Arrange
        _service.SetConfig(new ErrorRecoveryConfig { EnableFileSystemRecovery = true });
        _mockPathValidator.Setup(x => x.ValidatePath("/test/path")).Returns(false);
        _mockPathValidator.Setup(x => x.GetValidationErrors("/test/path"))
            .Returns(new System.Collections.Generic.List<string> { "Invalid path" });

        // Act
        var result = await _service.RecoverFromFileSystemErrorAsync("read", "/test/path", "test error");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("Invalid path", result.Error);
    }

    [Fact]
    public async Task RecoverFromFileSystemErrorAsync_WhenValidationThrows_ReturnsFailure()
    {
        // Arrange
        _service.SetConfig(new ErrorRecoveryConfig { EnableFileSystemRecovery = true });
        _mockPathValidator.Setup(x => x.ValidatePath("/test/path"))
            .Throws(new Exception("Validation failed"));

        // Act
        var result = await _service.RecoverFromFileSystemErrorAsync("read", "/test/path", "test error");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("Validation failed", result.Error);
    }

    [Fact]
    public void ConfigManagement_ShouldWorkCorrectly()
    {
        // Arrange
        var config = new ErrorRecoveryConfig
        {
            EnableAutoReconnect = false,
            MaxRetries = 10,
            InitialDelayMs = 200,
            MaxDelayMs = 5000,
            DelayMultiplier = 1.5,
            EnableSessionAutoRecovery = false,
            EnableFileSystemRecovery = false,
            ShowProtocolVersionWarning = false
        };

        // Act
        _service.SetConfig(config);
        var result = _service.GetConfig();

        // Assert
        Assert.Equal(config.EnableAutoReconnect, result.EnableAutoReconnect);
        Assert.Equal(config.MaxRetries, result.MaxRetries);
        Assert.Equal(config.InitialDelayMs, result.InitialDelayMs);
        Assert.Equal(config.MaxDelayMs, result.MaxDelayMs);
        Assert.Equal(config.DelayMultiplier, result.DelayMultiplier);
        Assert.Equal(config.EnableSessionAutoRecovery, result.EnableSessionAutoRecovery);
        Assert.Equal(config.EnableFileSystemRecovery, result.EnableFileSystemRecovery);
        Assert.Equal(config.ShowProtocolVersionWarning, result.ShowProtocolVersionWarning);
    }

    [Fact]
    public async Task ResetRetryCount_ShouldResetToZero()
    {
        // Arrange - simulate a failure to increase retry count
        _service.SetConfig(new ErrorRecoveryConfig { EnableAutoReconnect = false, MaxRetries = 1, InitialDelayMs = 1 });
        _ = await _service.RecoverFromConnectionErrorAsync("test");
        Assert.Equal(1, _service.GetCurrentRetryCount());

        // Act
        _service.ResetRetryCount();

        // Assert
        Assert.Equal(0, _service.GetCurrentRetryCount());
    }
}
