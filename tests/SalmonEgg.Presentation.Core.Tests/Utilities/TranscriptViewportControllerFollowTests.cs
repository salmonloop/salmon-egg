using SalmonEgg.Presentation.Utilities;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Utilities;

/// <summary>
/// Facade-level contracts for the epoch-free follow architecture.
/// These catch regressions that pure FollowController tests cannot see.
/// </summary>
public sealed class TranscriptViewportControllerFollowTests
{
    [Fact]
    public void TryActivateAfterLoad_WhenSessionActive_ActivatesOnceAndDoesNotPinFromGeometry()
    {
        // Arrange
        var sut = new TranscriptViewportController();
        sut.Load("conv-1", isSessionActive: true, isOverlayVisible: false);
        var generationBeforeActivation = sut.Generation;
        var stateBeforeActivation = sut.State;

        // Act
        var activated = sut.TryActivateAfterLoad(
            "conv-1",
            isSessionActive: true,
            isOverlayVisible: false,
            hasMessages: true,
            actions: out var actions);
        var duplicateActivation = sut.TryActivateAfterLoad(
            "conv-1",
            isSessionActive: true,
            isOverlayVisible: false,
            hasMessages: true,
            actions: out var duplicateActions);

        // Assert
        Assert.Equal(TranscriptViewportState.Suspended, stateBeforeActivation);
        Assert.True(activated);
        Assert.False(duplicateActivation);
        Assert.Equal(generationBeforeActivation + 1, sut.Generation);
        Assert.Contains(actions, a => a.Kind == TranscriptViewportControllerActionKind.ScrollTranscriptToEnd);
        Assert.Empty(duplicateActions);
        Assert.True(sut.IsAutoFollowAttached);
        Assert.False(sut.IsViewportDetached);

        // Passive off-bottom observation without user intent must not pin.
        var afterLayout = sut.OnViewportChanged(
            ViewState(isAtBottom: false));

        Assert.DoesNotContain(afterLayout, a => a.Kind == TranscriptViewportControllerActionKind.ScrollIntoView);
        Assert.False(sut.IsViewportDetached);
        Assert.True(sut.IsAutoFollowAttached);
    }

    [Fact]
    public void UserDetachIntent_WithRestorableToken_PinsWithoutScrollToEnd()
    {
        // Arrange
        var sut = new TranscriptViewportController();
        _ = LoadAndActivate(sut, "conv-1");

        // Act
        sut.MarkUserScrollIntentStarted();
        var actions = sut.OnUserViewportDetachIntent(
            ViewState(isAtBottom: false),
            new TranscriptProjectionRestoreToken("conv-1", "msg:a"));

        // Assert
        Assert.True(sut.IsViewportDetached);
        Assert.DoesNotContain(actions, a => a.Kind == TranscriptViewportControllerActionKind.ScrollTranscriptToEnd);
    }

    [Fact]
    public void OnTranscriptContentChanged_WhileFollowing_RequestsScrollToEnd()
    {
        // Arrange
        var sut = new TranscriptViewportController();
        _ = LoadAndActivate(sut, "conv-1");

        // Act
        var actions = sut.OnTranscriptContentChanged(ViewState(isAtBottom: true));

        // Assert
        Assert.Contains(actions, a => a.Kind == TranscriptViewportControllerActionKind.ScrollTranscriptToEnd);
    }

    [Fact]
    public void OnTranscriptContentChanged_ConsecutiveRequests_OnlyLatestTokenRemainsActive()
    {
        // Arrange
        var sut = new TranscriptViewportController();
        _ = LoadAndActivate(sut, "conv-1");

        // Act
        var firstActions = sut.OnTranscriptContentChanged(ViewState(isAtBottom: true));
        var secondActions = sut.OnTranscriptContentChanged(ViewState(isAtBottom: true));
        var firstToken = Assert.Single(
            firstActions,
            action => action.Kind == TranscriptViewportControllerActionKind.ScrollTranscriptToEnd).ScrollRequestToken;
        var secondToken = Assert.Single(
            secondActions,
            action => action.Kind == TranscriptViewportControllerActionKind.ScrollTranscriptToEnd).ScrollRequestToken;

        // Assert
        Assert.NotEqual(firstToken, secondToken);
        Assert.Equal(firstToken.ActivationGeneration, secondToken.ActivationGeneration);
        Assert.Equal("conv-1", firstToken.ConversationId);
        Assert.Equal("conv-1", secondToken.ConversationId);
        Assert.True(secondToken.RequestGeneration > firstToken.RequestGeneration);
        Assert.False(sut.MatchesActiveScrollRequest(firstToken));
        Assert.True(sut.MatchesActiveScrollRequest(secondToken));
    }

