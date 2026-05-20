using System;
using Uno.Extensions.Reactive;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Interfaces;
using SalmonEgg.Domain.Interfaces.Storage;
using SalmonEgg.Domain.Interfaces.Transport;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.Content;
using SalmonEgg.Domain.Models.JsonRpc;
using SalmonEgg.Domain.Models.Mcp;
using SalmonEgg.Domain.Models.Protocol;
using SalmonEgg.Domain.Models.ProjectAffinity;
using SalmonEgg.Domain.Models.Session;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Core.Services.ProjectAffinity;
using SalmonEgg.Presentation.Core.Services.Input;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.ViewModels.Chat.AskUser;
using SalmonEgg.Presentation.ViewModels.Chat.Hydration;
using SalmonEgg.Presentation.Core.ViewModels.Chat.Input;
using SalmonEgg.Presentation.ViewModels.Chat.Interactions;
using SalmonEgg.Presentation.Core.ViewModels.Chat.Overlay;
using SalmonEgg.Presentation.Core.ViewModels.Chat.PlanPanel;
using SalmonEgg.Presentation.Core.ViewModels.Chat.ProjectAffinity;
using SalmonEgg.Presentation.ViewModels.Chat.Activation;
using SalmonEgg.Presentation.ViewModels.Chat.Transcript;
using SalmonEgg.Presentation.ViewModels.Chat.Panels;
using SalmonEgg.Presentation.Models.Navigation;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.ViewModels.Settings;

namespace SalmonEgg.Presentation.ViewModels.Chat;

public partial class ChatViewModel
{
    private sealed record PromptSendContext(
        string ConversationId,
        string TurnId,
        string PromptText,
        string PromptMessageId,
        ConversationMessageSnapshot UserSnapshot);

    private async Task PublishDisconnectedConnectionStateAsync(string? errorMessage)
    {
        await _acpConnectionCoordinator.SetConnectionInstanceIdAsync(null).ConfigureAwait(false);
        await _acpConnectionCoordinator.SetDisconnectedAsync(errorMessage).ConfigureAwait(false);
    }

    [RelayCommand]
    private async Task CreateNewSessionAsync()
    {
        if (IsConnecting)
        {
            return;
        }

        try
        {
            ClearError();
            var sessionParams = CreateSessionNewParams();
            var response = await CreateRemoteSessionAsync(sessionParams, CancellationToken.None).ConfigureAwait(false);
            var localConversationId = await CreateAndActivateLocalConversationAsync(sessionParams.Cwd).ConfigureAwait(false);
            await BindLocalConversationToRemoteSessionAsync(localConversationId, response.SessionId).ConfigureAwait(false);
            await ApplyCreatedSessionProjectionAsync(localConversationId, response).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to create session");
            SetError($"Failed to create session: {ex.Message}");
        }
    }

