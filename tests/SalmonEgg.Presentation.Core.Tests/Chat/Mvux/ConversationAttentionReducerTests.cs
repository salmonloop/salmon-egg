using System;
using System.Collections.Generic;
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

        var next = ConversationAttentionReducer.Reduce(initial, new ClearConversationUnreadAction("conv-1"));

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

        var cleared = ConversationAttentionReducer.Reduce(initial, new ClearConversationUnreadAction("missing"));
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
}
