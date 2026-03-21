using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.Content;
using SalmonEgg.Domain.Models.JsonRpc;
using SalmonEgg.Domain.Models.Mcp;
using SalmonEgg.Domain.Models.Protocol;

namespace SalmonEgg.Presentation.Core.Services.Chat;

/// <summary>
/// Minimal ACP service lifecycle coordinator.
/// This slice extracts transport/profile/service seams so ChatViewModel can delegate incrementally.
/// </summary>
public sealed class AcpChatCoordinator : IAcpConnectionCommands
{
    private const int DefaultSessionUpdateBufferLimit = 256;

    private readonly IAcpChatServiceFactory _chatServiceFactory;
    private readonly IAcpConnectionCoordinator _connectionCoordinator;
    private readonly ILogger<AcpChatCoordinator> _logger;
    private readonly int _sessionUpdateBufferLimit;
    private AcpChatServiceAdapter? _activeChatServiceAdapter;

    public AcpChatCoordinator(
        IAcpChatServiceFactory chatServiceFactory,
        ILogger<AcpChatCoordinator> logger,
        IAcpConnectionCoordinator? connectionCoordinator = null,
        int sessionUpdateBufferLimit = DefaultSessionUpdateBufferLimit)
    {
        _chatServiceFactory = chatServiceFactory ?? throw new ArgumentNullException(nameof(chatServiceFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        if (sessionUpdateBufferLimit <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sessionUpdateBufferLimit),
                "Session update buffer limit must be positive.");
        }

        _connectionCoordinator = connectionCoordinator ?? NoopAcpConnectionCoordinator.Instance;
        _sessionUpdateBufferLimit = sessionUpdateBufferLimit;
    }

