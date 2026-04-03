using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Models.Content;
using SalmonEgg.Domain.Models.Mcp;
using SalmonEgg.Domain.Models.Protocol;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public interface IAcpSessionCommandOrchestrator
{
    Task<AcpRemoteSessionResult> EnsureRemoteSessionAsync(
        IAcpChatCoordinatorSink sink,
        Func<CancellationToken, Task<bool>> authenticateAsync,
        Action markHydrated,
        CancellationToken cancellationToken = default);

    Task<AcpPromptDispatchResult> SendPromptAsync(
        string promptText,
        IAcpChatCoordinatorSink sink,
        Func<CancellationToken, Task<bool>> authenticateAsync,
        Func<IAcpChatCoordinatorSink, Func<CancellationToken, Task<bool>>, Action, CancellationToken, Task<AcpRemoteSessionResult>> ensureRemoteSessionAsync,
        CancellationToken cancellationToken = default);

    Task<AcpPromptDispatchResult> DispatchPromptToRemoteSessionAsync(
        string remoteSessionId,
        string promptText,
        IAcpChatCoordinatorSink sink,
        Func<CancellationToken, Task<bool>> authenticateAsync,
        Func<IAcpChatCoordinatorSink, Func<CancellationToken, Task<bool>>, Action, CancellationToken, Task<AcpRemoteSessionResult>> ensureRemoteSessionAsync,
        CancellationToken cancellationToken = default);

    Task CancelPromptAsync(
        IAcpChatCoordinatorSink sink,
        string? reason = null,
        CancellationToken cancellationToken = default);
}

public sealed class AcpSessionCommandOrchestrator : IAcpSessionCommandOrchestrator
{
    private readonly ILogger<AcpSessionCommandOrchestrator> _logger;

    public AcpSessionCommandOrchestrator(ILogger<AcpSessionCommandOrchestrator> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<AcpRemoteSessionResult> EnsureRemoteSessionAsync(
        IAcpChatCoordinatorSink sink,
        Func<CancellationToken, Task<bool>> authenticateAsync,
        Action markHydrated,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(authenticateAsync);
        ArgumentNullException.ThrowIfNull(markHydrated);

        var chatService = RequireReadyChatService(sink);
        if (!sink.IsSessionActive || string.IsNullOrWhiteSpace(sink.CurrentSessionId))
        {
            throw new InvalidOperationException("No active local conversation is available for ACP session creation.");
        }

        var currentBinding = await sink.GetCurrentRemoteBindingAsync(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(currentBinding?.RemoteSessionId))
        {
            return new AcpRemoteSessionResult(
                currentBinding.RemoteSessionId!,
                new SessionNewResponse(currentBinding.RemoteSessionId!),
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
        catch (Exception ex) when (AcpErrorClassifier.IsAuthenticationRequired(ex))
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
        markHydrated();
        return new AcpRemoteSessionResult(response.SessionId, response, UsedExistingBinding: false);
    }

    public async Task<AcpPromptDispatchResult> SendPromptAsync(
        string promptText,
        IAcpChatCoordinatorSink sink,
        Func<CancellationToken, Task<bool>> authenticateAsync,
        Func<IAcpChatCoordinatorSink, Func<CancellationToken, Task<bool>>, Action, CancellationToken, Task<AcpRemoteSessionResult>> ensureRemoteSessionAsync,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(promptText))
        {
            throw new ArgumentException("Prompt text must not be empty.", nameof(promptText));
        }

        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(authenticateAsync);
        ArgumentNullException.ThrowIfNull(ensureRemoteSessionAsync);

        if (sink.IsAuthenticationRequired)
        {
            var authenticated = await authenticateAsync(cancellationToken).ConfigureAwait(false);
            if (!authenticated)
            {
                throw new InvalidOperationException(
                    sink.AuthenticationHintMessage ?? "The agent requires authentication before it can respond.");
            }
        }

        var currentBinding = await sink.GetCurrentRemoteBindingAsync(cancellationToken).ConfigureAwait(false);
        var remoteSessionId = !string.IsNullOrWhiteSpace(currentBinding?.RemoteSessionId)
            ? currentBinding.RemoteSessionId!
            : (await ensureRemoteSessionAsync(
                sink,
                authenticateAsync,
                static () => { },
                cancellationToken).ConfigureAwait(false)).RemoteSessionId;

        return await DispatchPromptToRemoteSessionAsync(
            remoteSessionId,
            promptText,
            sink,
            authenticateAsync,
            ensureRemoteSessionAsync,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<AcpPromptDispatchResult> DispatchPromptToRemoteSessionAsync(
        string remoteSessionId,
        string promptText,
        IAcpChatCoordinatorSink sink,
        Func<CancellationToken, Task<bool>> authenticateAsync,
        Func<IAcpChatCoordinatorSink, Func<CancellationToken, Task<bool>>, Action, CancellationToken, Task<AcpRemoteSessionResult>> ensureRemoteSessionAsync,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(promptText))
        {
            throw new ArgumentException("Prompt text must not be empty.", nameof(promptText));
        }

        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(authenticateAsync);
        ArgumentNullException.ThrowIfNull(ensureRemoteSessionAsync);

        var chatService = RequireReadyChatService(sink);
        var promptParams = new SessionPromptParams(
            remoteSessionId,
            new List<ContentBlock> { new TextContentBlock { Text = promptText } });

        try
        {
            var response = await chatService.SendPromptAsync(promptParams, cancellationToken).ConfigureAwait(false);
            return new AcpPromptDispatchResult(promptParams.SessionId, response, RetriedAfterSessionRecovery: false);
        }
        catch (Exception ex) when (AcpErrorClassifier.IsAuthenticationRequired(ex))
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
        catch (Exception ex) when (AcpErrorClassifier.IsRemoteSessionNotFound(ex))
        {
            _logger.LogWarning(ex, "Remote session {RemoteSessionId} not found. Attempting recovery...", remoteSessionId);
            await ClearBindingForCurrentConversationAsync(sink).ConfigureAwait(false);
            var recreated = await ensureRemoteSessionAsync(
                sink,
                authenticateAsync,
                static () => { },
                cancellationToken).ConfigureAwait(false);
            var retryParams = new SessionPromptParams(
                recreated.RemoteSessionId,
                promptParams.Prompt,
                promptParams.MaxTokens,
                promptParams.StopSequences);

            var response = await chatService.SendPromptAsync(retryParams, cancellationToken).ConfigureAwait(false);
            return new AcpPromptDispatchResult(retryParams.SessionId, response, RetriedAfterSessionRecovery: true);
        }
    }

    public async Task CancelPromptAsync(
        IAcpChatCoordinatorSink sink,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sink);

        var chatService = sink.CurrentChatService;
        var currentBinding = await sink.GetCurrentRemoteBindingAsync(cancellationToken).ConfigureAwait(false);
        if (chatService == null || string.IsNullOrWhiteSpace(currentBinding?.RemoteSessionId))
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        await chatService.CancelSessionAsync(
            new SessionCancelParams(currentBinding.RemoteSessionId!, reason)).ConfigureAwait(false);
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

    private static IChatService RequireReadyChatService(IAcpChatCoordinatorSink sink)
    {
        if (sink.CurrentChatService is not { IsConnected: true, IsInitialized: true } chatService)
        {
            throw new InvalidOperationException("ACP chat service is not connected and initialized.");
        }

        return chatService;
    }

}

