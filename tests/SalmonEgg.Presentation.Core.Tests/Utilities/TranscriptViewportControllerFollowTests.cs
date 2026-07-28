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
    public void Load_WhenSessionActive_RequestsScrollToEnd_AndDoesNotPinFromGeometry()
    {
        // Arrange
        var sut = new TranscriptViewportController();

        // Act
        var actions = sut.Load("conv-1", isSessionActive: true, isOverlayVisible: false, hasMessages: true);

        // Assert
        Assert.Contains(actions, a => a.Kind == TranscriptViewportControllerActionKind.ScrollTranscriptToEnd);
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
        _ = sut.Load("conv-1", true, false, true);

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
        _ = sut.Load("conv-1", true, false, true);

        // Act
        var actions = sut.OnTranscriptContentChanged(ViewState(isAtBottom: true));

        // Assert
        Assert.Contains(actions, a => a.Kind == TranscriptViewportControllerActionKind.ScrollTranscriptToEnd);
    }

    [Fact]
    public void OnTranscriptContentChanged_WhilePinned_DoesNotRequestScrollToEnd()
    {
        // Arrange
        var sut = new TranscriptViewportController();
        _ = sut.Load("conv-1", true, false, true);
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
        _ = sut.Load("conv-1", true, false, true);
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
        _ = sut.Load("conv-1", true, false, true);

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
        _ = sut.Load("conv-1", true, false, true);
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
        _ = sut.Load("conv-a", true, false, true);
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
        _ = sut.Load("conv-a", true, false, true);
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
        var loadActions = sut.Load(
            "conv-a",
            isSessionActive: true,
            isOverlayVisible: true,
            hasMessages: true);
        _ = sut.OnOverlayVisibilityChanged(isOverlayVisible: false);
        var resumed = sut.TryResumeAfterOverlay(
            "conv-a",
            isSessionActive: true,
            isOverlayVisible: false,
            hasMessages: true,
            actions: out var resumeActions);

        // Assert
        Assert.Empty(loadActions);
        Assert.True(resumed);
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
        _ = sut.Load("conv-a", true, false, true);
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
        _ = sut.Load("conv-a", true, false, true);
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
    public void Unload_AndReloadDoesNotCarryConversationStateAcrossColdStart()
    {
        // Arrange
        var sut = new TranscriptViewportController();
        _ = sut.Load("conv-a", true, false, true);
        PinConversation(sut, "conv-a", "msg:a");
        _ = sut.Unload();

        // Act
        var actions = sut.Load("conv-a", true, false, true);

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
        _ = sut.Load("conv-a", true, false, true);
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
        _ = sut.Load("conv-a", true, false, true);
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
        _ = sut.Load("conv-a", true, false, true);
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
        _ = sut.Load("conv-a", true, false, true);
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
        _ = sut.Load("conv-a", true, false, true);
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
        _ = sut.Load("conv-a", true, false, true);
        _ = sut.OnOverlayVisibilityChanged(isOverlayVisible: true);
        _ = sut.OnOverlayVisibilityChanged(isOverlayVisible: false);
        _ = sut.Unload();
        _ = sut.Load("conv-a", true, false, true);
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

    private static TranscriptViewportViewState ViewState(
        bool hasMessages = true,
        bool isAtBottom = true)
        => new(hasMessages, isAtBottom);
}
