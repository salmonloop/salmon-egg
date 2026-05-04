using System.Threading;
using System.Threading.Tasks;
using Moq;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.Protocol;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Services.Chat;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public sealed class ChatConversationProfileConnectionGatewayTests
{
    [Fact]
    public void CreateConnectionContext_WhenConversationHasMatchingRemoteBinding_PreservesConversation()
    {
        var gateway = new ChatConversationProfileConnectionGateway();

        var context = gateway.CreateConnectionContext(
            "conv-1",
            new ConversationBindingSlice("conv-1", "remote-1", "profile-1"),
            "profile-1",
            preserveConversation: true,
            activationVersion: 7);

        Assert.Equal("conv-1", context.ConversationId);
        Assert.True(context.PreserveConversation);
        Assert.Equal(7, context.ActivationVersion);
    }

    [Fact]
    public void CreateConnectionContext_WhenBindingDoesNotMatchProfile_DisablesPreserveConversation()
    {
        var gateway = new ChatConversationProfileConnectionGateway();

        var context = gateway.CreateConnectionContext(
            "conv-1",
            new ConversationBindingSlice("conv-1", "remote-1", "profile-2"),
            "profile-1",
            preserveConversation: true,
            activationVersion: 7);

        Assert.Equal("conv-1", context.ConversationId);
        Assert.False(context.PreserveConversation);
        Assert.Equal(7, context.ActivationVersion);
    }

    [Fact]
    public async Task ConnectAsync_WhenContextIsAmbient_UsesAmbientOverload()
    {
        var commands = new Mock<IAcpConnectionCommands>(MockBehavior.Strict);
        var gateway = new ChatConversationProfileConnectionGateway();
        var profile = new ServerConfiguration { Id = "profile-1", Transport = TransportType.Stdio };
        var transport = Mock.Of<IAcpTransportConfiguration>();
        var sink = Mock.Of<IAcpChatCoordinatorSink>();
        var expected = new AcpTransportApplyResult(Mock.Of<IChatService>(), new InitializeResponse());

        commands.Setup(x => x.ConnectToProfileAsync(
                profile,
                transport,
                sink,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await gateway.ConnectAsync(
            commands.Object,
            profile,
            transport,
            sink,
            AcpConnectionContext.None,
            CancellationToken.None);

        Assert.Equal(expected, result);
        commands.Verify(x => x.ConnectToProfileAsync(profile, transport, sink, It.IsAny<CancellationToken>()), Times.Once);
        commands.Verify(x => x.ConnectToProfileAsync(profile, transport, sink, It.IsAny<AcpConnectionContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ConnectAsync_WhenContextTargetsConversation_UsesConversationAwareOverload()
    {
        var commands = new Mock<IAcpConnectionCommands>(MockBehavior.Strict);
        var gateway = new ChatConversationProfileConnectionGateway();
        var profile = new ServerConfiguration { Id = "profile-1", Transport = TransportType.Stdio };
        var transport = Mock.Of<IAcpTransportConfiguration>();
        var sink = Mock.Of<IAcpChatCoordinatorSink>();
        var context = new AcpConnectionContext("conv-1", PreserveConversation: true, ActivationVersion: 5);
        var expected = new AcpTransportApplyResult(Mock.Of<IChatService>(), new InitializeResponse());

        commands.Setup(x => x.ConnectToProfileAsync(
                profile,
                transport,
                sink,
                context,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await gateway.ConnectAsync(
            commands.Object,
            profile,
            transport,
            sink,
            context,
            CancellationToken.None);

        Assert.Equal(expected, result);
        commands.Verify(x => x.ConnectToProfileAsync(profile, transport, sink, context, It.IsAny<CancellationToken>()), Times.Once);
        commands.Verify(x => x.ConnectToProfileAsync(profile, transport, sink, It.IsAny<CancellationToken>()), Times.Never);
    }
}
