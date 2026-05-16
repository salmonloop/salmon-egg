using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.Logging;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Presentation.Core.Services.Chat;

/// <summary>
/// Serializes ACP session updates onto a target dispatcher.
/// Provides buffering, backpressure, and resync gating until hydration completes.
/// </summary>
public sealed class AcpEventAdapter
{
    private const int DefaultBufferLimit = 256;
    private const int DefaultDrainBatchSize = 8;
    private const int CompletedHydrationAttemptRetentionLimit = 128;
    internal const int DefaultHydrationReplayBufferLimit = 8192;

    private readonly Action<SessionUpdateEventArgs> _handler;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly Func<string?, System.Threading.Tasks.Task>? _resyncRequired;
    private readonly ILogger<AcpEventAdapter>? _logger;
    private readonly Queue<SessionUpdateEventArgs> _buffer = new();
    private readonly Dictionary<long, HydrationBufferScope> _hydrationScopesByAttemptId = new();
    private readonly Dictionary<string, HydrationBufferScope> _hydrationScopesBySessionId =
        new(StringComparer.Ordinal);
    private readonly HashSet<long> _completedHydrationAttemptIds = new();
    private readonly Queue<long> _completedHydrationAttemptOrder = new();
    private readonly Dictionary<string, HashSet<long>> _completedHydrationAttemptsBySessionId =
        new(StringComparer.Ordinal);
    private readonly Dictionary<long, string> _completedHydrationSessionIdByAttemptId = new();
    private readonly object _gate = new();
    private readonly int _bufferLimit;
    private readonly int _hydrationReplayBufferLimit;
    private TaskCompletionSource<object?>? _drainIdleTcs;
    private long _hydrationAttemptId;
    private bool _isHydrated;
    private bool _isReplayProjectionReleased;
    private bool _isSuppressing;
    private bool _resyncRaised;
    private bool _drainScheduled;
    private bool _lowTrustReleaseLogged;

