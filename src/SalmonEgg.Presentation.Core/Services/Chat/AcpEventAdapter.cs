using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.Logging;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Presentation.Core.Services.Chat;

/// <summary>
/// Serializes ACP session updates onto a target synchronization context.
/// Provides buffering, backpressure, and resync gating until hydration completes.
/// </summary>
public sealed class AcpEventAdapter
{
    private const int DefaultBufferLimit = 256;
    private const int DefaultDrainBatchSize = 8;
    private const int DefaultHydrationReplayBufferLimit = 8192;

    private readonly Action<SessionUpdateEventArgs> _handler;
    private readonly SynchronizationContext _syncContext;
    private readonly Action<string?>? _resyncRequired;
    private readonly ILogger<AcpEventAdapter>? _logger;
    private readonly Queue<SessionUpdateEventArgs> _buffer = new();
    private readonly object _gate = new();
    private readonly int _bufferLimit;
    private readonly int _hydrationReplayBufferLimit;
    private string? _bufferingSessionId;
    private TaskCompletionSource<object?>? _drainIdleTcs;
    private long _hydrationAttemptId;
    private bool _isHydrated;
    private bool _isSuppressing;
    private bool _resyncRaised;
    private bool _drainScheduled;
    private bool _lowTrustReleaseLogged;

