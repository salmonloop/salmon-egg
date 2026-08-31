using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Acp.Mcp;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Presentation.Core.Mvux.Chat;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public interface IAcpConnectionCoordinator
{
    Task SetConnectingAsync(string? profileId, CancellationToken cancellationToken = default);
    Task SetInitializingAsync(string? profileId, CancellationToken cancellationToken = default);
    Task SetConnectedAsync(string? profileId, CancellationToken cancellationToken = default);
    Task SetConnectionInstanceIdAsync(string? connectionInstanceId, CancellationToken cancellationToken = default);

    Task SetDisconnectedAsync(string? errorMessage = null, CancellationToken cancellationToken = default);

    Task SetAuthenticationRequiredAsync(
        string? hintMessage,
        string? hintResourceKey = null,
        string? hintFallback = null,
        object[]? hintFormatArgs = null,
        CancellationToken cancellationToken = default);

    Task ClearAuthenticationRequiredAsync(CancellationToken cancellationToken = default);

    Task ResetAsync(CancellationToken cancellationToken = default);

    Task ResyncAsync(IAcpChatCoordinatorSink sink, CancellationToken cancellationToken = default);
}

internal interface IAcpConnectionStateReader
{
    ValueTask<ChatConnectionState> GetCurrentStateAsync(CancellationToken cancellationToken = default);
}

public sealed class AcpConnectionCoordinator : IAcpConnectionCoordinator, IAcpConnectionStateReader
{
    private readonly IChatConnectionStore _store;
    private readonly ILogger<AcpConnectionCoordinator> _logger;
    private readonly IAcpMcpServerResolver _mcpServerResolver;
    private readonly IAcpRemoteSessionRecoveryContextResolver _recoveryContextResolver;

