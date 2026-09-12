using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Domain.Models.Conversation;
using System.Collections.Immutable;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat.Mvux;

public class ChatReducerTests
{
    [Fact]
    public void GivenInitialState_WhenSetSelectedConversation_ThenHydratedConversationIdIsUpdated()
    {
        // Arrange
        var initialState = new ChatState();
        var conversationId = "test-conv-123";
        var action = new SelectConversationAction(conversationId);

        // Act
        var newState = ChatReducer.Reduce(initialState, action);

        // Assert
        Assert.Equal(conversationId, newState.HydratedConversationId);
    }

    [Fact]
    public void GivenState_WhenSetDraftText_ThenDraftTextIsUpdated()
    {
        // Arrange
        var initialState = new ChatState(DraftText: string.Empty);
        var action = new SetDraftTextAction("hello");

        // Act
        var newState = ChatReducer.Reduce(initialState, action);

        // Assert
        Assert.Equal("hello", newState.DraftText);
    }

    [Fact]
    public void GivenState_WhenSetDraftText_ThenDraftRevisionIncrements()
    {
        // Arrange
        var initialState = new ChatState(DraftText: "old", DraftRevision: 7);
        var action = new SetDraftTextAction("hello");

        // Act
        var newState = ChatReducer.Reduce(initialState, action);

        // Assert
        Assert.Equal(8, newState.DraftRevision);
    }

    [Fact]
    public void GivenEmptyState_WhenSetBindingSlice_ThenBindingAndGenerationAreUpdated()
    {
        // Arrange
        var initialState = ChatState.Empty;
        var binding = new ConversationBindingSlice("conv-1", "remote-1", "profile-1");

        // Act
        var newState = ChatReducer.Reduce(initialState, new SetBindingSliceAction(binding));

        // Assert
        Assert.Equal(binding, newState.ResolveBinding("conv-1"));
        Assert.Equal(1, newState.Generation);
    }

    [Theory]
    [InlineData("conv-2", 7)]
    [InlineData("conv-1", 6)]
    public void SetDraftText_StaleConversationOrRevision_PreservesCurrentDraft(string expectedConversation, long expectedRevision)
    {
        var state = ChatState.Empty with { HydratedConversationId = "conv-1", DraftText = "new input", DraftRevision = 7 };

        var result = ChatReducer.Reduce(state, new SetDraftTextAction("old input", expectedConversation, expectedRevision));

        Assert.Same(state, result);
    }

    [Fact]
    public void SetDraftText_MatchingConversationAndRevision_UpdatesDraftOnce()
    {
        var state = ChatState.Empty with { HydratedConversationId = "conv-1", DraftText = "input", DraftRevision = 7 };
        var action = new SetDraftTextAction(string.Empty, "conv-1", 7);

        var result = ChatReducer.Reduce(state, action);

        Assert.Equal(string.Empty, result.DraftText);
        Assert.Equal(8, result.DraftRevision);
        Assert.Same(result, ChatReducer.Reduce(result, action));
    }

    [Fact]
    public void ApplyBindingUpdate_ScopedRecovery_PreservesCurrentTurnWithoutRestoringStalePhase()
    {
        var original = new ActiveTurnState("conv-1", "turn-1", ChatTurnPhase.Thinking, DateTime.UtcNow, DateTime.UtcNow,
            ProfileId: "profile", RemoteSessionId: "old-remote", ConnectionInstanceId: "connection");
        var currentTurn = original with { Phase = ChatTurnPhase.ToolRunning };
        var state = ChatState.Empty with
        {
            Turns = ImmutableDictionary<string, ActiveTurnState>.Empty.Add("conv-1", currentTurn)
        };
        var action = new ApplyBindingUpdateAction(
            new("conv-1", "new-remote", "profile"),
            ImmutableDictionary<string, ConversationSessionInfoSnapshot?>.Empty,
            ImmutableHashSet.Create("conv-1"),
            original);

        var result = ChatReducer.Reduce(state, action);

        Assert.Same(currentTurn, result.ResolveTurn("conv-1"));
        Assert.Equal("new-remote", result.ResolveBinding("conv-1")!.RemoteSessionId);
    }

    [Fact]
    public void ApplyBindingUpdate_StaleRecoveryTurn_RejectsBindingAndPreservesNewTurn()
    {
        var original = new ActiveTurnState("conv-1", "old-turn", ChatTurnPhase.Thinking, DateTime.UtcNow, DateTime.UtcNow,
            ProfileId: "profile", RemoteSessionId: "old-remote", ConnectionInstanceId: "connection");
        var state = ChatState.Empty with
        {
            Turns = ImmutableDictionary<string, ActiveTurnState>.Empty.Add("conv-1", original with { TurnId = "new-turn" })
        };

        var result = ChatReducer.Reduce(state, new ApplyBindingUpdateAction(
            new("conv-1", "new-remote", "profile"),
            ImmutableDictionary<string, ConversationSessionInfoSnapshot?>.Empty,
            ImmutableHashSet.Create("conv-1"),
            original));

        Assert.Same(state, result);
    }

    [Fact]
    public void GivenMultipleBindings_WhenSelectingConversation_ThenMatchingBindingProjectsFromDictionary()
    {
        var initialState = ChatState.Empty with
        {
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1"))
                .Add("conv-2", new ConversationBindingSlice("conv-2", "remote-2", "profile-2"))
        };

        var newState = ChatReducer.Reduce(initialState, new SelectConversationAction("conv-2"));

        Assert.Equal("conv-2", newState.HydratedConversationId);
        Assert.Equal(new ConversationBindingSlice("conv-2", "remote-2", "profile-2"), newState.Binding);
    }

    [Fact]
    public void GivenRemoteHydratingRuntime_WhenPromoteToWarm_ThenRuntimeBecomesWarm()
    {
        // Arrange
        var state = ChatReducer.Reduce(
            ChatState.Empty,
            new SetConversationRuntimeStateAction(CreateRuntime(ConversationRuntimePhase.RemoteHydrating)));

        // Act
        var next = ChatReducer.Reduce(
            state,
            new PromoteConversationRuntimeToWarmAction(
                CreateRuntime(ConversationRuntimePhase.Warm, reason: ConversationRuntimeReasons.SessionLoadCompleted)));

        // Assert
        var runtime = next.ResolveRuntimeState("conv-1");
        Assert.Equal(ConversationRuntimePhase.Warm, runtime?.Phase);
        Assert.Equal(ConversationRuntimeReasons.SessionLoadCompleted, runtime?.Reason);
    }

    [Fact]
    public void GivenHydratingRuntimeOnDifferentConnection_WhenStalePromoteToWarm_ThenPromotionIsRejected()
    {
        // Arrange:同会话更新激活已在新连接身份上重建 RemoteHydrating(case study
        // 「后台恢复完成的权威晋升」验证场景 3),旧身份的过时完成不得先戳 Warm。
        var state = ChatReducer.Reduce(
            ChatState.Empty,
            new SetConversationRuntimeStateAction(
                CreateRuntime(ConversationRuntimePhase.RemoteHydrating, connectionInstanceId: "conn-newer")));

        // Act
        var next = ChatReducer.Reduce(
            state,
            new PromoteConversationRuntimeToWarmAction(
                CreateRuntime(ConversationRuntimePhase.Warm, connectionInstanceId: "conn-old")));

        // Assert
        Assert.Equal(ConversationRuntimePhase.RemoteHydrating, next.ResolveRuntimeState("conv-1")?.Phase);
        Assert.Same(state, next);
    }

    [Fact]
    public void GivenHydratingRuntimeOnDifferentRemoteSession_WhenStalePromoteToWarm_ThenPromotionIsRejected()
    {
        // Arrange:同会话 rebind 后以新 remote session 身份重建 RemoteHydrating,
        // 旧 remote session 的过时完成不得晋升。
        var state = ChatReducer.Reduce(
            ChatState.Empty,
            new SetConversationRuntimeStateAction(
                CreateRuntime(ConversationRuntimePhase.RemoteHydrating, remoteSessionId: "remote-newer")));

        // Act
        var next = ChatReducer.Reduce(
            state,
            new PromoteConversationRuntimeToWarmAction(
                CreateRuntime(ConversationRuntimePhase.Warm, remoteSessionId: "remote-old")));

        // Assert
        Assert.Equal(ConversationRuntimePhase.RemoteHydrating, next.ResolveRuntimeState("conv-1")?.Phase);
        Assert.Same(state, next);
    }

    [Theory]
    [InlineData(ConversationRuntimePhase.Selecting)]
    [InlineData(ConversationRuntimePhase.Selected)]
    [InlineData(ConversationRuntimePhase.RemoteConnectionReady)]
    [InlineData(ConversationRuntimePhase.Stale)]
    [InlineData(ConversationRuntimePhase.Faulted)]
    public void GivenRuntimeResetByNewerActivation_WhenStalePromoteToWarm_ThenPromotionIsRejected(
        ConversationRuntimePhase currentPhase)
    {
        // Arrange:同会话更新激活已把 runtime 重置到更早/终态阶段(case study「后台恢复完成的权威晋升」唯一例外)。
        var state = ChatReducer.Reduce(
            ChatState.Empty,
            new SetConversationRuntimeStateAction(CreateRuntime(currentPhase)));

        // Act
        var next = ChatReducer.Reduce(
            state,
            new PromoteConversationRuntimeToWarmAction(CreateRuntime(ConversationRuntimePhase.Warm)));

        // Assert
        Assert.Equal(currentPhase, next.ResolveRuntimeState("conv-1")?.Phase);
        Assert.Same(state, next);
    }