    [Fact]
    public void OnActiveScrollObservation_StaleToken_DoesNotCompleteLatestRequest()
    {
        // Arrange
        var sut = new TranscriptViewportController();
        _ = LoadAndActivate(sut, "conv-1");
        var staleToken = Assert.Single(
            sut.OnTranscriptContentChanged(ViewState(isAtBottom: true)),
            action => action.Kind == TranscriptViewportControllerActionKind.ScrollTranscriptToEnd).ScrollRequestToken;
        var latestToken = Assert.Single(
            sut.OnTranscriptContentChanged(ViewState(isAtBottom: true)),
            action => action.Kind == TranscriptViewportControllerActionKind.ScrollTranscriptToEnd).ScrollRequestToken;

        // Act
        var staleActions = sut.OnActiveScrollObservation(staleToken);
        var latestRemainedActive = sut.MatchesActiveScrollRequest(latestToken);
        var latestActions = sut.OnActiveScrollObservation(latestToken);

        // Assert
        Assert.Empty(staleActions);
        Assert.True(latestRemainedActive);
        Assert.Empty(latestActions);
        Assert.False(sut.MatchesActiveScrollRequest(latestToken));
    }

    [Fact]
    public void OnTranscriptContentChanged_WhilePinned_DoesNotRequestScrollToEnd()
    {
        // Arrange
        var sut = new TranscriptViewportController();
        _ = LoadAndActivate(sut, "conv-1");
        sut.MarkUserScrollIntentStarted();
        _ = sut.OnUserViewportDetachIntent(
            ViewState(isAtBottom: false),
            new TranscriptProjectionRestoreToken("conv-1", "msg:a"));

        // Act
        var actions = sut.OnTranscriptContentChanged(ViewState(isAtBottom: false));

        // Assert
        Assert.DoesNotContain(actions, a => a.Kind == TranscriptViewportControllerActionKind.ScrollTranscriptToEnd);
        Assert.DoesNotContain(actions, a => a.Kind == TranscriptViewportControllerActionKind.ScrollIntoView);
        Assert.True(sut.IsViewportDetached);
    }

    [Fact]
    public void OnProjectionReady_WhilePinned_DoesNotStormScrollIntoView()
    {
        // Arrange
        var sut = new TranscriptViewportController();
        _ = LoadAndActivate(sut, "conv-1");
        sut.MarkUserScrollIntentStarted();
        _ = sut.OnUserViewportDetachIntent(
            ViewState(isAtBottom: false),
            new TranscriptProjectionRestoreToken("conv-1", "msg:a"));

        // Act
        var actions = sut.OnProjectionReady("conv-1");

        // Assert
        Assert.DoesNotContain(actions, a => a.Kind == TranscriptViewportControllerActionKind.ScrollIntoView);
        Assert.DoesNotContain(actions, a => a.Kind == TranscriptViewportControllerActionKind.ScrollTranscriptToEnd);
        Assert.True(sut.IsViewportDetached);
    }

    [Fact]
    public void OnProjectionReady_StaleConversation_IsIgnored()
    {
        // Arrange
        var sut = new TranscriptViewportController();
        _ = LoadAndActivate(sut, "conv-1");

        // Act
        var actions = sut.OnProjectionReady("conv-other");

        // Assert
        Assert.Empty(actions);
    }

    [Fact]
    public void ReturnToBottom_ReattachesFollow()
    {
        // Arrange
        var sut = new TranscriptViewportController();
        _ = LoadAndActivate(sut, "conv-1");
        sut.MarkUserScrollIntentStarted();
        _ = sut.OnUserViewportDetachIntent(
            ViewState(isAtBottom: false),
            new TranscriptProjectionRestoreToken("conv-1", "msg:a"));

        // Act
        _ = sut.OnViewportChanged(ViewState(isAtBottom: true));

        // Assert
        Assert.True(sut.IsAutoFollowAttached);
        Assert.False(sut.IsViewportDetached);
    }

