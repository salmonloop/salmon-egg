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
        var sut = new TranscriptViewportController();

        var actions = sut.Load("conv-1", isSessionActive: true, isOverlayVisible: false, hasMessages: true);

        Assert.Contains(actions, a => a.Kind == TranscriptViewportControllerActionKind.ScrollTranscriptToEnd);
        Assert.True(sut.IsAutoFollowAttached);
        Assert.False(sut.IsViewportDetached);

        // Passive off-bottom observation without user intent must not pin.
        var afterLayout = sut.OnViewportChanged(
            new TranscriptViewportViewState(true, true, true, IsAtBottom: false),
            restoreToken: new TranscriptProjectionRestoreToken("conv-1", "msg:top"));

        Assert.DoesNotContain(afterLayout, a => a.Kind == TranscriptViewportControllerActionKind.ScrollIntoView);
        Assert.False(sut.IsViewportDetached);
        Assert.True(sut.IsAutoFollowAttached);
    }

    [Fact]
    public void UserDetachIntent_WithRestorableToken_PinsWithoutScrollToEnd()
    {
        var sut = new TranscriptViewportController();
        _ = sut.Load("conv-1", true, false, true);

        sut.MarkUserScrollIntentStarted();
        var actions = sut.OnUserViewportDetachIntent(
            new TranscriptViewportViewState(true, true, true, IsAtBottom: false),
            new TranscriptProjectionRestoreToken("conv-1", "msg:a"));

        Assert.True(sut.IsViewportDetached);
        Assert.DoesNotContain(actions, a => a.Kind == TranscriptViewportControllerActionKind.ScrollTranscriptToEnd);
    }

    [Fact]
    public void OnMessagesAppended_WhileFollowing_RequestsScrollToEnd()
    {
        var sut = new TranscriptViewportController();
        _ = sut.Load("conv-1", true, false, true);

        var actions = sut.OnMessagesAppended(
            addedCount: 1,
            new TranscriptViewportViewState(true, true, true, IsAtBottom: true));

        Assert.Contains(actions, a => a.Kind == TranscriptViewportControllerActionKind.ScrollTranscriptToEnd);
    }

    [Fact]
    public void OnMessagesAppended_WhilePinned_DoesNotRequestScrollToEnd()
    {
        var sut = new TranscriptViewportController();
        _ = sut.Load("conv-1", true, false, true);
        sut.MarkUserScrollIntentStarted();
        _ = sut.OnUserViewportDetachIntent(
            new TranscriptViewportViewState(true, true, true, false),
            new TranscriptProjectionRestoreToken("conv-1", "msg:a"));

        var actions = sut.OnMessagesAppended(
            1,
            new TranscriptViewportViewState(true, true, true, IsAtBottom: false));

        Assert.DoesNotContain(actions, a => a.Kind == TranscriptViewportControllerActionKind.ScrollTranscriptToEnd);
        Assert.DoesNotContain(actions, a => a.Kind == TranscriptViewportControllerActionKind.ScrollIntoView);
        Assert.True(sut.IsViewportDetached);
    }

    [Fact]
    public void OnProjectionReady_WhilePinned_DoesNotStormScrollIntoView()
    {
        var sut = new TranscriptViewportController();
        _ = sut.Load("conv-1", true, false, true);
        sut.MarkUserScrollIntentStarted();
        _ = sut.OnUserViewportDetachIntent(
            new TranscriptViewportViewState(true, true, true, false),
            new TranscriptProjectionRestoreToken("conv-1", "msg:a"));

        var actions = sut.OnProjectionReady("conv-1");

        Assert.DoesNotContain(actions, a => a.Kind == TranscriptViewportControllerActionKind.ScrollIntoView);
        Assert.DoesNotContain(actions, a => a.Kind == TranscriptViewportControllerActionKind.ScrollTranscriptToEnd);
        Assert.True(sut.IsViewportDetached);
    }

    [Fact]
    public void OnProjectionReady_StaleConversation_IsIgnored()
    {
        var sut = new TranscriptViewportController();
        _ = sut.Load("conv-1", true, false, true);

        var actions = sut.OnProjectionReady("conv-other");

        Assert.Empty(actions);
    }

    [Fact]
    public void ReturnToBottom_ReattachesFollow()
    {
        var sut = new TranscriptViewportController();
        _ = sut.Load("conv-1", true, false, true);
        sut.MarkUserScrollIntentStarted();
        _ = sut.OnUserViewportDetachIntent(
            new TranscriptViewportViewState(true, true, true, false),
            new TranscriptProjectionRestoreToken("conv-1", "msg:a"));

        _ = sut.OnViewportChanged(new TranscriptViewportViewState(true, true, true, IsAtBottom: true));

        Assert.True(sut.IsAutoFollowAttached);
        Assert.False(sut.IsViewportDetached);
    }
}
