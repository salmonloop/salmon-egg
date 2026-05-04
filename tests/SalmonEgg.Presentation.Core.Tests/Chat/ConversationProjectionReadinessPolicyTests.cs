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
    public void HasReusableWarmProjection_WhenWorkspaceSnapshotContainsNoProjectedState_ReturnsFalse()
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

        Assert.False(result);
    }

    [Fact]
    public void HasReusableWarmProjection_WhenWorkspaceSnapshotContainsTranscript_ReturnsTrue()
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
            Transcript:
            [
                new ConversationMessageSnapshot
                {
                    Id = "m-1",
                    Timestamp = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc),
                    ContentType = "text",
                    TextContent = "cached"
                }
            ],
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

    [Fact]
    public void HasReusableWarmProjection_WhenSlicesExistButContainNoProjectedState_ReturnsFalse()
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
                    DateTime.UtcNow)),
            ConversationContents = ImmutableDictionary<string, ConversationContentSlice>.Empty.Add(
                "conv-1",
                new ConversationContentSlice(
                    ImmutableList<ConversationMessageSnapshot>.Empty,
                    ImmutableList<ConversationPlanEntrySnapshot>.Empty,
                    false,
                    null)),
            ConversationSessionStates = ImmutableDictionary<string, ConversationSessionStateSlice>.Empty.Add(
                "conv-1",
                new ConversationSessionStateSlice(
                    ImmutableList<ConversationModeOptionSnapshot>.Empty,
                    null,
                    ImmutableList<ConversationConfigOptionSnapshot>.Empty,
                    false,
                    ImmutableList<ConversationAvailableCommandSnapshot>.Empty,
                    null,
                    null))
        };

        var result = ConversationProjectionReadinessPolicy.HasReusableWarmProjection(
            state,
            "conv-1",
            snapshot: null);

        Assert.False(result);
    }

    [Fact]
    public void HasReusableWarmProjection_WhenSessionStateSliceHasAuxiliaryProjection_ReturnsTrue()
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
                    DateTime.UtcNow)),
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
                        Title = "Warm conversation"
                    },
                    null))
        };

        var result = ConversationProjectionReadinessPolicy.HasReusableWarmProjection(
            state,
            "conv-1",
            snapshot: null);

        Assert.True(result);
    }
}