    public AcpConnectionCoordinator(
        IChatConnectionStore store,
        ILogger<AcpConnectionCoordinator> logger,
        IAcpMcpServerResolver mcpServerResolver,
        IAcpRemoteSessionRecoveryContextResolver recoveryContextResolver)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _mcpServerResolver = mcpServerResolver ?? throw new ArgumentNullException(nameof(mcpServerResolver));
        _recoveryContextResolver = recoveryContextResolver
            ?? throw new ArgumentNullException(nameof(recoveryContextResolver));
    }

    public async Task SetConnectingAsync(string? profileId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _store.Dispatch(new SetConnectionPhaseAction(ConnectionPhase.Connecting, Error: null))
            .ConfigureAwait(false);
        await UpdateForegroundProfileAsync(profileId).ConfigureAwait(false);
    }

    public async Task SetInitializingAsync(string? profileId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await UpdateForegroundProfileAsync(profileId).ConfigureAwait(false);
        await _store.Dispatch(new SetConnectionPhaseAction(ConnectionPhase.Initializing, Error: null))
            .ConfigureAwait(false);
    }

    public async Task SetConnectedAsync(string? profileId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await UpdateForegroundProfileAsync(profileId).ConfigureAwait(false);
        await _store.Dispatch(new SetConnectionPhaseAction(ConnectionPhase.Connected, Error: null))
            .ConfigureAwait(false);
    }

    public async Task SetConnectionInstanceIdAsync(
        string? connectionInstanceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _store.Dispatch(new SetConnectionInstanceIdAction(connectionInstanceId)).ConfigureAwait(false);
    }

    public async Task SetDisconnectedAsync(string? errorMessage = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _store.Dispatch(new SetConnectionPhaseAction(ConnectionPhase.Disconnected, errorMessage))
            .ConfigureAwait(false);
    }

    public async Task SetAuthenticationRequiredAsync(
        string? hintMessage,
        string? hintResourceKey = null,
        string? hintFallback = null,
        object[]? hintFormatArgs = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _store.Dispatch(new SetConnectionAuthenticationStateAction(
                true,
                hintMessage,
                hintResourceKey,
                hintFallback,
                hintFormatArgs))
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

    public async ValueTask<ChatConnectionState> GetCurrentStateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await _store.GetCurrentStateAsync().ConfigureAwait(false);
    }

    public async Task ResyncAsync(IAcpChatCoordinatorSink sink, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sink);
        cancellationToken.ThrowIfCancellationRequested();

        var maybeRequest = await TryCreateResyncRequestAsync(sink, cancellationToken).ConfigureAwait(false);
        if (maybeRequest is not { } request)
        {
            return;
        }

        var adapter = request.ChatService as IAcpSessionUpdateBufferController;
        long? hydrationAttemptId = null;
        try
        {
            await sink.SetConversationHydratingAsync(request.ConversationId, true, cancellationToken)
                .ConfigureAwait(false);
            var recoveryContext = await ResolveResyncRecoveryContextAsync(sink, request, cancellationToken)
                .ConfigureAwait(false);
            if (recoveryContext is null)
            {
                return;
            }

            var recoveryProjection = await ExecuteResyncRecoveryAsync(
                    sink,
                    request,
                    recoveryContext.Value,
                    adapter,
                    attemptId => hydrationAttemptId = attemptId,
                    cancellationToken)
                .ConfigureAwait(false);
            if (recoveryProjection is not { } projection)
            {
                return;
            }

            await FinalizeResyncAsync(
                    sink,
                    request,
                    projection,
                    adapter,
                    hydrationAttemptId,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await sink.SetConversationHydratingAsync(request.ConversationId, false, CancellationToken.None)
                .ConfigureAwait(false);
            ReleaseBufferedUpdatesAfterInterruptedHydration(adapter, hydrationAttemptId, "LoadSessionCanceled");
            throw;
        }
        catch (Exception ex)
        {
            await sink.SetConversationHydratingAsync(request.ConversationId, false, CancellationToken.None)
                .ConfigureAwait(false);
            ReleaseBufferedUpdatesAfterInterruptedHydration(adapter, hydrationAttemptId, "LoadSessionFailed");
            _logger.LogWarning(ex, "ACP resync failed. SessionId={SessionId}", request.SessionId);
        }
    }

    private async Task<ResyncRequest?> TryCreateResyncRequestAsync(
        IAcpChatCoordinatorSink sink,
        CancellationToken cancellationToken)
    {
        var binding = await sink.GetCurrentRemoteBindingAsync(cancellationToken).ConfigureAwait(false);
        var conversationId = binding?.ConversationId;
        var sessionId = binding?.RemoteSessionId;
        if (string.IsNullOrWhiteSpace(conversationId) || string.IsNullOrWhiteSpace(sessionId))
        {
            _logger.LogDebug("Skipping ACP resync because no remote session binding is available.");
            return null;
        }

        var chatService = sink.CurrentChatService;
        if (chatService is null)
        {
            _logger.LogDebug("Skipping ACP resync because chat service is unavailable.");
            return null;
        }

        var recoveryMode = AcpSessionRecoveryPolicy.ResolveForResync(chatService.AgentCapabilities);
        if (recoveryMode == AcpSessionRecoveryMode.None)
        {
            _logger.LogDebug("Skipping ACP resync because agent does not advertise session recovery capability.");
            return null;
        }

        return new ResyncRequest(conversationId, sessionId, chatService, recoveryMode);
    }

    private async Task<AcpRemoteSessionRecoveryContext?> ResolveResyncRecoveryContextAsync(
        IAcpChatCoordinatorSink sink,
        ResyncRequest request,
        CancellationToken cancellationToken)
    {
        var fallback = await sink
            .GetSessionRecoveryFallbackAsync(request.ConversationId, cancellationToken)
            .ConfigureAwait(false);
        var resolution = await _recoveryContextResolver
            .ResolveAsync(request.ChatService, request.SessionId, fallback, cancellationToken)
            .ConfigureAwait(false);

        var currentBinding = await sink
            .GetConversationRemoteBindingAsync(request.ConversationId, cancellationToken)
            .ConfigureAwait(false);
        if (!ReferenceEquals(sink.CurrentChatService, request.ChatService)
            || !string.Equals(currentBinding?.RemoteSessionId, request.SessionId, StringComparison.Ordinal))
        {
            _logger.LogDebug(
                "Discarding ACP resync because the remote binding or chat service changed. ConversationId={ConversationId} RemoteSessionId={RemoteSessionId}",
                request.ConversationId,
                request.SessionId);
            await sink.SetConversationHydratingAsync(request.ConversationId, false, CancellationToken.None)
                .ConfigureAwait(false);
            return null;
        }

        if (resolution.Context is not { } recoveryContext)
        {
            _logger.LogWarning(
                "Skipping ACP resync because session working directory is missing. ConversationId={ConversationId} RemoteSessionId={RemoteSessionId}",
                request.ConversationId,
                request.SessionId);
            await sink.SetConversationHydratingAsync(request.ConversationId, false, CancellationToken.None)
                .ConfigureAwait(false);
            return null;
        }

        if (resolution.AuthoritativeSessionInfo is { } sessionInfo)
        {
            await sink
                .ApplyConversationRemoteSessionInfoAsync(request.ConversationId, sessionInfo, cancellationToken)
                .ConfigureAwait(false);
        }

        return recoveryContext;
    }

    private async Task<AcpSessionRecoveryProjection?> ExecuteResyncRecoveryAsync(
        IAcpChatCoordinatorSink sink,
        ResyncRequest request,
        AcpRemoteSessionRecoveryContext recoveryContext,
        IAcpSessionUpdateBufferController? adapter,
        Action<long?> captureHydrationAttemptId,
        CancellationToken cancellationToken)
    {
        var mcpServers = await _mcpServerResolver
            .ResolveCurrentMcpServersAsync(sink, cancellationToken)
            .ConfigureAwait(false);
        if (request.RecoveryMode == AcpSessionRecoveryMode.Load)
        {
            var hydrationAttemptId = adapter?.BeginHydrationBufferingScope(request.SessionId);
            captureHydrationAttemptId(hydrationAttemptId);
            await sink.ResetConversationForResyncAsync(request.ConversationId, cancellationToken)
                .ConfigureAwait(false);
            var loadTask = request.ChatService.LoadSessionAsync(
                AcpRemoteSessionRecoveryRequestFactory.CreateLoadParams(
                    request.SessionId,
                    recoveryContext,
                    mcpServers),
                cancellationToken);
            var projection = AcpSessionRecoveryProjection.FromLoad(
                await loadTask.WaitAsync(cancellationToken).ConfigureAwait(false));
            if (adapter != null && hydrationAttemptId.HasValue
                && !adapter.TryMarkHydrated(hydrationAttemptId.Value))
            {
                _logger.LogWarning(
                    "Discarding ACP resync completion because buffering attempt is stale. SessionId={SessionId}",
                    request.SessionId);
                await sink.SetConversationHydratingAsync(request.ConversationId, false, CancellationToken.None)
                    .ConfigureAwait(false);
                return null;
            }

            return projection;
        }

        var resumeTask = request.ChatService.ResumeSessionAsync(
            AcpRemoteSessionRecoveryRequestFactory.CreateResumeParams(
                request.SessionId,
                recoveryContext,
                mcpServers),
            cancellationToken);
        return AcpSessionRecoveryProjection.FromResume(
            await resumeTask.WaitAsync(cancellationToken).ConfigureAwait(false));
    }

    private async Task FinalizeResyncAsync(
        IAcpChatCoordinatorSink sink,
        ResyncRequest request,
        AcpSessionRecoveryProjection recoveryProjection,
        IAcpSessionUpdateBufferController? adapter,
        long? hydrationAttemptId,
        CancellationToken cancellationToken)
    {
        await sink.ApplyConversationSessionLoadResponseAsync(
            request.ConversationId,
            recoveryProjection.SessionLoadResponse,
            cancellationToken).ConfigureAwait(false);

        if (request.RecoveryMode == AcpSessionRecoveryMode.Load
            && adapter != null
            && hydrationAttemptId.HasValue)
        {
            await adapter
                .WaitForBufferedUpdatesDrainedAsync(hydrationAttemptId.Value, cancellationToken)
                .ConfigureAwait(false);
            if (!adapter.TryMarkHydrated(hydrationAttemptId.Value, reason: "PostDrainVerification"))
            {
                _logger.LogWarning(
                    "Discarding ACP resync finalization because buffering attempt became stale after drain. SessionId={SessionId}",
                    request.SessionId);
                await sink.SetConversationHydratingAsync(request.ConversationId, false, CancellationToken.None)
                    .ConfigureAwait(false);
                return;
            }
        }

        await sink.MarkConversationRemoteHydratedAsync(request.ConversationId, cancellationToken)
            .ConfigureAwait(false);
        await sink.SetConversationHydratingAsync(request.ConversationId, false, cancellationToken)
            .ConfigureAwait(false);
        _logger.LogInformation(
            "ACP resync completed. SessionId={SessionId} RecoveryMode={RecoveryMode}",
            request.SessionId,
            request.RecoveryMode);
    }

    private readonly record struct ResyncRequest(
        string ConversationId,
        string SessionId,
        IChatService ChatService,
        AcpSessionRecoveryMode RecoveryMode);


    private Task UpdateForegroundProfileAsync(string? profileId)
        => _store.Dispatch(new SetForegroundTransportProfileAction(profileId)).AsTask();

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