    [Fact]
    public void GivenMissingRuntime_WhenPromoteToWarm_ThenPromotionIsRejected()
    {
        // Act
        var next = ChatReducer.Reduce(
            ChatState.Empty,
            new PromoteConversationRuntimeToWarmAction(CreateRuntime(ConversationRuntimePhase.Warm)));

        // Assert
        Assert.Null(next.ResolveRuntimeState("conv-1"));
    }

    [Fact]
    public void GivenWarmRuntimeWithSameIdentity_WhenPromoteToWarm_ThenRestampIsApplied()
    {
        // Arrange
        var state = ChatReducer.Reduce(
            ChatState.Empty,
            new SetConversationRuntimeStateAction(CreateRuntime(ConversationRuntimePhase.Warm, reason: "old")));

        // Act
        var next = ChatReducer.Reduce(
            state,
            new PromoteConversationRuntimeToWarmAction(CreateRuntime(ConversationRuntimePhase.Warm, reason: "new")));

        // Assert
        Assert.Equal("new", next.ResolveRuntimeState("conv-1")?.Reason);
    }

    [Fact]
    public void GivenWarmRuntimeOnDifferentConnection_WhenStalePromoteToWarm_ThenPromotionIsRejected()
    {
        // Arrange:更新激活已在新连接上完成权威 Warm,过时完成不得改写其身份。
        var state = ChatReducer.Reduce(
            ChatState.Empty,
            new SetConversationRuntimeStateAction(
                CreateRuntime(ConversationRuntimePhase.Warm, connectionInstanceId: "conn-newer")));

        // Act
        var next = ChatReducer.Reduce(
            state,
            new PromoteConversationRuntimeToWarmAction(
                CreateRuntime(ConversationRuntimePhase.Warm, connectionInstanceId: "conn-old")));

        // Assert
        Assert.Equal("conn-newer", next.ResolveRuntimeState("conv-1")?.ConnectionInstanceId);
        Assert.Same(state, next);
    }

    [Fact]
    public void GivenState_WhenRuntimeMutationOccurs_ThenGenerationIncrements()
    {
        // Arrange
        var initialState = ChatState.Empty with { Generation = 2 };

        // Act
        var newState = ChatReducer.Reduce(initialState, new SetDraftTextAction("hi"));

        // Assert
        Assert.Equal(3, newState.Generation);
    }

    [Fact]
    public void GivenConnectionState_WhenPhaseChangesToConnected_ThenConnectionInstanceIdIsPreservedAndGenerationIncrements()
    {
        var initialState = ChatConnectionState.Empty with
        {
            Phase = ConnectionPhase.Connecting,
            SelectedProfileIntentId = "profile-1",
            ConnectionInstanceId = "conn-1",
            Generation = 7
        };

        var next = ChatConnectionReducer.Reduce(initialState, new SetConnectionPhaseAction(ConnectionPhase.Connected));

        Assert.Equal(ConnectionPhase.Connected, next.Phase);
        Assert.Equal("profile-1", next.SelectedProfileIntentId);
        Assert.Equal("conn-1", next.ConnectionInstanceId);
        Assert.Equal(8, next.Generation);
    }

    [Fact]
    public void GivenConnectionState_WhenSelectedProfileChanges_ThenConnectionInstanceIdIsPreservedAndGenerationIncrements()
    {
        var initialState = ChatConnectionState.Empty with
        {
            Phase = ConnectionPhase.Connected,
            SelectedProfileIntentId = "profile-1",
            ConnectionInstanceId = "conn-1",
            Generation = 4
        };

        var next = ChatConnectionReducer.Reduce(initialState, new SetSelectedProfileIntentAction("profile-2"));

        Assert.Equal("profile-2", next.SelectedProfileIntentId);
        Assert.Equal("conn-1", next.ConnectionInstanceId);
        Assert.Equal(ConnectionPhase.Connected, next.Phase);
        Assert.Equal(5, next.Generation);
    }

    [Fact]
    public void GivenEmptyProfileIntent_WhenProfileIntentIsInitialized_ThenPreferenceBecomesAuthoritative()
    {
        var initialState = ChatConnectionState.Empty with { Generation = 4 };

        var next = ChatConnectionReducer.Reduce(
            initialState,
            new InitializeSelectedProfileIntentAction("profile-preferred"));

        Assert.Equal("profile-preferred", next.SelectedProfileIntentId);
        Assert.Equal(5, next.Generation);
    }

    [Fact]
    public void GivenAuthoritativeProfileIntent_WhenProfileIntentInitializationRaces_ThenAuthoritativeIntentIsPreserved()
    {
        var initialState = ChatConnectionState.Empty with
        {
            SelectedProfileIntentId = "profile-authoritative",
            Generation = 4
        };

        var next = ChatConnectionReducer.Reduce(
            initialState,
            new InitializeSelectedProfileIntentAction("profile-preferred"));

        Assert.Same(initialState, next);
    }

    [Fact]
    public void GivenConnectionState_WhenConnectionInstanceIdChanges_ThenOnlyIdentityAndGenerationUpdate()
    {
        var initialState = ChatConnectionState.Empty with
        {
            Phase = ConnectionPhase.Connected,
            SelectedProfileIntentId = "profile-1",
            Error = "previous-error",
            IsAuthenticationRequired = true,
            AuthenticationHintMessage = "hint",
            ConnectionInstanceId = "conn-old",
            ForegroundTransportProfileId = "profile-1",
            Generation = 12
        };

        var next = ChatConnectionReducer.Reduce(initialState, new SetConnectionInstanceIdAction("conn-new"));

        Assert.Equal("conn-new", next.ConnectionInstanceId);
        Assert.Equal(ConnectionPhase.Connected, next.Phase);
        Assert.Equal("profile-1", next.SelectedProfileIntentId);
        Assert.Equal("previous-error", next.Error);
        Assert.True(next.IsAuthenticationRequired);
        Assert.Equal("hint", next.AuthenticationHintMessage);
        Assert.Equal("profile-1", next.ForegroundTransportProfileId);
        Assert.Equal(13, next.Generation);
    }

    [Fact]
    public void Reduce_SetAuthenticationRequired_StoresPresentationIdentity()
    {
        // Arrange
        var formatArgs = new object[] { "denied" };
        var action = new SetConnectionAuthenticationStateAction(
            IsRequired: true,
            HintMessage: "认证失败：denied",
            HintResourceKey: "ChatAuth_FailedWithDetail",
            HintFallback: "Authentication failed: {0}",
            HintFormatArgs: formatArgs);

        // Act
        var next = ChatConnectionReducer.Reduce(ChatConnectionState.Empty, action);

        // Assert
        Assert.True(next.IsAuthenticationRequired);
        Assert.Equal("认证失败：denied", next.AuthenticationHintMessage);
        Assert.Equal("ChatAuth_FailedWithDetail", next.AuthenticationHintResourceKey);
        Assert.Equal("Authentication failed: {0}", next.AuthenticationHintFallback);
        Assert.Same(formatArgs, next.AuthenticationHintFormatArgs);
    }

    [Fact]
    public void Reduce_ClearAuthenticationRequired_ClearsPresentationIdentity()
    {
        // Arrange
        var initialState = ChatConnectionState.Empty with
        {
            IsAuthenticationRequired = true,
            AuthenticationHintMessage = "认证失败：denied",
            AuthenticationHintResourceKey = "ChatAuth_FailedWithDetail",
            AuthenticationHintFallback = "Authentication failed: {0}",
            AuthenticationHintFormatArgs = ["denied"]
        };

        // Act
        var next = ChatConnectionReducer.Reduce(
            initialState,
            new SetConnectionAuthenticationStateAction(false, HintMessage: "ignored"));

        // Assert
        Assert.False(next.IsAuthenticationRequired);
        Assert.Null(next.AuthenticationHintMessage);
        Assert.Null(next.AuthenticationHintResourceKey);
        Assert.Null(next.AuthenticationHintFallback);
        Assert.Null(next.AuthenticationHintFormatArgs);
    }

    [Fact]
    public void GivenConnectionStateWithNewSessionDraft_WhenConnectionIdentityChanges_ThenDraftIsCleared()
    {
        var draft = new NewSessionDraftState(
            ProfileId: "profile-1",
            Cwd: @"C:\Repo\App",
            RemoteSessionId: "remote-draft",
            ConnectionInstanceId: "conn-old",
            Phase: NewSessionDraftPhase.Ready,
            Version: 1,
            AvailableModes: ImmutableList<ConversationModeOptionSnapshot>.Empty,
            SelectedModeId: null,
            ConfigOptions: ImmutableList<ConversationConfigOptionSnapshot>.Empty,
            ShowConfigOptionsPanel: false,
            AvailableCommands: ImmutableList<ConversationAvailableCommandSnapshot>.Empty,
            SessionInfo: null);
        var initialState = ChatConnectionState.Empty with
        {
            Phase = ConnectionPhase.Connected,
            ConnectionInstanceId = "conn-old",
            ForegroundTransportProfileId = "profile-1",
            NewSessionDraft = draft,
            Generation = 3
        };

        var next = ChatConnectionReducer.Reduce(initialState, new SetConnectionInstanceIdAction("conn-new"));

        Assert.Null(next.NewSessionDraft);
        Assert.Equal("conn-new", next.ConnectionInstanceId);
        Assert.Equal(4, next.Generation);
    }

