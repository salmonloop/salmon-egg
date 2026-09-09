using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SalmonEgg.Acp.JsonRpc;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Core.Tests.Threading;
using SalmonEgg.Presentation.ViewModels.Chat;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public partial class ChatViewModelTests
{
    [Fact]
    public async Task EnsureNewSessionDraftAsync_NullResponseWithoutReconnect_FaultsInsteadOfRetrying()
    {
        // A null service response is invalid; only an actual authentication reconnect permits retry.
        await using var fixture = CreateViewModel();
        var service = CreateConnectedChatService();
        service.Setup(x => x.CreateSessionAsync(It.IsAny<SessionNewParams>()))
            .ReturnsAsync((SessionNewResponse)null!);
        await fixture.ViewModel.ReplaceChatServiceAsync(service.Object, TestContext.Current.CancellationToken);
        await fixture.DispatchConnectionAsync(new SetForegroundTransportProfileAction("profile-1"));
        await fixture.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-1"));
        await fixture.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));

        await fixture.ViewModel.EnsureNewSessionDraftAsync("/work", TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var state = await fixture.GetConnectionStateAsync();
        Assert.Equal(NewSessionDraftPhase.Faulted, state.NewSessionDraft?.Phase);
        Assert.False(fixture.ViewModel.IsNewSessionDraftLoading);
        Assert.False(fixture.ViewModel.IsNewSessionDraftReady);
        service.Verify(x => x.CreateSessionAsync(It.IsAny<SessionNewParams>()), Times.Once);
        service.Verify(x => x.AuthenticateAsync(It.IsAny<AuthenticateParams>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TerminalAuthentication_DraftCreation_RetriesThroughFreshConnection(bool missingExplicitIntent)
    {
        // Arrange: use the real ViewModel draft owner, consent coordinator and public connect path.
        var original = new Mock<IChatService>();
        original.As<IStdioInvocationSource>().SetupGet(x => x.StdioInvocation).Returns(
            new StdioInvocationSnapshot("agent", ["--acp"], new Dictionary<string, string>(), "/work", false));
        original.SetupGet(x => x.IsConnected).Returns(true);
        original.SetupGet(x => x.IsInitialized).Returns(true);
        original.Setup(x => x.CreateSessionAsync(It.IsAny<SessionNewParams>())).ThrowsAsync(
            new AcpException(JsonRpcErrorCode.AuthenticationRequired, "Sign in"));
        var fresh = CreateConnectedChatService();
        fresh.Setup(x => x.CreateSessionAsync(It.IsAny<SessionNewParams>())).ReturnsAsync(new SessionNewResponse("draft-after-login"));
        var terminalSession = new Mock<ITerminalAuthenticationSession>();
        terminalSession.SetupGet(x => x.Completion).Returns(Task.FromResult<int?>(0));
        var sessionFactory = new Mock<ITerminalAuthenticationSessionFactory>();
        sessionFactory.SetupGet(x => x.IsSupported).Returns(true);
        sessionFactory.Setup(x => x.StartAsync(It.IsAny<StdioInvocationSnapshot>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(terminalSession.Object);
        var interaction = new Mock<ITerminalAuthenticationInteraction>();
        interaction.Setup(x => x.ConfirmAsync(It.IsAny<TerminalAuthenticationViewModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        interaction.Setup(x => x.ShowSessionAsync(It.IsAny<TerminalAuthenticationViewModel>(), It.IsAny<CancellationToken>()))
            .Returns<TerminalAuthenticationViewModel, CancellationToken>((_, token) => Task.Delay(Timeout.InfiniteTimeSpan, token));
        await using var authentication = new TerminalAuthenticationCoordinator(sessionFactory.Object, interaction.Object,
            new ImmediateUiDispatcher(), NullLogger<TerminalAuthenticationCoordinator>.Instance);
        var commands = new Mock<IAcpConnectionCommands>();
        var profile = CreateConnectableStdioProfile("profile-terminal", "Terminal agent");
        var response = new InitializeResponse
        {
            ProtocolVersion = 1,
            AuthMethods = [new AuthMethodDefinition { Id = "login", Name = "Sign in", Type = "terminal" }]
        };
        await using var fixture = CreateViewModel(acpConnectionCommands: commands.Object, terminalAuthenticationCoordinator: authentication);
        fixture.Profiles.Profiles.Add(profile);
        commands.Setup(x => x.ConnectToProfileAsync(profile, It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(), It.IsAny<CancellationToken>()))
            .Returns<ServerConfiguration, IAcpTransportConfiguration, IAcpChatCoordinatorSink, CancellationToken>(
                async (_, _, sink, token) =>
                {
                    await sink.ReplaceChatServiceAsync(original.Object, token);
                    await fixture.DispatchConnectionAsync(new SetForegroundTransportProfileAction(profile.Id));
                    await fixture.DispatchConnectionAsync(new SetConnectionInstanceIdAction("before-login"));
                    await fixture.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));
                    return new AcpTransportApplyResult(original.Object, response);
                });
        AcpConnectionContext? observedContext = null;
        commands.Setup(x => x.ConnectToProfileAsync(profile, It.IsAny<IAcpTransportConfiguration>(),
                It.IsAny<IAcpChatCoordinatorSink>(), It.IsAny<AcpConnectionContext>(), It.IsAny<CancellationToken>()))
            .Returns<ServerConfiguration, IAcpTransportConfiguration, IAcpChatCoordinatorSink, AcpConnectionContext, CancellationToken>(
                async (_, _, sink, context, token) =>
                {
                    observedContext = context;
                    await sink.ReplaceChatServiceAsync(fresh.Object, ServiceReplaceIntent.PoolOnly, token);
                    await fixture.DispatchConnectionAsync(new SetConnectionInstanceIdAction("after-login"));
                    await fixture.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));
                    return new AcpTransportApplyResult(fresh.Object, new InitializeResponse());
                });
        await fixture.ViewModel.ConnectToAcpProfileCommand.ExecuteAsync(profile);
        if (missingExplicitIntent) await fixture.DispatchConnectionAsync(new SetSelectedProfileIntentAction(null));
        await fixture.ApplyCurrentStoreProjectionAsync();

        // Act: the original session/new fails, signs in, reconnects, and re-enters the same draft owner.
        await fixture.ViewModel.EnsureNewSessionDraftAsync("/work", TestContext.Current.CancellationToken);

        // Assert: no authenticate request or second call reaches the stale service.
        var state = await fixture.GetConnectionStateAsync();
        Assert.True(observedContext?.ForceReconnect);
        Assert.Same(original.Object, observedContext?.ExpectedChatService);
        Assert.Equal("draft-after-login", state.NewSessionDraft?.RemoteSessionId);
        Assert.Equal("after-login", state.NewSessionDraft?.ConnectionInstanceId);
        Assert.Equal(NewSessionDraftPhase.Ready, state.NewSessionDraft?.Phase);
        original.Verify(x => x.CreateSessionAsync(It.IsAny<SessionNewParams>()), Times.Once);
        original.Verify(x => x.AuthenticateAsync(It.IsAny<AuthenticateParams>(), It.IsAny<CancellationToken>()), Times.Never);
        fresh.Verify(x => x.CreateSessionAsync(It.IsAny<SessionNewParams>()), Times.Once);
        terminalSession.Verify(x => x.DisposeAsync(), Times.Once);
    }
}
