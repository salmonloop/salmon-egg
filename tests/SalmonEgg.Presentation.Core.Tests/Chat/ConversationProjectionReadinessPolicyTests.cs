using System;
using System.Collections.Immutable;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Services.Chat;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public sealed class ConversationProjectionReadinessPolicyTests
{
    [Fact]
    public void HasReusableWarmProjection_WhenWorkspaceSnapshotExists_ReturnsTrue()
    {
        var state = ChatState.Empty with
        {
            RuntimeStates = ImmutableDictionary<string, ConversationRuntimeSlice>.Empty.Add(
                "conv-1",
                new ConversationRuntimeSlice(
                    "conv-1",
                    ConversationRuntimePhase.Warm,
                    "conn-1",
                    "remote-1",
                    "profile-1",
                    "SessionLoadCompleted",
                    DateTime.UtcNow))
        };
        var snapshot = new ConversationWorkspaceSnapshot(
            ConversationId: "conv-1",
            Transcript: [],
            Plan: [],
            ShowPlanPanel: false,
            PlanTitle: null,
            CreatedAt: new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt: new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc));

        var result = ConversationProjectionReadinessPolicy.HasReusableWarmProjection(
            state,
            "conv-1",
            snapshot);

        Assert.True(result);
    }

    [Fact]
    public void HasReusableWarmProjection_WhenConversationProjectionAndSnapshotAreMissing_ReturnsFalse()
    {
        var state = ChatState.Empty with
        {
            RuntimeStates = ImmutableDictionary<string, ConversationRuntimeSlice>.Empty.Add(
                "conv-1",
                new ConversationRuntimeSlice(
                    "conv-1",
                    ConversationRuntimePhase.Warm,
                    "conn-1",
                    "remote-1",
                    "profile-1",
                    "SessionLoadCompleted",
                    DateTime.UtcNow))
        };

        var result = ConversationProjectionReadinessPolicy.HasReusableWarmProjection(
            state,
            "conv-1",
            snapshot: null);

        Assert.False(result);
    }
}
