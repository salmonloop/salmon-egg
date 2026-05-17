using SalmonEgg.Presentation.Core.Mvux.Chat;
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
    public void GivenState_WhenSetPromptInFlight_ThenIsPromptInFlightIsTrue()
    {
        // Arrange
        var initialState = new ChatState(IsPromptInFlight: false);
        var action = new SetPromptInFlightAction(true);

        // Act
        var newState = ChatReducer.Reduce(initialState, action);

        // Assert
        Assert.True(newState.IsPromptInFlight);
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
            SettingsSelectedProfileId = "profile-1",
            ConnectionInstanceId = "conn-1",
            Generation = 7
        };

        var next = ChatConnectionReducer.Reduce(initialState, new SetConnectionPhaseAction(ConnectionPhase.Connected));

        Assert.Equal(ConnectionPhase.Connected, next.Phase);
        Assert.Equal("profile-1", next.SettingsSelectedProfileId);
        Assert.Equal("conn-1", next.ConnectionInstanceId);
        Assert.Equal(8, next.Generation);
    }

    [Fact]
    public void GivenConnectionState_WhenSelectedProfileChanges_ThenConnectionInstanceIdIsPreservedAndGenerationIncrements()
    {
        var initialState = ChatConnectionState.Empty with
        {
            Phase = ConnectionPhase.Connected,
            SettingsSelectedProfileId = "profile-1",
            ConnectionInstanceId = "conn-1",
            Generation = 4
        };

        var next = ChatConnectionReducer.Reduce(initialState, new SetSettingsSelectedProfileAction("profile-2"));

        Assert.Equal("profile-2", next.SettingsSelectedProfileId);
        Assert.Equal("conn-1", next.ConnectionInstanceId);
        Assert.Equal(ConnectionPhase.Connected, next.Phase);
        Assert.Equal(5, next.Generation);
    }

    [Fact]
    public void GivenConnectionState_WhenConnectionInstanceIdChanges_ThenOnlyIdentityAndGenerationUpdate()
    {
        var initialState = ChatConnectionState.Empty with
        {
            Phase = ConnectionPhase.Connected,
            SettingsSelectedProfileId = "profile-1",
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
        Assert.Equal("profile-1", next.SettingsSelectedProfileId);
        Assert.Equal("previous-error", next.Error);
        Assert.True(next.IsAuthenticationRequired);
        Assert.Equal("hint", next.AuthenticationHintMessage);
        Assert.Equal("profile-1", next.ForegroundTransportProfileId);
        Assert.Equal(13, next.Generation);
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
            SettingsSelectedProfileId = "profile-1",
            ConnectionInstanceId = "conn-1",
            ForegroundTransportProfileId = "profile-1",
            NewSessionDraft = draft,
            Generation = 4
        };

        var next = ChatConnectionReducer.Reduce(initialState, new SetSettingsSelectedProfileAction("profile-2"));

        Assert.Equal("profile-2", next.SettingsSelectedProfileId);
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
            SettingsSelectedProfileId = "profile-1",
            ConnectionInstanceId = "conn-1",
            ForegroundTransportProfileId = "profile-1",
            NewSessionDraft = draft,
            Generation = 4
        };

        var next = ChatConnectionReducer.Reduce(initialState, new SetSettingsSelectedProfileAction("profile-1"));

        Assert.Equal("profile-1", next.SettingsSelectedProfileId);
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
            SettingsSelectedProfileId = "profile-1",
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
            SettingsSelectedProfileId = "profile-1",
            ConnectionInstanceId = "conn-1",
            Generation = 20
        };

        var reset = ChatConnectionReducer.Reduce(connectedState, new ResetConnectionStateAction());

        Assert.Equal("conn-1", reset.ConnectionInstanceId);
        Assert.Equal(ConnectionPhase.Disconnected, reset.Phase);
        Assert.Equal("profile-1", reset.SettingsSelectedProfileId);
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
                Description = "after",
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
        Assert.Equal("after", sessionInfo.Description);
        Assert.Equal("value", sessionInfo.Meta!["existing"]);
        Assert.Equal("after", sessionInfo.Meta["shared"]);
        Assert.Equal(2, sessionInfo.Meta["added"]);
    }

    [Fact]
    public void MergeConversationSessionState_ReplacesTitleAndPreservesOtherStringFields_WhenIncomingTitleIsEmpty()
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
                        Description = "before description",
                        Cwd = @"C:\repo\before"
                    },
                    null))
        };

        var next = ChatReducer.Reduce(initialState, new MergeConversationSessionStateAction(
            "conv-1",
            SessionInfo: new ConversationSessionInfoSnapshot
            {
                Title = string.Empty,
                Description = "   ",
                Cwd = "\t",
                UpdatedAtUtc = new DateTime(2026, 3, 3, 0, 0, 0, DateTimeKind.Utc)
            }));

        var sessionState = next.ResolveSessionStateSlice("conv-1");
        Assert.NotNull(sessionState);
        var sessionInfo = sessionState!.Value.SessionInfo;
        Assert.NotNull(sessionInfo);
        Assert.Equal(string.Empty, sessionInfo!.Title);
        Assert.Equal("before description", sessionInfo.Description);
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
                        Description = "before description",
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
                Description = "\t",
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
        Assert.Equal("before description", sessionInfo.Description);
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
                        Description = "existing description",
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
                Description = "   ",
                Cwd = "\t",
                UpdatedAtUtc = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc)
            }));

        var sessionInfo = next.ResolveSessionStateSlice("conv-1")!.Value.SessionInfo;
        Assert.NotNull(sessionInfo);
        Assert.Equal(string.Empty, sessionInfo!.Title);
        Assert.Equal("existing description", sessionInfo.Description);
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
    public void BeginTurn_SetsActiveTurnAndGeneration()
    {
        var initialState = ChatState.Empty with { Generation = 10 };
        var action = new BeginTurnAction("conv-1", "turn-1", ChatTurnPhase.WaitingForAgent);

        var newState = ChatReducer.Reduce(initialState, action);

        Assert.Equal(11, newState.Generation);
        Assert.NotNull(newState.ActiveTurn);
        Assert.Equal("turn-1", newState.ActiveTurn!.TurnId);
        Assert.Equal(ChatTurnPhase.WaitingForAgent, newState.ActiveTurn.Phase);
    }

    [Fact]
    public void AdvanceTurnPhase_IgnoresStaleTurnId()
    {
        var initialState = ChatState.Empty with
        {
            ActiveTurn = new ActiveTurnState("conv-1", "turn-current", ChatTurnPhase.Thinking, DateTime.UtcNow, DateTime.UtcNow),
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
            ActiveTurn = new ActiveTurnState("conv-1", "turn-1", ChatTurnPhase.Thinking, DateTime.UtcNow, DateTime.UtcNow),
            Generation = 4
        };
        var action = new AdvanceTurnPhaseAction("conv-remote", "turn-1", ChatTurnPhase.Responding);

        var newState = ChatReducer.Reduce(initialState, action);

        Assert.Equal(4, newState.Generation);
        Assert.Equal(ChatTurnPhase.Thinking, newState.ActiveTurn!.Phase);
    }

    [Fact]
    public void SelectConversation_ClearsActiveTurnForPreviousConversation()
    {
        var initialState = ChatState.Empty with
        {
            HydratedConversationId = "conv-1",
            ActiveTurn = new ActiveTurnState("conv-1", "turn-1", ChatTurnPhase.Thinking, DateTime.UtcNow, DateTime.UtcNow)
        };
        var action = new SelectConversationAction("conv-2");

        var newState = ChatReducer.Reduce(initialState, action);

        Assert.Null(newState.ActiveTurn);
        Assert.Equal("conv-2", newState.HydratedConversationId);
    }

    [Fact]
    public void CompleteTurn_DoesNotOverride_FailedOrCancelled()
    {
        var failedState = ChatState.Empty with
        {
            ActiveTurn = new ActiveTurnState("conv-1", "turn-1", ChatTurnPhase.Failed, DateTime.UtcNow, DateTime.UtcNow)
        };
        var action = new CompleteTurnAction("conv-1", "turn-1");

        var newState = ChatReducer.Reduce(failedState, action);

        Assert.Equal(ChatTurnPhase.Failed, newState.ActiveTurn!.Phase);

        var cancelledState = ChatState.Empty with
        {
            ActiveTurn = new ActiveTurnState("conv-1", "turn-1", ChatTurnPhase.Cancelled, DateTime.UtcNow, DateTime.UtcNow)
        };
        var newState2 = ChatReducer.Reduce(cancelledState, action);

        Assert.Equal(ChatTurnPhase.Cancelled, newState2.ActiveTurn!.Phase);
    }

    [Fact]
    public void AdvanceTurnPhase_DoesNotOverrideTerminalPhase()
    {
        var completedState = ChatState.Empty with
        {
            ActiveTurn = new ActiveTurnState("conv-1", "turn-1", ChatTurnPhase.Completed, DateTime.UtcNow, DateTime.UtcNow)
        };

        var newState = ChatReducer.Reduce(
            completedState,
            new AdvanceTurnPhaseAction("conv-1", "turn-1", ChatTurnPhase.Responding));

        Assert.Equal(ChatTurnPhase.Completed, newState.ActiveTurn!.Phase);
    }
}
