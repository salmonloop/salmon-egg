using System;
using System.Threading.Tasks;
using Moq;
using Xunit;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Services;
using SalmonEgg.Domain.Services.Security;

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
            () => _mockChatService.Object,
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
    public async Task RecoverFromConnectionErrorAsync_WhenAutoReconnectDisabled_ReturnsFailureAfterMaxRetries()
    {
        // Arrange
        var config = _service.GetConfig();
        config.EnableAutoReconnect = false;
        config.InitialDelayMs = 1;
        config.MaxDelayMs = 1;
        _service.SetConfig(config);

        // Act
        var result = await _service.RecoverFromConnectionErrorAsync("Test error", 2, 1);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("reached max retries", result.Error);
    }

    [Fact]
    public async Task RecoverFromConnectionErrorAsync_WhenAutoReconnectEnabled_ReturnsSuccess()
    {
        // Arrange
        var config = _service.GetConfig();
        config.EnableAutoReconnect = true;
        config.InitialDelayMs = 1;
        config.MaxDelayMs = 1;
        _service.SetConfig(config);

        // Act
        var result = await _service.RecoverFromConnectionErrorAsync("Test error", 2, 1);

        // Assert
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task RecoverFromSessionErrorAsync_WhenSessionAutoRecoveryDisabled_ReturnsFailure()
    {
        // Arrange
        var config = _service.GetConfig();
        config.EnableSessionAutoRecovery = false;
        _service.SetConfig(config);

        // Act
        var result = await _service.RecoverFromSessionErrorAsync("session-123", "Test error");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("Session auto-recovery is disabled", result.Error);
    }

    [Fact]
    public async Task RecoverFromSessionErrorAsync_WhenSessionAutoRecoveryEnabled_ReturnsSuccess()
    {
        // Arrange
        var config = _service.GetConfig();
        config.EnableSessionAutoRecovery = true;
        _service.SetConfig(config);

        var newSessionId = "new-session-456";
        _mockChatService.Setup(x => x.CreateSessionAsync(It.IsAny<SalmonEgg.Domain.Models.Protocol.SessionNewParams>()))
            .ReturnsAsync(new SalmonEgg.Domain.Models.Protocol.SessionNewResponse { SessionId = newSessionId });

        // Act
        var result = await _service.RecoverFromSessionErrorAsync("old-session-123", "Test error");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(newSessionId, result.Value);
        _mockChatService.Verify(x => x.CreateSessionAsync(It.IsAny<SalmonEgg.Domain.Models.Protocol.SessionNewParams>()), Times.Once);
    }

    [Fact]
    public async Task RecoverFromFileSystemErrorAsync_WhenFileSystemRecoveryDisabled_ReturnsFailure()
    {
        // Arrange
        var config = _service.GetConfig();
        config.EnableFileSystemRecovery = false;
        _service.SetConfig(config);

        // Act
        var result = await _service.RecoverFromFileSystemErrorAsync("Read", "/invalid/path", "Test error");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("File system error recovery is disabled", result.Error);
    }

    [Fact]
    public async Task RecoverFromFileSystemErrorAsync_WhenPathValidationPasses_ReturnsSuccess()
    {
        // Arrange
        var config = _service.GetConfig();
        config.EnableFileSystemRecovery = true;
        _service.SetConfig(config);

        string testPath = "/valid/path";
        _mockPathValidator.Setup(x => x.ValidatePath(testPath)).Returns(true);

        // Act
        var result = await _service.RecoverFromFileSystemErrorAsync("Read", testPath, "Test error");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact]
    public async Task GetCurrentRetryCount_ResetRetryCount_WorksCorrectly()
    {
        // Arrange
        var config = _service.GetConfig();
        config.EnableAutoReconnect = false;
        config.InitialDelayMs = 1;
        config.MaxDelayMs = 1;
        _service.SetConfig(config);

        // Act 1
        await _service.RecoverFromConnectionErrorAsync("Test error", 3, 1);
        int retryCountAfterError = _service.GetCurrentRetryCount();

        // Act 2
        _service.ResetRetryCount();
        int retryCountAfterReset = _service.GetCurrentRetryCount();

        // Assert
        Assert.Equal(3, retryCountAfterError);
        Assert.Equal(0, retryCountAfterReset);
    }
}
