using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using SalmonEgg.Acp.Client;
using SalmonEgg.Acp.Content;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Domain.Models.Session;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Core.Tests.Threading;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public partial class ChatViewModelTests
{
    [Fact(Timeout = 15000)]
    public async Task ResetConversationForResync_PreservesFailedTurnAndUnreadWhileReleasingOnlyItsBodyAnchor()
    {
        // Arrange
        await using var fixture = CreateViewModel();
        var body = new ConversationMessageSnapshot { Id = "message", ContentType = "text", TextContent = "Old reply" };
        var failedTurn = new ActiveTurnState("a", "failed-turn", ChatTurnPhase.Failed, DateTime.UtcNow, DateTime.UtcNow,
            FailureMessage: "Needs retry", ProfileId: "profile", RemoteSessionId: "remote-a", ConnectionInstanceId: "connection");
        var fault = new ConversationOperationFailure("a", "Needs reconnect");
        await fixture.UpdateStateAsync(_ => ChatState.Empty with
        {
            HydratedConversationId = "a",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty.Add("a", new("a", "remote-a", "profile")),
            ConversationContents = ImmutableDictionary<string, ConversationContentSlice>.Empty.Add("a",
                new(ImmutableList.Create(body), ImmutableList<ConversationPlanEntrySnapshot>.Empty, false)),
            Turns = ImmutableDictionary<string, ActiveTurnState>.Empty.Add("a", failedTurn),
            OperationFailures = ImmutableDictionary<string, ConversationOperationFailure>.Empty.Add("a", fault)
        }).AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await fixture.DispatchAttentionAsync(new MarkConversationUnreadAction("a", ConversationAttentionSource.AgentMessage,
            DateTime.UtcNow, "profile", "remote-a", body, "connection")).AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await fixture.DispatchAttentionAsync(new MarkConversationUnreadAction("b", ConversationAttentionSource.AgentMessage,
            DateTime.UtcNow, "profile", "remote-b", body, "connection")).AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Act
        await fixture.ViewModel.ResetConversationForResyncAsync("a", TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Assert
        var state = await fixture.GetStateAsync();
        Assert.Equal(failedTurn, state.ResolveTurn("a"));
        Assert.Equal(fault, state.ResolveOperationFailure("a"));
        Assert.Empty(state.ResolveContentSlice("a")!.Value.Transcript);
        var attention = await fixture.GetAttentionStateAsync();
        Assert.True(attention.Conversations["a"].HasUnread);
        Assert.Equal(1, attention.Conversations["a"].UnreadVersion);
        Assert.Null(attention.Conversations["a"].Content);
        Assert.Null(attention.Conversations["a"].ContentConnectionInstanceId);
        Assert.Same(body, attention.Conversations["b"].Content);
    }

    [Fact(Timeout = 15000)]
    public async Task ConversationOperationFailures_WhenTwoConversationsFail_RetainsEachFailureAcrossSelectionAndScopedClear()
    {
        // Arrange
        await using var fixture = CreateViewModel();
        await fixture.UpdateStateAsync(_ => ChatState.Empty with { HydratedConversationId = "a" }).AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.False(await fixture.ViewModel.HydrateActiveConversationAsync(TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        var failureA = (await fixture.GetStateAsync()).ResolveOperationFailure("a");
        Assert.NotNull(failureA);

        // Act
        await fixture.DispatchAsync(new SelectConversationAction("b")).AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.False(await fixture.ViewModel.HydrateActiveConversationAsync(TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        var failureB = (await fixture.GetStateAsync()).ResolveOperationFailure("b");
        await fixture.DispatchAsync(new ClearConversationOperationFailureAction("b")).AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await fixture.DispatchAsync(new SelectConversationAction("a")).AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Assert
        var state = await fixture.GetStateAsync();
        Assert.NotNull(failureB);
        Assert.Equal(failureA, state.ResolveOperationFailure("a"));
        Assert.Null(state.ResolveOperationFailure("b"));
        Assert.Equal(failureA.Message, fixture.ViewModel.ConversationOperationFailureMessage);
    }

    [Fact(Timeout = 15000)]
    public async Task DirectConnection_WithRegistryAndNoProfile_ReceivesUpdatesAfterInitializationIdentityChanges()
    {
        // Arrange
        await using var fixture = CreateViewModel(connectionSessionRegistry: new InMemoryAcpConnectionSessionRegistry());
        var service = CreateConnectedChatService();
        await fixture.ViewModel.ReplaceChatServiceAsync(service.Object, TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await fixture.DispatchConnectionAsync(new SetConnectionInstanceIdAction("direct-connection")).AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await fixture.UpdateStateAsync(_ => ChatState.Empty with
        {
            HydratedConversationId = "conversation",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty.Add("conversation", new("conversation", "remote", null)),
            Turns = ImmutableDictionary<string, ActiveTurnState>.Empty.Add("conversation", new("conversation", "turn", ChatTurnPhase.Thinking,
                DateTime.UtcNow, DateTime.UtcNow, RemoteSessionId: "remote", ConnectionInstanceId: "direct-connection"))
        }).AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await WaitForConditionAsync(() => Task.FromResult(fixture.ViewModel.ConnectionInstanceId == "direct-connection"),
            timeoutMilliseconds: 5000).WaitAsync(TestContext.Current.CancellationToken);

        // Act
        service.Raise(chat => chat.SessionUpdateReceived += null,
            new SessionUpdateEventArgs("remote", new AgentMessageUpdate(new TextContentBlock("Direct reply"))));
        await WaitForPendingSessionUpdatesAsync(fixture.ViewModel)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Assert
        var state = await fixture.GetStateAsync();
        Assert.Equal(ChatTurnPhase.Responding, state.ResolveTurn("conversation")?.Phase);
        Assert.Equal("Direct reply", Assert.Single(state.ResolveContentSlice("conversation")!.Value.Transcript).TextContent);
    }

    [Fact(Timeout = 15000)]
    public async Task RegisteredConnections_WhenForegroundChanges_RouteSameRemoteIdToTheirOwnConversations()
    {
        // Arrange
        var registry = new InMemoryAcpConnectionSessionRegistry();
        await using var fixture = CreateViewModel(connectionSessionRegistry: registry);
        var first = CreateConnectedChatService();
        var second = CreateConnectedChatService();
        using var firstAdapter = CreateBackgroundAdapter(first.Object);
        using var secondAdapter = CreateBackgroundAdapter(second.Object);
        registry.Upsert(CreateBackgroundSession("profile-a", "connection-a", firstAdapter));
        registry.Upsert(CreateBackgroundSession("profile-b", "connection-b", secondAdapter));
        await fixture.UpdateStateAsync(_ => CreateTwoBackgroundConversations()).AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await fixture.ViewModel.ReplaceChatServiceAsync(firstAdapter, TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await fixture.ViewModel.ReplaceChatServiceAsync(secondAdapter, TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Act
        first.Raise(service => service.SessionUpdateReceived += null,
            new SessionUpdateEventArgs("shared-remote", new AgentMessageUpdate(new TextContentBlock("A result"))));
        second.Raise(service => service.SessionUpdateReceived += null,
            new SessionUpdateEventArgs("shared-remote", new AgentMessageUpdate(new TextContentBlock("B result"))));
        await WaitForPendingSessionUpdatesAsync(fixture.ViewModel)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Assert
        var state = await fixture.GetStateAsync();
        Assert.Equal("b", state.HydratedConversationId);
        Assert.Equal("A result", Assert.Single(state.ResolveContentSlice("a")!.Value.Transcript).TextContent);
        Assert.Equal("B result", Assert.Single(state.ResolveContentSlice("b")!.Value.Transcript).TextContent);
        Assert.Equal(ChatTurnPhase.Responding, state.ResolveTurn("a")?.Phase);
        Assert.Equal(ChatTurnPhase.Responding, state.ResolveTurn("b")?.Phase);
        Assert.Same(secondAdapter, fixture.ViewModel.CurrentChatService);
    }

    [Fact(Timeout = 15000)]
    public async Task RegisteredConnections_WhenOldConnectionRetires_CancelsOnlyItsTurnAndRejectsLateUpdates()
    {
        // Arrange
        var registry = new InMemoryAcpConnectionSessionRegistry();
        await using var fixture = CreateViewModel(connectionSessionRegistry: registry);
        var first = CreateConnectedChatService();
        var second = CreateConnectedChatService();
        using var firstAdapter = CreateBackgroundAdapter(first.Object);
        using var secondAdapter = CreateBackgroundAdapter(second.Object);
        registry.Upsert(CreateBackgroundSession("profile-a", "connection-a", firstAdapter));
        registry.Upsert(CreateBackgroundSession("profile-b", "connection-b", secondAdapter));
        await fixture.UpdateStateAsync(_ => CreateTwoBackgroundConversations()).AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await fixture.ViewModel.ReplaceChatServiceAsync(secondAdapter, TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Act
        registry.RemoveByProfile("profile-a", AcpConnectionRetirementReason.Disconnected);
        first.Raise(service => service.SessionUpdateReceived += null,
            new SessionUpdateEventArgs("shared-remote", new AgentMessageUpdate(new TextContentBlock("Late A"))));
        second.Raise(service => service.SessionUpdateReceived += null,
            new SessionUpdateEventArgs("shared-remote", new AgentMessageUpdate(new TextContentBlock("B continues"))));
        await WaitForPendingSessionUpdatesAsync(fixture.ViewModel)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Assert
        var state = await fixture.GetStateAsync();
        Assert.Equal(ChatTurnPhase.Cancelled, state.ResolveTurn("a")?.Phase);
        Assert.Equal(ChatTurnPhase.Responding, state.ResolveTurn("b")?.Phase);
        Assert.Empty(state.ResolveContentSlice("a")?.Transcript ?? ImmutableList<ConversationMessageSnapshot>.Empty);
        Assert.Equal("B continues", Assert.Single(state.ResolveContentSlice("b")!.Value.Transcript).TextContent);
        Assert.Same(secondAdapter, fixture.ViewModel.CurrentChatService);
    }

    [Fact(Timeout = 15000)]
    public async Task RegisteredConnections_WhenBackgroundResyncs_RecoversThatSessionWithoutChangingForeground()
    {
        // Arrange
        var registry = new InMemoryAcpConnectionSessionRegistry();
        var sessionManager = new Mock<ISessionManager>();
        sessionManager.Setup(manager => manager.GetSession("a")).Returns(new Session("a", "/tmp/background-project"));
        await using var fixture = CreateViewModel(connectionSessionRegistry: registry, sessionManager: sessionManager);
        var first = CreateConnectedChatService();
        first.SetupGet(service => service.AgentCapabilities).Returns(new AgentCapabilities(loadSession: true));
        var second = CreateConnectedChatService();
        using var firstAdapter = CreateBackgroundAdapter(first.Object);
        using var secondAdapter = CreateBackgroundAdapter(second.Object);
        registry.Upsert(CreateBackgroundSession("profile-a", "connection-a", firstAdapter));
        registry.Upsert(CreateBackgroundSession("profile-b", "connection-b", secondAdapter));
        var initial = CreateTwoBackgroundConversations();
        await fixture.UpdateStateAsync(_ => initial with
        {
            ConversationSessionStates = ImmutableDictionary<string, ConversationSessionStateSlice>.Empty.Add("a",
                new(ImmutableList<ConversationModeOptionSnapshot>.Empty, null,
                    ImmutableList<ConversationConfigOptionSnapshot>.Empty, false,
                    ImmutableList<ConversationAvailableCommandSnapshot>.Empty,
                    new ConversationSessionInfoSnapshot { Cwd = "/tmp/background-project" }, null))
        }).AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await fixture.ViewModel.ReplaceChatServiceAsync(secondAdapter, TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        first.Setup(service => service.LoadSessionAsync(It.IsAny<SessionLoadParams>(), It.IsAny<System.Threading.CancellationToken>()))
            .Returns((SessionLoadParams _, System.Threading.CancellationToken _) =>
            {
                first.Raise(service => service.SessionUpdateReceived += null,
                    new SessionUpdateEventArgs("shared-remote", new AgentMessageUpdate(new TextContentBlock("Recovered A"))));
                return Task.FromResult(SessionLoadResponse.Completed);
            });

        // Act
        var recovery = firstAdapter.RequestResyncAsync("shared-remote");
        bool handled;
        try
        {
            handled = await recovery.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
        finally
        {
            if (!recovery.IsCompleted)
            {
                registry.RemoveByProfile("profile-a", AcpConnectionRetirementReason.Shutdown);
                await recovery.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            }
        }

        // Assert
        Assert.True(handled);
        var state = await fixture.GetStateAsync();
        Assert.Equal("b", state.HydratedConversationId);
        Assert.Equal(ConversationRuntimePhase.Warm, state.ResolveRuntimeState("a")?.Phase);
        Assert.Contains(state.ResolveContentSlice("a")!.Value.Transcript, message => message.TextContent == "Recovered A");
        Assert.Same(secondAdapter, fixture.ViewModel.CurrentChatService);
        first.Verify(service => service.LoadSessionAsync(It.Is<SessionLoadParams>(request => request.SessionId == "shared-remote"),
            It.IsAny<System.Threading.CancellationToken>()), Times.Once);
        second.Verify(service => service.LoadSessionAsync(It.IsAny<SessionLoadParams>(), It.IsAny<System.Threading.CancellationToken>()), Times.Never);
    }

    private static ChatState CreateTwoBackgroundConversations()
    {
        var now = DateTime.UtcNow;
        return ChatState.Empty with
        {
            HydratedConversationId = "b",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("a", new("a", "shared-remote", "profile-a"))
                .Add("b", new("b", "shared-remote", "profile-b")),
            RuntimeStates = ImmutableDictionary<string, ConversationRuntimeSlice>.Empty
                .Add("a", new("a", ConversationRuntimePhase.Warm, "connection-a", "shared-remote", "profile-a", ConversationRuntimeReasons.SessionLoadCompleted, now))
                .Add("b", new("b", ConversationRuntimePhase.Warm, "connection-b", "shared-remote", "profile-b", ConversationRuntimeReasons.SessionLoadCompleted, now)),
            Turns = ImmutableDictionary<string, ActiveTurnState>.Empty
                .Add("a", new("a", "turn-a", ChatTurnPhase.Thinking, now, now, ProfileId: "profile-a", RemoteSessionId: "shared-remote", ConnectionInstanceId: "connection-a"))
                .Add("b", new("b", "turn-b", ChatTurnPhase.Thinking, now, now, ProfileId: "profile-b", RemoteSessionId: "shared-remote", ConnectionInstanceId: "connection-b"))
        };
    }

    private static AcpChatServiceAdapter CreateBackgroundAdapter(IChatService service)
    {
        AcpChatServiceAdapter? adapter = null;
        var events = new AcpEventAdapter(update => adapter!.PublishBufferedUpdate(update), new ImmediateUiDispatcher());
        adapter = new AcpChatServiceAdapter(service, events);
        adapter.ReleaseUnscopedBufferedUpdates();
        return adapter;
    }

    private static AcpConnectionSession CreateBackgroundSession(string profile, string connection, AcpChatServiceAdapter adapter)
        => new(profile, adapter, new InitializeResponse(), new AcpConnectionReuseKey(TransportType.Stdio, profile, "", ""), connection);
}