    [Fact]
    public void GivenConnectionStateWithNewSessionDraft_WhenSettingsSelectedProfileChanges_ThenDraftIsRetainedForLifecycleCleanup()
    {
        var draft = new NewSessionDraftState(
            ProfileId: "profile-1",
            Cwd: @"C:\Repo\App",
            RemoteSessionId: "remote-draft",
            ConnectionInstanceId: "conn-1",
            Phase: NewSessionDraftPhase.Ready,
            Version: 1,
            AvailableModes: ImmutableList<ConversationModeOptionSnapshot>.Empty,
            SelectedModeId: null,
            ConfigOptions: ImmutableList<ConversationConfigOptionSnapshot>.Empty,
            ShowConfigOptionsPanel: false,
            AvailableCommands: ImmutableList<ConversationAvailableCommandSnapshot>.Empty,
            SessionInfo: null);
        var initialState = ChatConnectionState.Empty with
        {
            Phase = ConnectionPhase.Connected,
            SelectedProfileIntentId = "profile-1",
            ConnectionInstanceId = "conn-1",
            ForegroundTransportProfileId = "profile-1",
            NewSessionDraft = draft,
            Generation = 4
        };

        var next = ChatConnectionReducer.Reduce(initialState, new SetSelectedProfileIntentAction("profile-2"));

        Assert.Equal("profile-2", next.SelectedProfileIntentId);
        Assert.Same(draft, next.NewSessionDraft);
        Assert.Equal(5, next.Generation);
    }

    [Fact]
    public void GivenConnectionStateWithNewSessionDraft_WhenSettingsSelectedProfileMatches_ThenDraftIsRetained()
    {
        var draft = new NewSessionDraftState(
            ProfileId: "profile-1",
            Cwd: @"C:\Repo\App",
            RemoteSessionId: "remote-draft",
            ConnectionInstanceId: "conn-1",
            Phase: NewSessionDraftPhase.Ready,
            Version: 1,
            AvailableModes: ImmutableList<ConversationModeOptionSnapshot>.Empty,
            SelectedModeId: null,
            ConfigOptions: ImmutableList<ConversationConfigOptionSnapshot>.Empty,
            ShowConfigOptionsPanel: false,
            AvailableCommands: ImmutableList<ConversationAvailableCommandSnapshot>.Empty,
            SessionInfo: null);
        var initialState = ChatConnectionState.Empty with
        {
            Phase = ConnectionPhase.Connected,
            SelectedProfileIntentId = "profile-1",
            ConnectionInstanceId = "conn-1",
            ForegroundTransportProfileId = "profile-1",
            NewSessionDraft = draft,
            Generation = 4
        };

        var next = ChatConnectionReducer.Reduce(initialState, new SetSelectedProfileIntentAction("profile-1"));

        Assert.Equal("profile-1", next.SelectedProfileIntentId);
        Assert.Same(draft, next.NewSessionDraft);
        Assert.Equal(5, next.Generation);
    }

    [Fact]
    public void GivenConnectionState_WhenNewSessionDraftIsSet_ThenItBecomesSingleConnectionDraft()
    {
        var draft = new NewSessionDraftState(
            ProfileId: "profile-1",
            Cwd: @"C:\Repo\App",
            RemoteSessionId: "remote-draft",
            ConnectionInstanceId: "conn-1",
            Phase: NewSessionDraftPhase.Ready,
            Version: 2,
            AvailableModes: ImmutableList.Create(new ConversationModeOptionSnapshot
            {
                ModeId = "code",
                ModeName = "Code"
            }),
            SelectedModeId: "code",
            ConfigOptions: ImmutableList<ConversationConfigOptionSnapshot>.Empty,
            ShowConfigOptionsPanel: false,
            AvailableCommands: ImmutableList<ConversationAvailableCommandSnapshot>.Empty,
            SessionInfo: null);

        var next = ChatConnectionReducer.Reduce(
            ChatConnectionState.Empty with { Generation = 9 },
            new SetNewSessionDraftAction(draft));

        Assert.Same(draft, next.NewSessionDraft);
        Assert.Equal("remote-draft", next.NewSessionDraft?.RemoteSessionId);
        Assert.Equal(10, next.Generation);
    }

    [Fact]
    public void GivenConnectionState_WhenDisconnected_ThenConnectionInstanceIdIsPreservedAndGenerationIncrements()
    {
        var connectedState = ChatConnectionState.Empty with
        {
            Phase = ConnectionPhase.Connected,
            SelectedProfileIntentId = "profile-1",
            ConnectionInstanceId = "conn-1",
            Generation = 20
        };

        var disconnected = ChatConnectionReducer.Reduce(
            connectedState,
            new SetConnectionPhaseAction(ConnectionPhase.Disconnected, Error: "network error"));

        Assert.Equal(ConnectionPhase.Disconnected, disconnected.Phase);
        Assert.Equal("conn-1", disconnected.ConnectionInstanceId);
        Assert.Null(disconnected.ForegroundTransportProfileId);
        Assert.Equal("network error", disconnected.Error);
        Assert.Equal(21, disconnected.Generation);
    }

    [Fact]
    public void GivenConnectionState_WhenReset_ThenConnectionInstanceIdIsPreservedAndGenerationIncrements()
    {
        var connectedState = ChatConnectionState.Empty with
        {
            Phase = ConnectionPhase.Connected,
            SelectedProfileIntentId = "profile-1",
            ConnectionInstanceId = "conn-1",
            Generation = 20
        };

        var reset = ChatConnectionReducer.Reduce(connectedState, new ResetConnectionStateAction());

        Assert.Equal("conn-1", reset.ConnectionInstanceId);
        Assert.Equal(ConnectionPhase.Disconnected, reset.Phase);
        Assert.Equal("profile-1", reset.SelectedProfileIntentId);
        Assert.Null(reset.ForegroundTransportProfileId);
        Assert.Equal(21, reset.Generation);
    }

    [Fact]
    public void GivenState_WhenSetConversationRuntimeState_ThenRuntimeStateIsStored()
    {
        var initialState = ChatState.Empty;
        var runtimeState = new ConversationRuntimeSlice(
            "conv-1",
            ConversationRuntimePhase.RemoteHydrating,
            ConnectionInstanceId: "conn-3",
            RemoteSessionId: "remote-1",
            ProfileId: "profile-1",
            Reason: "SessionLoadStarted",
            UpdatedAtUtc: new DateTime(2026, 4, 4, 0, 0, 0, DateTimeKind.Utc));

        var next = ChatReducer.Reduce(initialState, new SetConversationRuntimeStateAction(runtimeState));

        Assert.Equal(runtimeState, next.ResolveRuntimeState("conv-1"));
        Assert.Equal(1, next.Generation);
    }

    [Fact]
    public void GivenState_WhenSetConversationRuntimeStateWithBlankConversation_ThenNoMutation()
    {
        var initialState = ChatState.Empty with { Generation = 7 };
        var runtimeState = new ConversationRuntimeSlice(
            "",
            ConversationRuntimePhase.Warm,
            ConnectionInstanceId: "conn-1",
            RemoteSessionId: "remote-1",
            ProfileId: "profile-1",
            Reason: null,
            UpdatedAtUtc: DateTime.UtcNow);

        var next = ChatReducer.Reduce(initialState, new SetConversationRuntimeStateAction(runtimeState));

        Assert.Equal(initialState.Generation, next.Generation);
    }

    [Fact]
    public void GivenRuntimeState_WhenClearConversationRuntimeState_ThenEntryRemoved()
    {
        var runtimeState = new ConversationRuntimeSlice(
            "conv-1",
            ConversationRuntimePhase.Warm,
            ConnectionInstanceId: "conn-1",
            RemoteSessionId: "remote-1",
            ProfileId: "profile-1",
            Reason: "seed",
            UpdatedAtUtc: DateTime.UtcNow);
        var initialState = ChatReducer.Reduce(
            ChatState.Empty,
            new SetConversationRuntimeStateAction(runtimeState));

        var next = ChatReducer.Reduce(initialState, new ClearConversationRuntimeStateAction("conv-1"));

        Assert.Null(next.ResolveRuntimeState("conv-1"));
    }

    [Fact]
    public void GivenRuntimeStates_WhenResetConversationRuntimeStates_ThenAllEntriesCleared()
    {
        var seeded = ChatReducer.Reduce(
            ChatState.Empty,
            new SetConversationRuntimeStateAction(new ConversationRuntimeSlice(
                "conv-1",
                ConversationRuntimePhase.Warm,
                ConnectionInstanceId: "conn-1",
                RemoteSessionId: "remote-1",
                ProfileId: "profile-1",
                Reason: "seed",
                UpdatedAtUtc: DateTime.UtcNow)));
        seeded = ChatReducer.Reduce(
            seeded,
            new SetConversationRuntimeStateAction(new ConversationRuntimeSlice(
                "conv-2",
                ConversationRuntimePhase.Stale,
                ConnectionInstanceId: "conn-1",
                RemoteSessionId: "remote-2",
                ProfileId: "profile-1",
                Reason: "seed",
                UpdatedAtUtc: DateTime.UtcNow)));

        var reset = ChatReducer.Reduce(seeded, new ResetConversationRuntimeStatesAction());

        Assert.Null(reset.ResolveRuntimeState("conv-1"));
        Assert.Null(reset.ResolveRuntimeState("conv-2"));
    }

