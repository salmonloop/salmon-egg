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

        var before = sut.Project("conv-a", baseTranscript, firstVisibleIndex: 1);
        var after = sut.Project("conv-a", grownTranscript, firstVisibleIndex: 1);

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
            firstVisibleIndex: -1);

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
                    false)));
        var sut = new ChatStateProjector();

        var projection = sut.Apply(state, ChatConnectionState.Empty, "conv-a", binding: null);

        Assert.True(projection.RestoreProjection.IsReady);
        Assert.NotNull(projection.RestoreProjection.Token);
        Assert.Equal("conv-a", projection.RestoreProjection.Token.Value.ConversationId);
        Assert.Equal("msg:agent-002", projection.RestoreProjection.Token.Value.ProjectionItemKey);
    }

    [Fact]
    public void MissingMessageId_UsesIndexScopedFallbackWithoutMutableBody()
    {
        var sut = new TranscriptProjectionRestoreTokenProjector();
        var transcript = ImmutableList.Create(
            Message(string.Empty, "first"),
            Message(null, "fallback"));

        var projection = sut.Project("conv-a", transcript, firstVisibleIndex: 1);

        Assert.True(projection.IsReady);
        Assert.NotNull(projection.Token);
        Assert.Equal("idx:1:text:in", projection.Token.Value.ProjectionItemKey);
    }

    [Fact]
    public void MissingMessageId_StreamingTextContent_DoesNotInvalidateCapturedKey()
    {
        // First-principles defect: identity keys must not include mutable body text.
        // Detached viewport restore captures ProjectionItemKey; ACP streams rewrite TextContent
        // on the same row. Including TextContent made IndexOfProjectionItemKey miss the anchor
        // and fall back to bottom follow after every chunk.
        var beforeStream = new ConversationMessageSnapshot
        {
            Id = string.Empty,
            ContentType = "text",
            TextContent = "Hel",
            IsOutgoing = false
        };
        var afterStream = new ConversationMessageSnapshot
        {
            Id = string.Empty,
            ContentType = "text",
            TextContent = "Hello, world",
            IsOutgoing = false
        };

        var keyBefore = TranscriptProjectionRestoreTokenProjector.CreateProjectionItemKey(beforeStream, 0);
        var keyAfter = TranscriptProjectionRestoreTokenProjector.CreateProjectionItemKey(afterStream, 0);

        Assert.Equal(keyBefore, keyAfter);
        Assert.Equal("idx:0:text:in", keyBefore);
    }

    [Fact]
    public void MissingMessageId_PrefersProtocolMessageIdOverIndexFallback()
    {
        var snapshot = new ConversationMessageSnapshot
        {
            Id = string.Empty,
            ProtocolMessageId = "acp-msg-9",
            ContentType = "text",
            TextContent = "partial",
            IsOutgoing = false
        };

        Assert.Equal(
            "proto:acp-msg-9",
            TranscriptProjectionRestoreTokenProjector.CreateProjectionItemKey(snapshot, 3));
    }

    [Fact]
    public void ApplySnapshot_ProjectsProtocolMessageIdOntoViewModel()
    {
        // ProtocolMessageId is authoritative identity when app Id is empty; the VM must own
        // the same fact so patch matching / restore keys stay single-sourced.
        var vm = new SalmonEgg.Presentation.ViewModels.Chat.ChatMessageViewModel();
        vm.ApplySnapshot(
            new ConversationMessageSnapshot
            {
                Id = string.Empty,
                ProtocolMessageId = "acp-msg-9",
                ContentType = "text",
                TextContent = "partial",
                IsOutgoing = false
            },
            projectionIndex: 3);

        Assert.Equal("acp-msg-9", vm.ProtocolMessageId);
        Assert.Equal("proto:acp-msg-9", vm.ProjectionItemKey);
    }

    [Fact]
    public void MissingMessageId_PrefersToolCallIdForToolCallRows()
    {
        var snapshot = new ConversationMessageSnapshot
        {
            Id = string.Empty,
            ToolCallId = "call-42",
            ContentType = "tool_call",
            Title = "Read",
            IsOutgoing = false
        };

        Assert.Equal(
            "tool:call-42",
            TranscriptProjectionRestoreTokenProjector.CreateProjectionItemKey(snapshot, 2));
    }

    private static ConversationMessageSnapshot Message(string? id, string text)
        => new()
        {
            Id = id ?? string.Empty,
            ContentType = "text",
            TextContent = text,
            IsOutgoing = false,
            Timestamp = DateTime.UtcNow,
        };
}
