using System;
using System.Collections.Generic;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat.Mvux;

public class ConversationAttentionReducerTests
{
    [Fact]
    public void GivenEmptyState_WhenMarkConversationUnread_ThenConversationSliceIsCreated()
    {
        var next = ConversationAttentionReducer.Reduce(
            ConversationAttentionState.Empty,
            new MarkConversationUnreadAction("conv-1", ConversationAttentionSource.AgentMessage, new DateTime(2026, 4, 21, 1, 2, 3, DateTimeKind.Utc)));

        Assert.True(next.TryGetConversation("conv-1", out var slice));
        Assert.NotNull(slice);
        Assert.Equal("conv-1", slice!.ConversationId);
        Assert.True(slice.HasUnread);
        Assert.Equal(1, slice.UnreadVersion);
        Assert.Equal(new DateTime(2026, 4, 21, 1, 2, 3, DateTimeKind.Utc), slice.LastAttentionAtUtc);
        Assert.Equal(ConversationAttentionSource.AgentMessage, slice.LastAttentionSource);
    }

    [Fact]
    public void GivenUnreadConversation_WhenCleared_ThenUnreadFlagIsFalseAndVersionIsPreserved()
    {
        var initial = ConversationAttentionReducer.Reduce(
            ConversationAttentionState.Empty,
            new MarkConversationUnreadAction("conv-1", ConversationAttentionSource.ToolCall, new DateTime(2026, 4, 21, 2, 0, 0, DateTimeKind.Utc)));

        var next = ConversationAttentionReducer.Reduce(initial, new ClearConversationUnreadAction("conv-1", 1));

        Assert.True(next.TryGetConversation("conv-1", out var slice));
        Assert.NotNull(slice);
        Assert.False(slice!.HasUnread);
        Assert.Equal(1, slice.UnreadVersion);
        Assert.Equal(ConversationAttentionSource.ToolCall, slice.LastAttentionSource);
    }

    [Fact]
    public void GivenConversationAttention_WhenRemoved_ThenConversationSliceIsDeleted()
    {
        var initial = ConversationAttentionReducer.Reduce(
            ConversationAttentionState.Empty,
            new MarkConversationUnreadAction("conv-1", ConversationAttentionSource.AgentMessage, DateTime.UtcNow));

        var next = ConversationAttentionReducer.Reduce(initial, new RemoveConversationAttentionAction("conv-1"));

        Assert.False(next.TryGetConversation("conv-1", out _));
    }

    [Fact]
    public void GivenBlankConversationId_WhenMarkedUnread_ThenReducerReturnsCurrentState()
    {
        var initial = ConversationAttentionReducer.Reduce(
            ConversationAttentionState.Empty,
            new MarkConversationUnreadAction("conv-1", ConversationAttentionSource.AgentMessage, DateTime.UtcNow));

        var next = ConversationAttentionReducer.Reduce(
            initial,
            new MarkConversationUnreadAction("   ", ConversationAttentionSource.ToolCall, DateTime.UtcNow));

        Assert.Same(initial, next);
    }

    [Fact]
    public void GivenMissingConversation_WhenClearedOrRemoved_ThenReducerReturnsCurrentState()
    {
        var initial = ConversationAttentionReducer.Reduce(
            ConversationAttentionState.Empty,
            new MarkConversationUnreadAction("conv-1", ConversationAttentionSource.AgentMessage, DateTime.UtcNow));

        var cleared = ConversationAttentionReducer.Reduce(initial, new ClearConversationUnreadAction("missing", 1));
        var removed = ConversationAttentionReducer.Reduce(initial, new RemoveConversationAttentionAction("missing"));

        Assert.Same(initial, cleared);
        Assert.Same(initial, removed);
    }

    [Fact]
    public void GivenBlankConversationId_WhenLookupRequested_ThenTryGetConversationReturnsFalse()
    {
        Assert.False(ConversationAttentionState.Empty.TryGetConversation(" ", out var slice));
        Assert.Null(slice);
    }

    [Fact]
    public void ClearUnread_OldReceiptAfterSameMessageGrows_PreservesNewReply()
    {
        var first = Reply("first chunk");
        var second = Reply("first chunk and more");
        var state = Mark(ConversationAttentionState.Empty, first);
        var oldReceipt = new ClearConversationUnreadAction("conversation", 1, "profile", "remote", first, "connection");
        state = Mark(state, second);

        var result = ConversationAttentionReducer.Reduce(state, oldReceipt);

        Assert.Same(state, result);
        Assert.Equal(2, result.Conversations["conversation"].UnreadVersion);
        Assert.Same(second, result.Conversations["conversation"].Content);
        Assert.True(result.Conversations["conversation"].HasUnread);
    }

    [Theory]
    [InlineData("profile-old", "remote", "connection")]
    [InlineData("profile", "remote-old", "connection")]
    [InlineData("profile", "remote", "connection-old")]
    public void ClearUnread_MismatchedIdentity_PreservesUnread(string profile, string remote, string connection)
    {
        var content = Reply("reply");
        var state = Mark(ConversationAttentionState.Empty, content);

        var result = ConversationAttentionReducer.Reduce(state,
            new ClearConversationUnreadAction("conversation", 1, profile, remote, content, connection));

        Assert.Same(state, result);
    }

