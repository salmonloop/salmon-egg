using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Models;
using SalmonEgg.Presentation.Core.Services.Chat;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Services.Chat;

public sealed class AcpInitializeTimeoutTests
{
    [Fact]
    public void CreateTimeoutMessage_WhenIdentityNull_RendersNonePlaceholders()
    {
        var message = AcpInitializeTimeout.CreateTimeoutMessage(
            TransportType.Stdio,
            profileId: null,
            conversationId: null,
            TimeSpan.FromSeconds(120));

        Assert.Equal(
            "Timed out waiting for ACP initialize response. "
            + "profileId=(none) transport=Stdio timeoutSeconds=120 conversationId=(none)",
            message);
    }

    [Fact]
    public void CreateTimeoutMessage_WhenIdentityPopulated_RendersValuesAndTransport()
    {
        var message = AcpInitializeTimeout.CreateTimeoutMessage(
            TransportType.WebSocket,
            "p1",
            "c1",
            TimeSpan.FromMilliseconds(1500));

        Assert.Equal(
            "Timed out waiting for ACP initialize response. "
            + "profileId=p1 transport=WebSocket timeoutSeconds=1.5 conversationId=c1",
            message);
    }

    [Theory]
    [InlineData(120000, "120")]
    [InlineData(250, "0.25")]
    [InlineData(1500, "1.5")]
    public void CreateTimeoutMessage_FormatsTimeoutSecondsToThreeDecimalPlaces(int ms, string expected)
    {
        var message = AcpInitializeTimeout.CreateTimeoutMessage(
            TransportType.Stdio,
            null,
            null,
            TimeSpan.FromMilliseconds(ms));

        Assert.Contains($"timeoutSeconds={expected}", message);
    }

    [Fact]
    public void Resolve_WhenProfileNull_DelegatesToDefaultTimeout()
    {
        // The adapter must not NRE on a null profile; it collapses to the policy default.
        Assert.Equal(
            AcpConnectionTimeoutPolicy.ResolveTimeout(0),
            AcpInitializeTimeout.Resolve(null));
    }

    [Fact]
    public void Resolve_WhenProfileConfigured_DelegatesToConfiguredTimeout()
    {
        var profile = new ServerConfiguration { ConnectionTimeout = 30 };

        Assert.Equal(
            AcpConnectionTimeoutPolicy.ResolveTimeout(30),
            AcpInitializeTimeout.Resolve(profile));
    }

    [Fact]
    public async Task WaitForInitializeAsync_WhenInitializeNeverCompletes_RaisesEnrichedTimeout()
    {
        var chatService = new Mock<IChatService>();
        var neverCompletes = new TaskCompletionSource<InitializeResponse>();
        chatService
            .Setup(c => c.InitializeAsync(It.IsAny<InitializeParams>()))
            .Returns(neverCompletes.Task);
        var timeout = TimeSpan.FromMilliseconds(100);

        var ex = await Assert.ThrowsAsync<TimeoutException>(() =>
            AcpInitializeTimeout.WaitForInitializeAsync(
                chatService.Object,
                TransportType.Stdio,
                "p1",
                "c1",
                timeout,
                CancellationToken.None));

        // The user-facing message is exactly the enriched diagnostic, and the original
        // framework timeout is preserved as the inner exception for diagnostics.
        Assert.Equal(
            AcpInitializeTimeout.CreateTimeoutMessage(TransportType.Stdio, "p1", "c1", timeout),
            ex.Message);
        Assert.NotNull(ex.InnerException);
    }

    [Fact]
    public async Task WaitForInitializeAsync_WhenInitializeCompletes_ReturnsResponse()
    {
        var chatService = new Mock<IChatService>();
        var expected = new InitializeResponse();
        chatService
            .Setup(c => c.InitializeAsync(It.IsAny<InitializeParams>()))
            .ReturnsAsync(expected);

        var actual = await AcpInitializeTimeout.WaitForInitializeAsync(
            chatService.Object,
            TransportType.Stdio,
            "p1",
            "c1",
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        Assert.Same(expected, actual);
    }
}
