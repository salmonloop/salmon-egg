using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.Specialized;
using System.Linq;
using SalmonEgg.Acp.Tool;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.ViewModels.Chat;
using SalmonEgg.Presentation.ViewModels.Chat.Transcript;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat.Transcript;

/// <summary>
/// Cross-layer materialization identity contracts for virtualized chat transcripts.
/// Models the ListView adapter dual-cache protocol without Uno UI types so the
/// tests stay portable across platforms (AGENTS.md §5.2 / §5.5).
/// </summary>
public sealed class TranscriptMaterializationIdentityTests
{
    [Fact]
    public void CreateItem_ProducesEphemeralProjectionOutsideAuthoritativeCache()
    {
        // MatchesItem compares snapshot fields, not cache membership. CreateItem still
        // matches the current snapshot, but each call is a new identity and is not the
        // instance owned by the authoritative indexer cache.
        var source = CreateSource(BuildMixedTranscript());
        var orphanA = source.CreateItem(0);
        var orphanB = source.CreateItem(0);
        var bound = source[0];

        Assert.NotSame(orphanA, orphanB);
        Assert.NotSame(orphanA, bound);
        Assert.True(source.MatchesItem(0, orphanA));
        Assert.True(source.MatchesItem(0, bound));
        Assert.Same(bound, source[0]);
    }

    [Fact]
    public void Indexer_RegistersAuthoritativeIdentity_AndReusesSameInstance()
    {
        var source = CreateSource(BuildMixedTranscript());
        var bound = source[0];

        Assert.True(source.MatchesItem(0, bound));
        Assert.Same(bound, source[0]);
        Assert.Same(bound, source[0]);
    }

    [Fact]
    public void AuthoritativeIndexer_StreamingSameShapeMixedTranscript_PatchesInPlaceWithoutNativeReset()
    {
        var source = CreateSource(BuildMixedTranscript());
        var adapter = new DualCacheAdapter(source, MaterializationMode.AuthoritativeIndexer);

        var markdown = adapter.Materialize(0);
        var toolCall = adapter.Materialize(1);
        adapter.ClearNativeEvents();

        var streamed = BuildMixedTranscript()
            .SetItem(0, TextMessage(
                id: "message-0",
                text: "## streamed markdown\n\n- item",
                seconds: 0))
            .SetItem(1, ToolCallMessage(
                id: "message-1",
                toolCallId: "tool-1",
                title: "Read file",
                status: ToolCallStatus.Completed,
                seconds: 1));

        source.Reset(ConversationId, streamed, Project, MatchesSnapshot, PatchProjectedMessage);

        Assert.Empty(adapter.NativeEvents);
        Assert.Same(markdown, adapter.Materialize(0));
        Assert.Same(toolCall, adapter.Materialize(1));
        Assert.Equal("## streamed markdown\n\n- item", markdown.TextContent);
        Assert.Equal(ToolCallStatus.Completed, toolCall.ToolCallStatus);
        Assert.True(source.MatchesItem(0, markdown));
        Assert.True(source.MatchesItem(1, toolCall));
    }

