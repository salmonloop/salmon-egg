using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
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
    private readonly IAcpConnectionSessionRegistry _sessionRegistry;
    private readonly IAcpConnectionEvictionPolicy _evictionPolicy;
    private readonly AcpConnectionEvictionOptions _evictionOptions;
    private readonly ILogger<AcpConnectionSessionCleaner> _logger;

    public AcpConnectionSessionCleaner(
        IAcpConnectionSessionRegistry sessionRegistry,
        IAcpConnectionEvictionPolicy evictionPolicy,
        AcpConnectionEvictionOptions evictionOptions,
        ILogger<AcpConnectionSessionCleaner> logger)
    {
        _sessionRegistry = sessionRegistry ?? throw new ArgumentNullException(nameof(sessionRegistry));
        _evictionPolicy = evictionPolicy ?? throw new ArgumentNullException(nameof(evictionPolicy));
        _evictionOptions = evictionOptions ?? throw new ArgumentNullException(nameof(evictionOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

        var disposeFailureCount = 0;

        async Task DisposeSessionAsync(AcpConnectionSession session)
        {
            try
            {
                await DisconnectAndDisposeAsync(session.Service).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref disposeFailureCount);
                _logger.LogDebug(
                    ex,
                    "Failed to dispose stale cached ACP session. profileId={ProfileId}",
                    session.ProfileId);

                if (session.Service is IDisposable disposable)
                {
                    try
                    {
                        disposable.Dispose();
                    }
                    catch (Exception disposeEx)
                    {
                        // Ignore errors during disposal
                        _logger.LogDebug(
                            disposeEx,
                            "Failed to release stale cached ACP session after disconnect failure. profileId={ProfileId}",
                            session.ProfileId);
                    }
                }
            }
        }

        var staleTasks = new List<Task>(removed.Count);
        foreach (var session in removed)
        {
            if (ReferenceEquals(activeService, session.Service))
            {
                continue;
            }
            staleTasks.Add(DisposeSessionAsync(session));
        }

        if (staleTasks.Count > 0)
        {
            await Task.WhenAll(staleTasks).ConfigureAwait(false);
        }

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

        async Task DisposeEvictedSessionAsync(AcpConnectionSession session)
        {
            try
            {
                await DisconnectAndDisposeAsync(session.Service).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref disposeFailureCount);
                _logger.LogDebug(
                    ex,
                    "Failed to dispose evicted cached ACP session. profileId={ProfileId}",
                    session.ProfileId);
            }
        }

        var evictedTasks = new List<Task>(sessionsToEvict.Length);
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

            evictedTasks.Add(DisposeEvictedSessionAsync(session));
        }

        if (evictedTasks.Count > 0)
        {
            await Task.WhenAll(evictedTasks).ConfigureAwait(false);
        }

        return new AcpConnectionSessionCleanupResult(removed.Count + removedWarmCount, disposeFailureCount);
    }

    private static async Task DisconnectAndDisposeAsync(IChatService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        await service.DisconnectAsync().ConfigureAwait(false);

        if (service is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
