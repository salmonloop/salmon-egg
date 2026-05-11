using Microsoft.UI.Dispatching;
using SalmonEgg.Presentation.Utilities;

namespace SalmonEgg.Presentation.Transcript;

internal enum TranscriptProjectionRestoreResultKind
{
    None = 0,
    Retry = 1,
    Confirmed = 2,
    Unavailable = 3,
    Abandoned = 4,
}

internal readonly record struct TranscriptProjectionRestoreResult(
    TranscriptProjectionRestoreResultKind Kind,
    TranscriptProjectionRestoreToken? Token = null,
    string? ConversationId = null,
    int Generation = -1,
    string? Reason = null);

internal sealed class TranscriptProjectionRestoreController
{
    private readonly int _maxAttempts;
    private TranscriptProjectionRestoreToken? _pendingToken;
    private string? _pendingConversationId;
    private int _pendingGeneration = -1;
    private int _pendingAttemptCount;
    private int _pendingResolvedIndex = -1;
    private int _pendingRequestedMaterializationIndex = -1;
    private bool _pendingRetryScheduled;

    public TranscriptProjectionRestoreController(int maxAttempts)
    {
        if (maxAttempts <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts));
        }

        _maxAttempts = maxAttempts;
    }

    public bool HasPending => _pendingToken is not null;

    public string? PendingConversationId => _pendingConversationId;

    public int PendingGeneration => _pendingGeneration;

    public void Queue(TranscriptProjectionRestoreToken token, int generation)
    {
        _pendingToken = token;
        _pendingConversationId = token.ConversationId;
        _pendingGeneration = generation;
        _pendingAttemptCount = 0;
        _pendingResolvedIndex = -1;
        _pendingRequestedMaterializationIndex = -1;
        _pendingRetryScheduled = false;
    }

    public void Clear()
    {
        _pendingToken = null;
        _pendingConversationId = null;
        _pendingGeneration = -1;
        _pendingAttemptCount = 0;
        _pendingResolvedIndex = -1;
        _pendingRequestedMaterializationIndex = -1;
        _pendingRetryScheduled = false;
    }

    public TranscriptProjectionRestoreResult Abandon(string currentConversationId, string reason)
    {
        if (_pendingToken is null)
        {
            Clear();
            return default;
        }

        var conversationId = _pendingConversationId ?? currentConversationId;
        var generation = _pendingGeneration;
        Clear();
        return new TranscriptProjectionRestoreResult(
            TranscriptProjectionRestoreResultKind.Abandoned,
            ConversationId: conversationId,
            Generation: generation,
            Reason: reason);
    }

    public TranscriptProjectionRestoreResult TryApply(
        ITranscriptViewportHost host,
        int messageCount,
        string currentConversationId,
        int currentGeneration,
        Func<TranscriptProjectionRestoreToken, int> resolveIndex)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(resolveIndex);

        _pendingRetryScheduled = false;
        if (_pendingToken is not { } token)
        {
            return default;
        }

        if (!string.Equals(currentConversationId, token.ConversationId, StringComparison.Ordinal)
            || currentGeneration != _pendingGeneration)
        {
            return Abandon(currentConversationId, "RestoreContextChanged");
        }

        var index = ResolveIndex(token, messageCount, resolveIndex);
        if (index < 0 || index >= messageCount)
        {
            return Unavailable("ProjectionItemMissing");
        }

        if (!host.HasRealizedItem(index))
        {
            if (_pendingRequestedMaterializationIndex == index)
            {
                if (++_pendingAttemptCount >= _maxAttempts)
                {
                    return Unavailable("ProjectionItemNotMaterialized");
                }

                host.ScrollItemIntoView(index, TranscriptItemScrollAlignment.Leading);
                return new TranscriptProjectionRestoreResult(TranscriptProjectionRestoreResultKind.Retry);
            }

            if (++_pendingAttemptCount >= _maxAttempts)
            {
                return Unavailable("ProjectionItemNotMaterialized");
            }

            _pendingRequestedMaterializationIndex = index;
            host.ScrollItemIntoView(index, TranscriptItemScrollAlignment.Leading);
            return new TranscriptProjectionRestoreResult(TranscriptProjectionRestoreResultKind.Retry);
        }

        if (!host.TryGetFirstVisibleIndex(messageCount, out var firstVisibleIndex)
            || firstVisibleIndex != index)
        {
            if (++_pendingAttemptCount >= _maxAttempts)
            {
                return Unavailable("ProjectionAnchorNotRestored");
            }

            _pendingRequestedMaterializationIndex = index;
            host.ScrollItemIntoView(index, TranscriptItemScrollAlignment.Leading);
            return new TranscriptProjectionRestoreResult(TranscriptProjectionRestoreResultKind.Retry);
        }

        _pendingRequestedMaterializationIndex = -1;
        var generation = _pendingGeneration;
        Clear();
        return new TranscriptProjectionRestoreResult(
            TranscriptProjectionRestoreResultKind.Confirmed,
            token,
            token.ConversationId,
            generation);
    }

    public bool TryScheduleRetry(DispatcherQueue dispatcherQueue, Action retry)
    {
        ArgumentNullException.ThrowIfNull(dispatcherQueue);
        ArgumentNullException.ThrowIfNull(retry);

        if (_pendingRetryScheduled)
        {
            return false;
        }

        _pendingRetryScheduled = true;
        _ = ScheduleRetryAsync(dispatcherQueue, retry);
        return true;
    }

    private async Task ScheduleRetryAsync(DispatcherQueue dispatcherQueue, Action retry)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(16)).ConfigureAwait(false);
        _ = dispatcherQueue.TryEnqueue(retry.Invoke);
    }

    private int ResolveIndex(
        TranscriptProjectionRestoreToken token,
        int messageCount,
        Func<TranscriptProjectionRestoreToken, int> resolveIndex)
    {
        if (_pendingResolvedIndex >= 0 && _pendingResolvedIndex < messageCount)
        {
            return _pendingResolvedIndex;
        }

        _pendingResolvedIndex = resolveIndex(token);
        return _pendingResolvedIndex;
    }

    private TranscriptProjectionRestoreResult Unavailable(string reason)
    {
        var conversationId = _pendingConversationId;
        var generation = _pendingGeneration;
        Clear();
        return new TranscriptProjectionRestoreResult(
            TranscriptProjectionRestoreResultKind.Unavailable,
            ConversationId: conversationId,
            Generation: generation,
            Reason: reason);
    }
}
