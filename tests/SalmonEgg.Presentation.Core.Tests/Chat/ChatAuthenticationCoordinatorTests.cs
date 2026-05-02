using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Models.Protocol;
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
            .ReturnsAsync(new AuthenticateResponse(true, null));
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
}