    [Fact]
    public void OnConversationChanged_WarmReturnToPinnedConversation_RequestsProjectionRestore()
    {
        // Arrange
        var sut = new TranscriptViewportController();
        _ = LoadAndActivate(sut, "conv-a");
        PinConversation(sut, "conv-a", "msg:a");
        _ = sut.OnConversationChanged("conv-b", true, false, true);

        // Act
        var actions = sut.OnConversationChanged("conv-a", true, false, true);

        // Assert
        Assert.True(sut.IsViewportDetached);
        Assert.Contains(actions, action =>
            action.Kind == TranscriptViewportControllerActionKind.RequestRestore
            && action.RestoreToken?.ConversationId == "conv-a"
            && action.RestoreToken?.ProjectionItemKey == "msg:a");
        Assert.DoesNotContain(actions, action => action.Kind == TranscriptViewportControllerActionKind.ScrollTranscriptToEnd);
    }

    [Fact]
    public void OnConversationChanged_WarmReturnToFollowingConversation_RequestsScrollToEnd()
    {
        // Arrange
        var sut = new TranscriptViewportController();
        _ = LoadAndActivate(sut, "conv-a");
        _ = sut.OnConversationChanged("conv-b", true, false, true);

        // Act
        var actions = sut.OnConversationChanged("conv-a", true, false, true);

        // Assert
        Assert.True(sut.IsAutoFollowAttached);
        Assert.Contains(actions, action => action.Kind == TranscriptViewportControllerActionKind.ScrollTranscriptToEnd);
        Assert.DoesNotContain(actions, action => action.Kind == TranscriptViewportControllerActionKind.RequestRestore);
    }

    [Fact]
    public void Load_WithOverlayVisible_DefersActivationUntilOverlayDismissal()
    {
        // Arrange
        var sut = new TranscriptViewportController();

        // Act
        sut.Load(
            "conv-a",
            isSessionActive: true,
            isOverlayVisible: true);
        var activatedWhileOverlayVisible = sut.TryActivateAfterLoad(
            "conv-a",
            isSessionActive: true,
            isOverlayVisible: true,
            hasMessages: true,
            actions: out var activationActions);
        _ = sut.OnOverlayVisibilityChanged(isOverlayVisible: false);
        var resumed = sut.TryResumeAfterOverlay(
            "conv-a",
            isSessionActive: true,
            isOverlayVisible: false,
            hasMessages: true,
            actions: out var resumeActions);
        var activatedAfterResume = sut.TryActivateAfterLoad(
            "conv-a",
            isSessionActive: true,
            isOverlayVisible: false,
            hasMessages: true,
            actions: out var duplicateActions);

        // Assert
        Assert.False(activatedWhileOverlayVisible);
        Assert.Empty(activationActions);
        Assert.True(resumed);
        Assert.False(activatedAfterResume);
        Assert.Empty(duplicateActions);
        Assert.True(sut.IsAutoFollowAttached);
        Assert.False(sut.IsViewportDetached);
        Assert.Contains(resumeActions, action => action.Kind == TranscriptViewportControllerActionKind.ScrollTranscriptToEnd);
        Assert.DoesNotContain(resumeActions, action => action.Kind == TranscriptViewportControllerActionKind.RequestRestore);
    }

    [Fact]
    public void TryResumeAfterOverlay_SameConversation_RestoresPinnedItem()
    {
        // Arrange
        var sut = new TranscriptViewportController();
        _ = LoadAndActivate(sut, "conv-a");
        PinConversation(sut, "conv-a", "msg:a");
        _ = sut.OnOverlayVisibilityChanged(isOverlayVisible: true);
        _ = sut.OnOverlayVisibilityChanged(isOverlayVisible: false);

        // Act
        var resumed = sut.TryResumeAfterOverlay(
            "conv-a",
            isSessionActive: true,
            isOverlayVisible: false,
            hasMessages: true,
            actions: out var actions);

        // Assert
        Assert.True(resumed);
        Assert.True(sut.IsViewportDetached);
        Assert.Contains(actions, action =>
            action.Kind == TranscriptViewportControllerActionKind.RequestRestore
            && action.RestoreToken?.ConversationId == "conv-a"
            && action.RestoreToken?.ProjectionItemKey == "msg:a");
    }

    [Fact]
    public void TryResumeAfterOverlay_DifferentConversation_DoesNotLeakPreviousPin()
    {
        // Arrange
        var sut = new TranscriptViewportController();
        _ = LoadAndActivate(sut, "conv-a");
        PinConversation(sut, "conv-a", "msg:a");
        _ = sut.OnOverlayVisibilityChanged(isOverlayVisible: true);
        _ = sut.OnConversationChanged("conv-b", true, true, true);
        _ = sut.OnOverlayVisibilityChanged(isOverlayVisible: false);

        // Act
        var resumed = sut.TryResumeAfterOverlay(
            "conv-b",
            isSessionActive: true,
            isOverlayVisible: false,
            hasMessages: true,
            actions: out var actions);

        // Assert
        Assert.True(resumed);
        Assert.True(sut.IsAutoFollowAttached);
        Assert.False(sut.IsViewportDetached);
        Assert.Contains(actions, action => action.Kind == TranscriptViewportControllerActionKind.ScrollTranscriptToEnd);
        Assert.DoesNotContain(actions, action => action.Kind == TranscriptViewportControllerActionKind.RequestRestore);
    }