    [Fact]
    public void ClearUnread_EqualMessageDataWithDifferentSnapshot_DoesNotClearUnread()
    {
        var content = Reply("reply");
        var state = Mark(ConversationAttentionState.Empty, content);

        var result = ConversationAttentionReducer.Reduce(state,
            new ClearConversationUnreadAction("conversation", 1, "profile", "remote", Reply("reply"), "connection"));

        Assert.Same(state, result);
    }

    [Fact]
    public void ClearUnread_CurrentReceipt_PreservesWatermarkAndAllowsNextReplyToBecomeUnread()
    {
        var content = Reply("reply");
        var state = Mark(ConversationAttentionState.Empty, content);
        var receipt = new ClearConversationUnreadAction("conversation", 1, "profile", "remote", content, "connection");

        var cleared = ConversationAttentionReducer.Reduce(state, receipt);
        var next = Mark(cleared, Reply("next reply"));

        Assert.False(cleared.Conversations["conversation"].HasUnread);
        Assert.Equal(1, cleared.Conversations["conversation"].UnreadVersion);
        Assert.Same(cleared, ConversationAttentionReducer.Reduce(cleared, receipt));
        Assert.True(next.Conversations["conversation"].HasUnread);
        Assert.Equal(2, next.Conversations["conversation"].UnreadVersion);
    }

    [Fact]
    public void MarkUnread_SameAcceptedContentAfterRead_DoesNotCreateNewUnreadVersion()
    {
        var content = Reply("reply");
        var state = Mark(ConversationAttentionState.Empty, content);
        state = ConversationAttentionReducer.Reduce(state,
            new ClearConversationUnreadAction("conversation", 1, "profile", "remote", content, "connection"));

        var result = Mark(state, content);

        Assert.Same(state, result);
        Assert.False(result.Conversations["conversation"].HasUnread);
        Assert.Equal(1, result.Conversations["conversation"].UnreadVersion);
    }

    [Fact]
    public void DetachRetiredConnection_ReleasesBodyButKeepsUnreadMetadataAndOtherConnections()
    {
        var content = Reply("private reply");
        var state = Mark(ConversationAttentionState.Empty, content);
        state = ConversationAttentionReducer.Reduce(state,
            new MarkConversationUnreadAction("other", ConversationAttentionSource.AgentMessage, DateTime.UtcNow,
                "profile", "remote-2", Reply("other"), "other-connection"));

        var detached = ConversationAttentionReducer.Reduce(state,
            new DetachConversationAttentionContentAction("profile", "connection"));
        var staleReceipt = ConversationAttentionReducer.Reduce(detached,
            new ClearConversationUnreadAction("conversation", 1, "profile", "remote", content, "connection"));

        var metadata = detached.Conversations["conversation"];
        Assert.True(metadata.HasUnread);
        Assert.Equal(1, metadata.UnreadVersion);
        Assert.Equal("profile", metadata.ProfileId);
        Assert.Equal("remote", metadata.RemoteSessionId);
        Assert.Null(metadata.Content);
        Assert.Null(metadata.ContentConnectionInstanceId);
        Assert.Same(state.Conversations["other"], detached.Conversations["other"]);
        Assert.Same(detached, staleReceipt);
    }

    [Fact]
    public void DetachResetConversation_KeepsOtherConversationOnSameConnectionAndPreservesUnreadWatermark()
    {
        // Arrange
        var state = Mark(ConversationAttentionState.Empty, Reply("old A"));
        state = ConversationAttentionReducer.Reduce(state,
            new MarkConversationUnreadAction("other", ConversationAttentionSource.AgentMessage, DateTime.UtcNow,
                "profile", "remote-b", Reply("B"), "connection"));

        // Act
        var result = ConversationAttentionReducer.Reduce(state,
            new DetachConversationAttentionContentAction("profile", "connection", "conversation", 1));

        // Assert
        Assert.Null(result.Conversations["conversation"].Content);
        Assert.True(result.Conversations["conversation"].HasUnread);
        Assert.Equal(1, result.Conversations["conversation"].UnreadVersion);
        Assert.Same(state.Conversations["other"], result.Conversations["other"]);
    }

    [Fact]
    public void DetachResetConversation_WhenNewerReplyArrives_RejectsStaleWatermark()
    {
        // Arrange
        var state = Mark(ConversationAttentionState.Empty, Reply("old"));
        var reset = new DetachConversationAttentionContentAction("profile", "connection", "conversation", 1);
        state = Mark(state, Reply("new"));

        // Act
        var result = ConversationAttentionReducer.Reduce(state, reset);

        // Assert
        Assert.Same(state, result);
        Assert.Equal(2, result.Conversations["conversation"].UnreadVersion);
        Assert.NotNull(result.Conversations["conversation"].Content);
    }

