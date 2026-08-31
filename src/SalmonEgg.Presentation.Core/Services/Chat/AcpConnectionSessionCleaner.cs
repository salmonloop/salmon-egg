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

    /// <summary>
    /// 进程退出路径专用：摘除并释放<b>全部</b>缓存会话，含当前活跃的那一个。
    /// </summary>
    /// <remarks>
    /// 为什么不是给 <see cref="CleanupStaleAsync"/> 加参数：那条路的两项保护
    /// （放过 <c>activeService</c>、放过 soft/hard pinned）在关闭语义下全部是错的——
    /// 关闭时活跃连接同样必须终止，否则 agent 子进程会被 reparent 到 init 后继续运行
    /// （issue #126 实测）。把互斥语义塞进同一方法会让"关闭"与"换配置"共用一组判定，
    /// 日后任一侧的改动都会静默影响另一侧。
    ///
    /// 不接受 <c>CancellationToken</c>：摘除之后这些会话再无任何持有者，唯有本方法负责
    /// 归还底层进程 / 套接字；中途放弃就等于制造无主的泄漏连接。
    /// </remarks>
    Task<AcpConnectionSessionCleanupResult> DrainAllAsync();
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

    /// <inheritdoc />
    public async Task<AcpConnectionSessionCleanupResult> DrainAllAsync()
    {
        // 一次 RemoveWhere 全部摘净：逐个 RemoveByProfile 会在两次调用之间留出窗口，
        // 让并发的 connect apply 又塞回一个会话，从而漏掉它。
        var removed = _sessionRegistry.RemoveWhere(static _ => true);
        if (removed.Count == 0)
        {
            return new AcpConnectionSessionCleanupResult(0, 0);
        }

        // releaseAfterDisconnectFailure: true —— 关闭路径上 DisconnectAsync 失败（例如对端
        // 已经没了）绝不能就此放手：Dispose 才是真正 Kill(entireProcessTree) 的那一步。
        var disposeFailureCount = await DisposeDetachedSessionsAsync(
            removed,
            static (logger, session, ex) => logger.LogDebug(
                ex,
                "Failed to disconnect cached ACP session during shutdown drain. profileId={ProfileId}",
                session.ProfileId),
            releaseAfterDisconnectFailure: true).ConfigureAwait(false);

        _logger.LogInformation(
            "ACP connection pool drained for shutdown. removedCount={RemovedCount} disposeFailureCount={DisposeFailureCount}",
            removed.Count,
            disposeFailureCount);

        return new AcpConnectionSessionCleanupResult(removed.Count, disposeFailureCount);
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
