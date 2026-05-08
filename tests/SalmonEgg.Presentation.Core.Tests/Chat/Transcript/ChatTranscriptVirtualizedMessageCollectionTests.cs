using System;
using System.Collections.Immutable;
using System.Collections.Specialized;
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
    public void RangeCache_ProjectsTrackedRangesAndPrunesUntrackedCache()
    {
        var projectedIndexes = new List<int>();
        var transcript = BuildTranscript(100);
        var source = new ChatTranscriptVirtualizedMessageCollection();
        source.Reset("conv-large", transcript, (snapshot, index) =>
        {
            projectedIndexes.Add(index);
            return Project(snapshot, index);
        }, MatchesSnapshot);
        var sut = new ChatTranscriptRangeCache(source);

        _ = sut[5];
        sut.ApplyRequiredRanges(
            visibleRange: new TranscriptVirtualizationRange(10, 3),
            trackedRanges: [new TranscriptVirtualizationRange(20, 2)]);

        Assert.Equal(new[] { 5, 10, 11, 12, 20, 21 }, projectedIndexes);
        Assert.False(sut.IsItemCached(5));
        Assert.True(sut.IsItemCached(10));
        Assert.True(sut.IsItemCached(21));
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
    public void Reset_WhenSameConversationChangesPrefixAndAppends_PublishesReset()
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

        Assert.Equal([NotifyCollectionChangedAction.Reset], events);
        Assert.Equal("Changed", sut[0].TextContent);
    }

    [Fact]
    public void RangeCaches_ForSeparateNativeItemsControls_AreIndependent()
    {
        var source = new ChatTranscriptVirtualizedMessageCollection();
        source.Reset("conv-1", BuildTranscript(100), Project, MatchesSnapshot);
        var firstControlCache = new ChatTranscriptRangeCache(source);
        var secondControlCache = new ChatTranscriptRangeCache(source);

        firstControlCache.ApplyRequiredRanges(
            visibleRange: new TranscriptVirtualizationRange(10, 2),
            trackedRanges: []);
        secondControlCache.ApplyRequiredRanges(
            visibleRange: new TranscriptVirtualizationRange(80, 2),
            trackedRanges: []);

        Assert.True(firstControlCache.IsItemCached(10));
        Assert.True(secondControlCache.IsItemCached(80));
        Assert.False(firstControlCache.IsItemCached(80));
        Assert.False(secondControlCache.IsItemCached(10));
    }

    [Fact]
    public void RangeCache_Clear_DropsProjectedItemsForSourceReset()
    {
        var projectedIndexes = new List<int>();
        var source = new ChatTranscriptVirtualizedMessageCollection();
        source.Reset("conv-1", BuildTranscript(10), (snapshot, index) =>
        {
            projectedIndexes.Add(index);
            return Project(snapshot, index);
        }, MatchesSnapshot);
        var sut = new ChatTranscriptRangeCache(source);

        _ = sut[3];
        sut.Clear();
        _ = sut[3];

        Assert.Equal([3, 3], projectedIndexes);
    }

    [Fact]
    public void RangeCache_WithOversizedNativeRange_ClampsWithoutOverflow()
    {
        var source = new ChatTranscriptVirtualizedMessageCollection();
        source.Reset("conv-1", BuildTranscript(3), Project, MatchesSnapshot);
        var sut = new ChatTranscriptRangeCache(source);

        sut.ApplyRequiredRanges(
            visibleRange: new TranscriptVirtualizationRange(2, int.MaxValue),
            trackedRanges: []);

        Assert.True(sut.IsItemCached(2));
        Assert.False(sut.IsItemCached(0));
        Assert.False(sut.IsItemCached(1));
    }

    private static ImmutableList<ConversationMessageSnapshot> BuildTranscript(int count) =>
        Enumerable.Range(0, count)
            .Select(index => new ConversationMessageSnapshot
            {
                Id = $"message-{index}",
                Timestamp = new DateTime(2026, 5, 8, 0, 0, 0, DateTimeKind.Utc).AddSeconds(index),
                ContentType = "text",
                TextContent = $"Message {index}"
            })
            .ToImmutableList();

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
