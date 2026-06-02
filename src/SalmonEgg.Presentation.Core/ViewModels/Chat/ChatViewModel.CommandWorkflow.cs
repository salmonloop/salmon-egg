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
using SalmonEgg.Presentation.Core.ViewModels.Chat.Selectors;
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
            var sessionParams = await CreateSessionNewParamsAsync(CancellationToken.None).ConfigureAwait(false);
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

    private async Task<SessionNewParams> CreateSessionNewParamsAsync(CancellationToken cancellationToken)
        => new(
            GetActiveSessionCwdOrDefault(),
            McpServerJsonConverter.CloneServers(
                await ResolveCurrentMcpServersAsync(cancellationToken).ConfigureAwait(false)));

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
            VoiceInputTransportState: _voiceInputTransportState,
            HasPendingAskUserRequest: PendingAskUserRequest is not null,
            ShouldShowLoadingOverlayPresenter: ShouldShowLoadingOverlayPresenter,
            IsSessionActive: IsSessionActive,
            HasChatService: _chatService is not null,
            IsInitialized: IsInitialized,
            HasCurrentSessionId: !string.IsNullOrWhiteSpace(CurrentSessionId),
            HasPromptText: !string.IsNullOrWhiteSpace(CurrentPrompt),
            IsVoiceInputSupported: IsVoiceInputSupported));

    private VoiceInputUiState ResolveVoiceInputUiState()
    {
        var composerState = ResolveInputState();
        return _voiceInputUiStatePresenter.Present(new VoiceInputUiStateInput(
            IsVoiceInputSupported: IsVoiceInputSupported,
            IsVoiceInputListening: IsVoiceInputListening,
            TransportState: _voiceInputTransportState,
            CanStartVoiceInput: composerState.CanStartVoiceInput,
            CanStopVoiceInput: composerState.CanStopVoiceInput));
    }

    private SelectorProjectionResult ResolveChatModeSelectorProjection()
    {
        var identity = BuildModeSelectorIdentity(
            SelectedProfileId,
            ConnectionInstanceId,
            GetActiveSessionCwdOrDefault(),
            version: AvailableModes.Count);
        var policy = _modeSelectorPolicy.Project(new ModeSelectorPolicyInput(
            Identity: identity,
            CurrentIdentity: identity,
            Modes: AvailableModes,
            SelectedModeId: SelectedMode?.ModeId,
            IsAuthoritative: IsSessionActive,
            IsLoading: IsConnecting || IsInitializing,
            HasError: HasConnectionError,
            HasModeCapabilitySignal: AvailableModes.Count > 0,
            Labels: ResolveModeSelectorPlaceholderLabels()));

        return _selectorProjectionPresenter.Present(new SelectorProjectionInput(
            ComposerSelectorKind.Mode,
            policy.RealItems,
            policy.SelectedSemanticValue,
            policy.Placeholder,
            policy.ReplaceSelectionWithPlaceholder,
            policy.DisableRealItems,
            policy.SelectorEnabled && AreComposerToolsEnabled));
    }

    private static string BuildModeSelectorIdentity(
        string? profileId,
        string? connectionInstanceId,
        string? cwd,
        long version)
        => string.Join(
            "|",
            profileId ?? string.Empty,
            connectionInstanceId ?? string.Empty,
            cwd ?? string.Empty,
            version.ToString(CultureInfo.InvariantCulture));

    private ModeSelectorPlaceholderLabels ResolveModeSelectorPlaceholderLabels()
        => new(
            Unresolved: Localize("Selector_Mode_Unresolved", "模式尚未就绪"),
            Loading: Localize("Selector_Mode_Loading", "正在加载模式..."),
            Error: Localize("Selector_Mode_Error", "模式不可用"),
            Default: Localize("Selector_Mode_Default", "默认模式"));

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

    private ChatAskUserState ResolveAskUserState()
        => _askUserStatePresenter.Present(PendingAskUserRequest, _emptyAskUserQuestions);

    private ChatPlanPanelState ResolvePlanPanelState()
        => _planPanelStatePresenter.Present(ShowPlanPanel, PlanEntries.Count);

    private bool CanSendPrompt() => ResolveInputState().CanSendPrompt;

    [RelayCommand]
    private void SelectChatModeDisplay(ComposerSelectorItemViewModel? item)
    {
        if (item is null
            || item.Kind != ComposerSelectorKind.Mode
            || item.IsPlaceholder
            || !item.IsSelectable
            || string.IsNullOrWhiteSpace(item.SemanticValue))
        {
            return;
        }

        var current = ChatModeSelectorItems.FirstOrDefault(candidate =>
            string.Equals(candidate.SemanticValue, item.SemanticValue, StringComparison.Ordinal)
            && string.Equals(candidate.Identity, item.Identity, StringComparison.Ordinal));
        if (current is null)
        {
            return;
        }

        var mode = AvailableModes.FirstOrDefault(candidate =>
            string.Equals(candidate.ModeId, item.SemanticValue, StringComparison.Ordinal));
        if (mode is not null)
        {
            SetModeCommand.Execute(mode);
        }
    }

    [RelayCommand]
    private Task StartVoiceInputAsync()
        => StartVoiceInputCoreAsync(
            requestAuthorizationHelpOnDenied: true,
            isAuthorizationResumeAttempt: false);

    private async Task StartVoiceInputCoreAsync(
        bool requestAuthorizationHelpOnDenied,
        bool isAuthorizationResumeAttempt)
    {
        if (!CanStartVoiceInput)
        {
            if (isAuthorizationResumeAttempt)
            {
                _voiceInputAuthorizationRetryState = VoiceInputAuthorizationRetryState.WaitingForComposerReady;
            }

            return;
        }

        if (!isAuthorizationResumeAttempt)
        {
            ClearPendingVoiceInputAuthorizationRetry();
        }

        VoiceInputErrorMessage = null;

        TryDisposeVoiceInputCts();
        _voiceInputCts = new CancellationTokenSource();
        string? requestId = null;

        try
        {
            SetVoiceInputTransportState(VoiceInputTransportState.Authorizing);
            var permission = await _voiceInputService.EnsurePermissionAsync(_voiceInputCts.Token);
            if (!permission.IsGranted)
            {
                Logger.LogWarning(
                    "Voice input permission denied. Status={Status} RequiresAuthorization={RequiresAuthorization}",
                    permission.Status,
                    permission.RequiresAuthorization);
                var message = VoiceInputErrorMessageSanitizer.Normalize(
                    permission.Message,
                    "Voice input permission check failed.");
                if (permission.RequiresAuthorization && requestAuthorizationHelpOnDenied)
                {
                    await RequestVoiceInputAuthorizationHelpAndArmRetryAsync(_voiceInputCts.Token).ConfigureAwait(false);
                }
                else
                {
                    ClearPendingVoiceInputAuthorizationRetry();
                }

                VoiceInputErrorMessage = message;
                ShowTransientNotificationToast(message);
                SetVoiceInputTransportState(VoiceInputTransportState.Idle);
                TryDisposeVoiceInputCts();
                return;
            }

            SetVoiceInputTransportState(VoiceInputTransportState.Starting);
            requestId = Guid.NewGuid().ToString("N");
            var languageTag = ResolveVoiceInputLanguageTag();
            _transportVoiceInputRequestId = requestId;
            _activeVoiceInputRequestId = requestId;
            _voiceInputBasePrompt = CurrentPrompt ?? string.Empty;
            Logger.LogInformation(
                "Voice input start requested. RequestId={RequestId} LanguageTag={LanguageTag}",
                requestId,
                languageTag);

            var options = new VoiceInputSessionOptions(
                requestId,
                languageTag,
                EnablePartialResults: true,
                PreferOffline: false);

            await _voiceInputService.StartAsync(options, _voiceInputCts.Token);
            ClearPendingVoiceInputAuthorizationRetry();
            IsVoiceInputListening = true;
            Logger.LogInformation(
                "Voice input recognizer ready. RequestId={RequestId} LanguageTag={LanguageTag}",
                requestId,
                languageTag);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is expected when voice input is quickly superseded.
            if (requestId is not null && IsCurrentVoiceInputRequest(requestId))
            {
                IsVoiceInputListening = false;
                _activeVoiceInputRequestId = null;
                ResetVoiceInputDiagnosticsState(requestId);
            }

            if (requestId is not null
                && IsCurrentVoiceTransportRequest(requestId)
                && _voiceInputTransportState == VoiceInputTransportState.Starting)
            {
                ClearVoiceInputTransport(requestId, disposeCts: true);
            }
            else if (requestId is null
                && _voiceInputTransportState == VoiceInputTransportState.Authorizing)
            {
                SetVoiceInputTransportState(VoiceInputTransportState.Idle);
                TryDisposeVoiceInputCts();
            }
        }
        catch (Exception ex)
        {
            if (ex is VoiceInputStartFailureException startFailure && startFailure.RequiresAuthorization)
            {
                if (requestAuthorizationHelpOnDenied)
                {
                    await RequestVoiceInputAuthorizationHelpAndArmRetryAsync(_voiceInputCts.Token).ConfigureAwait(false);
                }
                else
                {
                    ClearPendingVoiceInputAuthorizationRetry();
                }
            }
            else
            {
                ClearPendingVoiceInputAuthorizationRetry();
            }

            var message = VoiceInputErrorMessageSanitizer.Normalize(ex.Message, "Voice input failed.");
            Logger.LogWarning(
                ex,
                "Voice input start failed before recognizer ready. RequestId={RequestId}",
                requestId);
            VoiceInputErrorMessage = message;
            ShowTransientNotificationToast(
                string.Equals(message, "Voice input failed.", StringComparison.Ordinal)
                    ? message
                    : $"Voice input failed: {message}");
            if (requestId is not null && IsCurrentVoiceInputRequest(requestId))
            {
                IsVoiceInputListening = false;
                _activeVoiceInputRequestId = null;
                _voiceInputBasePrompt = CurrentPrompt ?? string.Empty;
                ResetVoiceInputDiagnosticsState(requestId);
            }

            if (requestId is not null
                && IsCurrentVoiceTransportRequest(requestId)
                && _voiceInputTransportState == VoiceInputTransportState.Starting)
            {
                ClearVoiceInputTransport(requestId, disposeCts: true);
            }
            else if (requestId is null
                && _voiceInputTransportState == VoiceInputTransportState.Authorizing)
            {
                SetVoiceInputTransportState(VoiceInputTransportState.Idle);
                TryDisposeVoiceInputCts();
            }
        }
        finally
        {
            if (requestId is not null
                && IsCurrentVoiceTransportRequest(requestId)
                && _voiceInputTransportState == VoiceInputTransportState.Starting)
            {
                SetVoiceInputTransportState(VoiceInputTransportState.Idle);
            }
        }
    }

    private async Task RequestVoiceInputAuthorizationHelpAndArmRetryAsync(CancellationToken cancellationToken)
        => await RequestVoiceInputAuthorizationHelpAndArmRetryAsync(
            cancellationToken,
            CurrentSessionId,
            SelectedProfileId,
            ConnectionInstanceId).ConfigureAwait(false);

    private async Task RequestVoiceInputAuthorizationHelpAndArmRetryAsync(
        CancellationToken cancellationToken,
        string? conversationId,
        string? profileId,
        string? connectionInstanceId)
    {
        var opened = false;
        try
        {
            opened = await _voiceInputService.TryRequestAuthorizationHelpAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            opened = false;
        }

        if (opened)
        {
            ArmPendingVoiceInputAuthorizationRetry(conversationId, profileId, connectionInstanceId);
        }
        else
        {
            ClearPendingVoiceInputAuthorizationRetry();
        }
    }

    private void ArmPendingVoiceInputAuthorizationRetry()
        => ArmPendingVoiceInputAuthorizationRetry(CurrentSessionId, SelectedProfileId, ConnectionInstanceId);

    private void ArmPendingVoiceInputAuthorizationRetry(
        string? conversationId,
        string? profileId,
        string? connectionInstanceId)
    {
        _voiceInputAuthorizationRetryState = VoiceInputAuthorizationRetryState.WaitingForActivation;
        _voiceInputAuthorizationRetryConversationId = conversationId;
        _voiceInputAuthorizationRetryProfileId = profileId;
        _voiceInputAuthorizationRetryConnectionInstanceId = connectionInstanceId;
        Logger.LogInformation("Voice input authorization retry armed.");
    }

    private void ClearPendingVoiceInputAuthorizationRetry()
    {
        _voiceInputAuthorizationRetryState = VoiceInputAuthorizationRetryState.None;
        _voiceInputAuthorizationRetryConversationId = null;
        _voiceInputAuthorizationRetryProfileId = null;
        _voiceInputAuthorizationRetryConnectionInstanceId = null;
    }

    private void OnApplicationActivated(object? sender, EventArgs e)
    {
        if (_voiceInputAuthorizationRetryState != VoiceInputAuthorizationRetryState.WaitingForActivation || _disposed)
        {
            return;
        }

        _voiceInputAuthorizationRetryState = VoiceInputAuthorizationRetryState.WaitingForComposerReady;
        Logger.LogInformation(
            "Voice input authorization retry observed app activation. CanStartVoiceInput={CanStartVoiceInput} IsBusy={IsBusy} HasPendingAskUserRequest={HasPendingAskUserRequest} ShouldShowLoadingOverlayPresenter={ShouldShowLoadingOverlayPresenter} IsVoiceInputTransportBusy={IsVoiceInputTransportBusy}",
            CanStartVoiceInput,
            IsBusy,
            HasPendingAskUserRequest,
            ShouldShowLoadingOverlayPresenter,
            IsVoiceInputTransportBusy);
        TryResumeVoiceInputAfterAuthorizationIfReady();
    }

    private void TryResumeVoiceInputAfterAuthorizationIfReady()
    {
        if (_voiceInputAuthorizationRetryState != VoiceInputAuthorizationRetryState.WaitingForComposerReady || _disposed)
        {
            return;
        }

        if (!DoesPendingVoiceInputAuthorizationRetryMatchCurrentIdentity())
        {
            Logger.LogInformation(
                "Voice input authorization retry was discarded because the conversation identity changed. RetryConversationId={RetryConversationId} CurrentConversationId={CurrentConversationId} RetryProfileId={RetryProfileId} CurrentProfileId={CurrentProfileId} RetryConnectionInstanceId={RetryConnectionInstanceId} CurrentConnectionInstanceId={CurrentConnectionInstanceId}",
                _voiceInputAuthorizationRetryConversationId,
                CurrentSessionId,
                _voiceInputAuthorizationRetryProfileId,
                SelectedProfileId,
                _voiceInputAuthorizationRetryConnectionInstanceId,
                ConnectionInstanceId);
            ClearPendingVoiceInputAuthorizationRetry();
            return;
        }

        if (!CanStartVoiceInput)
        {
            return;
        }

        _voiceInputAuthorizationRetryState = VoiceInputAuthorizationRetryState.Starting;
        Logger.LogInformation("Voice input authorization retry is starting automatically.");
        _ = PostToUiAsync(() => StartVoiceInputCoreAsync(
            requestAuthorizationHelpOnDenied: false,
            isAuthorizationResumeAttempt: true));
    }

    private bool DoesPendingVoiceInputAuthorizationRetryMatchCurrentIdentity()
        => string.Equals(_voiceInputAuthorizationRetryConversationId, CurrentSessionId, StringComparison.Ordinal)
            && string.Equals(_voiceInputAuthorizationRetryProfileId, SelectedProfileId, StringComparison.Ordinal)
            && string.Equals(_voiceInputAuthorizationRetryConnectionInstanceId, ConnectionInstanceId, StringComparison.Ordinal);

    [RelayCommand]
    private async Task StopVoiceInputAsync()
    {
        var requestId = _transportVoiceInputRequestId;
        if (string.IsNullOrWhiteSpace(requestId))
        {
            return;
        }

        if (!IsVoiceInputListening
            && _voiceInputTransportState != VoiceInputTransportState.Starting)
        {
            return;
        }

        SetVoiceInputTransportState(VoiceInputTransportState.Stopping);
        Logger.LogInformation(
            "Voice input stop requested. RequestId={RequestId} WasListening={WasListening}",
            requestId,
            IsVoiceInputListening);

        try
        {
            await _voiceInputService.StopAsync();
            Logger.LogInformation(
                "Voice input service stop completed in view model. RequestId={RequestId}",
                requestId);
            if (IsCurrentVoiceInputRequest(requestId))
            {
                IsVoiceInputListening = false;
                _activeVoiceInputRequestId = null;
                _voiceInputBasePrompt = CurrentPrompt ?? string.Empty;
                ResetVoiceInputDiagnosticsState(requestId);
            }

            ClearVoiceInputTransport(requestId, disposeCts: true);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is expected when stopping a live recognition request.
            if (IsCurrentVoiceInputRequest(requestId))
            {
                IsVoiceInputListening = false;
                _activeVoiceInputRequestId = null;
                ResetVoiceInputDiagnosticsState(requestId);
            }

            if (IsCurrentVoiceTransportRequest(requestId))
            {
                ClearVoiceInputTransport(requestId, disposeCts: true);
            }
        }
        catch (Exception ex)
        {
            RestoreVoiceInputFrontSession(requestId);
            var message = VoiceInputErrorMessageSanitizer.Normalize(ex.Message, "Failed to stop voice input.");
            VoiceInputErrorMessage = message;
            ShowTransientNotificationToast(
                string.Equals(message, "Failed to stop voice input.", StringComparison.Ordinal)
                    ? message
                    : $"Failed to stop voice input: {message}");
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

        var text = result.Text;

        await PostToUiAsync(() =>
        {
            if (!IsCurrentVoiceInputRequest(result.RequestId))
            {
                return;
            }

            CurrentPrompt = MergeVoiceInputText(text);
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

        var text = result.Text;

        Logger.LogInformation(
            "Voice input final result received. RequestId={RequestId} TextLength={TextLength}",
            result.RequestId,
            text.Length);

        await PostToUiAsync(() =>
        {
            if (!IsCurrentVoiceInputRequest(result.RequestId))
            {
                return;
            }

            CurrentPrompt = MergeVoiceInputText(text);
            _voiceInputBasePrompt = CurrentPrompt ?? string.Empty;
        }).ConfigureAwait(false);
    }

    private void OnVoiceInputSessionEnded(object? sender, VoiceInputSessionEndedResult result)
        => _ = HandleVoiceInputSessionEndedAsync(result);

    private async Task HandleVoiceInputSessionEndedAsync(VoiceInputSessionEndedResult result)
    {
        Logger.LogInformation("Voice input session ended. RequestId={RequestId}", result.RequestId);
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
            ResetVoiceInputDiagnosticsState(result.RequestId);
        }).ConfigureAwait(false);
    }

    private void OnVoiceInputErrorOccurred(object? sender, VoiceInputErrorResult result)
        => _ = HandleVoiceInputErrorAsync(result);

    private async Task HandleVoiceInputErrorAsync(VoiceInputErrorResult result)
    {
        Logger.LogWarning(
            "Voice input session error. RequestId={RequestId} ErrorCode={ErrorCode} RequiresAuthorization={RequiresAuthorization} Message={Message}",
            result.RequestId,
            result.ErrorCode,
            result.RequiresAuthorization,
            result.Message);
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

        var retryConversationId = CurrentSessionId;
        var retryProfileId = SelectedProfileId;
        var retryConnectionInstanceId = ConnectionInstanceId;

        await PostToUiAsync(() =>
        {
            if (!IsCurrentVoiceInputRequest(result.RequestId))
            {
                return;
            }

            var message = VoiceInputErrorMessageSanitizer.Normalize(result.Message, "Voice input failed.");
            VoiceInputErrorMessage = message;
            IsVoiceInputListening = false;
            _activeVoiceInputRequestId = null;
            _voiceInputBasePrompt = CurrentPrompt ?? string.Empty;
            ResetVoiceInputDiagnosticsState(result.RequestId);
            ShowTransientNotificationToast(message);
        }).ConfigureAwait(false);

        if (result.RequiresAuthorization)
        {
            await RequestVoiceInputAuthorizationHelpAndArmRetryAsync(
                CancellationToken.None,
                retryConversationId,
                retryProfileId,
                retryConnectionInstanceId).ConfigureAwait(false);
        }
        else
        {
            ClearPendingVoiceInputAuthorizationRetry();
        }
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

    private void ResetVoiceInputDiagnosticsState(string requestId)
    {
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

        if (string.Equals(SelectedMode?.ModeId, mode.ModeId, StringComparison.Ordinal))
        {
            return;
        }

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
        NotifyComposerProjectionChanged();
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
