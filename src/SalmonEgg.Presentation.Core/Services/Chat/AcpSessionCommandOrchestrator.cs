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

    Task<AcpPromptDispatchResult> SendPromptAsync(
        string promptText,
        string? promptMessageId,
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

    Task<AcpPromptDispatchResult> DispatchPromptToRemoteSessionAsync(
        string remoteSessionId,
        string promptText,
        string? promptMessageId,
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
    private readonly IAcpMcpServerResolver _mcpServerResolver;
    private readonly ILogger<AcpSessionCommandOrchestrator> _logger;

    public AcpSessionCommandOrchestrator(
        ILogger<AcpSessionCommandOrchestrator> logger,
        IAcpMcpServerResolver mcpServerResolver)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _mcpServerResolver = mcpServerResolver ?? throw new ArgumentNullException(nameof(mcpServerResolver));
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

        var selectedProfileId = sink.SelectedProfileId;
        var currentBinding = await sink.GetCurrentRemoteBindingAsync(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(currentBinding?.RemoteSessionId))
        {
            return new AcpRemoteSessionResult(
                currentBinding.RemoteSessionId!,
                new SessionNewResponse(currentBinding.RemoteSessionId!),
                UsedExistingBinding: true);
        }

        var activeSessionCwd = ResolveActiveSessionCwdOrProtocolError(sink);
        var sessionParams = new SessionNewParams(
            activeSessionCwd,
            McpServerJsonConverter.CloneServers(
                await _mcpServerResolver.ResolveCurrentMcpServersAsync(sink, cancellationToken)
                    .ConfigureAwait(false)));

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

            sessionParams = new SessionNewParams(
                activeSessionCwd,
                McpServerJsonConverter.CloneServers(
                    await _mcpServerResolver.ResolveCurrentMcpServersAsync(sink, cancellationToken)
                        .ConfigureAwait(false)));
            response = await chatService.CreateSessionAsync(sessionParams).ConfigureAwait(false);
        }

        await UpdateBindingForCurrentConversationAsync(sink, response.SessionId, selectedProfileId).ConfigureAwait(false);
        markHydrated();
        return new AcpRemoteSessionResult(response.SessionId, response, UsedExistingBinding: false);
    }

    private string ResolveActiveSessionCwdOrProtocolError(IAcpChatCoordinatorSink sink)
    {
        var cwdResolution = AcpSessionNewCwdResolver.Resolve(
            sink.GetActiveSessionCwdOrDefault()?.Trim(),
            sink.ResolveProfile(sink.SelectedProfileId),
            sink.GetProjectPathMappings());
        var cwd = cwdResolution.Cwd?.Trim();
        if (string.IsNullOrWhiteSpace(cwd))
        {
            _logger.LogWarning("Skipping remote session creation because session cwd is missing or empty.");
            throw new InvalidOperationException(
                cwdResolution.ErrorMessage ?? AcpSessionNewCwdResolver.MissingRemoteCwdMessage);
        }

        return cwd;
    }

    public async Task<AcpPromptDispatchResult> SendPromptAsync(
        string promptText,
        IAcpChatCoordinatorSink sink,
        Func<CancellationToken, Task<bool>> authenticateAsync,
        Func<IAcpChatCoordinatorSink, Func<CancellationToken, Task<bool>>, Action, CancellationToken, Task<AcpRemoteSessionResult>> ensureRemoteSessionAsync,
        CancellationToken cancellationToken = default)
        => await SendPromptAsync(
            promptText,
            promptMessageId: null,
            sink,
            authenticateAsync,
            ensureRemoteSessionAsync,
            cancellationToken).ConfigureAwait(false);

    public async Task<AcpPromptDispatchResult> SendPromptAsync(
        string promptText,
        string? promptMessageId,
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
            promptMessageId,
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
        => await DispatchPromptToRemoteSessionAsync(
            remoteSessionId,
            promptText,
            promptMessageId: null,
            sink,
            authenticateAsync,
            ensureRemoteSessionAsync,
            cancellationToken).ConfigureAwait(false);

    public async Task<AcpPromptDispatchResult> DispatchPromptToRemoteSessionAsync(
        string remoteSessionId,
        string promptText,
        string? promptMessageId,
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
            new List<ContentBlock> { new TextContentBlock { Text = promptText } },
            messageId: promptMessageId);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await sink.NotifyPromptRequestDispatchedAsync(cancellationToken).ConfigureAwait(false);
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

            cancellationToken.ThrowIfCancellationRequested();
            await sink.NotifyPromptRequestDispatchedAsync(cancellationToken).ConfigureAwait(false);
            var response = await chatService.SendPromptAsync(promptParams, cancellationToken).ConfigureAwait(false);
            return new AcpPromptDispatchResult(promptParams.SessionId, response, RetriedAfterSessionRecovery: false);
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

    private static IChatService RequireReadyChatService(IAcpChatCoordinatorSink sink)
    {
        if (sink.CurrentChatService is not { IsConnected: true, IsInitialized: true } chatService)
        {
            throw new InvalidOperationException("ACP chat service is not connected and initialized.");
        }

        return chatService;
    }

}