    [Fact]
    public void Unload_AndReloadSameController_RestoresPinnedConversationState()
    {
        // Arrange
        var sut = new TranscriptViewportController();
        _ = LoadAndActivate(sut, "conv-a");
        PinConversation(sut, "conv-a", "msg:a");
        _ = sut.Unload();

        // Act
        sut.Load("conv-a", isSessionActive: true, isOverlayVisible: false);
        var activated = sut.TryActivateAfterLoad(
            "conv-a",
            isSessionActive: true,
            isOverlayVisible: false,
            hasMessages: true,
            actions: out var actions);

        // Assert
        Assert.True(activated);
        Assert.True(sut.IsViewportDetached);
        Assert.Contains(actions, action =>
            action.Kind == TranscriptViewportControllerActionKind.RequestRestore
            && action.RestoreToken?.ConversationId == "conv-a"
            && action.RestoreToken?.ProjectionItemKey == "msg:a");
        Assert.DoesNotContain(actions, action => action.Kind == TranscriptViewportControllerActionKind.ScrollTranscriptToEnd);
        var state = sut.GetConversationState("conv-a");
        Assert.True(state.HasValue);
        Assert.Equal(TranscriptViewportState.DetachedByUser, state.Value.Mode);
        Assert.Equal("msg:a", state.Value.RestoreToken?.ProjectionItemKey);
    }

    [Fact]
    public void NewController_LoadStartsColdWithoutSharingPinnedConversationState()
    {
        // Arrange
        var previousController = new TranscriptViewportController();
        _ = LoadAndActivate(previousController, "conv-a");
        PinConversation(previousController, "conv-a", "msg:a");
        _ = previousController.Unload();
        var sut = new TranscriptViewportController();

        // Act
        var actions = LoadAndActivate(sut, "conv-a");

        // Assert
        Assert.True(sut.IsAutoFollowAttached);
        Assert.Contains(actions, action => action.Kind == TranscriptViewportControllerActionKind.ScrollTranscriptToEnd);
        Assert.DoesNotContain(actions, action => action.Kind == TranscriptViewportControllerActionKind.RequestRestore);
        var state = sut.GetConversationState("conv-a");
        Assert.True(state.HasValue);
        Assert.Equal(TranscriptViewportState.Following, state.Value.Mode);
        Assert.Null(state.Value.RestoreToken);
    }

    [Fact]
    public void GetConversationState_ReturnsStoredPinnedTokenForInactiveConversation()
    {
        // Arrange
        var sut = new TranscriptViewportController();
        _ = LoadAndActivate(sut, "conv-a");
        PinConversation(sut, "conv-a", "msg:a");
        _ = sut.OnConversationChanged("conv-b", true, false, true);

        // Act
        var state = sut.GetConversationState("conv-a");

        // Assert
        Assert.True(state.HasValue);
        Assert.Equal(TranscriptViewportState.DetachedByUser, state.Value.Mode);
        Assert.Equal("msg:a", state.Value.RestoreToken?.ProjectionItemKey);
    }

    [Fact]
    public void OnOverlayVisibilityChanged_EnteringOverlay_SuspendsActiveFollowAndPreservesStoredPin()
    {
        // Arrange
        var sut = new TranscriptViewportController();
        _ = LoadAndActivate(sut, "conv-a");
        PinConversation(sut, "conv-a", "msg:a");

        // Act
        var actions = sut.OnOverlayVisibilityChanged(isOverlayVisible: true);

        // Assert
        Assert.Empty(actions);
        Assert.Equal(TranscriptViewportState.Suspended, sut.State);
        Assert.False(sut.IsViewportDetached);
        var state = sut.GetConversationState("conv-a");
        Assert.True(state.HasValue);
        Assert.Equal(TranscriptViewportState.DetachedByUser, state.Value.Mode);
        Assert.Equal("msg:a", state.Value.RestoreToken?.ProjectionItemKey);
    }

