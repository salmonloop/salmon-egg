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
    Task SetInitializingAsync(string? profileId, CancellationToken cancellationToken = default);
    Task SetConnectedAsync(string? profileId, CancellationToken cancellationToken = default);

    Task SetDisconnectedAsync(string? errorMessage = null, CancellationToken cancellationToken = default);

    Task SetAuthenticationRequiredAsync(string? hintMessage, CancellationToken cancellationToken = default);

    Task ClearAuthenticationRequiredAsync(CancellationToken cancellationToken = default);

    Task ResetAsync(CancellationToken cancellationToken = default);

    Task ResyncAsync(IAcpChatCoordinatorSink sink, CancellationToken cancellationToken = default);
}

public sealed class AcpConnectionCoordinator : IAcpConnectionCoordinator
{
    private static readonly TimeSpan SessionLoadTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan ReplayDrainTimeout = TimeSpan.FromSeconds(10);

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

    public async Task SetInitializingAsync(string? profileId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await UpdateProfileAsync(profileId).ConfigureAwait(false);
        await _store.Dispatch(new SetConnectionPhaseAction(ConnectionPhase.Initializing, Error: null))
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

        var binding = await sink.GetCurrentRemoteBindingAsync(cancellationToken).ConfigureAwait(false);
        var conversationId = binding?.ConversationId;
        var sessionId = binding?.RemoteSessionId;
        if (string.IsNullOrWhiteSpace(conversationId) || string.IsNullOrWhiteSpace(sessionId))
        {
            _logger.LogDebug("Skipping ACP resync because no remote session binding is available.");
            return;
        }

        if (sink.CurrentChatService is null)
        {
            _logger.LogDebug("Skipping ACP resync because chat service is unavailable.");
            return;
        }

        if (sink.CurrentChatService.AgentCapabilities?.LoadSession != true)
        {
            _logger.LogDebug("Skipping ACP resync because agent does not advertise loadSession capability.");
            return;
        }

        var adapter = sink.CurrentChatService as IAcpSessionUpdateBufferController;
        long? hydrationAttemptId = null;

        try
        {
            hydrationAttemptId = adapter?.BeginHydrationBufferingScope(sessionId);
            await sink.SetConversationHydratingAsync(conversationId!, true, cancellationToken).ConfigureAwait(false);
            await sink.ResetConversationForResyncAsync(conversationId!, cancellationToken).ConfigureAwait(false);
            var loadTask = sink.CurrentChatService.LoadSessionAsync(
                new SessionLoadParams(sessionId, sink.GetSessionCwdOrDefault(conversationId!)),
                cancellationToken);
            var sessionLoadResponse = await loadTask.WaitAsync(SessionLoadTimeout, cancellationToken).ConfigureAwait(false);
            if (adapter != null && hydrationAttemptId.HasValue)
            {
                if (!adapter.TryMarkHydrated(hydrationAttemptId.Value))
                {
                    _logger.LogWarning(
                        "Discarding ACP resync completion because buffering attempt is stale. sessionId={SessionId}",
                        sessionId);
                    await sink.SetConversationHydratingAsync(conversationId!, false, CancellationToken.None).ConfigureAwait(false);
                    return;
                }
            }

            await sink.ApplyConversationSessionLoadResponseAsync(conversationId!, sessionLoadResponse, cancellationToken).ConfigureAwait(false);

            if (adapter != null && hydrationAttemptId.HasValue)
            {

                await adapter
                    .WaitForBufferedUpdatesDrainedAsync(hydrationAttemptId.Value, cancellationToken)
                    .WaitAsync(ReplayDrainTimeout, cancellationToken)
                    .ConfigureAwait(false);
                if (!adapter.TryMarkHydrated(hydrationAttemptId.Value, reason: "PostDrainVerification"))
                {
                    _logger.LogWarning(
                        "Discarding ACP resync finalization because buffering attempt became stale after drain. sessionId={SessionId}",
                        sessionId);
                    await sink.SetConversationHydratingAsync(conversationId!, false, CancellationToken.None).ConfigureAwait(false);
                    return;
                }
            }

            await sink.MarkConversationRemoteHydratedAsync(conversationId!, cancellationToken).ConfigureAwait(false);
            await sink.SetConversationHydratingAsync(conversationId!, false, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("ACP resync completed. sessionId={SessionId}", sessionId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await sink.SetConversationHydratingAsync(conversationId!, false, CancellationToken.None).ConfigureAwait(false);
            ReleaseBufferedUpdatesAfterInterruptedHydration(adapter, hydrationAttemptId, "LoadSessionCanceled");

            throw;
        }
        catch (Exception ex)
        {
            await sink.SetConversationHydratingAsync(conversationId!, false, CancellationToken.None).ConfigureAwait(false);
            ReleaseBufferedUpdatesAfterInterruptedHydration(adapter, hydrationAttemptId, "LoadSessionFailed");

            _logger.LogWarning(ex, "ACP resync failed. sessionId={SessionId}", sessionId);
        }
    }

    private Task UpdateProfileAsync(string? profileId)
        => _store.Dispatch(new SetSelectedProfileAction(profileId)).AsTask();

    private static void ReleaseBufferedUpdatesAfterInterruptedHydration(
        IAcpSessionUpdateBufferController? adapter,
        long? hydrationAttemptId,
        string reason)
    {
        if (adapter is null || !hydrationAttemptId.HasValue)
        {
            return;
        }

        adapter.SuppressBufferedUpdates(hydrationAttemptId.Value, reason);
        adapter.TryMarkHydrated(hydrationAttemptId.Value, lowTrust: true, reason: reason);
    }
}
