using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SalmonEgg.Domain.Models.Protocol;
using SalmonEgg.Presentation.Core.Mvux.Chat;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public interface IAcpConnectionCoordinator
{
    Task SetConnectingAsync(string? profileId, CancellationToken cancellationToken = default);

    Task SetConnectedAsync(string? profileId, CancellationToken cancellationToken = default);

    Task SetDisconnectedAsync(string? errorMessage = null, CancellationToken cancellationToken = default);

    Task SetAuthenticationRequiredAsync(string? hintMessage, CancellationToken cancellationToken = default);

    Task ClearAuthenticationRequiredAsync(CancellationToken cancellationToken = default);

    Task ResetAsync(CancellationToken cancellationToken = default);

    Task ResyncAsync(IAcpChatCoordinatorSink sink, CancellationToken cancellationToken = default);
}

public sealed class AcpConnectionCoordinator : IAcpConnectionCoordinator
{
    private readonly IChatConnectionStore _store;
    private readonly ILogger<AcpConnectionCoordinator> _logger;

    public AcpConnectionCoordinator(
        IChatConnectionStore store,
        ILogger<AcpConnectionCoordinator> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task SetConnectingAsync(string? profileId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await UpdateProfileAsync(profileId).ConfigureAwait(false);
        await _store.Dispatch(new SetConnectionPhaseAction(ConnectionPhase.Connecting, Error: null))
            .ConfigureAwait(false);
    }

    public async Task SetConnectedAsync(string? profileId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await UpdateProfileAsync(profileId).ConfigureAwait(false);
        await _store.Dispatch(new SetConnectionPhaseAction(ConnectionPhase.Connected, Error: null))
            .ConfigureAwait(false);
    }

    public async Task SetDisconnectedAsync(string? errorMessage = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _store.Dispatch(new SetConnectionPhaseAction(ConnectionPhase.Disconnected, errorMessage))
            .ConfigureAwait(false);
    }

    public async Task SetAuthenticationRequiredAsync(
        string? hintMessage,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _store.Dispatch(new SetConnectionAuthenticationStateAction(true, hintMessage))
            .ConfigureAwait(false);
    }

    public async Task ClearAuthenticationRequiredAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _store.Dispatch(new SetConnectionAuthenticationStateAction(false, null))
            .ConfigureAwait(false);
    }

    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _store.Dispatch(new ResetConnectionStateAction()).ConfigureAwait(false);
    }

    public async Task ResyncAsync(IAcpChatCoordinatorSink sink, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sink);
        cancellationToken.ThrowIfCancellationRequested();

        var sessionId = sink.CurrentRemoteSessionId;
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            _logger.LogDebug("Skipping ACP resync because no remote session binding is available.");
            return;
        }

        if (sink.CurrentChatService is null)
        {
            _logger.LogDebug("Skipping ACP resync because chat service is unavailable.");
            return;
        }

        try
        {
            await sink.CurrentChatService.LoadSessionAsync(
                new SessionLoadParams(sessionId, sink.GetActiveSessionCwdOrDefault())).ConfigureAwait(false);

            _logger.LogInformation("ACP resync completed. sessionId={SessionId}", sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ACP resync failed. sessionId={SessionId}", sessionId);
        }
    }

    private Task UpdateProfileAsync(string? profileId)
        => _store.Dispatch(new SetSelectedProfileAction(profileId)).AsTask();
}
