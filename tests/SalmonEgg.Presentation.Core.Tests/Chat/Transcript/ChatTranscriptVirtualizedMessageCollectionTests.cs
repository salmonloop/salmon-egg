using System;
using System.Collections.Immutable;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Presentation.ViewModels.Chat;
using SalmonEgg.Presentation.ViewModels.Chat.Transcript;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat.Transcript;

public sealed class ChatTranscriptVirtualizedMessageCollectionTests
{
    [Fact]
    public void Reset_WithLargeTranscript_ExposesFullCountWithoutProjectingAllItems()
    {
        var projectedIndexes = new List<int>();
        var transcript = BuildTranscript(5000);
        var sut = new ChatTranscriptVirtualizedMessageCollection();

        sut.Reset("conv-large", transcript, (snapshot, index) =>
        {
            projectedIndexes.Add(index);
            return Project(snapshot, index);
        }, MatchesSnapshot);

        Assert.Equal(5000, sut.Count);
        Assert.Empty(projectedIndexes);

        Assert.Equal("message-1234", sut[1234].Id);
        Assert.Equal([1234], projectedIndexes);
    }

    [Fact]
    public void Reset_WhenSameConversationAppendsStablePrefix_PublishesAddWithoutFullReset()
    {
        var projectedIndexes = new List<int>();
        var sut = new ChatTranscriptVirtualizedMessageCollection();
        var events = new List<NotifyCollectionChangedAction>();
        sut.CollectionChanged += (_, args) => events.Add(args.Action);

        sut.Reset("conv-1", BuildTranscript(2), (snapshot, index) =>
        {
            projectedIndexes.Add(index);
            return Project(snapshot, index);
        }, MatchesSnapshot);
        events.Clear();

        sut.Reset("conv-1", BuildTranscript(3), (snapshot, index) =>
        {
            projectedIndexes.Add(index);
            return Project(snapshot, index);
        }, MatchesSnapshot);

        Assert.Equal([NotifyCollectionChangedAction.Add], events);
        Assert.Equal(3, sut.Count);
        Assert.Empty(projectedIndexes);
        Assert.Equal("message-2", sut[2].Id);
        Assert.Equal([2], projectedIndexes);
    }

    [Fact]
    public void Reset_WhenSameConversationReplaysSharedAppends_DoesNotRescanHistoricalPrefix()
    {
        var sut = new ChatTranscriptVirtualizedMessageCollection();
        var transcript = BuildTranscript(1);
        sut.Reset("conv-1", transcript, Project, MatchesSnapshot);

        var elapsed = Stopwatch.StartNew();
        for (var index = 1; index < 8_000; index++)
        {
            transcript = transcript.Add(CreateMessage(index));
            sut.Reset("conv-1", transcript, Project, MatchesSnapshot);
        }

        elapsed.Stop();

        Assert.True(
            elapsed.Elapsed < TimeSpan.FromSeconds(1),
            $"Replay append projection should stay bounded; elapsed={elapsed.Elapsed}.");
    }

    [Fact]
    public void Reset_WhenSameConversationReceivesSameTranscriptReference_DoesNotResetNativeItemsSource()
    {
        var transcript = BuildTranscript(100);
        var sut = new ChatTranscriptVirtualizedMessageCollection();
        var events = new List<NotifyCollectionChangedAction>();
        sut.CollectionChanged += (_, args) => events.Add(args.Action);

        sut.Reset("conv-1", transcript, Project, MatchesSnapshot);
        events.Clear();

        sut.Reset("conv-1", transcript, Project, MatchesSnapshot);

        Assert.Empty(events);
    }

    [Fact]
    public void Reset_WhenSameConversationChangesUncachedItem_PublishesReplaceForVirtualizedAdapter()
    {
        var sut = new ChatTranscriptVirtualizedMessageCollection();
        var events = new List<NotifyCollectionChangedEventArgs>();
        sut.CollectionChanged += (_, args) => events.Add(args);

        sut.Reset("conv-1", BuildTranscript(1), Project, MatchesSnapshot);
        events.Clear();

        var changedTranscript = BuildTranscript(1).SetItem(
            0,
            new ConversationMessageSnapshot
            {
                Id = "message-0",
                Timestamp = new DateTime(2026, 5, 8, 0, 0, 0, DateTimeKind.Utc),
                ContentType = "text",
                TextContent = "streamed"
            });

        sut.Reset("conv-1", changedTranscript, Project, MatchesSnapshot);

        var replace = Assert.Single(events);
        Assert.Equal(NotifyCollectionChangedAction.Replace, replace.Action);
        Assert.Equal(0, replace.NewStartingIndex);
        var newItem = Assert.IsType<ChatMessageViewModel>(Assert.Single(replace.NewItems!));
        var oldItem = Assert.IsType<ChatMessageViewModel>(Assert.Single(replace.OldItems!));
        Assert.Equal("streamed", newItem.TextContent);
        Assert.Equal("Message 0", oldItem.TextContent);
    }

    [Fact]
    public void Reset_WhenSameConversationChangesCachedPrefixAndAppends_PublishesReplaceAndAddWithoutReset()
    {
        var sut = new ChatTranscriptVirtualizedMessageCollection();
        var events = new List<NotifyCollectionChangedAction>();
        sut.CollectionChanged += (_, args) => events.Add(args.Action);
        sut.Reset("conv-1", BuildTranscript(2), Project, MatchesSnapshot);
        _ = sut[0];
        events.Clear();

        var changedTranscript = BuildTranscript(3).SetItem(
            0,
            new ConversationMessageSnapshot
            {
                Id = "message-0",
                Timestamp = new DateTime(2026, 5, 8, 0, 0, 0, DateTimeKind.Utc),
                ContentType = "text",
                TextContent = "Changed"
            });

        sut.Reset("conv-1", changedTranscript, Project, MatchesSnapshot);

        Assert.Equal([NotifyCollectionChangedAction.Replace, NotifyCollectionChangedAction.Add], events);
        Assert.Equal("Changed", sut[0].TextContent);
    }

    private static ImmutableList<ConversationMessageSnapshot> BuildTranscript(int count) =>
        Enumerable.Range(0, count)
            .Select(CreateMessage)
            .ToImmutableList();

    private static ConversationMessageSnapshot CreateMessage(int index) =>
        new()
        {
            Id = $"message-{index}",
            Timestamp = new DateTime(2026, 5, 8, 0, 0, 0, DateTimeKind.Utc).AddSeconds(index),
            ContentType = "text",
            TextContent = $"Message {index}"
        };

    private static ChatMessageViewModel Project(ConversationMessageSnapshot snapshot, int index) =>
        new()
        {
            Id = snapshot.Id ?? string.Empty,
            ProjectionItemKey = $"msg:{index}",
            TextContent = snapshot.TextContent ?? string.Empty
        };

    private static bool MatchesSnapshot(ChatMessageViewModel viewModel, ConversationMessageSnapshot snapshot) =>
        string.Equals(viewModel.Id, snapshot.Id, StringComparison.Ordinal)
        && string.Equals(viewModel.TextContent, snapshot.TextContent, StringComparison.Ordinal);
}