    [Fact]
    public void GivenBackgroundConversationMessage_WhenUpdated_ThenGenerationIncrementsAndActiveProjectionStaysUnchanged()
    {
        var initialState = ChatState.Empty with { HydratedConversationId = "conv-1", Generation = 5 };
        var message = new ConversationMessageSnapshot
        {
            Id = "m-1",
            ContentType = "text",
            TextContent = "hello"
        };

        var newState = ChatReducer.Reduce(initialState, new UpsertTranscriptMessageAction("conv-2", message));

        Assert.Equal(6, newState.Generation);
        Assert.True(newState.Transcript is null or { Count: 0 });
        Assert.NotNull(newState.ResolveContentSlice("conv-2"));
    }

    [Fact]
    public void GivenBackgroundConversationUpdate_WhenSelectingThatConversation_ThenTranscriptProjectsFromStoredSlice()
    {
        var initialState = ChatState.Empty with
        {
            HydratedConversationId = "conv-1"
        };
        var message = new ConversationMessageSnapshot
        {
            Id = "m-bg-1",
            ContentType = "text",
            TextContent = "background"
        };

        var updated = ChatReducer.Reduce(initialState, new UpsertTranscriptMessageAction("conv-2", message));
        var selected = ChatReducer.Reduce(updated, new SelectConversationAction("conv-2"));

        Assert.Equal("conv-2", selected.HydratedConversationId);
        Assert.NotNull(selected.Transcript);
        Assert.Single(selected.Transcript!);
        Assert.Equal("background", selected.Transcript[0].TextContent);
    }

    [Fact]
    public void UpsertTranscript_WhenMessageIdsAreEmpty_DoesNotCollapseDistinctMessages()
    {
        // Production defect: ConversationMessageSnapshot.Id defaults to string.Empty.
        // Upsert matched by raw string.Equals(Id), so every empty-Id snapshot replaced the
        // first empty-Id row and silently dropped distinct transcript content (corrupt
        // persistence, partial projectors, or any path that forgot to assign Id).
        var initialState = ChatState.Empty with { HydratedConversationId = "conv-1" };
        var first = new ConversationMessageSnapshot
        {
            Id = string.Empty,
            ContentType = "text",
            TextContent = "first empty-id message"
        };
        var second = new ConversationMessageSnapshot
        {
            Id = string.Empty,
            ContentType = "tool_call",
            Title = "Read file",
            ToolCallId = "tool-1"
        };

        var afterFirst = ChatReducer.Reduce(initialState, new UpsertTranscriptMessageAction("conv-1", first));
        var afterSecond = ChatReducer.Reduce(afterFirst, new UpsertTranscriptMessageAction("conv-1", second));

        var transcript = afterSecond.ResolveContentSlice("conv-1")?.Transcript;
        Assert.NotNull(transcript);
        Assert.Equal(2, transcript!.Count);
        Assert.Equal("first empty-id message", transcript[0].TextContent);
        Assert.Equal("tool_call", transcript[1].ContentType);
        Assert.Equal("tool-1", transcript[1].ToolCallId);
    }

    [Fact]
    public void UpsertTranscript_WhenMessageIdIsStable_StillReplacesSameRow()
    {
        var initialState = ChatState.Empty with { HydratedConversationId = "conv-1" };
        var original = new ConversationMessageSnapshot
        {
            Id = "m-stable",
            ContentType = "text",
            TextContent = "before"
        };
        var replacement = new ConversationMessageSnapshot
        {
            Id = "m-stable",
            ContentType = "text",
            TextContent = "after"
        };

        var afterOriginal = ChatReducer.Reduce(initialState, new UpsertTranscriptMessageAction("conv-1", original));
        var afterReplace = ChatReducer.Reduce(afterOriginal, new UpsertTranscriptMessageAction("conv-1", replacement));

        var transcript = afterReplace.ResolveContentSlice("conv-1")?.Transcript;
        Assert.NotNull(transcript);
        var message = Assert.Single(transcript!);
        Assert.Equal("m-stable", message.Id);
        Assert.Equal("after", message.TextContent);
    }

    [Fact]
    public void GivenBackgroundConversationSessionState_WhenSelectingThatConversation_ThenSessionStateProjectsFromStoredSlice()
    {
        var initialState = ChatState.Empty with
        {
            HydratedConversationId = "conv-1"
        };

        var updated = ChatReducer.Reduce(
            initialState,
            new SetConversationSessionStateAction(
                "conv-2",
                ImmutableList.Create(new ConversationModeOptionSnapshot { ModeId = "agent", ModeName = "Agent" }),
                "agent",
                ImmutableList.Create(new ConversationConfigOptionSnapshot { Id = "mode", Name = "Mode", SelectedValue = "agent" }),
                true));

        var selected = ChatReducer.Reduce(updated, new SelectConversationAction("conv-2"));

        Assert.Equal("conv-2", selected.HydratedConversationId);
        Assert.NotNull(selected.AvailableModes);
        Assert.Single(selected.AvailableModes!);
        Assert.Equal("agent", selected.SelectedModeId);
        Assert.NotNull(selected.ConfigOptions);
        Assert.Single(selected.ConfigOptions!);
        Assert.True(selected.ShowConfigOptionsPanel);
    }

    [Fact]
    public void GivenConversationState_WhenSelectConversation_ThenConversationSliceIsCleared()
    {
        var initialState = new ChatState(
            HydratedConversationId: "conv-1",
            Transcript: ImmutableList.Create(new ConversationMessageSnapshot { Id = "m-1", TextContent = "hello", ContentType = "text" }),
            PlanEntries: ImmutableList.Create(new ConversationPlanEntrySnapshot { Content = "step-1" }),
            AvailableModes: ImmutableList.Create(new ConversationModeOptionSnapshot { ModeId = "agent", ModeName = "Agent" }),
            SelectedModeId: "agent",
            ConfigOptions: ImmutableList.Create(new ConversationConfigOptionSnapshot { Id = "mode", Name = "Mode", SelectedValue = "agent" }),
            ShowConfigOptionsPanel: true,
            ShowPlanPanel: true);

        var newState = ChatReducer.Reduce(initialState, new SelectConversationAction("conv-2"));

        Assert.Equal("conv-2", newState.HydratedConversationId);
        Assert.Null(newState.Transcript);
        Assert.Null(newState.PlanEntries);
        Assert.Null(newState.AvailableModes);
        Assert.Null(newState.SelectedModeId);
        Assert.Null(newState.ConfigOptions);
        Assert.False(newState.ShowConfigOptionsPanel);
        Assert.False(newState.ShowPlanPanel);
    }

    [Fact]
    public void GivenHydratingConversation_WhenSelectConversation_ThenHydrationFlagIsCleared()
    {
        var initialState = ChatState.Empty with
        {
            HydratedConversationId = "conv-1",
            IsHydrating = true
        };

        var newState = ChatReducer.Reduce(initialState, new SelectConversationAction("conv-2"));

        Assert.Equal("conv-2", newState.HydratedConversationId);
        Assert.False(newState.IsHydrating);
    }

    [Fact]
    public void SetConversationSessionState_StoresBackgroundConversationWithoutMutatingActiveProjection()
    {
        var initialState = ChatState.Empty with
        {
            HydratedConversationId = "conv-1",
            Generation = 11
        };

        var action = new SetConversationSessionStateAction(
            "conv-1",
            ImmutableList.Create(new ConversationModeOptionSnapshot { ModeId = "agent", ModeName = "Agent" }),
            "agent",
            ImmutableList.Create(new ConversationConfigOptionSnapshot { Id = "mode", Name = "Mode", SelectedValue = "agent" }),
            true);

        var projected = ChatReducer.Reduce(initialState, action);
        Assert.Equal(12, projected.Generation);
        Assert.NotNull(projected.AvailableModes);
        Assert.Single(projected.AvailableModes!);
        Assert.Equal("agent", projected.SelectedModeId);
        Assert.NotNull(projected.ConfigOptions);
        Assert.Single(projected.ConfigOptions!);
        Assert.True(projected.ShowConfigOptionsPanel);
        var projectedSlice = projected.ResolveSessionStateSlice("conv-1");
        Assert.NotNull(projectedSlice);
        Assert.Single(projectedSlice!.Value.AvailableModes);
        Assert.Equal("agent", projectedSlice.Value.SelectedModeId);
        Assert.Single(projectedSlice.Value.ConfigOptions);
        Assert.True(projectedSlice.Value.ShowConfigOptionsPanel);

        var stale = ChatReducer.Reduce(initialState, action with { ConversationId = "conv-2" });
        Assert.Equal(12, stale.Generation);
        Assert.Null(stale.AvailableModes);
        Assert.Null(stale.ConfigOptions);
        Assert.NotNull(stale.ResolveSessionStateSlice("conv-2"));
    }

