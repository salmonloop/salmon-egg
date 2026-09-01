using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.ViewModels.Chat;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public sealed class ChatAuthenticationCoordinatorTests
{
    [Fact]
    public async Task UpdateAgentInfoAsync_WhenAgentInfoExists_DispatchesIdentity()
    {
        var sut = new ChatAuthenticationCoordinator();
        var store = new Mock<IChatStore>();
        store.Setup(x => x.Dispatch(It.IsAny<SetAgentIdentityAction>())).Returns(ValueTask.CompletedTask);
        var service = new Mock<IChatService>();
        service.SetupGet(x => x.AgentInfo).Returns(new AgentInfo("agent-name", "1.0.0", "Agent Title"));

        await sut.UpdateAgentInfoAsync(service.Object, store.Object, "profile-1");

        store.Verify(x => x.Dispatch(It.Is<SetAgentIdentityAction>(a =>
            a.ProfileId == "profile-1"
            && a.AgentName == "Agent Title"
            && a.AgentVersion == "1.0.0")), Times.Once);
    }


    [Fact]
    public async Task TryAuthenticateAsync_WhenFirstAdvertisedMethodHasNoId_UsesFirstValidMethod()
    {
        var sut = new ChatAuthenticationCoordinator();
        sut.CacheAuthMethods(new InitializeResponse
        {
            ProtocolVersion = 1,
            AgentInfo = new AgentInfo("agent", "1.0.0"),
            AgentCapabilities = new AgentCapabilities(),
            AuthMethods =
            [
                new AuthMethodDefinition
                {
                    Id = string.Empty,
                    Name = "Malformed method",
                    Description = "Missing required id"
                },
                new AuthMethodDefinition
                {
                    Id = "chat-gpt",
                    Name = "ChatGPT",
                    Description = "Use ChatGPT"
                }
            ]
        });
        var connectionCoordinator = new Mock<IAcpConnectionCoordinator>();
        var service = new Mock<IChatService>();
        service.Setup(x => x.AuthenticateAsync(It.IsAny<AuthenticateParams>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthenticateResponse());
        var notifications = new List<string>();

        var result = await sut.TryAuthenticateAsync(
            service.Object,
            true,
            connectionCoordinator.Object,
            NullLogger.Instance,
            notifications.Add,
            CancellationToken.None);

        Assert.True(result);
        service.Verify(x => x.AuthenticateAsync(
            It.Is<AuthenticateParams>(p => p.MethodId == "chat-gpt"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TryAuthenticateAsync_WhenOnlyMethodIsTerminalType_DoesNotCallAuthenticate()
    {
        var sut = new ChatAuthenticationCoordinator();
        sut.CacheAuthMethods(CreateInitializeResponse(
            new AuthMethodDefinition
            {
                Id = "terminal-login",
                Name = "Terminal login",
                Type = AuthMethodDefinition.TerminalType
            }));
        var connectionCoordinator = CreateConnectionCoordinator();
        var service = new Mock<IChatService>();
        var notifications = new List<string>();

        var result = await sut.TryAuthenticateAsync(
            service.Object,
            true,
            connectionCoordinator.Object,
            NullLogger.Instance,
            notifications.Add,
            CancellationToken.None,
            unsupportedMethodTypeFallback: UnsupportedMethodTypeHint);

        Assert.False(result);
        service.Verify(
            chatService => chatService.AuthenticateAsync(
                It.IsAny<AuthenticateParams>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        Assert.Equal(["无法使用的登录方式"], notifications);
        connectionCoordinator.Verify(coordinator => coordinator.SetAuthenticationRequiredAsync(
            "无法使用的登录方式",
            "ChatAuth_UnsupportedMethodType",
            "Unsupported sign-in method",
            null,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("_vendor_x")]
    [InlineData("future_thing")]
    public async Task TryAuthenticateAsync_WhenOnlyMethodTypeIsUnknown_DoesNotCallAuthenticate(string methodType)
    {
        var sut = new ChatAuthenticationCoordinator();
        sut.CacheAuthMethods(CreateInitializeResponse(
            new AuthMethodDefinition
            {
                Id = "vendor-login",
                Name = "Vendor login",
                Type = methodType
            }));
        var connectionCoordinator = CreateConnectionCoordinator();
        var service = new Mock<IChatService>();
        var notifications = new List<string>();

        var result = await sut.TryAuthenticateAsync(
            service.Object,
            true,
            connectionCoordinator.Object,
            NullLogger.Instance,
            notifications.Add,
            CancellationToken.None,
            unsupportedMethodTypeFallback: UnsupportedMethodTypeHint);

        Assert.False(result);
        service.Verify(
            chatService => chatService.AuthenticateAsync(
                It.IsAny<AuthenticateParams>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        Assert.Equal(["无法使用的登录方式"], notifications);
    }

    [Fact]
    public async Task TryAuthenticateAsync_WhenMethodTypeIsAbsent_TreatsItAsAgentAndAuthenticates()
    {
        var sut = new ChatAuthenticationCoordinator();
        sut.CacheAuthMethods(CreateInitializeResponse(
            new AuthMethodDefinition { Id = "agent-login", Name = "Agent login" }));
        var connectionCoordinator = CreateConnectionCoordinator();
        var service = new Mock<IChatService>();
        service
            .Setup(chatService => chatService.AuthenticateAsync(
                It.IsAny<AuthenticateParams>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthenticateResponse());
        var notifications = new List<string>();

        var result = await sut.TryAuthenticateAsync(
            service.Object,
            true,
            connectionCoordinator.Object,
            NullLogger.Instance,
            notifications.Add,
            CancellationToken.None,
            unsupportedMethodTypeFallback: UnsupportedMethodTypeHint);

        Assert.True(result);
        service.Verify(
            chatService => chatService.AuthenticateAsync(
                It.Is<AuthenticateParams>(p => p.MethodId == "agent-login"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task TryAuthenticateAsync_WhenTypeIsExplicitlyAgent_Authenticates()
    {
        var sut = new ChatAuthenticationCoordinator();
        sut.CacheAuthMethods(CreateInitializeResponse(
            new AuthMethodDefinition
            {
                Id = "agent-login",
                Name = "Agent login",
                Type = AuthMethodDefinition.AgentType
            }));
        var connectionCoordinator = CreateConnectionCoordinator();
        var service = new Mock<IChatService>();
        service
            .Setup(chatService => chatService.AuthenticateAsync(
                It.IsAny<AuthenticateParams>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthenticateResponse());

        var result = await sut.TryAuthenticateAsync(
            service.Object,
            true,
            connectionCoordinator.Object,
            NullLogger.Instance,
            _ => { },
            CancellationToken.None);

        Assert.True(result);
        service.Verify(
            chatService => chatService.AuthenticateAsync(
                It.Is<AuthenticateParams>(p => p.MethodId == "agent-login"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task TryAuthenticateAsync_WhenTerminalMethodPrecedesAgentMethod_SkipsTerminalAndUsesAgent()
    {
        var sut = new ChatAuthenticationCoordinator();
        sut.CacheAuthMethods(CreateInitializeResponse(
            new AuthMethodDefinition
            {
                Id = "terminal-login",
                Name = "Terminal login",
                Type = AuthMethodDefinition.TerminalType
            },
            new AuthMethodDefinition { Id = "agent-login", Name = "Agent login" }));
        var connectionCoordinator = CreateConnectionCoordinator();
        var service = new Mock<IChatService>();
        service
            .Setup(chatService => chatService.AuthenticateAsync(
                It.IsAny<AuthenticateParams>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthenticateResponse());

        var result = await sut.TryAuthenticateAsync(
            service.Object,
            true,
            connectionCoordinator.Object,
            NullLogger.Instance,
            _ => { },
            CancellationToken.None);

        Assert.True(result);
        service.Verify(
            chatService => chatService.AuthenticateAsync(
                It.Is<AuthenticateParams>(p => p.MethodId == "agent-login"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task TryAuthenticateAsync_WhenAgentAdvertisesNothing_UsesRequiredFallbackNotUnsupportedHint()
    {
        var sut = new ChatAuthenticationCoordinator();
        var connectionCoordinator = CreateConnectionCoordinator();
        var service = new Mock<IChatService>();
        var notifications = new List<string>();

        var result = await sut.TryAuthenticateAsync(
            service.Object,
            true,
            connectionCoordinator.Object,
            NullLogger.Instance,
            notifications.Add,
            CancellationToken.None,
            requiredFallback: new AuthenticationHintPresentation(
                "需要认证",
                ResourceKey: "ChatAuth_Required",
                Fallback: "Authentication required"),
            unsupportedMethodTypeFallback: UnsupportedMethodTypeHint);

        Assert.False(result);
        Assert.Equal(["需要认证"], notifications);
    }

    [Fact]
    public async Task TryAuthenticateAsync_WhenAuthenticateSucceeds_ClearsRequirement()
    {
        var sut = new ChatAuthenticationCoordinator();
        sut.CacheAuthMethods(new InitializeResponse
        {
            ProtocolVersion = 1,
            AgentInfo = new AgentInfo("agent", "1.0.0"),
            AgentCapabilities = new AgentCapabilities(),
            AuthMethods =
            [
                new AuthMethodDefinition
                {
                    Id = "auth-1",
                    Name = "Auth",
                    Description = "Need auth"
                }
            ]
        });
        var connectionCoordinator = new Mock<IAcpConnectionCoordinator>();
        var service = new Mock<IChatService>();
        service.Setup(x => x.AuthenticateAsync(It.IsAny<AuthenticateParams>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthenticateResponse());
        var notifications = new List<string>();

        var result = await sut.TryAuthenticateAsync(
            service.Object,
            true,
            connectionCoordinator.Object,
            NullLogger.Instance,
            notifications.Add,
            CancellationToken.None);

        Assert.True(result);
        connectionCoordinator.Verify(x => x.ClearAuthenticationRequiredAsync(It.IsAny<CancellationToken>()), Times.Once);
        Assert.NotEmpty(notifications);
    }

    [Fact]
    public async Task TryAuthenticateAsync_WhenNoUsableMethod_StoresRequiredFallbackIdentity()
    {
        // Arrange
        var sut = new ChatAuthenticationCoordinator();
        var connectionCoordinator = new Mock<IAcpConnectionCoordinator>();
        connectionCoordinator
            .Setup(coordinator => coordinator.SetAuthenticationRequiredAsync(
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<object[]?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var service = new Mock<IChatService>();
        var notifications = new List<string>();
        var required = new AuthenticationHintPresentation(
            "需要认证",
            ResourceKey: "ChatAuth_Required",
            Fallback: "Authentication required");

        // Act
        var result = await sut.TryAuthenticateAsync(
            service.Object,
            true,
            connectionCoordinator.Object,
            NullLogger.Instance,
            notifications.Add,
            CancellationToken.None,
            requiredFallback: required);

        // Assert
        Assert.False(result);
        Assert.Equal(["需要认证"], notifications);
        connectionCoordinator.Verify(coordinator => coordinator.SetAuthenticationRequiredAsync(
            "需要认证",
            "ChatAuth_Required",
            "Authentication required",
            null,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TryAuthenticateAsync_WhenAuthenticationFails_StoresFailureResourceIdentity()
    {
        // Arrange
        var sut = new ChatAuthenticationCoordinator();
        sut.CacheAuthMethods(new InitializeResponse
        {
            ProtocolVersion = 1,
            AgentInfo = new AgentInfo("agent", "1.0.0"),
            AgentCapabilities = new AgentCapabilities(),
            AuthMethods =
            [
                new AuthMethodDefinition
                {
                    Id = "auth-1",
                    Name = "Auth",
                    Description = "Open the agent sign-in page."
                }
            ]
        });
        var connectionCoordinator = new Mock<IAcpConnectionCoordinator>();
        connectionCoordinator
            .Setup(coordinator => coordinator.SetAuthenticationRequiredAsync(
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<object[]?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var service = new Mock<IChatService>();
        service
            .Setup(chatService => chatService.AuthenticateAsync(
                It.IsAny<AuthenticateParams>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("denied"));
        var notifications = new List<string>();

        // Act
        var result = await sut.TryAuthenticateAsync(
            service.Object,
            true,
            connectionCoordinator.Object,
            NullLogger.Instance,
            notifications.Add,
            CancellationToken.None,
            formatAuthenticationFailed: detail => new AuthenticationHintPresentation(
                $"认证失败：{detail}",
                ResourceKey: "ChatAuth_FailedWithDetail",
                Fallback: "Authentication failed: {0}",
                FormatArgs: [detail]));

        // Assert
        Assert.False(result);
        Assert.Equal("认证失败：denied", notifications[^1]);
        connectionCoordinator.Verify(coordinator => coordinator.SetAuthenticationRequiredAsync(
            "认证失败：denied",
            "ChatAuth_FailedWithDetail",
            "Authentication failed: {0}",
            It.Is<object[]?>(arguments => arguments != null
                && arguments.Length == 1
                && string.Equals(arguments[0] as string, "denied", StringComparison.Ordinal)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private static readonly AuthenticationHintPresentation UnsupportedMethodTypeHint = new(
        "无法使用的登录方式",
        ResourceKey: "ChatAuth_UnsupportedMethodType",
        Fallback: "Unsupported sign-in method");

    private static InitializeResponse CreateInitializeResponse(params AuthMethodDefinition[] authMethods)
        => new()
        {
            ProtocolVersion = 1,
            AgentInfo = new AgentInfo("agent", "1.0.0"),
            AgentCapabilities = new AgentCapabilities(),
            AuthMethods = [.. authMethods]
        };

    private static Mock<IAcpConnectionCoordinator> CreateConnectionCoordinator()
    {
        var coordinator = new Mock<IAcpConnectionCoordinator>();
        coordinator
            .Setup(instance => instance.SetAuthenticationRequiredAsync(
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<object[]?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return coordinator;
    }
}