    public AcpEventAdapter(
        Action<SessionUpdateEventArgs> handler,
        SynchronizationContext syncContext,
        int bufferLimit = DefaultBufferLimit,
        int hydrationReplayBufferLimit = DefaultHydrationReplayBufferLimit,
        Action<string?>? resyncRequired = null,
        ILogger<AcpEventAdapter>? logger = null)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _syncContext = syncContext ?? throw new ArgumentNullException(nameof(syncContext));
        if (bufferLimit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bufferLimit), "Buffer limit must be positive.");
        }

        if (hydrationReplayBufferLimit < bufferLimit)
        {
            throw new ArgumentOutOfRangeException(
                nameof(hydrationReplayBufferLimit),
                "Hydration replay buffer limit must be greater than or equal to the steady-state buffer limit.");
        }

        _bufferLimit = bufferLimit;
        _hydrationReplayBufferLimit = hydrationReplayBufferLimit;
        _resyncRequired = resyncRequired;
        _logger = logger;
    }

    public AcpEventAdapter(
        Action<SessionUpdateEventArgs> handler,
        SynchronizationContext syncContext,
        int bufferLimit,
        Action? resyncRequired,
        ILogger<AcpEventAdapter>? logger = null)
        : this(
            handler,
            syncContext,
            bufferLimit,
            DefaultHydrationReplayBufferLimit,
            resyncRequired is null ? null : _ => resyncRequired(),
            logger)
    {
    }

    public void OnSessionUpdate(SessionUpdateEventArgs update)
    {
        Enqueue(update);
    }

    public void Enqueue(SessionUpdateEventArgs update)
    {
        ArgumentNullException.ThrowIfNull(update);

        var scheduleDrain = false;
        Action<string?>? resync = null;
        string? resyncSessionId = null;

        lock (_gate)
        {
            if (_isSuppressing)
            {
                return;
            }

            if (!_isHydrated
                && !string.IsNullOrWhiteSpace(_bufferingSessionId)
                && !string.Equals(_bufferingSessionId, update.SessionId, StringComparison.Ordinal))
            {
                return;
            }

            if (_buffer.Count >= ResolveEffectiveBufferLimitLocked())
            {
                (resync, resyncSessionId) = TriggerResyncLocked(update);
            }
            else if (!_isHydrated)
            {
                _buffer.Enqueue(update);
            }
            else
            {
                _buffer.Enqueue(update);
                if (!_drainScheduled)
                {
                    _drainScheduled = true;
                    scheduleDrain = true;
                }
            }
        }

        if (resync != null)
        {
            PostResyncRequired(resync, resyncSessionId);
        }

        if (scheduleDrain)
        {
            PostDrain();
        }
    }

    public bool MarkHydrated()
        => MarkHydrated(_hydrationAttemptId, lowTrust: false);

    public bool MarkHydrated(bool lowTrust, string? reason = null)
        => MarkHydrated(_hydrationAttemptId, lowTrust, reason);

    public bool MarkHydrated(long hydrationAttemptId, bool lowTrust, string? reason = null)
    {
        var scheduleDrain = false;
        var bufferedCount = 0;
        var logLowTrustRelease = false;

        lock (_gate)
        {
            if (hydrationAttemptId != _hydrationAttemptId)
            {
                return false;
            }

            bufferedCount = _buffer.Count;
            _isHydrated = true;
            _isSuppressing = false;
            _resyncRaised = false;
            _bufferingSessionId = null;

            if (lowTrust && bufferedCount > 0 && !_lowTrustReleaseLogged)
            {
                _lowTrustReleaseLogged = true;
                logLowTrustRelease = true;
            }
            else if (!lowTrust)
            {
                _lowTrustReleaseLogged = false;
            }

            if (_buffer.Count > 0 && !_drainScheduled)
            {
                _drainScheduled = true;
                scheduleDrain = true;
            }
        }

        if (logLowTrustRelease)
        {
            _logger?.LogWarning(
                "Releasing buffered ACP session updates without hydration gate. bufferedCount={BufferedCount} reason={Reason}",
                bufferedCount,
                string.IsNullOrWhiteSpace(reason) ? "Unknown" : reason);
        }

        if (scheduleDrain)
        {
            PostDrain();
        }

        return true;
    }

    public Task WaitForDrainIdleAsync(CancellationToken cancellationToken = default)
        => WaitForDrainIdleAsync(_hydrationAttemptId, cancellationToken);

    public Task WaitForDrainIdleAsync(long hydrationAttemptId, CancellationToken cancellationToken = default)
    {
        Task waitTask;
        lock (_gate)
        {
            if (hydrationAttemptId != _hydrationAttemptId)
            {
                return Task.CompletedTask;
            }

            if (_buffer.Count == 0 && !_drainScheduled)
            {
                return Task.CompletedTask;
            }

            _drainIdleTcs ??= new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            waitTask = _drainIdleTcs.Task;
        }

        return cancellationToken.CanBeCanceled
            ? waitTask.WaitAsync(cancellationToken)
            : waitTask;
    }

    public long BeginHydrationBuffering(string? sessionId)
    {
        var droppedBufferedCount = 0;
        TaskCompletionSource<object?>? drainIdle = null;
        long hydrationAttemptId;
        lock (_gate)
        {
            hydrationAttemptId = unchecked(_hydrationAttemptId + 1);
            _hydrationAttemptId = hydrationAttemptId;
            droppedBufferedCount = _buffer.Count;
            _buffer.Clear();
            _isHydrated = false;
            _isSuppressing = false;
            _resyncRaised = false;
            _lowTrustReleaseLogged = false;
            _drainScheduled = false;
            _bufferingSessionId = string.IsNullOrWhiteSpace(sessionId) ? null : sessionId;
            if (_buffer.Count == 0 && !_drainScheduled)
            {
                drainIdle = _drainIdleTcs;
                _drainIdleTcs = null;
            }
        }

        _logger?.LogDebug(
            "ACP hydration buffering armed. attemptId={AttemptId} sessionId={SessionId} droppedBufferedCount={DroppedBufferedCount}",
            hydrationAttemptId,
            _bufferingSessionId,
            droppedBufferedCount);
        drainIdle?.TrySetResult(null);
        return hydrationAttemptId;
    }

    public void SuppressBufferedUpdates(string? reason = null)
        => SuppressBufferedUpdates(_hydrationAttemptId, reason);

    public void SuppressBufferedUpdates(long hydrationAttemptId, string? reason = null)
    {
        var droppedBufferedCount = 0;
        TaskCompletionSource<object?>? drainIdle = null;
        lock (_gate)
        {
            if (hydrationAttemptId != _hydrationAttemptId)
            {
                return;
            }

            droppedBufferedCount = _buffer.Count;
            _buffer.Clear();
            _isHydrated = false;
            _isSuppressing = true;
            _resyncRaised = false;
            _lowTrustReleaseLogged = false;
            _bufferingSessionId = null;
            if (_buffer.Count == 0 && !_drainScheduled)
            {
                drainIdle = _drainIdleTcs;
                _drainIdleTcs = null;
            }
        }

        _logger?.LogInformation(
            "ACP buffered session updates suppressed. droppedBufferedCount={DroppedBufferedCount} reason={Reason}",
            droppedBufferedCount,
            string.IsNullOrWhiteSpace(reason) ? "Unknown" : reason);
        drainIdle?.TrySetResult(null);
    }

    private (Action<string?>? Callback, string? SessionId) TriggerResyncLocked(SessionUpdateEventArgs update)
    {
        var bufferedCount = _buffer.Count;
        _buffer.Clear();
        _isSuppressing = true;
        _lowTrustReleaseLogged = false;

        if (_resyncRaised)
        {
            return (null, null);
        }

        _resyncRaised = true;
        _logger?.LogWarning(
            "ACP session update buffer overflow detected; requesting resync. bufferLimit={BufferLimit} droppedUpdates={DroppedUpdates}",
            _bufferLimit,
            bufferedCount);
        return (_resyncRequired, update.SessionId);
    }

    private int ResolveEffectiveBufferLimitLocked()
        => !_isHydrated && !string.IsNullOrWhiteSpace(_bufferingSessionId)
            ? _hydrationReplayBufferLimit
            : _bufferLimit;

    private void PostDrain()
    {
        _syncContext.Post(static state => ((AcpEventAdapter)state!).Drain(), this);
    }

    private void PostResyncRequired(Action<string?> resyncRequired, string? sessionId)
    {
        _syncContext.Post(static state =>
        {
            var callbackState = ((Action<string?> Callback, string? SessionId))state!;
            callbackState.Callback(callbackState.SessionId);
        }, (resyncRequired, sessionId));
    }

    private void Drain()
    {
        var drainedCount = 0;
        while (drainedCount < DefaultDrainBatchSize)
        {
            SessionUpdateEventArgs update;
            TaskCompletionSource<object?>? drainIdle = null;
            lock (_gate)
            {
                if (!_isHydrated || _isSuppressing || _buffer.Count == 0)
                {
                    _drainScheduled = false;
                    drainIdle = _drainIdleTcs;
                    _drainIdleTcs = null;
                }
                else
                {
                    update = _buffer.Dequeue();
                    goto HandleUpdate;
                }
            }

            drainIdle?.TrySetResult(null);
            return;

        HandleUpdate:
            _handler(update);
            drainedCount++;
        }

        TaskCompletionSource<object?>? finalDrainIdle = null;
        lock (_gate)
        {
            if (!_isHydrated || _isSuppressing || _buffer.Count == 0)
            {
                _drainScheduled = false;
                finalDrainIdle = _drainIdleTcs;
                _drainIdleTcs = null;
            }
            else
            {
                PostDrain();
            }
        }

        finalDrainIdle?.TrySetResult(null);
    }
}
