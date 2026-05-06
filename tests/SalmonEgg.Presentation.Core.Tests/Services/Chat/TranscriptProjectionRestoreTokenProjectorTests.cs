using System.Collections.Immutable;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Services.Chat;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Services.Chat;

public sealed class TranscriptProjectionRestoreTokenProjectorTests
{
    [Fact]
    public void SameSemanticMessage_KeepsProjectionItemKeyAcrossAppend()
    {
        var sut = new TranscriptProjectionRestoreTokenProjector();
        var baseTranscript = ImmutableList.Create(
            Message("agent-001", "first"),
            Message("agent-002", "second"));
        var grownTranscript = baseTranscript.Add(Message("agent-003", "third"));

        var before = sut.Project("conv-a", baseTranscript, firstVisibleIndex: 1, relativeOffsetWithinItem: 12d);
        var after = sut.Project("conv-a", grownTranscript, firstVisibleIndex: 1, relativeOffsetWithinItem: 12d);

        Assert.True(before.IsReady);
        Assert.True(after.IsReady);
        Assert.NotNull(before.Token);
        Assert.NotNull(after.Token);
        Assert.Equal(before.Token.Value.ProjectionItemKey, after.Token.Value.ProjectionItemKey);
    }

    [Fact]
    public void EmptyTranscript_IsNotRestoreReady()
    {
        var sut = new TranscriptProjectionRestoreTokenProjector();

        var projection = sut.Project(
            "conv-a",
            ImmutableList<ConversationMessageSnapshot>.Empty,
            firstVisibleIndex: -1,
            relativeOffsetWithinItem: 0d);

        Assert.False(projection.IsReady);
        Assert.Null(projection.Token);
    }

    [Fact]
    public void Apply_PopulatesRestoreProjectionFromProjectedTranscriptSlice()
    {
        var transcript = ImmutableList.Create(
            Message("agent-001", "first"),
            Message("agent-002", "second"));
        var state = new ChatState(
            HydratedConversationId: "conv-a",
            Transcript: ImmutableList.Create(Message("stale", "stale")),
            PlanEntries: ImmutableList<ConversationPlanEntrySnapshot>.Empty,
            ConversationContents: ImmutableDictionary<string, ConversationContentSlice>.Empty.Add(
                "conv-a",
                new ConversationContentSlice(
                    transcript,
                    ImmutableList<ConversationPlanEntrySnapshot>.Empty,
                    false,
                    null)));
        var sut = new ChatStateProjector();

        var projection = sut.Apply(state, ChatConnectionState.Empty, "conv-a", binding: null);

        Assert.True(projection.RestoreProjection.IsReady);
        Assert.NotNull(projection.RestoreProjection.Token);
        Assert.Equal("conv-a", projection.RestoreProjection.Token.Value.ConversationId);
        Assert.Equal("msg:agent-002", projection.RestoreProjection.Token.Value.ProjectionItemKey);
    }

    [Fact]
    public void MissingMessageId_UsesFallbackProjectionItemKey()
    {
        var sut = new TranscriptProjectionRestoreTokenProjector();
        var transcript = ImmutableList.Create(
            Message(string.Empty, "first"),
            Message(null, "fallback"));

        var projection = sut.Project("conv-a", transcript, firstVisibleIndex: 1, relativeOffsetWithinItem: 3d);

        Assert.True(projection.IsReady);
        Assert.NotNull(projection.Token);
        Assert.Equal("idx:1:text/plain:fallback", projection.Token.Value.ProjectionItemKey);
    }

    private static ConversationMessageSnapshot Message(string? id, string text)
        => new()
        {
            Id = id ?? string.Empty,
            ContentType = "text/plain",
            TextContent = text,
            Timestamp = DateTime.UtcNow,
        };
}
