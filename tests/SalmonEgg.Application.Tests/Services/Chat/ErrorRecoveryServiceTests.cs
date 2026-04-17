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
}
