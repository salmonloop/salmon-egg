using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SalmonEgg.Application.Services.Chat;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public readonly record struct AcpConnectionSessionCleanupResult(
    int RemovedCount,
    int DisposeFailureCount);

public interface IAcpConnectionSessionCleaner
{
    Task<AcpConnectionSessionCleanupResult> CleanupStaleAsync(
        IChatService? activeService,
        Func<AcpConnectionSession, bool>? isPinned = null,
        Func<AcpConnectionSession, bool>? isHardPinned = null,
        CancellationToken cancellationToken = default);
}

public sealed class AcpConnectionSessionCleaner : IAcpConnectionSessionCleaner
{
    /// <summary>
    /// 同时进行的断连/释放上限。每个释放都会终止一个 agent 进程或套接字，
    /// 无上限并发会在缓存较大时一次性制造进程/句柄风暴，故必须节流。
    /// </summary>
    internal const int DefaultMaxConcurrentDisposals = 4;

    private readonly IAcpConnectionSessionRegistry _sessionRegistry;
    private readonly IAcpConnectionEvictionPolicy _evictionPolicy;
    private readonly AcpConnectionEvictionOptions _evictionOptions;
    private readonly ILogger<AcpConnectionSessionCleaner> _logger;
    private readonly int _maxConcurrentDisposals;

    public AcpConnectionSessionCleaner(
        IAcpConnectionSessionRegistry sessionRegistry,
        IAcpConnectionEvictionPolicy evictionPolicy,
        AcpConnectionEvictionOptions evictionOptions,
        ILogger<AcpConnectionSessionCleaner> logger)
        : this(sessionRegistry, evictionPolicy, evictionOptions, logger, DefaultMaxConcurrentDisposals)
    {
    }

    internal AcpConnectionSessionCleaner(
        IAcpConnectionSessionRegistry sessionRegistry,
        IAcpConnectionEvictionPolicy evictionPolicy,
        AcpConnectionEvictionOptions evictionOptions,
        ILogger<AcpConnectionSessionCleaner> logger,
        int maxConcurrentDisposals)
    {
        _sessionRegistry = sessionRegistry ?? throw new ArgumentNullException(nameof(sessionRegistry));
        _evictionPolicy = evictionPolicy ?? throw new ArgumentNullException(nameof(evictionPolicy));
        _evictionOptions = evictionOptions ?? throw new ArgumentNullException(nameof(evictionOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentOutOfRangeException.ThrowIfLessThan(maxConcurrentDisposals, 1);
        _maxConcurrentDisposals = maxConcurrentDisposals;
    }

    public async Task<AcpConnectionSessionCleanupResult> CleanupStaleAsync(
        IChatService? activeService,
        Func<AcpConnectionSession, bool>? isPinned = null,
        Func<AcpConnectionSession, bool>? isHardPinned = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var removed = _sessionRegistry.RemoveWhere(static session =>
            !session.Service.IsConnected
            || !session.Service.IsInitialized);

        // RemoveWhere 已把这些会话从注册表批量摘除,再无任何持有者;此后必须无条件全部释放。
        // 若在此阶段响应取消,剩余的已摘除会话就成了无主的泄漏连接(进程/套接字/HttpClient)。
        // 取消只应在摘除之前(方法入口)或仍查询注册表的淘汰选择阶段起作用。
        var disposeFailureCount = await DisposeDetachedSessionsAsync(
            removed.Where(session => !ReferenceEquals(activeService, session.Service)),
            static (logger, session, ex) => logger.LogDebug(
                ex,
                "Failed to dispose stale cached ACP session. profileId={ProfileId}",
                session.ProfileId),
            releaseAfterDisconnectFailure: true).ConfigureAwait(false);

        var warmCandidates = _sessionRegistry.GetSnapshot()
            .Where(session =>
                session.Service.IsConnected
                && session.Service.IsInitialized
                && !ReferenceEquals(activeService, session.Service))
            .ToArray();
        var hardPinnedCandidates = warmCandidates
            .Where(session => isHardPinned?.Invoke(session) ?? false)
            .ToArray();
        var softPinnedCandidates = warmCandidates
            .Where(session => !(isHardPinned?.Invoke(session) ?? false) && (isPinned?.Invoke(session) ?? false))
            .ToArray();
        var unpinnedCandidates = warmCandidates
            .Where(session => !(isHardPinned?.Invoke(session) ?? false) && !(isPinned?.Invoke(session) ?? false))
            .ToArray();

        var evictProfiles = _evictionPolicy.GetProfilesToEvict(
            unpinnedCandidates,
            new AcpConnectionEvictionContext(DateTime.UtcNow, unpinnedCandidates.Length));
        var evictProfileSet = evictProfiles.ToHashSet(StringComparer.Ordinal);
        if (_evictionOptions.MaxPinnedProfiles is { } maxPinnedProfiles && maxPinnedProfiles >= 0)
        {
            var pinnedOverflow = softPinnedCandidates.Length - maxPinnedProfiles;
            if (pinnedOverflow > 0)
            {
                foreach (var pinned in softPinnedCandidates
                             .OrderBy(session => session.LastUsedUtc)
                             .Take(pinnedOverflow))
                {
                    evictProfileSet.Add(pinned.ProfileId);
                }

                _logger.LogInformation(
                    "ACP pinned session budget enforced. softPinned={SoftPinned} maxPinned={MaxPinned} hardPinned={HardPinned} evictedPinned={EvictedPinned}",
                    softPinnedCandidates.Length,
                    maxPinnedProfiles,
                    hardPinnedCandidates.Length,
                    pinnedOverflow);
            }
        }

        var sessionsToEvict = warmCandidates
            .Where(session => evictProfileSet.Contains(session.ProfileId))
            .ToArray();
        var removedWarmCount = 0;
        var detachedForEviction = new List<AcpConnectionSession>(sessionsToEvict.Length);

        // 选择阶段仍可响应取消,但一旦 RemoveByProfile 成功,该会话就已脱离注册表;
        // 因此把已摘除者收集起来,并在 finally 中无条件释放,取消不得让它们变成无主连接。
        try
        {
            foreach (var session in sessionsToEvict)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!_sessionRegistry.RemoveByProfile(session.ProfileId))
                {
                    continue;
                }

                removedWarmCount++;
                if (ReferenceEquals(activeService, session.Service))
                {
                    continue;
                }

                detachedForEviction.Add(session);
            }
        }
        finally
        {
            var evictedFailureCount = await DisposeDetachedSessionsAsync(
                detachedForEviction,
                static (logger, session, ex) => logger.LogDebug(
                    ex,
                    "Failed to dispose evicted cached ACP session. profileId={ProfileId}",
                    session.ProfileId),
                releaseAfterDisconnectFailure: false).ConfigureAwait(false);
            disposeFailureCount += evictedFailureCount;
        }

        return new AcpConnectionSessionCleanupResult(removed.Count + removedWarmCount, disposeFailureCount);
    }