    [Fact]
    public void ReanchorRecovery_NoProtocolMessageId_CurrentWatermarkCanBeReadOnNewConnection()
    {
        var oldContent = Reply("reply");
        var state = Mark(ConversationAttentionState.Empty, oldContent);
        state = ConversationAttentionReducer.Reduce(state, new DetachConversationAttentionContentAction("profile", "connection"));
        var replayed = new ConversationMessageSnapshot { Id = "new-local-id", ContentType = "text", TextContent = "reply" };

        var reanchored = ConversationAttentionReducer.Reduce(state,
            new ReanchorConversationUnreadAction("conversation", 1, "profile", "remote", replayed, "connection-new"));
        var stale = ConversationAttentionReducer.Reduce(reanchored,
            new ClearConversationUnreadAction("conversation", 1, "profile", "remote", oldContent, "connection"));
        var cleared = ConversationAttentionReducer.Reduce(reanchored,
            new ClearConversationUnreadAction("conversation", 1, "profile", "remote", replayed, "connection-new"));

        Assert.True(reanchored.Conversations["conversation"].HasUnread);
        Assert.Equal(1, reanchored.Conversations["conversation"].UnreadVersion);
        Assert.Same(replayed, reanchored.Conversations["conversation"].Content);
        Assert.Same(reanchored, stale);
        Assert.False(cleared.Conversations["conversation"].HasUnread);
    }

    [Fact]
    public void ReanchorRecovery_NewerLiveReplyArrived_PreservesNewContentAndWatermark()
    {
        var state = Mark(ConversationAttentionState.Empty, Reply("old reply"));
        var newContent = Reply("new reply");
        state = Mark(state, newContent);

        var result = ConversationAttentionReducer.Reduce(state,
            new ReanchorConversationUnreadAction("conversation", 1, "profile", "remote", Reply("replay"), "new-connection"));

        Assert.Same(state, result);
        Assert.Same(newContent, result.Conversations["conversation"].Content);
    }

    [Theory]
    [InlineData("other-profile", "remote")]
    [InlineData("profile", "other-remote")]
    public void ReanchorRecovery_DifferentLogicalSession_IsIgnored(string profile, string remote)
    {
        var state = Mark(ConversationAttentionState.Empty, Reply("old reply"));

        var result = ConversationAttentionReducer.Reduce(state,
            new ReanchorConversationUnreadAction("conversation", 1, profile, remote, Reply("other reply"), "new-connection"));

        Assert.Same(state, result);
    }

    [Theory]
    [InlineData("other-profile", "remote")]
    [InlineData("profile", "other-remote")]
    [InlineData(null, null)]
    public void ReconcileBinding_DifferentLogicalSession_RemovesOldUnreadAndContent(string? profile, string? remote)
    {
        var state = Mark(ConversationAttentionState.Empty, Reply("old reply"));

        var result = ConversationAttentionReducer.Reduce(state,
            new ReconcileConversationAttentionBindingAction("conversation", profile, remote, 1, "profile", "remote"));

        Assert.False(result.TryGetConversation("conversation", out _));
    }

    [Fact]
    public void ReconcileBinding_UnchangedLogicalSession_PreservesUnread()
    {
        var state = Mark(ConversationAttentionState.Empty, Reply("reply"));

        var result = ConversationAttentionReducer.Reduce(state,
            new ReconcileConversationAttentionBindingAction("conversation", "profile", "remote", 1, "profile", "remote"));

        Assert.Same(state, result);
    }

    [Fact]
    public void ReconcileBinding_OldReconciliationAfterNewLiveReply_PreservesNewWatermark()
    {
        var state = Mark(ConversationAttentionState.Empty, Reply("old reply"));
        state = ConversationAttentionReducer.Reduce(state,
            new MarkConversationUnreadAction("conversation", ConversationAttentionSource.AgentMessage, DateTime.UtcNow,
                "new-profile", "new-remote", Reply("new reply"), "new-connection"));

        var result = ConversationAttentionReducer.Reduce(state,
            new ReconcileConversationAttentionBindingAction("conversation", "third-profile", "third-remote", 1, "profile", "remote"));

        Assert.Same(state, result);
        Assert.Equal("new reply", result.Conversations["conversation"].Content!.TextContent);
    }

    [Fact]
    public void ReconcileBinding_RemovedAndRecreatedAttentionWithSameVersion_PreservesNewIdentity()
    {
        var state = ConversationAttentionReducer.Reduce(ConversationAttentionState.Empty,
            new MarkConversationUnreadAction("conversation", ConversationAttentionSource.AgentMessage, DateTime.UtcNow,
                "new-profile", "new-remote", Reply("new reply"), "new-connection"));

        var result = ConversationAttentionReducer.Reduce(state,
            new ReconcileConversationAttentionBindingAction("conversation", null, null, 1, "profile", "remote"));

        Assert.Same(state, result);
    }

    private static ConversationAttentionState Mark(ConversationAttentionState state, ConversationMessageSnapshot content)
        => ConversationAttentionReducer.Reduce(state,
            new MarkConversationUnreadAction("conversation", ConversationAttentionSource.AgentMessage, DateTime.UtcNow,
                "profile", "remote", content, "connection"));

    private static ConversationMessageSnapshot Reply(string text)
        => new() { Id = "same-message", ContentType = "text", TextContent = text };
}
