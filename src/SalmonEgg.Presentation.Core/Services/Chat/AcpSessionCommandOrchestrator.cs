using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Localization;
using SalmonEgg.Presentation.Core.Resources;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Acp.Content;
using SalmonEgg.Acp.Mcp;
using SalmonEgg.Acp.Protocol;

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
        CancellationToken cancellationToken = default);
}

public sealed class AcpSessionCommandOrchestrator : IAcpSessionCommandOrchestrator
{
    private readonly IAcpMcpServerResolver _mcpServerResolver;
    private readonly ILogger<AcpSessionCommandOrchestrator> _logger;
    private readonly IStringLocalizer<CoreStrings>? _localizer;

    public AcpSessionCommandOrchestrator(
        ILogger<AcpSessionCommandOrchestrator> logger,
        IAcpMcpServerResolver mcpServerResolver,
        IStringLocalizer<CoreStrings>? localizer = null)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _mcpServerResolver = mcpServerResolver ?? throw new ArgumentNullException(nameof(mcpServerResolver));
        _localizer = localizer;
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
            throw new InvalidOperationException(
                Localize(
                    "ChatSession_NoActiveLocalConversation",
                    "No active local conversation is available for ACP session creation."));
        }

        var conversationId = sink.CurrentSessionId!;
        var selectedProfileId = sink.SelectedProfileId;
        var currentBinding = await sink.GetConversationRemoteBindingAsync(conversationId, cancellationToken)
            .ConfigureAwait(false);
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
            McpServerSnapshots.CloneServers(
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
                    sink.AuthenticationHintMessage ?? ResolveAuthenticationRequiredMessage(),
                    ex);
            }

            sessionParams = new SessionNewParams(
                activeSessionCwd,
                McpServerSnapshots.CloneServers(
                    await _mcpServerResolver.ResolveCurrentMcpServersAsync(sink, cancellationToken)
                        .ConfigureAwait(false)));
            response = await chatService.CreateSessionAsync(sessionParams).ConfigureAwait(false);
        }

        await UpdateBindingForConversationAsync(sink, conversationId, response.SessionId, selectedProfileId)
            .ConfigureAwait(false);
        markHydrated();
        return new AcpRemoteSessionResult(response.SessionId, response, UsedExistingBinding: false);
    }

    private string ResolveActiveSessionCwdOrProtocolError(IAcpChatCoordinatorSink sink)
    {
        var cwdResolution = AcpSessionNewCwdResolver.Resolve(
            sink.GetActiveSessionCwdOrDefault()?.Trim(),
            sink.ResolveProfile(sink.SelectedProfileId));
        var cwd = cwdResolution.Cwd?.Trim();
        if (string.IsNullOrWhiteSpace(cwd))
        {
            _logger.LogInformation(
                "ACP remote session cwd resolution rejected. profileId={ProfileId} transport={Transport} requestedCwd={RequestedCwd} reason={Reason}",
                sink.SelectedProfileId,
                sink.ResolveProfile(sink.SelectedProfileId)?.Transport,
                sink.GetActiveSessionCwdOrDefault(),
                cwdResolution.ErrorMessage ?? AcpSessionNewCwdResolver.MissingRemoteCwdMessage);
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
                    sink.AuthenticationHintMessage ?? ResolveAuthenticationRequiredMessage());
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

        var conversationId = sink.CurrentSessionId;
        var chatService = RequireReadyChatService(sink);
        var promptParams = new SessionPromptParams(
            remoteSessionId,
            new List<ContentBlock> { new TextContentBlock { Text = promptText } });

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
                    sink.AuthenticationHintMessage ?? ResolveAuthenticationRequiredMessage(),
                    ex);
            }

            cancellationToken.ThrowIfCancellationRequested();
            await sink.NotifyPromptRequestDispatchedAsync(cancellationToken).ConfigureAwait(false);
            var response = await chatService.SendPromptAsync(promptParams, cancellationToken).ConfigureAwait(false);
            return new AcpPromptDispatchResult(promptParams.SessionId, response, RetriedAfterSessionRecovery: false);
        }
        catch (Exception ex) when (AcpErrorClassifier.IsRemoteSessionNotFound(ex))
        {
            // The remote session expired on the agent side (e.g. WebSocket transport reconnected
            // but the agent-side session timed out in the interim). Clear the stale binding so
            // EnsureRemoteSessionAsync will create a fresh remote session, then retry the prompt.
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(conversationId)
                || !string.Equals(sink.CurrentSessionId, conversationId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Remote session recovery was abandoned because the active conversation changed.",
                    ex);
            }

            await sink.ConversationBindingCommands
                .ClearBindingAsync(conversationId!)
                .ConfigureAwait(false);
            var recovered = await ensureRemoteSessionAsync(
                    sink, authenticateAsync, static () => { }, cancellationToken)
                .ConfigureAwait(false);
            var recoveredParams = new SessionPromptParams(recovered.RemoteSessionId, promptParams.Prompt);
            cancellationToken.ThrowIfCancellationRequested();
            await sink.NotifyPromptRequestDispatchedAsync(cancellationToken).ConfigureAwait(false);
            var recoveredResponse = await chatService.SendPromptAsync(recoveredParams, cancellationToken).ConfigureAwait(false);
            return new AcpPromptDispatchResult(recovered.RemoteSessionId, recoveredResponse, RetriedAfterSessionRecovery: true);
        }
    }

    public async Task CancelPromptAsync(
        IAcpChatCoordinatorSink sink,
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
            new SessionCancelParams(currentBinding.RemoteSessionId!)).ConfigureAwait(false);
    }

    private async Task UpdateBindingForConversationAsync(
        IAcpChatCoordinatorSink sink,
        string conversationId,
        string remoteSessionId,
        string? profileId)
    {
        if (string.IsNullOrWhiteSpace(remoteSessionId))
        {
            throw new ArgumentException("Remote session id must not be empty.", nameof(remoteSessionId));
        }

        if (string.IsNullOrWhiteSpace(conversationId))
        {
            throw new InvalidOperationException(
                Localize(
                    "ChatBinding_NoActiveLocalConversation",
                    "Cannot update remote binding without an active local conversation."));
        }

        var result = await sink.ConversationBindingCommands
            .UpdateBindingAsync(
                conversationId,
                remoteSessionId,
                profileId)
            .ConfigureAwait(false);

        if (result.Status is not BindingUpdateStatus.Success)
        {
            throw new InvalidOperationException(
                FormatLocalize(
                    "ChatBinding_UpdateFailedWithStatus",
                    "Failed to update conversation binding ({0}): {1}",
                    result.Status,
                    ResolveBindingErrorDetail(result.ErrorMessage)));
        }
    }

    private IChatService RequireReadyChatService(IAcpChatCoordinatorSink sink)
    {
        if (sink.CurrentChatService is not { IsConnected: true, IsInitialized: true } chatService)
        {
            throw new InvalidOperationException(
                Localize(
                    "ChatService_NotConnectedInitialized",
                    "ACP chat service is not connected and initialized."));
        }

        return chatService;
    }



    private string ResolveBindingErrorDetail(string? errorMessage)
        => string.IsNullOrWhiteSpace(errorMessage)
            ? Localize("ChatBinding_UnknownError", "UnknownError")
            : errorMessage.Trim();

    private string Localize(string key, string fallback)
    {
        if (_localizer is null)
        {
            return fallback;
        }

        var localized = _localizer[key];
        return localized.ResourceNotFound || string.IsNullOrWhiteSpace(localized.Value)
            ? fallback
            : localized.Value;
    }

    private string FormatLocalize(string key, string fallback, params object[] arguments)
    {
        if (_localizer is null)
        {
            return string.Format(System.Globalization.CultureInfo.CurrentCulture, fallback, arguments);
        }

        var localized = _localizer[key, arguments];
        return localized.ResourceNotFound || string.IsNullOrWhiteSpace(localized.Value)
            ? string.Format(System.Globalization.CultureInfo.CurrentCulture, fallback, arguments)
            : localized.Value;
    }

    private string ResolveAuthenticationRequiredMessage()
    {
        const string fallback = "The agent requires authentication before it can respond.";
        if (_localizer is null)
        {
            return fallback;
        }

        var localized = _localizer["ChatAuth_Required"];
        return localized.ResourceNotFound || string.IsNullOrWhiteSpace(localized.Value)
            ? fallback
            : localized.Value;
    }
}