    /// <summary>
    /// 并发释放已脱离注册表的会话,并发度受 <see cref="_maxConcurrentDisposals"/> 限制。
    /// 不接受 CancellationToken:调用方在此之前已摘除会话,唯有此处负责归还底层资源。
    /// </summary>
    private async Task<int> DisposeDetachedSessionsAsync(
        IEnumerable<AcpConnectionSession> sessions,
        Action<ILogger, AcpConnectionSession, Exception> logDisconnectFailure,
        bool releaseAfterDisconnectFailure)
    {
        var pending = sessions as IReadOnlyList<AcpConnectionSession> ?? sessions.ToArray();
        if (pending.Count == 0)
        {
            return 0;
        }

        var failureCount = 0;
        using var throttle = new SemaphoreSlim(_maxConcurrentDisposals, _maxConcurrentDisposals);
        var tasks = new List<Task>(pending.Count);
        foreach (var session in pending)
        {
            tasks.Add(DisposeThrottledAsync(session));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
        return failureCount;

        async Task DisposeThrottledAsync(AcpConnectionSession session)
        {
            await throttle.WaitAsync().ConfigureAwait(false);
            try
            {
                await DisposeOneAsync(session).ConfigureAwait(false);
            }
            finally
            {
                throttle.Release();
            }
        }

        async Task DisposeOneAsync(AcpConnectionSession session)
        {
            try
            {
                await DisconnectAndDisposeAsync(session.Service).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref failureCount);
                logDisconnectFailure(_logger, session, ex);

                if (!releaseAfterDisconnectFailure)
                {
                    return;
                }

                try
                {
                    session.Service.Dispose();
                }
                catch (Exception disposeEx)
                {
                    _logger.LogDebug(
                        disposeEx,
                        "Failed to release stale cached ACP session after disconnect failure. profileId={ProfileId}",
                        session.ProfileId);
                }
            }
        }
    }

    private static async Task DisconnectAndDisposeAsync(IChatService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        await service.DisconnectAsync().ConfigureAwait(false);
        service.Dispose();
    }
}