    [Fact]
    public void TryResumeAfterOverlay_WhenAttemptIsDeferred_ResumesOnlyOnce()
    {
        // Arrange
        var sut = new TranscriptViewportController();
        _ = LoadAndActivate(sut, "conv-a");
        _ = sut.OnOverlayVisibilityChanged(isOverlayVisible: true);
        _ = sut.OnOverlayVisibilityChanged(isOverlayVisible: false);
        var generationBeforeResume = sut.Generation;
        var stateBeforeResume = sut.State;

        // Act
        var firstResume = sut.TryResumeAfterOverlay(
            "conv-a",
            isSessionActive: true,
            isOverlayVisible: false,
            hasMessages: true,
            actions: out var firstActions);
        var secondResume = sut.TryResumeAfterOverlay(
            "conv-a",
            isSessionActive: true,
            isOverlayVisible: false,
            hasMessages: true,
            actions: out var secondActions);

        // Assert
        Assert.Equal(TranscriptViewportState.Suspended, stateBeforeResume);
        Assert.True(firstResume);
        Assert.False(secondResume);
        Assert.Equal(generationBeforeResume + 1, sut.Generation);
        Assert.Contains(firstActions, action => action.Kind == TranscriptViewportControllerActionKind.ScrollTranscriptToEnd);
        Assert.Empty(secondActions);
    }

    [Fact]
    public void OnConversationChanged_WhileOverlayVisible_ResumesLatestConversationState()
    {
        // Arrange
        var sut = new TranscriptViewportController();
        _ = LoadAndActivate(sut, "conv-a");
        PinConversation(sut, "conv-a", "msg:a");
        _ = sut.OnConversationChanged("conv-b", true, false, true);
        PinConversation(sut, "conv-b", "msg:b");
        _ = sut.OnConversationChanged("conv-a", true, false, true);
        _ = sut.OnOverlayVisibilityChanged(isOverlayVisible: true);
        _ = sut.OnConversationChanged("conv-b", true, true, true);
        _ = sut.OnOverlayVisibilityChanged(isOverlayVisible: false);

        // Act
        var resumed = sut.TryResumeAfterOverlay(
            "conv-b",
            isSessionActive: true,
            isOverlayVisible: false,
            hasMessages: true,
            actions: out var actions);

        // Assert
        Assert.True(resumed);
        Assert.True(sut.IsViewportDetached);
        Assert.Contains(actions, action =>
            action.Kind == TranscriptViewportControllerActionKind.RequestRestore
            && action.RestoreToken?.ConversationId == "conv-b"
            && action.RestoreToken?.ProjectionItemKey == "msg:b");
        Assert.DoesNotContain(actions, action => action.RestoreToken?.ConversationId == "conv-a");
    }

    [Fact]
    public void OnOverlayVisibilityChanged_RepeatedNotifications_DoNotAdvanceGenerationOrDuplicateResume()
    {
        // Arrange
        var sut = new TranscriptViewportController();
        _ = LoadAndActivate(sut, "conv-a");
        var generationBeforeOverlay = sut.Generation;

        // Act
        _ = sut.OnOverlayVisibilityChanged(isOverlayVisible: true);
        _ = sut.OnOverlayVisibilityChanged(isOverlayVisible: true);
        _ = sut.OnOverlayVisibilityChanged(isOverlayVisible: false);
        _ = sut.OnOverlayVisibilityChanged(isOverlayVisible: false);
        var resumed = sut.TryResumeAfterOverlay(
            "conv-a",
            isSessionActive: true,
            isOverlayVisible: false,
            hasMessages: true,
            actions: out var actions);
        var duplicateResume = sut.TryResumeAfterOverlay(
            "conv-a",
            isSessionActive: true,
            isOverlayVisible: false,
            hasMessages: true,
            actions: out var duplicateActions);

        // Assert
        Assert.True(resumed);
        Assert.False(duplicateResume);
        Assert.Equal(generationBeforeOverlay + 1, sut.Generation);
        Assert.Single(actions, action => action.Kind == TranscriptViewportControllerActionKind.ScrollTranscriptToEnd);
        Assert.Empty(duplicateActions);
    }