    [Fact]
    public void MergeConversationSessionState_PreservesExistingValuesForPartialDelta()
    {
        var initialState = ChatState.Empty with
        {
            HydratedConversationId = "conv-1",
            Generation = 21,
            ConversationSessionStates = ImmutableDictionary<string, ConversationSessionStateSlice>.Empty.Add(
                "conv-1",
                new ConversationSessionStateSlice(
                    ImmutableList.Create(
                        new ConversationModeOptionSnapshot { ModeId = "agent", ModeName = "Agent" },
                        new ConversationModeOptionSnapshot { ModeId = "plan", ModeName = "Plan" }),
                    "agent",
                    ImmutableList.Create(
                        new ConversationConfigOptionSnapshot { Id = "mode", Name = "Mode", SelectedValue = "agent" }),
                    true,
                    ImmutableList<ConversationAvailableCommandSnapshot>.Empty,
                    null,
                    null))
        };

        var projected = ChatReducer.Reduce(initialState, new MergeConversationSessionStateAction(
            "conv-1",
            SelectedModeId: "plan",
            HasSelectedModeId: true));

        Assert.Equal(22, projected.Generation);
        var projectedSlice = projected.ResolveSessionStateSlice("conv-1");
        Assert.NotNull(projectedSlice);
        Assert.Equal(2, projectedSlice!.Value.AvailableModes.Count);
        Assert.Equal("plan", projectedSlice.Value.SelectedModeId);
        Assert.Single(projectedSlice.Value.ConfigOptions);
        Assert.True(projectedSlice.Value.ShowConfigOptionsPanel);

        var cleared = ChatReducer.Reduce(projected, new MergeConversationSessionStateAction(
            "conv-1",
            AvailableModes: ImmutableList<ConversationModeOptionSnapshot>.Empty,
            SelectedModeId: null,
            HasSelectedModeId: true));

        var clearedSlice = cleared.ResolveSessionStateSlice("conv-1");
        Assert.NotNull(clearedSlice);
        Assert.Empty(clearedSlice!.Value.AvailableModes);
        Assert.Null(clearedSlice.Value.SelectedModeId);
        Assert.Single(clearedSlice.Value.ConfigOptions);
    }

    [Fact]
    public void MergeConversationSessionState_PreservesExistingSessionInfoMetadata_ForPartialUpdates()
    {
        var initialState = ChatState.Empty with
        {
            HydratedConversationId = "conv-1",
            ConversationSessionStates = ImmutableDictionary<string, ConversationSessionStateSlice>.Empty.Add(
                "conv-1",
                new ConversationSessionStateSlice(
                    ImmutableList<ConversationModeOptionSnapshot>.Empty,
                    null,
                    ImmutableList<ConversationConfigOptionSnapshot>.Empty,
                    false,
                    ImmutableList<ConversationAvailableCommandSnapshot>.Empty,
                    new ConversationSessionInfoSnapshot
                    {
                        Title = "before",
                        Meta = new Dictionary<string, object?>(StringComparer.Ordinal)
                        {
                            ["existing"] = "value",
                            ["shared"] = "before"
                        }
                    },
                    null))
        };

        var next = ChatReducer.Reduce(initialState, new MergeConversationSessionStateAction(
            "conv-1",
            SessionInfo: new ConversationSessionInfoSnapshot
            {
                Meta = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["shared"] = "after",
                    ["added"] = 2
                }
            }));

