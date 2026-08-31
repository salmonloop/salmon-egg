using Microsoft.UI.Dispatching;
using SalmonEgg.Presentation.Utilities;

namespace SalmonEgg.Presentation.Transcript;

internal enum TranscriptNativeScrollScheduleResult
{
    Rejected = 0,
    Scheduled = 1,
    Coalesced = 2,
}

internal sealed class TranscriptNativeScrollScheduler
{
    private PendingSchedule? _pendingSchedule;

    public TranscriptNativeScrollScheduleResult Schedule(
        DispatcherQueue dispatcherQueue,
        TranscriptScrollRequestToken requestToken,
        Action<TranscriptScrollRequestToken> callback)
    {
        ArgumentNullException.ThrowIfNull(dispatcherQueue);
        ArgumentNullException.ThrowIfNull(callback);

        if (_pendingSchedule is { } pendingSchedule)
        {
            pendingSchedule.RequestToken = requestToken;
            pendingSchedule.Callback = callback;
            return TranscriptNativeScrollScheduleResult.Coalesced;
        }

        var schedule = new PendingSchedule(requestToken, callback);
        _pendingSchedule = schedule;
        if (dispatcherQueue.TryEnqueue(() => Invoke(schedule)))
        {
            return TranscriptNativeScrollScheduleResult.Scheduled;
        }

        if (ReferenceEquals(_pendingSchedule, schedule))
        {
            _pendingSchedule = null;
        }

        return TranscriptNativeScrollScheduleResult.Rejected;
    }

    public void Clear()
    {
        _pendingSchedule = null;
    }

    private void Invoke(PendingSchedule schedule)
    {
        if (!ReferenceEquals(_pendingSchedule, schedule))
        {
            return;
        }

        _pendingSchedule = null;
        schedule.Callback(schedule.RequestToken);
    }

    private sealed class PendingSchedule
    {
        public PendingSchedule(
            TranscriptScrollRequestToken requestToken,
            Action<TranscriptScrollRequestToken> callback)
        {
            RequestToken = requestToken;
            Callback = callback;
        }

        public TranscriptScrollRequestToken RequestToken { get; set; }

        public Action<TranscriptScrollRequestToken> Callback { get; set; }
    }
}
