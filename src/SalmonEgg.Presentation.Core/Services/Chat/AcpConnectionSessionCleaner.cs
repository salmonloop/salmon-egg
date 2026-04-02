using System;
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
        CancellationToken cancellationToken = default);
}

public sealed class AcpConnectionSessionCleaner : IAcpConnectionSessionCleaner
{
    private readonly IAcpConnectionSessionRegistry _sessionRegistry;
    private readonly ILogger<AcpConnectionSessionCleaner> _logger;

    public AcpConnectionSessionCleaner(
        IAcpConnectionSessionRegistry sessionRegistry,
        ILogger<AcpConnectionSessionCleaner> logger)
    {
        _sessionRegistry = sessionRegistry ?? throw new ArgumentNullException(nameof(sessionRegistry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<AcpConnectionSessionCleanupResult> CleanupStaleAsync(
        IChatService? activeService,
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

                if (session.Service is IDisposable disposable)
                {
                    try
                    {
                        disposable.Dispose();
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

        return new AcpConnectionSessionCleanupResult(removed.Count, disposeFailureCount);
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