        var sessionState = next.ResolveSessionStateSlice("conv-1");
        Assert.NotNull(sessionState);
        var sessionInfo = sessionState!.Value.SessionInfo;
        Assert.NotNull(sessionInfo);
        Assert.Equal("before", sessionInfo!.Title);
        Assert.Equal("value", sessionInfo.Meta!["existing"]);
        Assert.Equal("after", sessionInfo.Meta["shared"]);
        Assert.Equal(2, sessionInfo.Meta["added"]);
    }

    [Fact]
    public void MergeConversationSessionState_ReplacesTitleAndPreservesCwd_WhenIncomingTitleIsEmpty()
    {
        var initialState = ChatState.Empty with
        {
            HydratedConversationId = "conv-1",
            ConversationSessionStates = ImmutableDictionary<string, ConversationSessionStateSlice>.Empty.Add(
                "conv-1",
                new ConversationSessionStateSlice(
                    ImmutableList<ConversationModeOptionSnapshot>.Empty,
                    null,
                    ImmutableList<ConversationConfigOptionSnapshot>.Empty,
                    false,
                    ImmutableList<ConversationAvailableCommandSnapshot>.Empty,
                    new ConversationSessionInfoSnapshot
                    {
                        Title = "before title",
                        Cwd = @"C:\repo\before"
                    },
                    null))
        };

        var next = ChatReducer.Reduce(initialState, new MergeConversationSessionStateAction(
            "conv-1",
            SessionInfo: new ConversationSessionInfoSnapshot
            {
                Title = string.Empty,
                Cwd = "\t",
                UpdatedAtUtc = new DateTime(2026, 3, 3, 0, 0, 0, DateTimeKind.Utc)
            }));

        var sessionState = next.ResolveSessionStateSlice("conv-1");
        Assert.NotNull(sessionState);
        var sessionInfo = sessionState!.Value.SessionInfo;
        Assert.NotNull(sessionInfo);
        Assert.Equal(string.Empty, sessionInfo!.Title);
        Assert.Equal(@"C:\repo\before", sessionInfo.Cwd);
        Assert.Equal(new DateTime(2026, 3, 3, 0, 0, 0, DateTimeKind.Utc), sessionInfo.UpdatedAtUtc);
    }

    [Fact]
    public void MergeConversationSessionState_ReplacesWhitespaceTitle_AndMergesMetadata()
    {
        var initialState = ChatState.Empty with
        {
            HydratedConversationId = "conv-1",
            ConversationSessionStates = ImmutableDictionary<string, ConversationSessionStateSlice>.Empty.Add(
                "conv-1",
                new ConversationSessionStateSlice(
                    ImmutableList<ConversationModeOptionSnapshot>.Empty,
                    null,
                    ImmutableList<ConversationConfigOptionSnapshot>.Empty,
                    false,
                    ImmutableList<ConversationAvailableCommandSnapshot>.Empty,
                    new ConversationSessionInfoSnapshot
                    {
                        Title = "before title",
                        Cwd = @"C:\repo\before",
                        Meta = new Dictionary<string, object?>(StringComparer.Ordinal)
                        {
                            ["existing"] = "value",
                            ["shared"] = "before"
                        }
                    },
                    null))
        };

        var next = ChatReducer.Reduce(initialState, new MergeConversationSessionStateAction(
            "conv-1",
            SessionInfo: new ConversationSessionInfoSnapshot
            {
                Title = " ",
                Cwd = " ",
                UpdatedAtUtc = new DateTime(2026, 3, 4, 0, 0, 0, DateTimeKind.Utc),
                Meta = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["shared"] = "after",
                    ["added"] = 2
                }
            }));

        var sessionState = next.ResolveSessionStateSlice("conv-1");
        Assert.NotNull(sessionState);
        var sessionInfo = sessionState!.Value.SessionInfo;
        Assert.NotNull(sessionInfo);
        Assert.Equal(" ", sessionInfo!.Title);
        Assert.Equal(@"C:\repo\before", sessionInfo.Cwd);
        Assert.Equal(new DateTime(2026, 3, 4, 0, 0, 0, DateTimeKind.Utc), sessionInfo.UpdatedAtUtc);
        Assert.Equal("value", sessionInfo.Meta!["existing"]);
        Assert.Equal("after", sessionInfo.Meta["shared"]);
        Assert.Equal(2, sessionInfo.Meta["added"]);
    }

    [Fact]
    public void MergeConversationSessionState_EmptyTitleReplacesTitleAndPreservesOtherValues()
    {
        var initialState = ChatState.Empty with
        {
            HydratedConversationId = "conv-1",
            ConversationSessionStates = ImmutableDictionary<string, ConversationSessionStateSlice>.Empty.Add(
                "conv-1",
                new ConversationSessionStateSlice(
                    ImmutableList<ConversationModeOptionSnapshot>.Empty,
                    null,
                    ImmutableList<ConversationConfigOptionSnapshot>.Empty,
                    false,
                    ImmutableList<ConversationAvailableCommandSnapshot>.Empty,
                    new ConversationSessionInfoSnapshot
                    {
                        Title = "before",
                        Cwd = @"C:\repo\one",
                        UpdatedAtUtc = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)
                    },
                    null))
        };

        var next = ChatReducer.Reduce(initialState, new MergeConversationSessionStateAction(
            "conv-1",
            SessionInfo: new ConversationSessionInfoSnapshot
            {
                Title = string.Empty,
                Cwd = "\t",
                UpdatedAtUtc = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc)
            }));

        var sessionInfo = next.ResolveSessionStateSlice("conv-1")!.Value.SessionInfo;
        Assert.NotNull(sessionInfo);
        Assert.Equal(string.Empty, sessionInfo!.Title);
        Assert.Equal(@"C:\repo\one", sessionInfo.Cwd);
        Assert.Equal(new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc), sessionInfo.UpdatedAtUtc);
    }

    [Fact]
    public void SetConversationSessionState_ClonesSessionInfoMetadata_FromCallerOwnedDictionary()
    {
        var meta = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["source"] = "before"
        };
        var sessionInfo = new ConversationSessionInfoSnapshot
        {
            Title = "title",
            Meta = meta
        };

        var next = ChatReducer.Reduce(
            ChatState.Empty with { HydratedConversationId = "conv-1" },
            new SetConversationSessionStateAction(
                "conv-1",
                ImmutableList<ConversationModeOptionSnapshot>.Empty,
                null,
                ImmutableList<ConversationConfigOptionSnapshot>.Empty,
                false,
                ImmutableList<ConversationAvailableCommandSnapshot>.Empty,
                sessionInfo,
                null));

        meta["source"] = "after";
        sessionInfo.Meta!["added"] = 2;

        var sessionState = next.ResolveSessionStateSlice("conv-1");
        Assert.NotNull(sessionState);
        var storedSessionInfo = sessionState!.Value.SessionInfo;
        Assert.NotNull(storedSessionInfo);
        Assert.Equal("before", storedSessionInfo!.Meta!["source"]);
        Assert.False(storedSessionInfo.Meta.ContainsKey("added"));
        Assert.NotSame(meta, storedSessionInfo.Meta);
    }

    [Fact]
    public void GivenDifferentSelectedConversation_WhenHydrating_ThenReducerStoresSliceWithoutMutatingActiveProjection()
    {
        var initialState = new ChatState(HydratedConversationId: "conv-1", Generation: 7);
        var action = new HydrateConversationAction(
            "conv-2",
            ImmutableList.Create(new ConversationMessageSnapshot { Id = "m-1", TextContent = "stale", ContentType = "text" }),
            ImmutableList.Create(new ConversationPlanEntrySnapshot { Content = "step-1" }),
            true);

        var newState = ChatReducer.Reduce(initialState, action);

        Assert.Equal("conv-1", newState.HydratedConversationId);
        Assert.True(newState.Transcript is null or { Count: 0 });
        Assert.True(newState.PlanEntries is null or { Count: 0 });
        Assert.False(newState.ShowPlanPanel);
        Assert.Equal(8, newState.Generation);
        Assert.NotNull(newState.ResolveContentSlice("conv-2"));
    }

    [Fact]
    public void BeginTurn_StoresActiveTurnWithoutAdvancingWorkspaceGeneration()
    {
        var initialState = ChatState.Empty with { HydratedConversationId = "conv-1", Generation = 10 };
        var action = new BeginTurnAction(
            "conv-1",
            "turn-1",
            ChatTurnPhase.WaitingForAgent,
            PendingUserMessageLocalId: "local-1",
            PendingUserProtocolMessageId: "protocol-1",
            PendingUserMessageText: "hello",
            ProfileId: "profile-1",
            RemoteSessionId: "remote-1",
            ConnectionInstanceId: "connection-1");

        var newState = ChatReducer.Reduce(initialState, action);

        Assert.Equal(initialState.Generation, newState.Generation);
        Assert.NotNull(newState.ActiveTurn);
        Assert.Equal("turn-1", newState.ActiveTurn!.TurnId);
        Assert.Equal(ChatTurnPhase.WaitingForAgent, newState.ActiveTurn.Phase);
        Assert.Equal("local-1", newState.ActiveTurn.PendingUserMessageLocalId);
        Assert.Equal("protocol-1", newState.ActiveTurn.PendingUserProtocolMessageId);
        Assert.Equal("hello", newState.ActiveTurn.PendingUserMessageText);
        Assert.Equal("profile-1", newState.ActiveTurn.ProfileId);
        Assert.Equal("remote-1", newState.ActiveTurn.RemoteSessionId);
        Assert.Equal("connection-1", newState.ActiveTurn.ConnectionInstanceId);
        Assert.Null(initialState.ResolveTurn("conv-1"));
    }

    [Fact]
    public void AdvanceTurnPhase_IgnoresStaleTurnId()
    {
        var initialState = ChatState.Empty with
        {
            HydratedConversationId = "conv-1",
            Turns = ImmutableDictionary<string, ActiveTurnState>.Empty.Add(
                "conv-1", new ActiveTurnState("conv-1", "turn-current", ChatTurnPhase.Thinking, DateTime.UtcNow, DateTime.UtcNow)),
            Generation = 10
        };
        var action = new AdvanceTurnPhaseAction("conv-1", "turn-stale", ChatTurnPhase.Responding);

        var newState = ChatReducer.Reduce(initialState, action);

        Assert.Equal(10, newState.Generation);
        Assert.Equal(ChatTurnPhase.Thinking, newState.ActiveTurn!.Phase);
    }

    [Fact]
    public void AdvanceTurnPhase_IgnoresConversationMismatchEvenWhenTurnIdMatches()
    {
        var initialState = ChatState.Empty with
        {
            HydratedConversationId = "conv-1",
            Turns = ImmutableDictionary<string, ActiveTurnState>.Empty.Add(
                "conv-1", new ActiveTurnState("conv-1", "turn-1", ChatTurnPhase.Thinking, DateTime.UtcNow, DateTime.UtcNow)),
            Generation = 4
        };
        var action = new AdvanceTurnPhaseAction("conv-remote", "turn-1", ChatTurnPhase.Responding);

        var newState = ChatReducer.Reduce(initialState, action);

        Assert.Equal(4, newState.Generation);
        Assert.Equal(ChatTurnPhase.Thinking, newState.ActiveTurn!.Phase);
    }

    [Fact]
    public void SelectConversation_PreservesPreviousTurnAndRestoresItsProjectionWhenSelectedAgain()
    {
        var initialState = ChatState.Empty with
        {
            HydratedConversationId = "conv-1",
            Turns = ImmutableDictionary<string, ActiveTurnState>.Empty.Add(
                "conv-1", new ActiveTurnState("conv-1", "turn-1", ChatTurnPhase.Thinking, DateTime.UtcNow, DateTime.UtcNow))
        };
        var action = new SelectConversationAction("conv-2");

        var newState = ChatReducer.Reduce(initialState, action);

        Assert.Null(newState.ActiveTurn);
        Assert.Equal("conv-2", newState.HydratedConversationId);
        Assert.Same(initialState.ActiveTurn, newState.ResolveTurn("conv-1"));

        var selectedAgain = ChatReducer.Reduce(newState, new SelectConversationAction("conv-1"));
        Assert.Same(initialState.ActiveTurn, selectedAgain.ActiveTurn);
    }

    [Fact]
    public void SelectConversation_PreservesRunningActiveTurnForSameConversation()
    {
        var startedAt = DateTime.UtcNow;
        var activeTurn = new ActiveTurnState("conv-1", "turn-1", ChatTurnPhase.Responding, startedAt, startedAt);
        var initialState = ChatState.Empty with
        {
            HydratedConversationId = "conv-1",
            Turns = ImmutableDictionary<string, ActiveTurnState>.Empty.Add("conv-1", activeTurn),
            Generation = 12
        };

        var newState = ChatReducer.Reduce(initialState, new SelectConversationAction("conv-1"));

        Assert.Equal("conv-1", newState.HydratedConversationId);
        Assert.Equal(activeTurn, newState.ActiveTurn);
    }

    [Fact]
    public void ClearTerminalTurn_PreservesRunningActiveTurn()
    {
        var startedAt = DateTime.UtcNow;
        var initialState = ChatState.Empty with
        {
            HydratedConversationId = "conv-1",
            Turns = ImmutableDictionary<string, ActiveTurnState>.Empty.Add(
                "conv-1", new ActiveTurnState("conv-1", "turn-1", ChatTurnPhase.Responding, startedAt, startedAt)),
            Generation = 12
        };

        var newState = ChatReducer.Reduce(initialState, new ClearTerminalTurnAction("conv-1"));

        Assert.Equal(12, newState.Generation);
        Assert.Equal(initialState.ActiveTurn, newState.ActiveTurn);
    }

    [Fact]
    public void ClearTerminalTurn_ClearsTerminalActiveTurn()
    {
        var startedAt = DateTime.UtcNow;
        var initialState = ChatState.Empty with
        {
            HydratedConversationId = "conv-1",
            Turns = ImmutableDictionary<string, ActiveTurnState>.Empty.Add(
                "conv-1", new ActiveTurnState("conv-1", "turn-1", ChatTurnPhase.Completed, startedAt, startedAt)),
            Generation = 12
        };

        var newState = ChatReducer.Reduce(initialState, new ClearTerminalTurnAction("conv-1"));

        Assert.Equal(initialState.Generation, newState.Generation);
        Assert.Null(newState.ActiveTurn);
    }

    [Fact]
    public void CompleteTurn_DoesNotOverride_FailedOrCancelled()
    {
        var failedState = ChatState.Empty with
        {
            HydratedConversationId = "conv-1",
            Turns = ImmutableDictionary<string, ActiveTurnState>.Empty.Add(
                "conv-1", new ActiveTurnState("conv-1", "turn-1", ChatTurnPhase.Failed, DateTime.UtcNow, DateTime.UtcNow))
        };
        var action = new CompleteTurnAction("conv-1", "turn-1");

        var newState = ChatReducer.Reduce(failedState, action);

        Assert.Equal(ChatTurnPhase.Failed, newState.ActiveTurn!.Phase);

        var cancelledState = ChatState.Empty with
        {
            HydratedConversationId = "conv-1",
            Turns = ImmutableDictionary<string, ActiveTurnState>.Empty.Add(
                "conv-1", new ActiveTurnState("conv-1", "turn-1", ChatTurnPhase.Cancelled, DateTime.UtcNow, DateTime.UtcNow))
        };
        var newState2 = ChatReducer.Reduce(cancelledState, action);

        Assert.Equal(ChatTurnPhase.Cancelled, newState2.ActiveTurn!.Phase);
    }

    [Fact]
    public void AdvanceTurnPhase_DoesNotOverrideTerminalPhase()
    {
        var completedState = ChatState.Empty with
        {
            HydratedConversationId = "conv-1",
            Turns = ImmutableDictionary<string, ActiveTurnState>.Empty.Add(
                "conv-1", new ActiveTurnState("conv-1", "turn-1", ChatTurnPhase.Completed, DateTime.UtcNow, DateTime.UtcNow))
        };

        var newState = ChatReducer.Reduce(
            completedState,
            new AdvanceTurnPhaseAction("conv-1", "turn-1", ChatTurnPhase.Responding));

        Assert.Equal(ChatTurnPhase.Completed, newState.ActiveTurn!.Phase);
    }

    [Theory]
    [InlineData(ChatTurnPhase.WaitingForAgent, "turn-1")]
    [InlineData(ChatTurnPhase.Thinking, "turn-2")]
    [InlineData(ChatTurnPhase.Completed, "turn-1")]
    public void BeginTurn_ExistingRunningTurnOrRepeatedId_DoesNotReplaceTurn(
        ChatTurnPhase existingPhase,
        string nextTurnId)
    {
        // Arrange
        var now = DateTime.UtcNow;
        var initialState = ChatState.Empty with
        {
            Turns = ImmutableDictionary<string, ActiveTurnState>.Empty.Add(
                "conv-1", new ActiveTurnState("conv-1", "turn-1", existingPhase, now, now))
        };

        // Act
        var result = ChatReducer.Reduce(
            initialState,
            new BeginTurnAction("conv-1", nextTurnId, ChatTurnPhase.DispatchingPrompt));

        // Assert
        Assert.Same(initialState, result);
    }

    [Fact]
    public void BeginTurn_NewTurnAfterCompletion_ReplacesOnlyItsConversationAndRejectsOldResult()
    {
        // Arrange
        var initialState = ChatReducer.Reduce(ChatState.Empty,
            new BeginTurnAction("conv-1", "old-turn", ChatTurnPhase.Thinking));
        initialState = ChatReducer.Reduce(initialState,
            new BeginTurnAction("conv-2", "background-turn", ChatTurnPhase.ToolRunning));
        initialState = ChatReducer.Reduce(initialState,
            new CompleteTurnAction("conv-1", "old-turn", "end_turn", HasStopReason: true));

        // Act
        var result = ChatReducer.Reduce(initialState,
            new BeginTurnAction("conv-1", "new-turn", ChatTurnPhase.WaitingForAgent));
        var afterOldFailure = ChatReducer.Reduce(result,
            new FailTurnAction("conv-1", "old-turn", "late failure"));

        // Assert
        Assert.Same(result, afterOldFailure);
        Assert.Equal("new-turn", result.ResolveTurn("conv-1")!.TurnId);
        Assert.Equal(ChatTurnPhase.WaitingForAgent, result.ResolveTurn("conv-1")!.Phase);
        Assert.Null(result.ResolveTurn("conv-1")!.StopReason);
        Assert.False(result.ResolveTurn("conv-1")!.HasStopReason);
        Assert.Same(initialState.ResolveTurn("conv-2"), result.ResolveTurn("conv-2"));
    }

    [Theory]
    [InlineData(ChatTurnPhase.Responding)]
    [InlineData(ChatTurnPhase.Completed)]
    [InlineData(ChatTurnPhase.Failed)]
    [InlineData(ChatTurnPhase.Cancelled)]
    public void UpdateTurn_BackgroundConversation_LeavesForegroundTurnAndDraftUnchanged(ChatTurnPhase phase)
    {
        // Arrange
        var initialState = ChatState.Empty with
        {
            HydratedConversationId = "conv-2",
            DraftText = "new foreground draft",
            DraftRevision = 9,
            Generation = 3
        };
        initialState = ChatReducer.Reduce(initialState,
            new BeginTurnAction("conv-1", "same-turn-id", ChatTurnPhase.Thinking, ConnectionInstanceId: "connection-1"));
        initialState = ChatReducer.Reduce(initialState,
            new BeginTurnAction("conv-2", "same-turn-id", ChatTurnPhase.Thinking, ConnectionInstanceId: "connection-2"));

        // Act
        var result = ChatReducer.Reduce(initialState,
            CreateTurnUpdate(phase, "conv-1", "same-turn-id", "connection-1"));

        // Assert
        Assert.Equal(phase, result.ResolveTurn("conv-1")!.Phase);
        Assert.Same(initialState.ActiveTurn, result.ActiveTurn);
        Assert.Equal("conv-2", result.HydratedConversationId);
        Assert.Equal(initialState.DraftText, result.DraftText);
        Assert.Equal(initialState.DraftRevision, result.DraftRevision);
        Assert.Equal(initialState.Generation, result.Generation);
        Assert.Equal(2, result.Turns!.Count);
    }

    [Theory]
    [InlineData(ChatTurnPhase.Responding)]
    [InlineData(ChatTurnPhase.Completed)]
    [InlineData(ChatTurnPhase.Failed)]
    [InlineData(ChatTurnPhase.Cancelled)]
    public void UpdateTurn_StaleConversationTurnOrConnection_IsIgnored(ChatTurnPhase phase)
    {
        // Arrange
        var initialState = ChatReducer.Reduce(ChatState.Empty,
            new BeginTurnAction("conv-1", "turn-1", ChatTurnPhase.Thinking, ConnectionInstanceId: "connection-2"));
        var staleActions = new[]
        {
            CreateTurnUpdate(phase, "other-conversation", "turn-1", "connection-2"),
            CreateTurnUpdate(phase, "conv-1", "old-turn", "connection-2"),
            CreateTurnUpdate(phase, "conv-1", "turn-1", "connection-1"),
            CreateTurnUpdate(phase, "conv-1", "turn-1", null)
        };

        // Act / Assert
        foreach (var action in staleActions)
        {
            Assert.Same(initialState, ChatReducer.Reduce(initialState, action));
        }
    }

    [Theory]
    [InlineData(ChatTurnPhase.Completed)]
    [InlineData(ChatTurnPhase.Failed)]
    [InlineData(ChatTurnPhase.Cancelled)]
    public void UpdateTurn_TerminalTurn_RejectsFurtherPhaseAndTerminalUpdates(ChatTurnPhase terminalPhase)
    {
        // Arrange
        var state = ChatReducer.Reduce(ChatState.Empty,
            new BeginTurnAction("conv-1", "turn-1", ChatTurnPhase.Thinking));
        state = ChatReducer.Reduce(state, CreateTurnUpdate(terminalPhase, "conv-1", "turn-1", null));
        var nextPhases = new[]
        {
            ChatTurnPhase.Responding,
            ChatTurnPhase.Completed,
            ChatTurnPhase.Failed,
            ChatTurnPhase.Cancelled
        };

        // Act / Assert
        foreach (var phase in nextPhases)
        {
            Assert.Same(state, ChatReducer.Reduce(state, CreateTurnUpdate(phase, "conv-1", "turn-1", null)));
        }
    }

    [Theory]
    [InlineData("end_turn", true)]
    [InlineData("max_tokens", true)]
    [InlineData("max_turn_requests", true)]
    [InlineData("refusal", true)]
    [InlineData("future_reason", true)]
    [InlineData("_agent_extension", true)]
    [InlineData("", true)]
    [InlineData(null, false)]
    [InlineData("end_turn", false)]
    public void CompleteTurn_RawReasonAndPresence_ArePreserved(string? stopReason, bool hasStopReason)
    {
        // Arrange
        var state = ChatReducer.Reduce(ChatState.Empty,
            new BeginTurnAction("conv-1", "turn-1", ChatTurnPhase.Responding));

        // Act
        var result = ChatReducer.Reduce(state,
            new CompleteTurnAction("conv-1", "turn-1", stopReason, hasStopReason));

        // Assert
        var turn = Assert.IsType<ActiveTurnState>(result.ResolveTurn("conv-1"));
        Assert.Equal(ChatTurnPhase.Completed, turn.Phase);
        Assert.Equal(stopReason, turn.StopReason);
        Assert.Equal(hasStopReason, turn.HasStopReason);
        Assert.Null(result.ActiveTurn);
    }

    [Fact]
    public void FailAndCancelTurn_ProtocolReason_RemainsAvailableAfterTermination()
    {
        // Arrange
        var state = ChatReducer.Reduce(ChatState.Empty,
            new BeginTurnAction("conv-1", "turn-1", ChatTurnPhase.Responding));
        state = ChatReducer.Reduce(state,
            new BeginTurnAction("conv-2", "turn-2", ChatTurnPhase.Responding));

        // Act
        var result = ChatReducer.Reduce(state,
            new FailTurnAction("conv-1", "turn-1", "retry available", "max_tokens", HasStopReason: true));
        result = ChatReducer.Reduce(result,
            new CancelTurnAction("conv-2", "turn-2", "cancelled", HasStopReason: true));

        // Assert
        Assert.Equal(ChatTurnPhase.Failed, result.ResolveTurn("conv-1")!.Phase);
        Assert.Equal("retry available", result.ResolveTurn("conv-1")!.FailureMessage);
        Assert.Equal("max_tokens", result.ResolveTurn("conv-1")!.StopReason);
        Assert.True(result.ResolveTurn("conv-1")!.HasStopReason);
        Assert.Equal(ChatTurnPhase.Cancelled, result.ResolveTurn("conv-2")!.Phase);
        Assert.Equal("cancelled", result.ResolveTurn("conv-2")!.StopReason);
        Assert.True(result.ResolveTurn("conv-2")!.HasStopReason);
    }

    [Fact]
    public void SetTurnBinding_Reconnect_RejectsOldBindingAndOldConnectionResults()
    {
        // Arrange
        var state = ChatReducer.Reduce(ChatState.Empty,
            new BeginTurnAction("conv-1", "turn-1", ChatTurnPhase.CreatingRemoteSession));
        state = ChatReducer.Reduce(state,
            new SetTurnBindingAction("conv-1", "turn-1", "profile-1", "remote-1", "connection-1"));

        // Act
        var rebound = ChatReducer.Reduce(state,
            new SetTurnBindingAction("conv-1", "turn-1", "profile-1", "remote-1", "connection-2", "connection-1"));
        var staleBinding = ChatReducer.Reduce(rebound,
            new SetTurnBindingAction("conv-1", "turn-1", "old-profile", "old-remote", "connection-1"));
        var staleCompletion = ChatReducer.Reduce(rebound,
            new CompleteTurnAction("conv-1", "turn-1", "end_turn", true, "connection-1"));
        var completed = ChatReducer.Reduce(rebound,
            new CompleteTurnAction("conv-1", "turn-1", "end_turn", true, "connection-2"));

        // Assert
        Assert.Same(rebound, staleBinding);
        Assert.Same(rebound, staleCompletion);
        Assert.Equal("profile-1", rebound.ResolveTurn("conv-1")!.ProfileId);
        Assert.Equal("remote-1", rebound.ResolveTurn("conv-1")!.RemoteSessionId);
        Assert.Equal("connection-2", rebound.ResolveTurn("conv-1")!.ConnectionInstanceId);
        Assert.Equal(ChatTurnPhase.Completed, completed.ResolveTurn("conv-1")!.Phase);
        Assert.Equal("connection-2", completed.ResolveTurn("conv-1")!.ConnectionInstanceId);
    }

    [Fact]
    public void SetTurnBinding_StaleTurnOrTerminalTurn_IsIgnored()
    {
        // Arrange
        var state = ChatReducer.Reduce(ChatState.Empty,
            new BeginTurnAction("conv-1", "turn-1", ChatTurnPhase.CreatingRemoteSession));
        var binding = new SetTurnBindingAction("conv-1", "turn-1", "profile", "remote", "connection");

        // Act
        var wrongTurn = ChatReducer.Reduce(state, binding with { TurnId = "old-turn" });
        var wrongConversation = ChatReducer.Reduce(state, binding with { ConversationId = "conv-2" });
        var cancelled = ChatReducer.Reduce(state, new CancelTurnAction("conv-1", "turn-1"));
        var lateBinding = ChatReducer.Reduce(cancelled, binding);

        // Assert
        Assert.Same(state, wrongTurn);
        Assert.Same(state, wrongConversation);
        Assert.Same(cancelled, lateBinding);
    }

    [Fact]
    public void AdvanceTurnPhase_IdenticalPhaseAndTool_DoesNotRepublishOrChangeActivityTime()
    {
        // Arrange
        var state = ChatReducer.Reduce(ChatState.Empty,
            new BeginTurnAction("conv-1", "turn-1", ChatTurnPhase.ToolRunning));
        var action = new AdvanceTurnPhaseAction("conv-1", "turn-1", ChatTurnPhase.ToolRunning, "tool-1", "read_file");
        state = ChatReducer.Reduce(state, action);

        // Act
        var duplicate = ChatReducer.Reduce(state, action);

        // Assert
        Assert.Same(state, duplicate);
        Assert.Equal("tool-1", duplicate.ResolveTurn("conv-1")!.ToolCallId);
        Assert.Equal("read_file", duplicate.ResolveTurn("conv-1")!.ToolTitle);
    }

    [Fact]
    public void ClearTerminalTurn_BackgroundTurn_LeavesForegroundAndOtherTurnsUnchanged()
    {
        // Arrange
        var state = ChatState.Empty with { HydratedConversationId = "conv-2" };
        state = ChatReducer.Reduce(state,
            new BeginTurnAction("conv-1", "turn-1", ChatTurnPhase.Thinking));
        state = ChatReducer.Reduce(state,
            new BeginTurnAction("conv-2", "turn-2", ChatTurnPhase.Thinking));
        state = ChatReducer.Reduce(state, new CompleteTurnAction("conv-1", "turn-1"));

        // Act
        var result = ChatReducer.Reduce(state, new ClearTerminalTurnAction("conv-1"));

        // Assert
        Assert.Null(result.ResolveTurn("conv-1"));
        Assert.Same(state.ActiveTurn, result.ActiveTurn);
        Assert.Equal(state.Generation, result.Generation);
    }

    [Fact]
    public void ScrubConversationDerivedState_BackgroundTurn_RemovesOnlyAffectedTurn()
    {
        // Arrange
        var state = ChatState.Empty with { HydratedConversationId = "conv-2" };
        state = ChatReducer.Reduce(state,
            new BeginTurnAction("conv-1", "turn-1", ChatTurnPhase.Thinking));
        state = ChatReducer.Reduce(state,
            new BeginTurnAction("conv-2", "turn-2", ChatTurnPhase.Thinking));

        // Act
        var result = ChatReducer.Reduce(state, new ScrubConversationDerivedStateAction("conv-1"));

        // Assert
        Assert.Null(result.ResolveTurn("conv-1"));
        Assert.Same(state.ActiveTurn, result.ActiveTurn);
    }

    [Fact]
    public void AppendTextDelta_FirstChunk_DoesNotInventMessageTimestamp()
    {
        // ACP agent_message_chunk carries no per-message timestamp. A first chunk must not
        // be stamped with a wall clock; null means "no authoritative time".
        var initialState = ChatState.Empty with { HydratedConversationId = "conv-1" };

        var newState = ChatReducer.Reduce(
            initialState,
            new AppendTextDeltaAction("conv-1", "Hello", ProtocolMessageId: "msg-agent-1"));

        var transcript = newState.ResolveContentSlice("conv-1")?.Transcript;
        Assert.NotNull(transcript);
        var message = Assert.Single(transcript!);
        Assert.Equal("Hello", message.TextContent);
        Assert.Equal("msg-agent-1", message.ProtocolMessageId);
        Assert.False(message.IsOutgoing);
        Assert.Null(message.Timestamp);
    }

    [Fact]
    public void AppendTextDelta_SubsequentChunk_PreservesExistingNullTimestamp()
    {
        var initialState = ChatState.Empty with
        {
            HydratedConversationId = "conv-1",
            ConversationContents = ImmutableDictionary<string, ConversationContentSlice>.Empty.Add(
                "conv-1",
                new ConversationContentSlice(
                    ImmutableList.Create(new ConversationMessageSnapshot
                    {
                        Id = "m-1",
                        ContentType = "text",
                        TextContent = "Hello",
                        ProtocolMessageId = "msg-agent-1",
                        Timestamp = null
                    }),
                    ImmutableList<ConversationPlanEntrySnapshot>.Empty,
                    false))
        };

        var newState = ChatReducer.Reduce(
            initialState,
            new AppendTextDeltaAction("conv-1", " world", ProtocolMessageId: "msg-agent-1"));

        var slice = newState.ResolveContentSlice("conv-1");
        Assert.NotNull(slice);
        var message = Assert.Single(slice!.Value.Transcript);
        Assert.Equal("Hello world", message.TextContent);
        Assert.Null(message.Timestamp);
    }

    [Fact]
    public void AppendTextDelta_SubsequentChunk_DoesNotRefreshExistingTimestamp()
    {
        var originalTime = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);
        var initialState = ChatState.Empty with
        {
            HydratedConversationId = "conv-1",
            ConversationContents = ImmutableDictionary<string, ConversationContentSlice>.Empty.Add(
                "conv-1",
                new ConversationContentSlice(
                    ImmutableList.Create(new ConversationMessageSnapshot
                    {
                        Id = "m-1",
                        ContentType = "text",
                        TextContent = "Hello",
                        ProtocolMessageId = "msg-agent-1",
                        Timestamp = originalTime
                    }),
                    ImmutableList<ConversationPlanEntrySnapshot>.Empty,
                    false))
        };

        var newState = ChatReducer.Reduce(
            initialState,
            new AppendTextDeltaAction("conv-1", " world", ProtocolMessageId: "msg-agent-1"));

        var slice = newState.ResolveContentSlice("conv-1");
        Assert.NotNull(slice);
        var message = Assert.Single(slice!.Value.Transcript);
        Assert.Equal("Hello world", message.TextContent);
        Assert.Equal(originalTime, message.Timestamp);
    }

    private static ChatAction CreateTurnUpdate(
        ChatTurnPhase phase,
        string conversationId,
        string turnId,
        string? connectionInstanceId)
        => phase switch
        {
            ChatTurnPhase.Completed => new CompleteTurnAction(conversationId, turnId, ConnectionInstanceId: connectionInstanceId),
            ChatTurnPhase.Failed => new FailTurnAction(conversationId, turnId, "failure", ConnectionInstanceId: connectionInstanceId),
            ChatTurnPhase.Cancelled => new CancelTurnAction(conversationId, turnId, ConnectionInstanceId: connectionInstanceId),
            _ => new AdvanceTurnPhaseAction(conversationId, turnId, phase, ConnectionInstanceId: connectionInstanceId)
        };

    private static ConversationRuntimeSlice CreateRuntime(
        ConversationRuntimePhase phase,
        string connectionInstanceId = "conn-1",
        string remoteSessionId = "remote-1",
        string? reason = null)
        => new(
            ConversationId: "conv-1",
            Phase: phase,
            ConnectionInstanceId: connectionInstanceId,
            RemoteSessionId: remoteSessionId,
            ProfileId: "profile-1",
            Reason: reason,
            UpdatedAtUtc: DateTime.UtcNow);
}
