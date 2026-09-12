using System;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.ViewModels.Chat.Panels;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public sealed class ConversationStatusPolicyTests
{
    private static readonly DateTime StartedAt = new(2026, 9, 11, 10, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void Resolve_AnyActionableFailure_TakesPriorityOverPendingRunningAndUnread(
        bool operationFailure,
        bool interactionFailure,
        bool turnFailure)
    {
        // Arrange
        var turn = Turn(turnFailure ? ChatTurnPhase.Failed : ChatTurnPhase.Thinking);
        var interaction = new ConversationInteractionSummary(true, true, interactionFailure);

        // Act
        var result = ConversationStatusPolicy.Resolve(turn, interaction, Attention(true),
            operationFailure ? new ConversationOperationFailure("conversation", "Failure") : null);

        // Assert
        Assert.Equal(ConversationStatusGroup.NeedsAttention, result.Group);
        Assert.Equal(ConversationStatusIcon.Error, result.Icon);
    }

    [Theory]
    [InlineData(true, true, ConversationStatusIcon.Permission)]
    [InlineData(true, false, ConversationStatusIcon.Permission)]
    [InlineData(false, true, ConversationStatusIcon.Input)]
    public void Resolve_PendingInteraction_TakesPriorityOverRunningAndUnread(
        bool permission,
        bool input,
        ConversationStatusIcon expectedIcon)
    {
        // Arrange
        var interaction = new ConversationInteractionSummary(permission, input, false);

        // Act
        var result = ConversationStatusPolicy.Resolve(Turn(ChatTurnPhase.Responding), interaction, Attention(true));

        // Assert
        Assert.Equal(ConversationStatusGroup.NeedsAttention, result.Group);
        Assert.Equal(expectedIcon, result.Icon);
    }

    [Theory]
    [InlineData(ChatTurnPhase.CreatingRemoteSession)]
    [InlineData(ChatTurnPhase.DispatchingPrompt)]
    [InlineData(ChatTurnPhase.WaitingForAgent)]
    [InlineData(ChatTurnPhase.Thinking)]
    [InlineData(ChatTurnPhase.ToolPending)]
    [InlineData(ChatTurnPhase.ToolRunning)]
    [InlineData(ChatTurnPhase.Responding)]
    public void Resolve_RunningTurnWithUnreadChunks_RemainsWorkingAtSameActivityTime(ChatTurnPhase phase)
    {
        // Arrange
        var first = ConversationStatusPolicy.Resolve(Turn(phase), default, Attention(true));
        var nextChunk = Attention(true) with { UnreadVersion = 2, LastAttentionAtUtc = StartedAt.AddMinutes(2) };

        // Act
        var next = ConversationStatusPolicy.Resolve(Turn(phase) with { LastUpdatedAtUtc = StartedAt.AddMinutes(2) }, default, nextChunk);

        // Assert
        Assert.Equal(ConversationStatusGroup.Working, next.Group);
        Assert.Equal(ConversationStatusIcon.Working, next.Icon);
        Assert.Equal(first.ActivityAt, next.ActivityAt);
    }

    [Theory]
    [InlineData(ChatTurnPhase.Completed, true, ConversationStatusGroup.NeedsAttention, ConversationStatusIcon.Unread)]
    [InlineData(ChatTurnPhase.Completed, false, ConversationStatusGroup.Other, ConversationStatusIcon.Conversation)]
    [InlineData(ChatTurnPhase.Cancelled, true, ConversationStatusGroup.NeedsAttention, ConversationStatusIcon.Unread)]
    [InlineData(ChatTurnPhase.Cancelled, false, ConversationStatusGroup.Other, ConversationStatusIcon.Conversation)]
    public void Resolve_TerminalTurn_GroupsByRemainingUnread(
        ChatTurnPhase phase,
        bool unread,
        ConversationStatusGroup expectedGroup,
        ConversationStatusIcon expectedIcon)
    {
        // Arrange / Act
        var result = ConversationStatusPolicy.Resolve(Turn(phase), default, Attention(unread));

        // Assert
        Assert.Equal(expectedGroup, result.Group);
        Assert.Equal(expectedIcon, result.Icon);
    }

    [Theory]
    [InlineData(ChatTurnPhase.Completed, true)]
    [InlineData(ChatTurnPhase.Completed, false)]
    [InlineData(ChatTurnPhase.Cancelled, true)]
    [InlineData(ChatTurnPhase.Cancelled, false)]
    [InlineData(ChatTurnPhase.Failed, false)]
    public void Resolve_TurnTerminates_AdvancesActivityOnceToCompletionBoundary(ChatTurnPhase phase, bool unread)
    {
        var endedAt = StartedAt.AddMinutes(2);
        var running = Turn(ChatTurnPhase.Responding) with { LastUpdatedAtUtc = endedAt.AddSeconds(-1) };
        var completed = running with { Phase = phase, LastUpdatedAtUtc = endedAt };

        var before = ConversationStatusPolicy.Resolve(running, default, Attention(unread));
        var after = ConversationStatusPolicy.Resolve(completed, default, Attention(unread));

        Assert.Equal(StartedAt, before.ActivityAt);
        Assert.Equal(endedAt, after.ActivityAt);
    }

    [Fact]
    public void Resolve_NoRuntimeFacts_LeavesConversationInOther()
    {
        // Arrange / Act
        var result = ConversationStatusPolicy.Resolve(null, default, null);

        // Assert
        Assert.Equal(ConversationStatusGroup.Other, result.Group);
        Assert.Equal(ConversationStatusIcon.Conversation, result.Icon);
        Assert.Null(result.ActivityAt);
    }

    [Fact]
    public void Resolve_ReadReceipt_DoesNotResolveAnOutstandingPermission()
    {
        // Arrange
        var interaction = new ConversationInteractionSummary(true, false, false);

        // Act
        var result = ConversationStatusPolicy.Resolve(Turn(ChatTurnPhase.Completed), interaction, Attention(false));

        // Assert
        Assert.Equal(ConversationStatusGroup.NeedsAttention, result.Group);
        Assert.Equal(ConversationStatusIcon.Permission, result.Icon);
    }

    private static ActiveTurnState Turn(ChatTurnPhase phase) =>
        new("conversation", "turn", phase, StartedAt, StartedAt);

    private static ConversationAttentionSlice Attention(bool unread) =>
        new("conversation", unread, 1, StartedAt.AddSeconds(1), ConversationAttentionSource.AgentMessage);
}
