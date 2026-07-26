using System;
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
        foreach (var session in removed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ReferenceEquals(activeService, session.Service))
            {
                continue;
            }

            try
            {
                await DisconnectAndDisposeAsync(session.Service).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                disposeFailureCount++;
                _logger.LogDebug(
                    ex,
                    "Failed to dispose stale cached ACP session. profileId={ProfileId}",
                    session.ProfileId);

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

            try
            {
                await DisconnectAndDisposeAsync(session.Service).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                disposeFailureCount++;
                _logger.LogDebug(
                    ex,
                    "Failed to dispose evicted cached ACP session. profileId={ProfileId}",
                    session.ProfileId);
            }
        }

        return new AcpConnectionSessionCleanupResult(removed.Count + removedWarmCount, disposeFailureCount);
    }

    private static async Task DisconnectAndDisposeAsync(IChatService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        await service.DisconnectAsync().ConfigureAwait(false);
        service.Dispose();
    }
}