    public async Task<AcpTransportApplyResult> ConnectToProfileAsync(
        ServerConfiguration profile,
        IAcpTransportConfiguration transportConfiguration,
        IAcpChatCoordinatorSink sink,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(transportConfiguration);
        ArgumentNullException.ThrowIfNull(sink);

        sink.SelectProfile(profile);
        ApplyProfileToTransportConfiguration(profile, transportConfiguration);

        var preserveConversation = sink.IsSessionActive && !string.IsNullOrWhiteSpace(sink.CurrentSessionId);
        return await ApplyTransportConfigurationAsync(
            transportConfiguration,
            sink,
            preserveConversation,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<AcpTransportApplyResult> ApplyTransportConfigurationAsync(
        IAcpTransportConfiguration transportConfiguration,
        IAcpChatCoordinatorSink sink,
        bool preserveConversation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transportConfiguration);
        ArgumentNullException.ThrowIfNull(sink);
        cancellationToken.ThrowIfCancellationRequested();

        var (isValid, errorMessage) = transportConfiguration.Validate();
        if (!isValid)
        {
            await _connectionCoordinator.SetDisconnectedAsync(errorMessage, cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException(errorMessage ?? "Invalid ACP transport configuration.");
        }

        await _connectionCoordinator.SetConnectingAsync(sink.SelectedProfileId, cancellationToken).ConfigureAwait(false);

        var previousService = sink.CurrentChatService;
        try
        {
            var createdService = _chatServiceFactory.CreateChatService(
                transportConfiguration.SelectedTransportType,
                transportConfiguration.SelectedTransportType == TransportType.Stdio ? transportConfiguration.StdioCommand : null,
                transportConfiguration.SelectedTransportType == TransportType.Stdio ? transportConfiguration.StdioArgs : null,
                transportConfiguration.SelectedTransportType == TransportType.Stdio ? null : transportConfiguration.RemoteUrl);

            if (previousService != null)
            {
                try
                {
                    await previousService.DisconnectAsync().ConfigureAwait(false);
                    if (previousService is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to disconnect previous ACP service during transport replacement");
                }
            }

            var wrappedService = WrapChatService(createdService, sink, cancellationToken);
            sink.ReplaceChatService(wrappedService);
            _activeChatServiceAdapter = wrappedService;
            TryMarkHydratedForCurrentState(sink, wrappedService);

            var initializeResponse = await wrappedService
                .InitializeAsync(CreateDefaultInitializeParams())
                .ConfigureAwait(false);
            sink.UpdateAgentIdentity(initializeResponse.AgentInfo?.Name, initializeResponse.AgentInfo?.Version);
            await _connectionCoordinator.SetConnectedAsync(sink.SelectedProfileId, cancellationToken).ConfigureAwait(false);
            await _connectionCoordinator.ClearAuthenticationRequiredAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "ACP transport applied. transport={TransportType} preserveConversation={PreserveConversation}",
                transportConfiguration.SelectedTransportType,
                preserveConversation);

            return new AcpTransportApplyResult(wrappedService, initializeResponse);
        }
        catch (Exception ex)
        {
            try
            {
                if (sink.CurrentChatService != null)
                {
                    await sink.CurrentChatService.DisconnectAsync().ConfigureAwait(false);
                    if (sink.CurrentChatService is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                }
            }
            catch (Exception disconnectEx)
            {
                _logger.LogDebug(disconnectEx, "Failed to tear down ACP service after initialization error");
            }

            sink.ReplaceChatService(null);
            _activeChatServiceAdapter = null;
            sink.UpdateAgentIdentity(null, null);
            await _connectionCoordinator.SetDisconnectedAsync(ex.Message, cancellationToken).ConfigureAwait(false);
            _logger.LogError(ex, "Failed to apply ACP transport configuration");
            throw;
        }
    }

    public async Task<AcpRemoteSessionResult> EnsureRemoteSessionAsync(
        IAcpChatCoordinatorSink sink,
        Func<CancellationToken, Task<bool>> authenticateAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(authenticateAsync);

        var chatService = RequireReadyChatService(sink);
        if (!sink.IsSessionActive || string.IsNullOrWhiteSpace(sink.CurrentSessionId))
        {
            throw new InvalidOperationException("No active local conversation is available for ACP session creation.");
        }

        if (!string.IsNullOrWhiteSpace(sink.CurrentRemoteSessionId))
        {
            return new AcpRemoteSessionResult(
                sink.CurrentRemoteSessionId!,
                new SessionNewResponse(sink.CurrentRemoteSessionId!),
                UsedExistingBinding: true);
        }

        var sessionParams = new SessionNewParams(
            sink.GetActiveSessionCwdOrDefault(),
            new List<McpServer>());

        SessionNewResponse response;
        try
        {
            response = await chatService.CreateSessionAsync(sessionParams).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsAuthenticationRequiredError(ex))
        {
            var authenticated = await authenticateAsync(cancellationToken).ConfigureAwait(false);
            if (!authenticated)
            {
                throw new InvalidOperationException(
                    sink.AuthenticationHintMessage ?? "The agent requires authentication before it can respond.",
                    ex);
            }

            response = await chatService.CreateSessionAsync(sessionParams).ConfigureAwait(false);
        }

        await UpdateBindingForCurrentConversationAsync(sink, response.SessionId, sink.SelectedProfileId).ConfigureAwait(false);
        _activeChatServiceAdapter?.MarkHydrated();
        return new AcpRemoteSessionResult(response.SessionId, response, UsedExistingBinding: false);
    }

    public async Task<AcpPromptDispatchResult> SendPromptAsync(
        string promptText,
        IAcpChatCoordinatorSink sink,
        Func<CancellationToken, Task<bool>> authenticateAsync,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(promptText))
        {
            throw new ArgumentException("Prompt text must not be empty.", nameof(promptText));
        }

        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(authenticateAsync);

        var chatService = RequireReadyChatService(sink);

        if (sink.IsAuthenticationRequired)
        {
            var authenticated = await authenticateAsync(cancellationToken).ConfigureAwait(false);
            if (!authenticated)
            {
                throw new InvalidOperationException(
                    sink.AuthenticationHintMessage ?? "The agent requires authentication before it can respond.");
            }
        }

        var remoteSession = !string.IsNullOrWhiteSpace(sink.CurrentRemoteSessionId)
            ? new AcpRemoteSessionResult(
                sink.CurrentRemoteSessionId!,
                new SessionNewResponse(sink.CurrentRemoteSessionId!),
                UsedExistingBinding: true)
            : await EnsureRemoteSessionAsync(sink, authenticateAsync, cancellationToken).ConfigureAwait(false);

        var promptParams = new SessionPromptParams(
            remoteSession.RemoteSessionId,
            new List<ContentBlock> { new TextContentBlock { Text = promptText } });

        try
        {
            var response = await chatService.SendPromptAsync(promptParams, cancellationToken).ConfigureAwait(false);
            return new AcpPromptDispatchResult(promptParams.SessionId, response, RetriedAfterSessionRecovery: false);
        }
        catch (Exception ex) when (IsAuthenticationRequiredError(ex))
        {
            var authenticated = await authenticateAsync(cancellationToken).ConfigureAwait(false);
            if (!authenticated)
            {
                throw new InvalidOperationException(
                    sink.AuthenticationHintMessage ?? "The agent requires authentication before it can respond.",
                    ex);
            }

            var response = await chatService.SendPromptAsync(promptParams, cancellationToken).ConfigureAwait(false);
            return new AcpPromptDispatchResult(promptParams.SessionId, response, RetriedAfterSessionRecovery: false);
        }
        catch (Exception ex) when (IsRemoteSessionNotFound(ex))
        {
            await ClearBindingForCurrentConversationAsync(sink).ConfigureAwait(false);
            var recreated = await EnsureRemoteSessionAsync(sink, authenticateAsync, cancellationToken).ConfigureAwait(false);
            promptParams.SessionId = recreated.RemoteSessionId;

            var response = await chatService.SendPromptAsync(promptParams, cancellationToken).ConfigureAwait(false);
            return new AcpPromptDispatchResult(promptParams.SessionId, response, RetriedAfterSessionRecovery: true);
        }
    }

    public async Task CancelPromptAsync(
        IAcpChatCoordinatorSink sink,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sink);

        var chatService = sink.CurrentChatService;
        if (chatService == null || string.IsNullOrWhiteSpace(sink.CurrentRemoteSessionId))
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        await chatService.CancelSessionAsync(
            new SessionCancelParams(sink.CurrentRemoteSessionId!, reason)).ConfigureAwait(false);
    }

    public async Task DisconnectAsync(
        IAcpChatCoordinatorSink sink,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sink);
        cancellationToken.ThrowIfCancellationRequested();

        var chatService = sink.CurrentChatService;
        if (chatService != null)
        {
            await chatService.DisconnectAsync().ConfigureAwait(false);
            if (chatService is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        sink.ReplaceChatService(null);
        _activeChatServiceAdapter = null;
        await ClearBindingForCurrentConversationAsync(sink).ConfigureAwait(false);
        sink.UpdateAgentIdentity(null, null);
        await _connectionCoordinator.ResetAsync(cancellationToken).ConfigureAwait(false);
    }

    private AcpChatServiceAdapter WrapChatService(
        IChatService chatService,
        IAcpChatCoordinatorSink sink,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(chatService);
        ArgumentNullException.ThrowIfNull(sink);

        AcpChatServiceAdapter? wrappedService = null;
        var eventAdapter = new AcpEventAdapter(
            update => wrappedService!.PublishBufferedUpdate(update),
            sink.SessionUpdateSynchronizationContext,
            _sessionUpdateBufferLimit,
            resyncRequired: () => _ = HandleResyncRequiredAsync(sink, cancellationToken));
        wrappedService = new AcpChatServiceAdapter(chatService, eventAdapter);
        return wrappedService;
    }

    private async Task HandleResyncRequiredAsync(
        IAcpChatCoordinatorSink sink,
        CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            "ACP update stream requested resync. remoteSessionId={RemoteSessionId}",
            sink.CurrentRemoteSessionId);

        await _connectionCoordinator.ResyncAsync(sink, cancellationToken).ConfigureAwait(false);
    }

    private static async Task UpdateBindingForCurrentConversationAsync(
        IAcpChatCoordinatorSink sink,
        string remoteSessionId,
        string? profileId)
    {
        if (string.IsNullOrWhiteSpace(remoteSessionId))
        {
            throw new ArgumentException("Remote session id must not be empty.", nameof(remoteSessionId));
        }

        if (string.IsNullOrWhiteSpace(sink.CurrentSessionId))
        {
            throw new InvalidOperationException("Cannot update remote binding without an active local conversation.");
        }

        var result = await sink.ConversationBindingCommands
            .UpdateBindingAsync(
                sink.CurrentSessionId!,
                remoteSessionId,
                profileId)
            .ConfigureAwait(false);

        if (result.Status is not BindingUpdateStatus.Success)
        {
            throw new InvalidOperationException(
                $"Failed to update conversation binding ({result.Status}): {result.ErrorMessage ?? "UnknownError"}");
        }
    }

    private static async Task ClearBindingForCurrentConversationAsync(IAcpChatCoordinatorSink sink)
    {
        if (string.IsNullOrWhiteSpace(sink.CurrentSessionId))
        {
            return;
        }

        await sink.ConversationBindingCommands
            .UpdateBindingAsync(
                sink.CurrentSessionId!,
                remoteSessionId: null,
                sink.SelectedProfileId)
            .ConfigureAwait(false);
    }

    private void TryMarkHydratedForCurrentState(
        IAcpChatCoordinatorSink sink,
        AcpChatServiceAdapter wrappedService)
    {
        if (!string.IsNullOrWhiteSpace(sink.CurrentRemoteSessionId) && sink.IsSessionActive)
        {
            wrappedService.MarkHydrated();
            return;
        }

        if (sink.ConnectionGeneration > 0)
        {
            wrappedService.MarkHydrated(lowTrust: true, reason: "ConnectionGenerationAdvanced");
        }
    }

    private static void ApplyProfileToTransportConfiguration(
        ServerConfiguration profile,
        IAcpTransportConfiguration transportConfiguration)
    {
        transportConfiguration.SelectedTransportType = profile.Transport;

        if (profile.Transport == TransportType.Stdio)
        {
            transportConfiguration.StdioCommand = profile.StdioCommand ?? string.Empty;
            transportConfiguration.StdioArgs = profile.StdioArgs ?? string.Empty;
            transportConfiguration.RemoteUrl = string.Empty;
            return;
        }

        transportConfiguration.RemoteUrl = profile.ServerUrl ?? string.Empty;
        transportConfiguration.StdioCommand = string.Empty;
        transportConfiguration.StdioArgs = string.Empty;
    }

    private static IChatService RequireReadyChatService(IAcpChatCoordinatorSink sink)
    {
        if (sink.CurrentChatService is not { IsConnected: true, IsInitialized: true } chatService)
        {
            throw new InvalidOperationException("ACP chat service is not connected and initialized.");
        }

        return chatService;
    }

    private static bool IsAuthenticationRequiredError(Exception ex) =>
        ex is AcpException acp && acp.ErrorCode == JsonRpcErrorCode.AuthenticationRequired;

    private static bool IsRemoteSessionNotFound(Exception ex) =>
        ex is AcpException acp
        && (acp.ErrorCode == JsonRpcErrorCode.ResourceNotFound
            || (acp.Message.Contains("Session", StringComparison.OrdinalIgnoreCase)
                && acp.Message.Contains("not found", StringComparison.OrdinalIgnoreCase)));

    private sealed class NoopAcpConnectionCoordinator : IAcpConnectionCoordinator
    {
        public static NoopAcpConnectionCoordinator Instance { get; } = new();

        public Task SetConnectingAsync(string? profileId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SetConnectedAsync(string? profileId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SetDisconnectedAsync(string? errorMessage = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SetAuthenticationRequiredAsync(string? hintMessage, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ClearAuthenticationRequiredAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ResetAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ResyncAsync(IAcpChatCoordinatorSink sink, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private static InitializeParams CreateDefaultInitializeParams()
        => new()
        {
            ProtocolVersion = 1,
            ClientInfo = new ClientInfo
            {
                Name = "SalmonEgg",
                Title = "Uno Acp Client",
                Version = "1.0.0"
            },
            ClientCapabilities = new ClientCapabilities
            {
                Fs = new FsCapability
                {
                    ReadTextFile = true,
                    WriteTextFile = true
                }
            }
        };
}
