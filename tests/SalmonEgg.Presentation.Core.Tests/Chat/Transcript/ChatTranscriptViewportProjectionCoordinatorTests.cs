using System;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Linq;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Presentation.ViewModels.Chat;
using SalmonEgg.Presentation.ViewModels.Chat.Transcript;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat.Transcript;

public sealed class ChatTranscriptViewportProjectionCoordinatorTests
{
    [Fact]
    public void ActivateTranscript_LargeConversation_ProjectsOnlyTailWindow()
    {
        var rendered = new ObservableCollection<ChatMessageViewModel>();
        var coordinator = new ChatTranscriptViewportProjectionCoordinator(initialTailWindowSize: 4, prependBatchSize: 3);
        var transcript = BuildTranscript(count: 10);

        coordinator.ActivateTranscript(
            conversationId: "conv-large",
            transcript,
            rendered,
            ProjectMessage);

        Assert.Equal(4, rendered.Count);
        Assert.Equal("message-6", rendered[0].Id);
        Assert.Equal("message-9", rendered[^1].Id);
        Assert.Equal(6, coordinator.WindowStartIndex);
    }

    [Fact]
    public void ExpandOlderWindow_PrependsBoundedBatch_WithoutReplacingCollectionInstance()
    {
        var rendered = new ObservableCollection<ChatMessageViewModel>();
        var coordinator = new ChatTranscriptViewportProjectionCoordinator(initialTailWindowSize: 4, prependBatchSize: 3);
        var transcript = BuildTranscript(count: 10);

        coordinator.ActivateTranscript("conv-large", transcript, rendered, ProjectMessage);
        var sameInstance = rendered;

        coordinator.ExpandOlderWindow(rendered, ProjectMessage);

        Assert.Same(sameInstance, rendered);
        Assert.Equal(7, rendered.Count);
        Assert.Equal("message-3", rendered[0].Id);
        Assert.Equal("message-9", rendered[^1].Id);
    }

    [Fact]
    public void SyncTranscript_WhenTailWindowIsActive_KeepsRenderedWindowTailAnchored()
    {
        var rendered = new ObservableCollection<ChatMessageViewModel>();
        var coordinator = new ChatTranscriptViewportProjectionCoordinator(initialTailWindowSize: 4, prependBatchSize: 3);
        var transcript = BuildTranscript(count: 10);

        coordinator.ActivateTranscript("conv-large", transcript, rendered, ProjectMessage);
        var appendedTranscript = BuildTranscript(count: 11);

        var changed = coordinator.SyncTranscript(
            "conv-large",
            appendedTranscript,
            rendered,
            ProjectMessage,
            MatchesSnapshot,
            isTailAnchored: true);

        Assert.True(changed);
        Assert.Equal(4, rendered.Count);
        Assert.Equal("message-7", rendered[0].Id);
        Assert.Equal("message-10", rendered[^1].Id);
        Assert.Equal(7, coordinator.WindowStartIndex);
    }

    [Fact]
    public void SyncTranscript_WhenViewportIsDetached_KeepsWindowStartStableOnAppend()
    {
        var rendered = new ObservableCollection<ChatMessageViewModel>();
        var coordinator = new ChatTranscriptViewportProjectionCoordinator(initialTailWindowSize: 4, prependBatchSize: 3);
        var transcript = BuildTranscript(count: 10);

        coordinator.ActivateTranscript("conv-large", transcript, rendered, ProjectMessage);
        var originalFirst = rendered[0];
        var appendedTranscript = BuildTranscript(count: 11);

        var changed = coordinator.SyncTranscript(
            "conv-large",
            appendedTranscript,
            rendered,
            ProjectMessage,
            MatchesSnapshot,
            isTailAnchored: false);

        Assert.False(changed);
        Assert.Same(originalFirst, rendered[0]);
        Assert.Equal("message-6", rendered[0].Id);
        Assert.Equal("message-9", rendered[^1].Id);
        Assert.Equal(6, coordinator.WindowStartIndex);
    }

    [Fact]
    public void SyncTranscript_WhenVisibleSnapshotsAreUnchanged_ReusesExistingViewModels()
    {
        var rendered = new ObservableCollection<ChatMessageViewModel>();
        var coordinator = new ChatTranscriptViewportProjectionCoordinator(initialTailWindowSize: 4, prependBatchSize: 3);
        var transcript = BuildTranscript(count: 10);

        coordinator.ActivateTranscript("conv-large", transcript, rendered, ProjectMessage);
        var originalItems = rendered.ToArray();

        var changed = coordinator.SyncTranscript(
            "conv-large",
            transcript,
            rendered,
            ProjectMessage,
            MatchesSnapshot,
            isTailAnchored: true);

        Assert.False(changed);
        Assert.Equal(originalItems, rendered);
    }

    [Fact]
    public void MaterializeWindowAroundProjectionItem_WhenTokenIsOutsideTailWindow_MovesWindowToTarget()
    {
        var rendered = new ObservableCollection<ChatMessageViewModel>();
        var coordinator = new ChatTranscriptViewportProjectionCoordinator(initialTailWindowSize: 4, prependBatchSize: 3);
        var transcript = BuildTranscript(count: 10);

        coordinator.ActivateTranscript("conv-large", transcript, rendered, ProjectMessage);

        var materialized = coordinator.MaterializeWindowAroundProjectionItem(
            "item-2",
            rendered,
            ProjectMessage,
            static (_, index) => $"item-{index}");

        Assert.True(materialized);
        Assert.Contains(rendered, message => message.ProjectionItemKey == "item-2");
        Assert.Equal(0, coordinator.WindowStartIndex);
        Assert.Equal(4, rendered.Count);
        Assert.True(coordinator.HasPartialWindowForConversation("conv-large"));
    }

    private static ImmutableList<ConversationMessageSnapshot> BuildTranscript(int count) =>
        Enumerable.Range(0, count)
            .Select(i => new ConversationMessageSnapshot
            {
                Id = $"message-{i}",
                ContentType = "text",
                TextContent = $"Message {i}",
                Timestamp = new DateTime(2026, 5, 8, 0, 0, 0, DateTimeKind.Utc).AddSeconds(i)
            })
            .ToImmutableList();

    private static ChatMessageViewModel ProjectMessage(ConversationMessageSnapshot snapshot, int index) =>
        new()
        {
            Id = snapshot.Id ?? string.Empty,
            TextContent = snapshot.TextContent ?? string.Empty,
            ProjectionItemKey = $"item-{index}"
        };

    private static bool MatchesSnapshot(ChatMessageViewModel viewModel, ConversationMessageSnapshot snapshot) =>
        string.Equals(viewModel.Id, snapshot.Id, StringComparison.Ordinal)
        && string.Equals(viewModel.TextContent, snapshot.TextContent, StringComparison.Ordinal);
}