    /// <summary>
    /// Sends the current prompt to the active agent.
    /// Handles lazy session creation, authentication requirements, and error recovery.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSendPrompt))]
    private async Task SendPromptAsync()
    {
        var promptContext = TryCreatePromptSendContext();
        if (promptContext is null)
        {
            return;
        }

        if (!CanSendPrompt())
        {
            return;
        }

        if (IsAuthenticationRequired)
        {
            using var authenticationCts = new CancellationTokenSource();
            _sendPromptCts = authenticationCts;

            var authenticated = await TryAuthenticateAsync(authenticationCts.Token).ConfigureAwait(false);
            if (!authenticated)
            {
                _sendPromptCts = null;
                ShowTransientNotificationToast(AuthenticationHintMessage ?? "The agent requires authentication before it can respond.");
                return;
            }

            _sendPromptCts = null;
        }

        try
        {
            await BeginPromptSendAsync(promptContext).ConfigureAwait(true);
            await EnsurePromptDispatchAsync(promptContext).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // User-cancelled; keep input cleared.
            await PreemptivelyCancelTurnAsync(promptContext.ConversationId, promptContext.TurnId).ConfigureAwait(true);
        }
        catch (TimeoutException ex)
        {
            Logger.LogError(ex, "SendPrompt timed out");
            SetError("Send timed out: Agent did not respond for a long time.");

            await FailPromptSendAsync(promptContext, "Timed out").ConfigureAwait(true);

            RestoreCurrentPromptOnUiThread(promptContext.PromptText);

            ShowTransientNotificationToast("Agent no response (timeout). Please check if the agent needs login/initialization or try again later.");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "SendPrompt failed");
            SetError($"Send failed: {ex.Message}");

            await FailPromptSendAsync(promptContext, ex.Message).ConfigureAwait(true);

            // Restore text so the user can retry quickly.
            RestoreCurrentPromptOnUiThread(promptContext.PromptText);

            ShowTransientNotificationToast("Send failed, please try again later.");
        }
        finally
        {
            try { _sendPromptCts?.Dispose(); } catch { }
            _sendPromptCts = null;
        }
    }

    private SessionNewParams CreateSessionNewParams()
        => new()
        {
            Cwd = GetActiveSessionCwdOrDefault(),
            McpServers = McpServerJsonConverter.CloneServers(CurrentMcpServers)
        };

    private async Task<SessionNewResponse> CreateRemoteSessionAsync(
        SessionNewParams sessionParams,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessionParams);

        if (_chatService == null)
        {
            throw new InvalidOperationException("Chat service is not initialized");
        }

        try
        {
            return await _chatService.CreateSessionAsync(sessionParams).ConfigureAwait(false);
        }
        catch (Exception ex) when (ChatAuthenticationCoordinator.IsAuthenticationRequiredError(ex))
        {
            var authenticated = await TryAuthenticateAsync(cancellationToken).ConfigureAwait(false);
            if (!authenticated)
            {
                throw new OperationCanceledException("Authentication was not completed.", cancellationToken);
            }

            return await _chatService.CreateSessionAsync(sessionParams).ConfigureAwait(false);
        }
    }

    private async Task<string> CreateAndActivateLocalConversationAsync(string? cwd)
    {
        var localConversationId = Guid.NewGuid().ToString("N");
        await _sessionManager.CreateSessionAsync(localConversationId, cwd).ConfigureAwait(false);
        await _conversationWorkspace.RegisterConversationAsync(
            localConversationId,
            createdAt: DateTime.UtcNow,
            lastUpdatedAt: DateTime.UtcNow).ConfigureAwait(false);

        var switched = await ActivateConversationAsync(localConversationId).ConfigureAwait(false);
        if (!switched)
        {
            throw new InvalidOperationException("Failed to activate local conversation before applying session response.");
        }

        return localConversationId;
    }

    private async Task BindLocalConversationToRemoteSessionAsync(
        string localConversationId,
        string remoteSessionId)
    {
        var bindingResult = await _bindingCommands
            .UpdateBindingAsync(localConversationId, remoteSessionId, SelectedProfileId)
            .ConfigureAwait(false);
        if (bindingResult.Status is not BindingUpdateStatus.Success)
        {
            throw new InvalidOperationException(
                $"Failed to bind new conversation ({bindingResult.Status}): {bindingResult.ErrorMessage ?? "UnknownError"}");
        }
    }

    private async Task ApplyCreatedSessionProjectionAsync(
        string localConversationId,
        SessionNewResponse response)
    {
        await ApplyCurrentStoreProjectionAsync().ConfigureAwait(false);
        await ApplySessionNewResponseAsync(localConversationId, response).ConfigureAwait(true);
    }

    private PromptSendContext? TryCreatePromptSendContext()
    {
        if (string.IsNullOrWhiteSpace(CurrentPrompt)
            || !IsSessionActive
            || string.IsNullOrWhiteSpace(CurrentSessionId))
        {
            return null;
        }

        var promptText = CurrentPrompt;
        var userSnapshot = CreateContentSnapshot(new TextContentBlock { Text = promptText }, isOutgoing: true);
        return new PromptSendContext(
            CurrentSessionId,
            Guid.NewGuid().ToString(),
            promptText,
            Guid.NewGuid().ToString("D"),
            userSnapshot);
    }

    private async Task BeginPromptSendAsync(PromptSendContext context)
    {
        ClearError();
        _sendPromptCts?.Cancel();
        _sendPromptCts = new CancellationTokenSource();
        await _chatStore.Dispatch(new BeginTurnAction(
            context.ConversationId,
            context.TurnId,
            ChatTurnPhase.CreatingRemoteSession,
            PendingUserMessageLocalId: context.UserSnapshot.Id,
            PendingUserProtocolMessageId: context.PromptMessageId,
            PendingUserMessageText: context.PromptText));
        ClearCurrentPromptOnUiThread();
        await UpsertTranscriptSnapshotAsync(context.ConversationId, context.UserSnapshot).ConfigureAwait(true);
        NotifyComposerProjectionChanged();
    }

    private async Task EnsurePromptDispatchAsync(PromptSendContext context)
    {
        if (_chatService is null)
        {
            return;
        }

        var token = _sendPromptCts?.Token ?? CancellationToken.None;
        token.ThrowIfCancellationRequested();

        var sessionResult = await _acpConnectionCommands
            .EnsureRemoteSessionAsync(this, TryAuthenticateAsync, token)
            .ConfigureAwait(false);

        token.ThrowIfCancellationRequested();

        if (!sessionResult.UsedExistingBinding)
        {
            await ApplySessionNewResponseAsync(context.ConversationId, sessionResult.Session).ConfigureAwait(true);
        }

        token.ThrowIfCancellationRequested();

        await _chatStore.Dispatch(new AdvanceTurnPhaseAction(
            context.ConversationId,
            context.TurnId,
            ChatTurnPhase.DispatchingPrompt));

        var promptDispatchResult = await _acpConnectionCommands
            .DispatchPromptToRemoteSessionAsync(
                sessionResult.RemoteSessionId,
                context.PromptText,
                context.PromptMessageId,
                this,
                TryAuthenticateAsync,
                token)
            .ConfigureAwait(false);

        await ReconcilePromptUserMessageIdAsync(
            context.ConversationId,
            context.UserSnapshot.Id,
            context.PromptMessageId,
            promptDispatchResult.Response.UserMessageId).ConfigureAwait(false);

        await ApplyPromptDispatchResultAsync(
            context.ConversationId,
            context.TurnId,
            promptDispatchResult.Response).ConfigureAwait(false);
    }

    private Task FailPromptSendAsync(PromptSendContext context, string reason)
        => _chatStore.Dispatch(new FailTurnAction(context.ConversationId, context.TurnId, reason)).AsTask();

    private async Task OnPromptRequestDispatchedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var state = await _chatStore.GetCurrentStateAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var activeTurn = state.ActiveTurn;
        if (activeTurn is null || activeTurn.Phase != ChatTurnPhase.DispatchingPrompt)
        {
            return;
        }

        await _chatStore.Dispatch(new AdvanceTurnPhaseAction(
            activeTurn.ConversationId,
            activeTurn.TurnId,
            ChatTurnPhase.WaitingForAgent)).ConfigureAwait(false);
    }

    Task IAcpChatCoordinatorSink.NotifyPromptRequestDispatchedAsync(CancellationToken cancellationToken)
        => OnPromptRequestDispatchedAsync(cancellationToken);

    private ChatComposerPresentationState ResolveInputState()
        => _inputStatePresenter.Present(new ChatInputStateInput(
            IsBusy: IsBusy,
            IsPromptInFlight: IsPromptInFlight,
            IsPromptSubmitInFlight: IsPromptSubmitInFlight,
            IsVoiceInputListening: IsVoiceInputListening,
            IsVoiceInputTransportBusy: IsVoiceInputTransportBusy,
            HasPendingAskUserRequest: PendingAskUserRequest is not null,
            ShouldShowLoadingOverlayPresenter: ShouldShowLoadingOverlayPresenter,
            IsSessionActive: IsSessionActive,
            HasChatService: _chatService is not null,
            IsInitialized: IsInitialized,
            HasCurrentSessionId: !string.IsNullOrWhiteSpace(CurrentSessionId),
            HasPromptText: !string.IsNullOrWhiteSpace(CurrentPrompt),
            IsVoiceInputSupported: IsVoiceInputSupported));

    private ChatAskUserState ResolveAskUserState()
        => _askUserStatePresenter.Present(PendingAskUserRequest, _emptyAskUserQuestions);

    private ChatPlanPanelState ResolvePlanPanelState()
        => _planPanelStatePresenter.Present(ShowPlanPanel, PlanEntries.Count);

    private bool CanSendPrompt() => ResolveInputState().CanSendPrompt;

    [RelayCommand]
    private async Task StartVoiceInputAsync()
    {
        if (!CanStartVoiceInput)
        {
            return;
        }

        VoiceInputErrorMessage = null;

        TryDisposeVoiceInputCts();
        _voiceInputCts = new CancellationTokenSource();
        SetVoiceInputTransportState(VoiceInputTransportState.Starting);
        string? requestId = null;

        try
        {
            var permission = await _voiceInputService.EnsurePermissionAsync(_voiceInputCts.Token);
            if (!permission.IsGranted)
            {
                var message = string.IsNullOrWhiteSpace(permission.Message)
                    ? "Microphone permission was denied."
                    : permission.Message.Trim();
                if (permission.RequiresAuthorization)
                {
                    try
                    {
                        await _voiceInputService.TryRequestAuthorizationHelpAsync(_voiceInputCts.Token);
                    }
                    catch
                    {
                    }
                }

                VoiceInputErrorMessage = message;
                ShowTransientNotificationToast(message);
                return;
            }

            requestId = Guid.NewGuid().ToString("N");
            _transportVoiceInputRequestId = requestId;
            _activeVoiceInputRequestId = requestId;
            _voiceInputBasePrompt = CurrentPrompt ?? string.Empty;
            IsVoiceInputListening = true;

            var options = new VoiceInputSessionOptions(
                requestId,
                ResolveVoiceInputLanguageTag(),
                EnablePartialResults: true,
                PreferOffline: false);

            await _voiceInputService.StartAsync(options, _voiceInputCts.Token);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is expected when voice input is quickly superseded.
            if (requestId is not null && IsCurrentVoiceInputRequest(requestId))
            {
                IsVoiceInputListening = false;
                _activeVoiceInputRequestId = null;
            }

            if (requestId is not null
                && IsCurrentVoiceTransportRequest(requestId)
                && _voiceInputTransportState == VoiceInputTransportState.Starting)
            {
                ClearVoiceInputTransport(requestId, disposeCts: true);
            }
        }
        catch (Exception ex)
        {
            VoiceInputErrorMessage = ex.Message;
            ShowTransientNotificationToast($"Voice input failed: {ex.Message}");
            if (requestId is not null && IsCurrentVoiceInputRequest(requestId))
            {
                IsVoiceInputListening = false;
                _activeVoiceInputRequestId = null;
                _voiceInputBasePrompt = CurrentPrompt ?? string.Empty;
            }

            if (requestId is not null
                && IsCurrentVoiceTransportRequest(requestId)
                && _voiceInputTransportState == VoiceInputTransportState.Starting)
            {
                ClearVoiceInputTransport(requestId, disposeCts: true);
            }
        }
        finally
        {
            if (requestId is null
                && _voiceInputTransportState == VoiceInputTransportState.Starting)
            {
                SetVoiceInputTransportState(VoiceInputTransportState.Idle);
            }
            else if (requestId is not null
                && IsCurrentVoiceTransportRequest(requestId)
                && _voiceInputTransportState == VoiceInputTransportState.Starting)
            {
                SetVoiceInputTransportState(VoiceInputTransportState.Idle);
            }
        }
    }

    [RelayCommand]
    private async Task StopVoiceInputAsync()
    {
        if (!IsVoiceInputListening)
        {
            return;
        }

        var requestId = _transportVoiceInputRequestId;
        if (string.IsNullOrWhiteSpace(requestId))
        {
            return;
        }

        SetVoiceInputTransportState(VoiceInputTransportState.Stopping);
        IsVoiceInputListening = false;
        _activeVoiceInputRequestId = null;
        _voiceInputBasePrompt = CurrentPrompt ?? string.Empty;

        try
        {
            try
            {
                _voiceInputCts?.Cancel();
            }
            catch
            {
            }

            await _voiceInputService.StopAsync();
            ClearVoiceInputTransport(requestId, disposeCts: true);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is expected when stopping a live recognition request.
        }
        catch (Exception ex)
        {
            RestoreVoiceInputFrontSession(requestId);
            VoiceInputErrorMessage = ex.Message;
            ShowTransientNotificationToast($"Failed to stop voice input: {ex.Message}");
        }
    }

    private string ResolveVoiceInputLanguageTag()
        => CultureInfo.CurrentUICulture.Name;

    private void OnVoiceInputPartialResultReceived(object? sender, VoiceInputPartialResult result)
        => _ = HandleVoiceInputPartialResultAsync(result);

    private async Task HandleVoiceInputPartialResultAsync(VoiceInputPartialResult result)
    {
        if (!IsCurrentVoiceInputRequest(result.RequestId))
        {
            return;
        }

        await PostToUiAsync(() =>
        {
            if (!IsCurrentVoiceInputRequest(result.RequestId))
            {
                return;
            }

            CurrentPrompt = MergeVoiceInputText(result.Text);
        }).ConfigureAwait(false);
    }

    private void OnVoiceInputFinalResultReceived(object? sender, VoiceInputFinalResult result)
        => _ = HandleVoiceInputFinalResultAsync(result);

    private async Task HandleVoiceInputFinalResultAsync(VoiceInputFinalResult result)
    {
        if (!IsCurrentVoiceInputRequest(result.RequestId))
        {
            return;
        }

        await PostToUiAsync(() =>
        {
            if (!IsCurrentVoiceInputRequest(result.RequestId))
            {
                return;
            }

            CurrentPrompt = MergeVoiceInputText(result.Text);
            _voiceInputBasePrompt = CurrentPrompt ?? string.Empty;
        }).ConfigureAwait(false);
    }

    private void OnVoiceInputSessionEnded(object? sender, VoiceInputSessionEndedResult result)
        => _ = HandleVoiceInputSessionEndedAsync(result);

    private async Task HandleVoiceInputSessionEndedAsync(VoiceInputSessionEndedResult result)
    {
        if (IsCurrentVoiceTransportRequest(result.RequestId))
        {
            await PostToUiAsync(() =>
            {
                ClearVoiceInputTransport(result.RequestId, disposeCts: true);
            }).ConfigureAwait(false);
        }

        if (!IsCurrentVoiceInputRequest(result.RequestId))
        {
            return;
        }

        await PostToUiAsync(() =>
        {
            if (!IsCurrentVoiceInputRequest(result.RequestId))
            {
                return;
            }

            IsVoiceInputListening = false;
            _activeVoiceInputRequestId = null;
            _voiceInputBasePrompt = CurrentPrompt ?? string.Empty;
        }).ConfigureAwait(false);
    }

    private void OnVoiceInputErrorOccurred(object? sender, VoiceInputErrorResult result)
        => _ = HandleVoiceInputErrorAsync(result);

    private async Task HandleVoiceInputErrorAsync(VoiceInputErrorResult result)
    {
        if (IsCurrentVoiceTransportRequest(result.RequestId))
        {
            await PostToUiAsync(() =>
            {
                ClearVoiceInputTransport(result.RequestId, disposeCts: true);
            }).ConfigureAwait(false);
        }

        if (!IsCurrentVoiceInputRequest(result.RequestId))
        {
            return;
        }

        await PostToUiAsync(() =>
        {
            if (!IsCurrentVoiceInputRequest(result.RequestId))
            {
                return;
            }

            var message = string.IsNullOrWhiteSpace(result.Message)
                ? "Voice input failed."
                : result.Message.Trim();
            if (result.RequiresAuthorization)
            {
                try
                {
                    _ = _voiceInputService.TryRequestAuthorizationHelpAsync();
                }
                catch
                {
                }
            }
            VoiceInputErrorMessage = message;
            IsVoiceInputListening = false;
            _activeVoiceInputRequestId = null;
            _voiceInputBasePrompt = CurrentPrompt ?? string.Empty;
            ShowTransientNotificationToast(message);
        }).ConfigureAwait(false);
    }

    private bool IsCurrentVoiceInputRequest(string requestId)
        => !string.IsNullOrWhiteSpace(requestId)
            && string.Equals(_activeVoiceInputRequestId, requestId, StringComparison.Ordinal);

    private bool IsCurrentVoiceTransportRequest(string? requestId)
        => !string.IsNullOrWhiteSpace(requestId)
            && string.Equals(_transportVoiceInputRequestId, requestId, StringComparison.Ordinal);

    private void RestoreVoiceInputFrontSession(string requestId)
    {
        if (!IsCurrentVoiceTransportRequest(requestId))
        {
            return;
        }

        SetVoiceInputTransportState(VoiceInputTransportState.Idle);
        IsVoiceInputListening = true;
        _activeVoiceInputRequestId = requestId;
        _voiceInputBasePrompt = CurrentPrompt ?? string.Empty;
    }

    private void ClearVoiceInputTransport(string requestId, bool disposeCts)
    {
        if (!IsCurrentVoiceTransportRequest(requestId))
        {
            return;
        }

        _transportVoiceInputRequestId = null;
        SetVoiceInputTransportState(VoiceInputTransportState.Idle);
        if (disposeCts)
        {
            TryDisposeVoiceInputCts();
        }
    }

    private void SetVoiceInputTransportState(VoiceInputTransportState state)
    {
        if (_voiceInputTransportState == state)
        {
            return;
        }

        _voiceInputTransportState = state;
        SendPromptCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsVoiceInputTransportBusy));
        NotifyComposerProjectionChanged();
    }

    private void TryDisposeVoiceInputCts()
    {
        if (_voiceInputCts is null)
        {
            return;
        }

        try
        {
            _voiceInputCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
        catch
        {
        }

        try
        {
            _voiceInputCts.Dispose();
        }
        catch
        {
        }

        _voiceInputCts = null;
    }

    private string MergeVoiceInputText(string rawText)
    {
        var spokenText = rawText?.Trim() ?? string.Empty;
        var basePrompt = _voiceInputBasePrompt?.TrimEnd() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(spokenText))
        {
            return basePrompt;
        }

        if (string.IsNullOrWhiteSpace(basePrompt))
        {
            return spokenText;
        }

        return $"{basePrompt} {spokenText}";
    }

    [RelayCommand]
    private async Task CancelPromptAsync()
    {
        if (!IsPromptInFlight)
        {
            return;
        }

        var state = await _chatStore.GetCurrentStateAsync().ConfigureAwait(false);
        var activeTurn = state.ActiveTurn;
        var isDispatchedTurn = IsDispatchedPromptTurn(activeTurn);
        if (IsUndispatchedPromptTurn(activeTurn))
        {
            try
            {
                _sendPromptCts?.Cancel();
            }
            catch
            {
            }
        }

        if (!IsSessionActive)
        {
            return;
        }

        try
        {
            if (!isDispatchedTurn)
            {
                await PreemptivelyCancelTurnAsync().ConfigureAwait(false);
            }
            else
            {
                await PreemptivelyCancelOutstandingToolCallsAsync().ConfigureAwait(false);
            }

            if (isDispatchedTurn)
            {
                var activeBinding = await ResolveActiveConversationBindingAsync().ConfigureAwait(false);
                await CancelPendingPermissionRequestAsync(activeBinding?.RemoteSessionId).ConfigureAwait(false);
                await _acpConnectionCommands.CancelPromptAsync(this, "User cancelled").ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Cancel prompt failed");
            ShowTransientNotificationToast("Cancellation failed.");
        }
    }

    private static bool IsUndispatchedPromptTurn(ActiveTurnState? activeTurn)
        => activeTurn?.Phase is ChatTurnPhase.CreatingRemoteSession or ChatTurnPhase.DispatchingPrompt;

    private static bool IsDispatchedPromptTurn(ActiveTurnState? activeTurn)
        => activeTurn?.Phase is ChatTurnPhase.WaitingForAgent
            or ChatTurnPhase.Thinking
            or ChatTurnPhase.ToolPending
            or ChatTurnPhase.ToolRunning
            or ChatTurnPhase.Responding;

    private void ShowTransientNotificationToast(string message, int durationMs = 3000)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        _transientNotificationCts?.Cancel();
        try { _transientNotificationCts?.Dispose(); } catch { }

        _transientNotificationCts = new CancellationTokenSource();
        var token = _transientNotificationCts.Token;

        _uiDispatcher.Enqueue(() => {
            TransientNotificationMessage = message.Trim();
            ShowTransientNotification = true;
        });

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(durationMs, token).ConfigureAwait(false);
            }
            catch
            {
                return;
            }

            if (token.IsCancellationRequested)
            {
                return;
            }

            _uiDispatcher.Enqueue(() => { ShowTransientNotification = false; });
        });
    }

    public string GetActiveSessionCwdOrDefault()
        => GetSessionCwdOrDefault(CurrentSessionId);

    private string GetSessionCwdOrDefault(string? conversationId)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(conversationId))
            {
                var session = _sessionManager.GetSession(conversationId);
                if (!string.IsNullOrWhiteSpace(session?.Cwd))
                {
                    return session!.Cwd!.Trim();
                }
            }
        }
        catch
        {
        }

        try
        {
            return Environment.CurrentDirectory;
        }
        catch
        {
            return string.Empty;
        }
    }

    [RelayCommand]
    private async Task SetModeAsync(SessionModeViewModel? mode)
    {
        if (mode == null)
            return;

        try
        {
            IsBusy = true;
            ClearError();

            var activeBinding = await ResolveActiveConversationBindingAsync().ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(activeBinding?.RemoteSessionId))
            {
                return;
            }

            if (_chatService != null)
            {
                await ApplyModeSelectionAsync(
                    activeBinding.ConversationId,
                    activeBinding.RemoteSessionId!,
                    mode.ModeId).ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to switch mode");
            SetError($"Failed to switch mode: {ex.Message}");
            await ApplyCurrentStoreProjectionAsync().ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ApplyModeSelectionAsync(
        string conversationId,
        string remoteSessionId,
        string? modeId)
    {
        if (_chatService is null || string.IsNullOrWhiteSpace(remoteSessionId) || string.IsNullOrWhiteSpace(modeId))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(_modeConfigId))
        {
            var setParams = new SessionSetConfigOptionParams(
                remoteSessionId,
                _modeConfigId,
                modeId);
            var response = await _chatService.SetSessionConfigOptionAsync(setParams).ConfigureAwait(true);
            await ApplySessionConfigOptionResponseAsync(
                conversationId,
                response,
                remoteSessionId).ConfigureAwait(true);
            return;
        }

        var modeParams = new SessionSetModeParams
        {
            SessionId = remoteSessionId,
            ModeId = modeId
        };
        var modeResponse = await _chatService.SetSessionModeAsync(modeParams).ConfigureAwait(true);
        await ApplySessionModeResponseAsync(
            conversationId,
            modeResponse,
            remoteSessionId).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task CancelSessionAsync()
    {
        try
        {
            IsBusy = true;
            ClearError();

            var activeBinding = await ResolveActiveConversationBindingAsync().ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(activeBinding?.RemoteSessionId))
            {
                return;
            }

            var cancelParams = new SessionCancelParams
            {
                SessionId = activeBinding.RemoteSessionId!,
                Reason = "User cancelled"
            };

            if (_chatService != null)
            {
                await PreemptivelyCancelTurnAsync().ConfigureAwait(true);
                await CancelPendingPermissionRequestAsync(activeBinding.RemoteSessionId).ConfigureAwait(true);
                await _chatService.CancelSessionAsync(cancelParams);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to cancel session");
            SetError($"Failed to cancel session: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ClearHistory()
    {
        if (!string.IsNullOrWhiteSpace(CurrentSessionId))
        {
            _ = _chatStore.Dispatch(new HydrateConversationAction(
                CurrentSessionId,
                ImmutableList<ConversationMessageSnapshot>.Empty,
                ImmutableList<ConversationPlanEntrySnapshot>.Empty,
                false));
        }

        _chatService?.ClearHistory();
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        try
        {
            IsBusy = true;
            ClearError();
            await _acpConnectionCommands.DisconnectAsync(this);
            await _chatStore.Dispatch(new ResetConversationRuntimeStatesAction()).ConfigureAwait(false);
            _panelStateCoordinator.ClearAskUserRequests();
            PendingAskUserRequest = null;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to disconnect");
            SetError($"Failed to disconnect: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnCurrentPromptChanged(string value)
    {
        if (!_suppressStorePromptProjection)
        {
            _ = _chatStore.Dispatch(new SetDraftTextAction(value));
        }

        SendPromptCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ComposerState));
        OnPropertyChanged(nameof(CanSendPromptUi));
        RefreshSlashStateFromPrompt();
    }

    partial void OnIsPromptInFlightChanged(bool value)
    {
        SendPromptCommand.NotifyCanExecuteChanged();
        NotifyComposerProjectionChanged();
    }

    partial void OnIsPromptSubmitInFlightChanged(bool value)
    {
        SendPromptCommand.NotifyCanExecuteChanged();
        NotifyComposerProjectionChanged();
    }

    partial void OnIsVoiceInputListeningChanged(bool value)
    {
        SendPromptCommand.NotifyCanExecuteChanged();
        NotifyComposerProjectionChanged();
    }

    partial void OnIsVoiceInputSupportedChanged(bool value)
    {
        NotifyComposerProjectionChanged();
    }

    partial void OnPendingAskUserRequestChanged(AskUserRequestViewModel? value)
    {
        if (_observedPendingAskUserRequest != null)
        {
            _observedPendingAskUserRequest.PropertyChanged -= OnPendingAskUserRequestPropertyChanged;
        }

        _observedPendingAskUserRequest = value;
        if (_observedPendingAskUserRequest != null)
        {
            _observedPendingAskUserRequest.PropertyChanged += OnPendingAskUserRequestPropertyChanged;
        }

        SendPromptCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasPendingAskUserRequest));
        OnPropertyChanged(nameof(AskUserPrompt));
        OnPropertyChanged(nameof(AskUserQuestions));
        OnPropertyChanged(nameof(AskUserHasError));
        OnPropertyChanged(nameof(AskUserErrorMessage));
        OnPropertyChanged(nameof(AskUserSubmitCommand));
    }

    private void OnPendingAskUserRequestPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(AskUserHasError));
        OnPropertyChanged(nameof(AskUserErrorMessage));
        OnPropertyChanged(nameof(AskUserSubmitCommand));
    }

    partial void OnIsConnectedChanged(bool value)
    {
        SendPromptCommand.NotifyCanExecuteChanged();
        NotifyComposerProjectionChanged();
    }

    partial void OnIsSessionActiveChanged(bool value)
    {
        SendPromptCommand.NotifyCanExecuteChanged();
        NotifyComposerProjectionChanged();
        RefreshCurrentSessionDisplayName();
        OnPropertyChanged(nameof(ShouldShowActiveConversationRoot));
        OnPropertyChanged(nameof(ShouldLoadActiveConversationRoot));
        OnPropertyChanged(nameof(ShouldShowSessionHeader));
        OnPropertyChanged(nameof(ShouldShowTranscriptSurface));
        OnPropertyChanged(nameof(ShouldLoadTranscriptSurface));
        OnPropertyChanged(nameof(ShouldShowConversationInputSurface));
    }
}