    [Fact]
    public void CreateItemBypass_StreamingSameShape_ForcesReplaceAndNativeResetStorm()
    {
        // Regression contract for Skia blank rows: when the UI adapter materializes via
        // CreateItem, the source cache never owns the bound VM. Streaming then emits
        // Replace, and the adapter upgrades Replace into a full native Reset.
        var source = CreateSource(BuildMixedTranscript());
        var adapter = new DualCacheAdapter(source, MaterializationMode.CreateItemBypass);

        var orphanMarkdown = adapter.Materialize(0);
        var orphanToolCall = adapter.Materialize(1);
        adapter.ClearNativeEvents();

        var streamed = BuildMixedTranscript()
            .SetItem(0, TextMessage(
                id: "message-0",
                text: "## streamed markdown\n\n- item",
                seconds: 0))
            .SetItem(1, ToolCallMessage(
                id: "message-1",
                toolCallId: "tool-1",
                title: "Read file",
                status: ToolCallStatus.Completed,
                seconds: 1));

        source.Reset(ConversationId, streamed, Project, MatchesSnapshot, PatchProjectedMessage);

        Assert.Contains(NotifyCollectionChangedAction.Reset, adapter.NativeEvents);
        Assert.DoesNotContain(NotifyCollectionChangedAction.Replace, adapter.NativeEvents);
        Assert.Equal("hello", orphanMarkdown.TextContent);
        Assert.Null(orphanToolCall.ToolCallStatus);
        Assert.False(source.MatchesItem(0, orphanMarkdown));
        Assert.False(source.MatchesItem(1, orphanToolCall));

        var reboundMarkdown = adapter.Materialize(0);
        var reboundToolCall = adapter.Materialize(1);
        Assert.NotSame(orphanMarkdown, reboundMarkdown);
        Assert.NotSame(orphanToolCall, reboundToolCall);
        Assert.Equal("## streamed markdown\n\n- item", reboundMarkdown.TextContent);
        Assert.Equal(ToolCallStatus.Completed, reboundToolCall.ToolCallStatus);
    }

    [Fact]
    public void AuthoritativeIndexer_StreamingToolCallStatus_DoesNotChangeTemplateShapeOrIdentity()
    {
        var source = CreateSource(ImmutableList.Create(
            ToolCallMessage("message-0", "tool-1", "Shell", ToolCallStatus.InProgress, seconds: 0)));
        var adapter = new DualCacheAdapter(source, MaterializationMode.AuthoritativeIndexer);
        var bound = adapter.Materialize(0);
        adapter.ClearNativeEvents();

        source.Reset(
            ConversationId,
            ImmutableList.Create(
                ToolCallMessage("message-0", "tool-1", "Shell", ToolCallStatus.Completed, seconds: 0)),
            Project,
            MatchesSnapshot,
            PatchProjectedMessage);

        Assert.Empty(adapter.NativeEvents);
        Assert.Same(bound, adapter.Materialize(0));
        Assert.Equal(ToolCallStatus.Completed, bound.ToolCallStatus);
        Assert.Equal("tool_call", bound.ContentType);
        Assert.True(ChatMessageViewModel.HasSameTemplateShape(
            bound,
            ToolCallMessage("message-0", "tool-1", "Shell", ToolCallStatus.Completed, seconds: 0)));
    }

    [Fact]
    public void AuthoritativeIndexer_AppendAfterStreamingPatch_ForwardsAddWithoutReset()
    {
        var source = CreateSource(BuildMixedTranscript());
        var adapter = new DualCacheAdapter(source, MaterializationMode.AuthoritativeIndexer);
        _ = adapter.Materialize(0);
        _ = adapter.Materialize(1);
        adapter.ClearNativeEvents();

        var streamedAndAppended = BuildMixedTranscript()
            .SetItem(0, TextMessage(
                id: "message-0",
                text: "## streamed markdown",
                seconds: 0))
            .Add(TextMessage(id: "message-2", text: "tail", seconds: 2));

        source.Reset(ConversationId, streamedAndAppended, Project, MatchesSnapshot, PatchProjectedMessage);

        Assert.Equal([NotifyCollectionChangedAction.Add], adapter.NativeEvents);
        Assert.Equal(3, source.Count);
        Assert.Equal("## streamed markdown", adapter.Materialize(0).TextContent);
        Assert.Equal("tail", adapter.Materialize(2).TextContent);
    }