    [Fact]
    public void Unload_ClearsPendingOverlayResume()
    {
        // Arrange
        var sut = new TranscriptViewportController();
        _ = LoadAndActivate(sut, "conv-a");
        _ = sut.OnOverlayVisibilityChanged(isOverlayVisible: true);
        _ = sut.OnOverlayVisibilityChanged(isOverlayVisible: false);
        _ = sut.Unload();
        sut.Load("conv-a", isSessionActive: true, isOverlayVisible: false);
        var generationAfterReload = sut.Generation;

        // Act
        var resumed = sut.TryResumeAfterOverlay(
            "conv-a",
            isSessionActive: true,
            isOverlayVisible: false,
            hasMessages: true,
            actions: out var actions);

        // Assert
        Assert.False(resumed);
        Assert.Empty(actions);
        Assert.Equal(generationAfterReload, sut.Generation);
    }

    [Fact]
    public void OnConversationChanged_BeforeLoadActivation_ActivatesLatestConversationOnly()
    {
        // Arrange
        var sut = new TranscriptViewportController();
        sut.Load("conv-a", isSessionActive: true, isOverlayVisible: false);

        // Act
        var contextActions = sut.OnConversationChanged(
            "conv-b",
            isSessionActive: true,
            isOverlayVisible: false,
            hasMessages: true);
        var activated = sut.TryActivateAfterLoad(
            "conv-b",
            isSessionActive: true,
            isOverlayVisible: false,
            hasMessages: true,
            actions: out var activationActions);

        // Assert
        Assert.Empty(contextActions);
        Assert.True(activated);
        Assert.Equal(1, sut.Generation);
        Assert.Contains(activationActions, action =>
            action.Kind == TranscriptViewportControllerActionKind.ScrollTranscriptToEnd
            && action.ScrollRequestToken.ConversationId == "conv-b");
        Assert.Null(sut.GetConversationState("conv-a"));
    }

    [Fact]
    public void TryActivateAfterLoad_WhenSessionInitiallyInactive_WaitsForActiveConversation()
    {
        // Arrange
        var sut = new TranscriptViewportController();
        sut.Load(conversationId: null, isSessionActive: false, isOverlayVisible: false);

        // Act
        var activatedWhileInactive = sut.TryActivateAfterLoad(
            conversationId: null,
            isSessionActive: false,
            isOverlayVisible: false,
            hasMessages: false,
            actions: out var inactiveActions);
        var contextActions = sut.OnConversationChanged(
            "conv-a",
            isSessionActive: true,
            isOverlayVisible: false,
            hasMessages: true);
        var activatedAfterSessionStarted = sut.TryActivateAfterLoad(
            "conv-a",
            isSessionActive: true,
            isOverlayVisible: false,
            hasMessages: true,
            actions: out var activationActions);

        // Assert
        Assert.False(activatedWhileInactive);
        Assert.Empty(inactiveActions);
        Assert.Empty(contextActions);
        Assert.True(activatedAfterSessionStarted);
        Assert.Equal(1, sut.Generation);
        Assert.Contains(activationActions, action =>
            action.Kind == TranscriptViewportControllerActionKind.ScrollTranscriptToEnd
            && action.ScrollRequestToken.ConversationId == "conv-a");
    }

    [Fact]
    public void Unload_BeforeLoadActivation_ClearsPendingActivation()
    {
        // Arrange
        var sut = new TranscriptViewportController();
        sut.Load("conv-a", isSessionActive: true, isOverlayVisible: false);

        // Act
        _ = sut.Unload();
        var activated = sut.TryActivateAfterLoad(
            "conv-a",
            isSessionActive: true,
            isOverlayVisible: false,
            hasMessages: true,
            actions: out var actions);

        // Assert
        Assert.False(activated);
        Assert.Empty(actions);
        Assert.Equal(0, sut.Generation);
        Assert.Equal(TranscriptViewportState.Suspended, sut.State);
    }

    private static void PinConversation(
        TranscriptViewportController sut,
        string conversationId,
        string itemKey)
    {
        sut.MarkUserScrollIntentStarted();
        _ = sut.OnUserViewportDetachIntent(
            ViewState(isAtBottom: false),
            new TranscriptProjectionRestoreToken(conversationId, itemKey));
    }

    private static IReadOnlyList<TranscriptViewportControllerAction> LoadAndActivate(
        TranscriptViewportController sut,
        string conversationId,
        bool hasMessages = true)
    {
        sut.Load(conversationId, isSessionActive: true, isOverlayVisible: false);
        var activated = sut.TryActivateAfterLoad(
            conversationId,
            isSessionActive: true,
            isOverlayVisible: false,
            hasMessages,
            actions: out var actions);

        Assert.True(activated);
        return actions;
    }

    private static TranscriptViewportViewState ViewState(
        bool hasMessages = true,
        bool isAtBottom = true)
        => new(hasMessages, isAtBottom);
}
