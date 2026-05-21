using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Xunit;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Models.Mcp;
using SalmonEgg.Domain.Models.Protocol;
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
    public async Task RecoverFromSessionErrorAsync_ResolvesMcpServersAtRequestTime()
    {
        // Arrange
        var resolverCalled = false;
        SessionNewParams? capturedParams = null;
        var service = new ErrorRecoveryService(
            () => _mockChatService.Object,
            _mockPathValidator.Object,
            _mockErrorLogger.Object,
            _ =>
            {
                resolverCalled = true;
                return Task.FromResult<IReadOnlyList<McpServer>>(Array.Empty<McpServer>());
            });

        _mockChatService
            .Setup(chatService => chatService.CreateSessionAsync(It.IsAny<SessionNewParams>()))
            .Callback<SessionNewParams>(parameters =>
            {
                Assert.True(resolverCalled);
                capturedParams = parameters;
            })
            .ReturnsAsync(new SessionNewResponse("recovered-session"));

        // Act
        var result = await service.RecoverFromSessionErrorAsync("broken-session", "session failed");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("recovered-session", result.Value);
        Assert.NotNull(capturedParams);
        Assert.Empty(capturedParams.McpServers);
    }

    [Fact]
    public async Task RecoverFromSessionErrorAsync_DeepClonesResolvedMcpServers()
    {
        // Arrange
        var resolvedServer = new StdioMcpServer(
            "filesystem",
            "/usr/bin/mcp",
            new List<string> { "--stdio" },
            new List<McpEnvVariable> { new("ROOT", "/workspace") });
        SessionNewParams? capturedParams = null;
        var service = new ErrorRecoveryService(
            () => _mockChatService.Object,
            _mockPathValidator.Object,
            _mockErrorLogger.Object,
            _ => Task.FromResult<IReadOnlyList<McpServer>>(new[] { resolvedServer }));

        _mockChatService
            .Setup(chatService => chatService.CreateSessionAsync(It.IsAny<SessionNewParams>()))
            .Callback<SessionNewParams>(parameters => capturedParams = parameters)
            .ReturnsAsync(new SessionNewResponse("recovered-session"));

        // Act
        var result = await service.RecoverFromSessionErrorAsync("broken-session", "session failed");

        // Assert
        Assert.True(result.IsSuccess);
        var capturedServer = Assert.IsType<StdioMcpServer>(Assert.Single(capturedParams!.McpServers));
        Assert.NotSame(resolvedServer, capturedServer);
        Assert.Equal(resolvedServer.Name, capturedServer.Name);
        Assert.Equal(resolvedServer.Command, capturedServer.Command);
        Assert.NotSame(resolvedServer.Args, capturedServer.Args);
        Assert.NotSame(resolvedServer.Env, capturedServer.Env);
    }
}