    [Fact]
    public void TemplateShapeReplaceDuringAppend_NativeResetSuppressesCoveredAdd()
    {
        // Mirrors ListViewTranscriptItemsSource: a Replace-driven native Reset already
        // observes the post-reset count, so the matching Add must not be replayed.
        var source = CreateSource(BuildTranscript(1));
        var adapter = new DualCacheAdapter(source, MaterializationMode.AuthoritativeIndexer);
        _ = adapter.Materialize(0);
        adapter.ClearNativeEvents();

        var replacedAndAppended = ImmutableList.Create(
            ToolCallMessage(
                id: "message-0",
                toolCallId: "tool-1",
                title: "Read file",
                status: ToolCallStatus.InProgress,
                seconds: 0),
            TextMessage("message-1", "Message 1", seconds: 1));

        source.Reset(ConversationId, replacedAndAppended, Project, MatchesSnapshot, PatchProjectedMessage);

        Assert.Equal([NotifyCollectionChangedAction.Reset], adapter.NativeEvents);
        Assert.Equal(2, source.Count);
        Assert.Equal("tool_call", adapter.Materialize(0).ContentType);
        Assert.Equal("Message 1", adapter.Materialize(1).TextContent);
    }

    [Fact]
    public void CreateItemBypass_StreamingPrefixDuringAppend_LeavesOrphanStaleWithoutNativeEvent()
    {
        // CreateItem never registers the source cache. On the append-optimized path the
        // collection only patches/replaces cached items, so an orphan UI binding stays
        // stale and receives neither PropertyChanged nor Replace/Reset.
        var source = CreateSource(BuildTranscript(1));
        var adapter = new DualCacheAdapter(source, MaterializationMode.CreateItemBypass);
        var orphan = adapter.Materialize(0);
        adapter.ClearNativeEvents();

        var streamedAndAppended = BuildTranscript(1)
            .SetItem(0, TextMessage("message-0", "streamed prefix", seconds: 0))
            .Add(TextMessage("message-1", "Message 1", seconds: 1));

        source.Reset(ConversationId, streamedAndAppended, Project, MatchesSnapshot, PatchProjectedMessage);

        Assert.Equal([NotifyCollectionChangedAction.Add], adapter.NativeEvents);
        Assert.Equal("Message 0", orphan.TextContent);
        Assert.False(source.MatchesItem(0, orphan));
        Assert.Equal("streamed prefix", source[0].TextContent);
        Assert.NotSame(orphan, source[0]);
    }

    private const string ConversationId = "conv-materialization";

    private static ChatTranscriptVirtualizedMessageCollection CreateSource(
        ImmutableList<ConversationMessageSnapshot> transcript)
    {
        var source = new ChatTranscriptVirtualizedMessageCollection();
        source.Reset(ConversationId, transcript, Project, MatchesSnapshot, PatchProjectedMessage);
        return source;
    }

    private static ImmutableList<ConversationMessageSnapshot> BuildMixedTranscript() =>
        ImmutableList.Create(
            TextMessage("message-0", "hello", seconds: 0),
            ToolCallMessage("message-1", "tool-1", "Read file", status: null, seconds: 1));

    private static ImmutableList<ConversationMessageSnapshot> BuildTranscript(int count) =>
        Enumerable.Range(0, count)
            .Select(index => TextMessage($"message-{index}", $"Message {index}", seconds: index))
            .ToImmutableList();

    private static ConversationMessageSnapshot TextMessage(string id, string text, int seconds) =>
        new()
        {
            Id = id,
            Timestamp = new DateTime(2026, 5, 8, 0, 0, 0, DateTimeKind.Utc).AddSeconds(seconds),
            ContentType = "text",
            TextContent = text
        };

    private static ConversationMessageSnapshot ToolCallMessage(
        string id,
        string toolCallId,
        string title,
        ToolCallStatus? status,
        int seconds) =>
        new()
        {
            Id = id,
            Timestamp = new DateTime(2026, 5, 8, 0, 0, 0, DateTimeKind.Utc).AddSeconds(seconds),
            ContentType = "tool_call",
            ToolCallId = toolCallId,
            Title = title,
            ToolCallStatus = status
        };

    private static ChatMessageViewModel Project(ConversationMessageSnapshot snapshot, int index)
    {
        var viewModel = new ChatMessageViewModel();
        viewModel.ApplySnapshot(snapshot, index);
        return viewModel;
    }