    private AcpEventAdapter(
        Action<SessionUpdateEventArgs> handler,
        IUiDispatcher uiDispatcher,
        int bufferLimit,
        int hydrationReplayBufferLimit,
        Func<string?, System.Threading.Tasks.Task>? resyncRequiredAsync,
        ILogger<AcpEventAdapter>? logger)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
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
        _resyncRequired = resyncRequiredAsync;
        _logger = logger;
    }

    private sealed class HydrationBufferScope
    {
        public HydrationBufferScope(long attemptId, string sessionId)
        {
            AttemptId = attemptId;
            SessionId = sessionId;
        }

        public long AttemptId { get; }

        public string SessionId { get; }

        public Queue<SessionUpdateEventArgs> Buffer { get; } = new();

        public TaskCompletionSource<object?>? DrainIdleTcs { get; set; }

        public bool IsHydrated { get; set; }

        public bool IsReplayProjectionReleased { get; set; }

        public bool IsSuppressing { get; set; }

        public bool ResyncRaised { get; set; }

        public bool DrainScheduled { get; set; }

        public bool LowTrustReleaseLogged { get; set; }
    }

    public AcpEventAdapter(
        Action<SessionUpdateEventArgs> handler,
        IUiDispatcher uiDispatcher,
        int bufferLimit = DefaultBufferLimit,
        int hydrationReplayBufferLimit = DefaultHydrationReplayBufferLimit,
        Action<string?>? resyncRequired = null,
        ILogger<AcpEventAdapter>? logger = null)
        : this(
            handler,
            uiDispatcher,
            bufferLimit,
            hydrationReplayBufferLimit,
            resyncRequired is null
                ? null
                : sessionId =>
                {
                    resyncRequired(sessionId);
                    return System.Threading.Tasks.Task.CompletedTask;
                },
            logger)
    {
    }

    public AcpEventAdapter(
        Action<SessionUpdateEventArgs> handler,
        IUiDispatcher uiDispatcher,
        int bufferLimit,
        int hydrationReplayBufferLimit,
        ILogger<AcpEventAdapter>? logger,
        Func<string?, System.Threading.Tasks.Task>? resyncRequiredAsync)
        : this(
            handler,
            uiDispatcher,
            bufferLimit,
            hydrationReplayBufferLimit,
            resyncRequiredAsync,
            logger)
    {
    }

    public AcpEventAdapter(
        Action<SessionUpdateEventArgs> handler,
        IUiDispatcher uiDispatcher,
        int bufferLimit,
        Action? resyncRequired,
        ILogger<AcpEventAdapter>? logger = null)
        : this(
            handler,
            uiDispatcher,
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

        long? drainAttemptId = null;
        var scheduleGlobalDrain = false;
        Func<string?, System.Threading.Tasks.Task>? resync = null;
        string? resyncSessionId = null;

        lock (_gate)
        {
            if (!string.IsNullOrWhiteSpace(update.SessionId)
                && _hydrationScopesBySessionId.TryGetValue(update.SessionId, out var scope))
            {
                if (scope.IsSuppressing)
                {
                    return;
                }

                if (scope.Buffer.Count >= _hydrationReplayBufferLimit)
                {
                    (resync, resyncSessionId) = TriggerResyncLocked(update, scope);
                }
                else if (!CanDrainBufferedUpdatesLocked(scope))
                {
                    scope.Buffer.Enqueue(update);
                }
                else
                {
                    scope.Buffer.Enqueue(update);
                    if (!scope.DrainScheduled)
                    {
                        scope.DrainScheduled = true;
                        drainAttemptId = scope.AttemptId;
                    }
                }
            }
            else if (_hydrationScopesBySessionId.Count > 0)
            {
                return;
            }
            else
            {
                if (_isSuppressing)
                {
                    return;
                }

                if (_buffer.Count >= _bufferLimit)
                {
                    (resync, resyncSessionId) = TriggerResyncLocked(update);
                }
                else if (!CanDrainBufferedUpdatesLocked())
                {
                    _buffer.Enqueue(update);
                }
                else
                {
                    _buffer.Enqueue(update);
                    if (!_drainScheduled)
                    {
                        _drainScheduled = true;
                        scheduleGlobalDrain = true;
                    }
                }
            }
        }

        if (resync != null)
        {
            PostResyncRequired(resync, resyncSessionId);
        }

        if (drainAttemptId.HasValue)
        {
            PostDrain(drainAttemptId.Value);
        }

        if (scheduleGlobalDrain)
        {
            PostDrain();
        }
    }

    public bool MarkHydrated()
    {
        long hydrationAttemptId;
        lock (_gate)
        {
            hydrationAttemptId = _hydrationAttemptId;
        }

        return MarkHydrated(hydrationAttemptId, lowTrust: false);
    }

    public bool MarkHydrated(bool lowTrust, string? reason = null)
    {
        long hydrationAttemptId;
        lock (_gate)
        {
            hydrationAttemptId = _hydrationAttemptId;
        }

        return MarkHydrated(hydrationAttemptId, lowTrust, reason);
    }

    public bool MarkHydrated(long hydrationAttemptId, bool lowTrust, string? reason = null)
    {
        long? drainAttemptId = null;
        var scheduleGlobalDrain = false;
        var bufferedCount = 0;
        var logLowTrustRelease = false;

        lock (_gate)
        {
            if (_hydrationScopesByAttemptId.TryGetValue(hydrationAttemptId, out var scope))
            {
                bufferedCount = scope.Buffer.Count;
                scope.IsHydrated = true;
                scope.IsReplayProjectionReleased = false;
                scope.IsSuppressing = false;
                scope.ResyncRaised = false;

                if (lowTrust && bufferedCount > 0 && !scope.LowTrustReleaseLogged)
                {
                    scope.LowTrustReleaseLogged = true;
                    logLowTrustRelease = true;
                }
                else if (!lowTrust)
                {
                    scope.LowTrustReleaseLogged = false;
                }

                if (scope.Buffer.Count > 0 && !scope.DrainScheduled)
                {
                    scope.DrainScheduled = true;
                    drainAttemptId = scope.AttemptId;
                }
                else if (scope.Buffer.Count == 0)
                {
                    RemoveHydrationScopeLocked(scope, null, markCompleted: true);
                    RestoreSteadyStateWhenNoHydrationScopesLocked();
                }
            }
            else
            {
                if (hydrationAttemptId != _hydrationAttemptId)
                {
                    return IsCompletedHydrationAttemptLocked(hydrationAttemptId);
                }

                bufferedCount = _buffer.Count;
                _isHydrated = true;
                _isReplayProjectionReleased = false;
                _isSuppressing = false;
                _resyncRaised = false;

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
                    scheduleGlobalDrain = true;
                }
            }
        }

        if (logLowTrustRelease)
        {
            _logger?.LogWarning(
                "Releasing buffered ACP session updates without hydration gate. bufferedCount={BufferedCount} reason={Reason}",
                bufferedCount,
                string.IsNullOrWhiteSpace(reason) ? "Unknown" : reason);
        }

        if (drainAttemptId.HasValue)
        {
            PostDrain(drainAttemptId.Value);
        }

        if (scheduleGlobalDrain)
        {
            PostDrain();
        }

        return true;
    }

    public Task WaitForDrainIdleAsync(CancellationToken cancellationToken = default)
    {
        long hydrationAttemptId;
        lock (_gate)
        {
            hydrationAttemptId = _hydrationAttemptId;
        }

        return WaitForDrainIdleAsync(hydrationAttemptId, cancellationToken);
    }

    public Task WaitForDrainIdleAsync(long hydrationAttemptId, CancellationToken cancellationToken = default)
    {
        Task waitTask;
        lock (_gate)
        {
            if (_hydrationScopesByAttemptId.TryGetValue(hydrationAttemptId, out var scope))
            {
                if (scope.Buffer.Count == 0 && !scope.DrainScheduled)
                {
                    return Task.CompletedTask;
                }

                scope.DrainIdleTcs ??= new TaskCompletionSource<object?>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                waitTask = scope.DrainIdleTcs.Task;
            }
            else if (hydrationAttemptId != _hydrationAttemptId || IsCompletedHydrationAttemptLocked(hydrationAttemptId))
            {
                return Task.CompletedTask;
            }
            else if (_buffer.Count == 0 && !_drainScheduled)
            {
                return Task.CompletedTask;
            }
            else
            {
                _drainIdleTcs ??= new TaskCompletionSource<object?>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                waitTask = _drainIdleTcs.Task;
            }
        }

        return cancellationToken.CanBeCanceled
            ? waitTask.WaitAsync(cancellationToken)
            : waitTask;
    }

    public long BeginHydrationBuffering(string? sessionId)
    {
        var droppedBufferedCount = 0;
        var drainIdles = new List<TaskCompletionSource<object?>>();
        long hydrationAttemptId;
        var normalizedSessionId = string.IsNullOrWhiteSpace(sessionId) ? null : sessionId;
        lock (_gate)
        {
            hydrationAttemptId = unchecked(_hydrationAttemptId + 1);
            _hydrationAttemptId = hydrationAttemptId;

            if (normalizedSessionId is null)
            {
                droppedBufferedCount = _buffer.Count + CountScopedBufferedUpdatesLocked();
                ClearAllHydrationScopesLocked(drainIdles, markCompleted: false);
                _buffer.Clear();
                _isHydrated = false;
                _isReplayProjectionReleased = false;
                _isSuppressing = false;
                _resyncRaised = false;
                _lowTrustReleaseLogged = false;
                _drainScheduled = false;
                if (_buffer.Count == 0 && !_drainScheduled)
                {
                    AddDrainIdleIfPresent(drainIdles, _drainIdleTcs);
                    _drainIdleTcs = null;
                }
            }
            else
            {
                if (_hydrationScopesBySessionId.TryGetValue(normalizedSessionId, out var existingScope))
                {
                    droppedBufferedCount = existingScope.Buffer.Count;
                    RemoveHydrationScopeLocked(existingScope, drainIdles, markCompleted: false);
                }

                ForgetCompletedHydrationAttemptsForSessionLocked(normalizedSessionId);
                var scope = new HydrationBufferScope(hydrationAttemptId, normalizedSessionId);
                _hydrationScopesByAttemptId.Add(hydrationAttemptId, scope);
                _hydrationScopesBySessionId[normalizedSessionId] = scope;
            }
        }

        _logger?.LogDebug(
            "ACP hydration buffering armed. attemptId={AttemptId} sessionId={SessionId} droppedBufferedCount={DroppedBufferedCount}",
            hydrationAttemptId,
            normalizedSessionId,
            droppedBufferedCount);
        CompleteDrainIdles(drainIdles);
        return hydrationAttemptId;
    }

    public bool ReleaseBufferedUpdatesForReplayProjection(long hydrationAttemptId)
    {
        long? drainAttemptId = null;
        var scheduleGlobalDrain = false;
        lock (_gate)
        {
            if (_hydrationScopesByAttemptId.TryGetValue(hydrationAttemptId, out var scope))
            {
                if (scope.IsSuppressing)
                {
                    return false;
                }

                scope.IsReplayProjectionReleased = true;
                scope.ResyncRaised = false;
                scope.LowTrustReleaseLogged = false;
                if (scope.Buffer.Count > 0 && !scope.DrainScheduled)
                {
                    scope.DrainScheduled = true;
                    drainAttemptId = scope.AttemptId;
                }
            }
            else
            {
                if (hydrationAttemptId != _hydrationAttemptId || _isSuppressing)
                {
                    return false;
                }

                _isReplayProjectionReleased = true;
                _resyncRaised = false;
                _lowTrustReleaseLogged = false;
                if (_buffer.Count > 0 && !_drainScheduled)
                {
                    _drainScheduled = true;
                    scheduleGlobalDrain = true;
                }
            }
        }

        if (drainAttemptId.HasValue)
        {
            PostDrain(drainAttemptId.Value);
        }

        if (scheduleGlobalDrain)
        {
            PostDrain();
        }

        return true;
    }

    public void SuppressBufferedUpdates(string? reason = null)
    {
        long hydrationAttemptId;
        lock (_gate)
        {
            hydrationAttemptId = _hydrationAttemptId;
        }

        SuppressBufferedUpdates(hydrationAttemptId, reason);
    }

    public void SuppressBufferedUpdates(long hydrationAttemptId, string? reason = null)
    {
        var droppedBufferedCount = 0;
        var drainIdles = new List<TaskCompletionSource<object?>>();
        lock (_gate)
        {
            if (_hydrationScopesByAttemptId.TryGetValue(hydrationAttemptId, out var scope))
            {
                droppedBufferedCount = scope.Buffer.Count;
                scope.Buffer.Clear();
                scope.IsHydrated = false;
                scope.IsReplayProjectionReleased = false;
                scope.IsSuppressing = true;
                scope.ResyncRaised = false;
                scope.LowTrustReleaseLogged = false;
                RemoveHydrationScopeLocked(scope, drainIdles, markCompleted: false);
                RestoreSteadyStateWhenNoHydrationScopesLocked();
            }
            else
            {
                if (hydrationAttemptId != _hydrationAttemptId)
                {
                    return;
                }

                droppedBufferedCount = _buffer.Count;
                _buffer.Clear();
                _isHydrated = false;
                _isReplayProjectionReleased = false;
                _isSuppressing = true;
                _resyncRaised = false;
                _lowTrustReleaseLogged = false;
                if (_buffer.Count == 0 && !_drainScheduled)
                {
                    AddDrainIdleIfPresent(drainIdles, _drainIdleTcs);
                    _drainIdleTcs = null;
                }
            }
        }

        _logger?.LogInformation(
            "ACP buffered session updates suppressed. droppedBufferedCount={DroppedBufferedCount} reason={Reason}",
            droppedBufferedCount,
            string.IsNullOrWhiteSpace(reason) ? "Unknown" : reason);
        CompleteDrainIdles(drainIdles);
    }

    private (Func<string?, System.Threading.Tasks.Task>? Callback, string? SessionId) TriggerResyncLocked(
        SessionUpdateEventArgs update,
        HydrationBufferScope scope)
    {
        var bufferedCount = scope.Buffer.Count;
        scope.Buffer.Clear();
        scope.IsSuppressing = true;
        scope.LowTrustReleaseLogged = false;

        if (scope.ResyncRaised)
        {
            return (null, null);
        }

        scope.ResyncRaised = true;
        _logger?.LogWarning(
            "ACP session update replay buffer overflow detected; requesting resync. bufferLimit={BufferLimit} droppedUpdates={DroppedUpdates} sessionId={SessionId}",
            _hydrationReplayBufferLimit,
            bufferedCount,
            scope.SessionId);
        return (_resyncRequired, update.SessionId);
    }

    private (Func<string?, System.Threading.Tasks.Task>? Callback, string? SessionId) TriggerResyncLocked(SessionUpdateEventArgs update)
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

    private void PostDrain()
    {
        _uiDispatcher.Enqueue(Drain);
    }

    private void PostDrain(long hydrationAttemptId)
    {
        _uiDispatcher.Enqueue(() => Drain(hydrationAttemptId));
    }

    private void PostResyncRequired(Func<string?, System.Threading.Tasks.Task> resyncRequired, string? sessionId)
    {
        _ = _uiDispatcher.EnqueueAsync(() => resyncRequired(sessionId));
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
                if (!CanDrainBufferedUpdatesLocked() || _isSuppressing || _buffer.Count == 0)
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
            if (!CanDrainBufferedUpdatesLocked() || _isSuppressing || _buffer.Count == 0)
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

    private bool CanDrainBufferedUpdatesLocked()
        => _isHydrated || _isReplayProjectionReleased;

    private void Drain(long hydrationAttemptId)
    {
        var drainedCount = 0;
        while (drainedCount < DefaultDrainBatchSize)
        {
            SessionUpdateEventArgs update;
            TaskCompletionSource<object?>? drainIdle = null;
            lock (_gate)
            {
                if (!_hydrationScopesByAttemptId.TryGetValue(hydrationAttemptId, out var scope))
                {
                    return;
                }

                if (!CanDrainBufferedUpdatesLocked(scope) || scope.IsSuppressing || scope.Buffer.Count == 0)
                {
                    scope.DrainScheduled = false;
                    drainIdle = scope.DrainIdleTcs;
                    scope.DrainIdleTcs = null;

                    if (CanCompleteHydrationScopeLocked(scope))
                    {
                        RemoveHydrationScopeLocked(scope, null, markCompleted: true);
                        RestoreSteadyStateWhenNoHydrationScopesLocked();
                    }
                }
                else
                {
                    update = scope.Buffer.Dequeue();
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
            if (!_hydrationScopesByAttemptId.TryGetValue(hydrationAttemptId, out var scope))
            {
                return;
            }

            if (!CanDrainBufferedUpdatesLocked(scope) || scope.IsSuppressing || scope.Buffer.Count == 0)
            {
                scope.DrainScheduled = false;
                finalDrainIdle = scope.DrainIdleTcs;
                scope.DrainIdleTcs = null;

                if (CanCompleteHydrationScopeLocked(scope))
                {
                    RemoveHydrationScopeLocked(scope, null, markCompleted: true);
                    RestoreSteadyStateWhenNoHydrationScopesLocked();
                }
            }
            else
            {
                PostDrain(hydrationAttemptId);
            }
        }

        finalDrainIdle?.TrySetResult(null);
    }

    private static bool CanDrainBufferedUpdatesLocked(HydrationBufferScope scope)
        => scope.IsHydrated || scope.IsReplayProjectionReleased;

    private static bool CanCompleteHydrationScopeLocked(HydrationBufferScope scope)
        => scope.IsHydrated
            && scope.Buffer.Count == 0
            && !scope.DrainScheduled;

    private int CountScopedBufferedUpdatesLocked()
    {
        var count = 0;
        foreach (var scope in _hydrationScopesByAttemptId.Values)
        {
            count += scope.Buffer.Count;
        }

        return count;
    }

    private void ClearAllHydrationScopesLocked(
        List<TaskCompletionSource<object?>> drainIdles,
        bool markCompleted)
    {
        foreach (var scope in _hydrationScopesByAttemptId.Values)
        {
            AddDrainIdleIfPresent(drainIdles, scope.DrainIdleTcs);
            if (markCompleted)
            {
                RememberCompletedHydrationAttemptLocked(scope.AttemptId, scope.SessionId);
            }
        }

        _hydrationScopesByAttemptId.Clear();
        _hydrationScopesBySessionId.Clear();
    }

    private void RemoveHydrationScopeLocked(
        HydrationBufferScope scope,
        List<TaskCompletionSource<object?>>? drainIdles,
        bool markCompleted)
    {
        _hydrationScopesByAttemptId.Remove(scope.AttemptId);
        if (_hydrationScopesBySessionId.TryGetValue(scope.SessionId, out var currentScope)
            && ReferenceEquals(currentScope, scope))
        {
            _hydrationScopesBySessionId.Remove(scope.SessionId);
        }

        AddDrainIdleIfPresent(drainIdles, scope.DrainIdleTcs);
        scope.DrainIdleTcs = null;

        if (markCompleted)
        {
            RememberCompletedHydrationAttemptLocked(scope.AttemptId, scope.SessionId);
        }
    }

    private bool IsCompletedHydrationAttemptLocked(long hydrationAttemptId)
        => _completedHydrationAttemptIds.Contains(hydrationAttemptId);

    private void RestoreSteadyStateWhenNoHydrationScopesLocked()
    {
        if (_hydrationScopesByAttemptId.Count != 0)
        {
            return;
        }

        _isHydrated = true;
        _isReplayProjectionReleased = false;
        _isSuppressing = false;
        _resyncRaised = false;
        _lowTrustReleaseLogged = false;
    }

    private void RememberCompletedHydrationAttemptLocked(long hydrationAttemptId, string sessionId)
    {
        if (!_completedHydrationAttemptIds.Add(hydrationAttemptId))
        {
            return;
        }

        _completedHydrationAttemptOrder.Enqueue(hydrationAttemptId);
        _completedHydrationSessionIdByAttemptId[hydrationAttemptId] = sessionId;
        if (!_completedHydrationAttemptsBySessionId.TryGetValue(sessionId, out var attemptIds))
        {
            attemptIds = new HashSet<long>();
            _completedHydrationAttemptsBySessionId[sessionId] = attemptIds;
        }

        attemptIds.Add(hydrationAttemptId);
        while (_completedHydrationAttemptOrder.Count > CompletedHydrationAttemptRetentionLimit)
        {
            RemoveCompletedHydrationAttemptLocked(_completedHydrationAttemptOrder.Dequeue());
        }
    }

    private void ForgetCompletedHydrationAttemptsForSessionLocked(string sessionId)
    {
        if (!_completedHydrationAttemptsBySessionId.TryGetValue(sessionId, out var attemptIds))
        {
            return;
        }

        foreach (var attemptId in attemptIds)
        {
            _completedHydrationAttemptIds.Remove(attemptId);
            _completedHydrationSessionIdByAttemptId.Remove(attemptId);
        }

        _completedHydrationAttemptsBySessionId.Remove(sessionId);
    }

    private void RemoveCompletedHydrationAttemptLocked(long hydrationAttemptId)
    {
        _completedHydrationAttemptIds.Remove(hydrationAttemptId);
        if (_completedHydrationSessionIdByAttemptId.Remove(hydrationAttemptId, out var sessionId)
            && _completedHydrationAttemptsBySessionId.TryGetValue(sessionId, out var attemptIds))
        {
            attemptIds.Remove(hydrationAttemptId);
            if (attemptIds.Count == 0)
            {
                _completedHydrationAttemptsBySessionId.Remove(sessionId);
            }
        }
    }

    private static void AddDrainIdleIfPresent(
        List<TaskCompletionSource<object?>>? drainIdles,
        TaskCompletionSource<object?>? drainIdle)
    {
        if (drainIdles is not null && drainIdle is not null)
        {
            drainIdles.Add(drainIdle);
        }
    }

    private static void CompleteDrainIdles(IEnumerable<TaskCompletionSource<object?>> drainIdles)
    {
        foreach (var drainIdle in drainIdles)
        {
            drainIdle.TrySetResult(null);
        }
    }
}