    private static bool PatchProjectedMessage(
        ChatMessageViewModel message,
        ConversationMessageSnapshot snapshot,
        int index)
    {
        if (!ChatMessageViewModel.HasSameTemplateShape(message, snapshot))
        {
            return false;
        }

        message.ApplySnapshot(snapshot, index);
        return true;
    }

    private static bool MatchesSnapshot(ChatMessageViewModel viewModel, ConversationMessageSnapshot snapshot) =>
        string.Equals(viewModel.Id, snapshot.Id, StringComparison.Ordinal)
        && Nullable.Equals(viewModel.Timestamp, snapshot.Timestamp?.ToLocalTime())
        && viewModel.IsOutgoing == snapshot.IsOutgoing
        && string.Equals(viewModel.ContentType ?? string.Empty, snapshot.ContentType ?? string.Empty, StringComparison.Ordinal)
        && string.Equals(viewModel.Title ?? string.Empty, snapshot.Title ?? string.Empty, StringComparison.Ordinal)
        && string.Equals(viewModel.TextContent ?? string.Empty, snapshot.TextContent ?? string.Empty, StringComparison.Ordinal)
        && string.Equals(viewModel.ToolCallId, snapshot.ToolCallId, StringComparison.Ordinal)
        && viewModel.ToolCallStatus == snapshot.ToolCallStatus;

    private enum MaterializationMode
    {
        AuthoritativeIndexer,
        CreateItemBypass
    }

    /// <summary>
    /// Portable dual-cache adapter that mirrors ListViewTranscriptItemsSource event policy:
    /// Replace on a cached adapter index becomes a native Reset; Adds covered by that Reset
    /// are suppressed via the observed-count gate.
    /// </summary>
    private sealed class DualCacheAdapter
    {
        private readonly ChatTranscriptVirtualizedMessageCollection _source;
        private readonly MaterializationMode _mode;
        private readonly Dictionary<int, ChatMessageViewModel> _adapterCache = new();
        private int _nativeObservedCount;

        public DualCacheAdapter(
            ChatTranscriptVirtualizedMessageCollection source,
            MaterializationMode mode)
        {
            _source = source;
            _mode = mode;
            _nativeObservedCount = source.Count;
            _source.CollectionChanged += OnSourceCollectionChanged;
        }

        public List<NotifyCollectionChangedAction> NativeEvents { get; } = [];

        public ChatMessageViewModel Materialize(int index)
        {
            if (_adapterCache.TryGetValue(index, out var cached)
                && _source.MatchesItem(index, cached))
            {
                return cached;
            }

            var projected = _mode switch
            {
                MaterializationMode.AuthoritativeIndexer => _source[index],
                MaterializationMode.CreateItemBypass => _source.CreateItem(index),
                _ => throw new ArgumentOutOfRangeException(nameof(_mode))
            };

            _adapterCache[index] = projected;
            return projected;
        }

        public void ClearNativeEvents() => NativeEvents.Clear();

        private void OnSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    if (_source.Count <= _nativeObservedCount)
                    {
                        return;
                    }

                    NativeEvents.Add(NotifyCollectionChangedAction.Add);
                    _nativeObservedCount = _source.Count;
                    return;

                case NotifyCollectionChangedAction.Replace:
                    if (e.NewStartingIndex < 0 || !_adapterCache.Remove(e.NewStartingIndex))
                    {
                        return;
                    }

                    PublishNativeReset();
                    return;

                case NotifyCollectionChangedAction.Reset:
                    _adapterCache.Clear();
                    PublishNativeReset();
                    return;

                default:
                    NativeEvents.Add(e.Action);
                    _nativeObservedCount = _source.Count;
                    return;
            }
        }

        private void PublishNativeReset()
        {
            _nativeObservedCount = _source.Count;
            NativeEvents.Add(NotifyCollectionChangedAction.Reset);
        }
    }
}
