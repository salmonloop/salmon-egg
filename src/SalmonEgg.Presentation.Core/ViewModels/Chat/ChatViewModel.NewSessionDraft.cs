using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Domain.Models.Mcp;
using SalmonEgg.Domain.Models.Protocol;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.ViewModels.Chat;

namespace SalmonEgg.Presentation.ViewModels.Chat;

public partial class ChatViewModel
{
    private static readonly TimeSpan NewSessionDraftProfileWaitDelay = TimeSpan.FromMilliseconds(50);
    private readonly object _newSessionDraftRequestSync = new();
    private readonly Dictionary<NewSessionDraftRequestKey, Task> _inFlightNewSessionDraftRequests = new();
    private NewSessionDraftRequestKey? _desiredNewSessionDraftRequestKey;
    private CancellationToken _desiredNewSessionDraftCancellationToken;

    public async Task EnsureNewSessionDraftAsync(string? cwd, CancellationToken cancellationToken = default)
        => await EnsureNewSessionDraftForProfileAsync(cwd, requiredProfileId: null, cancellationToken).ConfigureAwait(false);

    public async Task EnsureNewSessionDraftForProfileAsync(
        string? cwd,
        string? requiredProfileId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_disposed)
        {
            return;
        }

        Task? inFlightRequestTask = null;
        await _newSessionDraftGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await TryAutoConnectAsync(cancellationToken).ConfigureAwait(false);
            var connectionState = await _chatConnectionStore.GetCurrentStateAsync().ConfigureAwait(false);
            var chatService = _chatService;
            var normalizedRequiredProfileId = NormalizeNewSessionDraftProfileId(requiredProfileId);
            var projectedProfileId = ResolveProjectedProfileId(connectionState);
            var foregroundProfileId = connectionState.ForegroundTransportProfileId;
            var profileId = normalizedRequiredProfileId ?? projectedProfileId ?? SelectedProfileId;
            var connectionInstanceId = connectionState.ConnectionInstanceId ?? ConnectionInstanceId;

            if (!string.IsNullOrWhiteSpace(normalizedRequiredProfileId)
                && !string.Equals(foregroundProfileId, normalizedRequiredProfileId, StringComparison.Ordinal))
            {
                if (connectionState.NewSessionDraft is not null
                    && chatService is { IsConnected: true, IsInitialized: true })
                {
                    await CloseNewSessionDraftQuietlyAsync(chatService, connectionState.NewSessionDraft, cancellationToken)
                        .ConfigureAwait(false);
                }

                await ClearNewSessionDraftStateAsync().ConfigureAwait(false);
                QueueRequiredProfileConnectionIfForegroundIsStale(
                    normalizedRequiredProfileId,
                    connectionState);
                var waitOutcome = await WaitForRequiredProfileIdentityAsync(
                    normalizedRequiredProfileId,
                    cancellationToken).ConfigureAwait(false);
                if (waitOutcome.Status == RequiredProfileIdentityWaitStatus.ConnectionFailed)
                {
                    await PublishRequiredProfileConnectionFailureDraftAsync(
                            normalizedRequiredProfileId,
                            waitOutcome.ErrorMessage)
                        .ConfigureAwait(false);
                }

                if (waitOutcome.Status != RequiredProfileIdentityWaitStatus.Ready)
                {
                    return;
                }

                connectionState = await _chatConnectionStore.GetCurrentStateAsync().ConfigureAwait(false);
                chatService = _chatService;
                profileId = normalizedRequiredProfileId;
                connectionInstanceId = connectionState.ConnectionInstanceId ?? ConnectionInstanceId;
                if (chatService is null
                    || string.IsNullOrWhiteSpace(profileId)
                    || string.IsNullOrWhiteSpace(connectionInstanceId))
                {
                    await ClearNewSessionDraftStateAsync().ConfigureAwait(false);
                    return;
                }
            }

            var authoritativeConnection = await ResolveAuthoritativeNewSessionDraftConnectionAsync(
                normalizedRequiredProfileId,
                cancellationToken).ConfigureAwait(false);
            if (authoritativeConnection is null
                || authoritativeConnection.Value.ChatService is not { IsConnected: true, IsInitialized: true }
                || string.IsNullOrWhiteSpace(authoritativeConnection.Value.ProfileId)
                || string.IsNullOrWhiteSpace(authoritativeConnection.Value.ConnectionInstanceId))
            {
                await ClearNewSessionDraftStateAsync().ConfigureAwait(false);
                return;
            }

            chatService = authoritativeConnection.Value.ChatService;
            profileId = authoritativeConnection.Value.ProfileId;
            connectionInstanceId = authoritativeConnection.Value.ConnectionInstanceId;

            var profile = ResolveNewSessionDraftProfile(profileId);
            var cwdResolution = AcpSessionNewCwdResolver.Resolve(
                cwd,
                profile,
                _preferences.AgentRemoteDirectories);

            if (!cwdResolution.IsSuccess || string.IsNullOrWhiteSpace(cwdResolution.Cwd))
            {
                Logger.LogInformation(
                    "ACP new-session draft cwd resolution failed. profileId={ProfileId} connectionInstanceId={ConnectionInstanceId} error={Error}",
                    profileId,
                    connectionInstanceId,
                    cwdResolution.ErrorMessage ?? AcpSessionNewCwdResolver.MissingRemoteCwdMessage);
                var failed = connectionState.NewSessionDraft is null
                    ? CreateNewSessionDraftState(
                        profileId ?? string.Empty,
                        string.Empty,
                        remoteSessionId: null,
                        connectionInstanceId ?? string.Empty,
                        NewSessionDraftPhase.Faulted,
                        version: 0,
                        AcpSessionUpdateDelta.Empty,
                        isConfigAuthoritative: false) with
                    {
                        Error = cwdResolution.ErrorMessage ?? AcpSessionNewCwdResolver.MissingRemoteCwdMessage
                    }
                    : connectionState.NewSessionDraft with
                    {
                        Phase = NewSessionDraftPhase.Faulted,
                        Error = cwdResolution.ErrorMessage ?? AcpSessionNewCwdResolver.MissingRemoteCwdMessage
                    };
                await _chatConnectionStore.Dispatch(new SetNewSessionDraftAction(failed)).ConfigureAwait(false);
                await ApplyNewSessionDraftProjectionAsync(
                    await _chatConnectionStore.GetCurrentStateAsync().ConfigureAwait(false)).ConfigureAwait(false);
                return;
            }

            var normalizedCwd = cwdResolution.Cwd;
            var requestKey = new NewSessionDraftRequestKey(
                profileId ?? string.Empty,
                connectionInstanceId ?? string.Empty,
                normalizedCwd);

            SetDesiredNewSessionDraftRequest(requestKey, cancellationToken);

            var existingDraft = connectionState.NewSessionDraft;
            if (IsReusableNewSessionDraft(existingDraft, profileId!, connectionInstanceId!, normalizedCwd))
            {
                await ApplyNewSessionDraftProjectionAsync(connectionState).ConfigureAwait(false);
                return;
            }

            if (TryGetInFlightNewSessionDraftRequestTask(requestKey, out inFlightRequestTask))
            {
                Logger.LogInformation(
                    "Joining in-flight ACP new-session draft request. profileId={ProfileId} connectionInstanceId={ConnectionInstanceId} cwd={Cwd}",
                    profileId,
                    connectionInstanceId,
                    normalizedCwd);
            }
            else
            {
                if (existingDraft is not null)
                {
                    await CloseNewSessionDraftQuietlyAsync(chatService, existingDraft, cancellationToken).ConfigureAwait(false);
                    await ClearNewSessionDraftStateAsync(clearDesiredRequest: false).ConfigureAwait(false);
                }

                var requestVersion = checked((existingDraft?.Version ?? 0) + 1);
                var creatingDraft = CreateNewSessionDraftState(
                    profileId!,
                    normalizedCwd,
                    remoteSessionId: null,
                    connectionInstanceId!,
                    NewSessionDraftPhase.Creating,
                    requestVersion,
                    AcpSessionUpdateDelta.Empty,
                    isConfigAuthoritative: false);
                await _chatConnectionStore.Dispatch(new SetNewSessionDraftAction(creatingDraft)).ConfigureAwait(false);
                await ApplyNewSessionDraftProjectionAsync(
                    await _chatConnectionStore.GetCurrentStateAsync().ConfigureAwait(false)).ConfigureAwait(false);

                var request = new PendingNewSessionDraftRequest(
                    requestKey,
                    profileId!,
                    connectionInstanceId!,
                    normalizedCwd,
                    requestVersion,
                    chatService,
                    cancellationToken);
                inFlightRequestTask = StartInFlightNewSessionDraftRequest(request);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Failed to prepare ACP new-session draft.");
            var connectionState = await _chatConnectionStore.GetCurrentStateAsync().ConfigureAwait(false);
            var failed = connectionState.NewSessionDraft is null
                ? null
                : connectionState.NewSessionDraft with
                {
                    Phase = NewSessionDraftPhase.Faulted,
                    Error = ex.Message
                };
            await _chatConnectionStore.Dispatch(new SetNewSessionDraftAction(failed)).ConfigureAwait(false);
            await ApplyNewSessionDraftProjectionAsync(
                await _chatConnectionStore.GetCurrentStateAsync().ConfigureAwait(false)).ConfigureAwait(false);
        }
        finally
        {
            _newSessionDraftGate.Release();
        }

        if (inFlightRequestTask is not null)
        {
            await inFlightRequestTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<RequiredProfileIdentityWaitOutcome> WaitForRequiredProfileIdentityAsync(
        string requiredProfileId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var timeout = ResolveRequiredProfileIdentityWaitTimeout(requiredProfileId);
        var timeoutAt = DateTime.UtcNow.Add(timeout);
        var observedRequiredIntent = false;
        while (DateTime.UtcNow < timeoutAt)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var state = await _chatConnectionStore.GetCurrentStateAsync().ConfigureAwait(false);
            if (string.Equals(state.SelectedProfileIntentId, requiredProfileId, StringComparison.Ordinal))
            {
                observedRequiredIntent = true;
            }

            if (!string.IsNullOrWhiteSpace(state.SelectedProfileIntentId)
                && !string.Equals(state.SelectedProfileIntentId, requiredProfileId, StringComparison.Ordinal))
            {
                if (observedRequiredIntent)
                {
                    Logger.LogInformation(
                        "New session draft preparation aborted because the selected profile intent changed. requiredProfileId={RequiredProfileId} currentSelectedProfileId={CurrentSelectedProfileId} currentForegroundProfileId={CurrentForegroundProfileId} currentPhase={Phase}",
                        requiredProfileId,
                        state.SelectedProfileIntentId,
                        state.ForegroundTransportProfileId,
                        state.Phase);
                    return new RequiredProfileIdentityWaitOutcome(
                        RequiredProfileIdentityWaitStatus.Superseded,
                        ErrorMessage: null);
                }
            }

            if (!string.IsNullOrWhiteSpace(requiredProfileId)
                && _authoritativeConnectionResolver.TryResolveReadyForegroundConnection(
                    _chatService,
                    state,
                    requiredProfileId,
                    out var snapshot)
                && !string.IsNullOrWhiteSpace(snapshot.ProfileId)
                && !string.IsNullOrWhiteSpace(snapshot.ConnectionInstanceId))
            {
                return new RequiredProfileIdentityWaitOutcome(
                    RequiredProfileIdentityWaitStatus.Ready,
                    ErrorMessage: null);
            }

            if (state.Phase is ConnectionPhase.Disconnected or ConnectionPhase.Error)
            {
                Logger.LogInformation(
                    "New session draft preparation aborted while waiting for required profile identity. requiredProfileId={RequiredProfileId} currentSelectedProfileId={CurrentSelectedProfileId} currentForegroundProfileId={CurrentForegroundProfileId} currentPhase={Phase}",
                    requiredProfileId,
                    state.SelectedProfileIntentId,
                    state.ForegroundTransportProfileId,
                    state.Phase);
                return new RequiredProfileIdentityWaitOutcome(
                    RequiredProfileIdentityWaitStatus.ConnectionFailed,
                    state.Error);
            }

            await Task.Delay(NewSessionDraftProfileWaitDelay, cancellationToken).ConfigureAwait(false);
        }

        Logger.LogWarning(
            "Timed out waiting for required profile identity before creating ACP new-session draft. requiredProfileId={RequiredProfileId} timeoutSeconds={TimeoutSeconds}",
            requiredProfileId,
            timeout.TotalSeconds);
        return new RequiredProfileIdentityWaitOutcome(
            RequiredProfileIdentityWaitStatus.TimedOut,
            ErrorMessage: null);
    }

    private TimeSpan ResolveRequiredProfileIdentityWaitTimeout(string requiredProfileId)
    {
        var profile = ResolveNewSessionDraftProfile(requiredProfileId);
        return AcpConnectionTimeoutPolicy.ResolveTimeout(profile?.ConnectionTimeout ?? 0);
    }

    private async Task<AcpAuthoritativeConnectionSnapshot?> ResolveAuthoritativeNewSessionDraftConnectionAsync(
        string? requiredProfileId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var state = await _chatConnectionStore.GetCurrentStateAsync().ConfigureAwait(false);
        if (_authoritativeConnectionResolver.TryResolveReadyForegroundConnection(
                _chatService,
                state,
                requiredProfileId,
                out var snapshot)
            && !string.IsNullOrWhiteSpace(snapshot.ProfileId)
            && !string.IsNullOrWhiteSpace(snapshot.ConnectionInstanceId))
        {
            return snapshot;
        }

        return null;
    }

    private void QueueRequiredProfileConnectionIfForegroundIsStale(
        string requiredProfileId,
        ChatConnectionState connectionState)
    {
        if (connectionState.Phase != ConnectionPhase.Connected
            || string.Equals(connectionState.ForegroundTransportProfileId, requiredProfileId, StringComparison.Ordinal))
        {
            return;
        }

        var profile = ResolveNewSessionDraftProfile(requiredProfileId);
        if (profile is null
            || !string.Equals(profile.Id, requiredProfileId, StringComparison.Ordinal))
        {
            return;
        }

        QueueSelectedProfileConnection(profile);
    }

    public async Task DiscardNewSessionDraftAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _newSessionDraftGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var connectionState = await _chatConnectionStore.GetCurrentStateAsync().ConfigureAwait(false);
            var draft = connectionState.NewSessionDraft;
            if (draft is null)
            {
                await ApplyNewSessionDraftProjectionAsync(connectionState).ConfigureAwait(false);
                return;
            }

            if (_chatService is { IsConnected: true, IsInitialized: true } chatService)
            {
                await CloseNewSessionDraftQuietlyAsync(chatService, draft, cancellationToken).ConfigureAwait(false);
            }

            await ClearNewSessionDraftStateAsync().ConfigureAwait(false);
        }
        finally
        {
            _newSessionDraftGate.Release();
        }
    }

    public async Task PromoteNewSessionDraftForLaunchAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _newSessionDraftGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var connectionState = await _chatConnectionStore.GetCurrentStateAsync().ConfigureAwait(false);
            var draft = connectionState.NewSessionDraft;
            if (draft is null
                || draft.Phase != NewSessionDraftPhase.Ready
                || string.IsNullOrWhiteSpace(draft.RemoteSessionId)
                || string.IsNullOrWhiteSpace(CurrentSessionId))
            {
                return;
            }

            if (!IsCurrentNewSessionDraft(connectionState, draft))
            {
                await ClearNewSessionDraftStateAsync().ConfigureAwait(false);
                return;
            }

            var promotingDraft = draft with { Phase = NewSessionDraftPhase.Promoting };
            await _chatConnectionStore.Dispatch(new SetNewSessionDraftAction(promotingDraft)).ConfigureAwait(false);

            var bindingResult = await _bindingCommands
                .UpdateBindingAsync(CurrentSessionId!, draft.RemoteSessionId!, draft.ProfileId)
                .ConfigureAwait(false);
            if (bindingResult.Status is not BindingUpdateStatus.Success)
            {
                throw new InvalidOperationException(
                    $"Failed to promote ACP new-session draft ({bindingResult.Status}): {bindingResult.ErrorMessage ?? "UnknownError"}");
            }

            SetConversationConfigAuthority(CurrentSessionId!, draft.IsConfigAuthoritative);
            await _chatStore.Dispatch(new MergeConversationSessionStateAction(
                CurrentSessionId,
                draft.AvailableModes,
                draft.SelectedModeId,
                HasSelectedModeId: !string.IsNullOrWhiteSpace(draft.SelectedModeId) || draft.AvailableModes.Count == 0,
                draft.ConfigOptions,
                draft.ShowConfigOptionsPanel,
                draft.AvailableCommands,
                ConversationSessionInfoSnapshots.Clone(draft.SessionInfo))).ConfigureAwait(true);

            if (draft.SessionInfo is not null)
            {
                await PersistProjectedSessionInfoSnapshotAsync(CurrentSessionId!).ConfigureAwait(true);
            }

            if (_chatService is AcpChatServiceAdapter adapter)
            {
                adapter.MarkHydrated();
            }

            await ClearNewSessionDraftStateAsync().ConfigureAwait(false);
        }
        finally
        {
            _newSessionDraftGate.Release();
        }
    }

    private void QueueNewSessionDraftModeSelection(SessionModeViewModel? mode)
    {
        try
        {
            _newSessionDraftModeSelectionCts?.Cancel();
            _newSessionDraftModeSelectionCts?.Dispose();
        }
        catch
        {
        }

        if (mode is null || string.IsNullOrWhiteSpace(mode.ModeId))
        {
            return;
        }

        _newSessionDraftModeSelectionCts = new CancellationTokenSource();
        var token = _newSessionDraftModeSelectionCts.Token;
        _ = SetNewSessionDraftModeAsync(mode.ModeId, token);
    }

    private void QueueNewSessionDraftModelSelection(OptionValueViewModel? model)
    {
        try
        {
            _newSessionDraftModelSelectionCts?.Cancel();
            _newSessionDraftModelSelectionCts?.Dispose();
        }
        catch
        {
        }

        if (model is null || string.IsNullOrWhiteSpace(model.Value))
        {
            return;
        }

        _newSessionDraftModelSelectionCts = new CancellationTokenSource();
        var token = _newSessionDraftModelSelectionCts.Token;
        _ = SetNewSessionDraftModelAsync(model.Value, token);
    }

    private async Task SetNewSessionDraftModeAsync(string modeId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _newSessionDraftGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var connectionState = await _chatConnectionStore.GetCurrentStateAsync().ConfigureAwait(false);
            var draft = connectionState.NewSessionDraft;
            if (draft is null
                || draft.Phase != NewSessionDraftPhase.Ready
                || string.IsNullOrWhiteSpace(draft.RemoteSessionId)
                || string.Equals(draft.SelectedModeId, modeId, StringComparison.Ordinal))
            {
                return;
            }

            if (!IsCurrentNewSessionDraft(connectionState, draft)
                || _chatService is not { IsConnected: true, IsInitialized: true } chatService)
            {
                await ClearNewSessionDraftStateAsync().ConfigureAwait(false);
                return;
            }

            AcpSessionUpdateDelta delta;
            var modeConfigId = ResolveModeConfigId(draft.ConfigOptions);
            if (!string.IsNullOrWhiteSpace(modeConfigId))
            {
                var response = await chatService.SetSessionConfigOptionAsync(
                    new SessionSetConfigOptionParams(draft.RemoteSessionId!, modeConfigId!, modeId)).ConfigureAwait(false);
                if (response.ConfigOptions is null)
                {
                    await ApplyNewSessionDraftProjectionAsync(connectionState).ConfigureAwait(false);
                    return;
                }

                delta = _acpSessionUpdateProjector.Project(new SessionUpdateEventArgs(
                    draft.RemoteSessionId!,
                    new ConfigOptionUpdate
                    {
                        ConfigOptions = response.ConfigOptions
                    }));
            }
            else
            {
                var response = await chatService.SetSessionModeAsync(
                    new SessionSetModeParams(draft.RemoteSessionId!, modeId)).ConfigureAwait(false);
                delta = _acpSessionUpdateProjector.Project(new SessionUpdateEventArgs(
                    draft.RemoteSessionId!,
                    new CurrentModeUpdate(modeId)));
            }

            var updatedDraft = MergeNewSessionDraftDelta(draft, delta);
            await _chatConnectionStore.Dispatch(new SetNewSessionDraftAction(updatedDraft)).ConfigureAwait(false);
            await ApplyNewSessionDraftProjectionAsync(
                await _chatConnectionStore.GetCurrentStateAsync().ConfigureAwait(false)).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to switch ACP new-session draft mode.");
            await ApplyNewSessionDraftProjectionAsync(
                await _chatConnectionStore.GetCurrentStateAsync().ConfigureAwait(false)).ConfigureAwait(false);
        }
        finally
        {
            _newSessionDraftGate.Release();
        }
    }

    private async Task SetNewSessionDraftModelAsync(string modelValue, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _newSessionDraftGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var connectionState = await _chatConnectionStore.GetCurrentStateAsync().ConfigureAwait(false);
            var draft = connectionState.NewSessionDraft;
            if (draft is null
                || draft.Phase != NewSessionDraftPhase.Ready
                || string.IsNullOrWhiteSpace(draft.RemoteSessionId)
                || string.Equals(ResolveSelectedModelValue(draft.ConfigOptions), modelValue, StringComparison.Ordinal))
            {
                return;
            }

            if (!IsCurrentNewSessionDraft(connectionState, draft)
                || _chatService is not { IsConnected: true, IsInitialized: true } chatService)
            {
                await ClearNewSessionDraftStateAsync().ConfigureAwait(false);
                return;
            }

            var modelConfigId = ResolveModelConfigId(draft.ConfigOptions);
            if (string.IsNullOrWhiteSpace(modelConfigId))
            {
                return;
            }

            var response = await chatService.SetSessionConfigOptionAsync(
                new SessionSetConfigOptionParams(draft.RemoteSessionId!, modelConfigId!, modelValue)).ConfigureAwait(false);
            if (response.ConfigOptions is null)
            {
                await ApplyNewSessionDraftProjectionAsync(connectionState).ConfigureAwait(false);
                return;
            }

            var delta = _acpSessionUpdateProjector.Project(new SessionUpdateEventArgs(
                draft.RemoteSessionId!,
                new ConfigOptionUpdate
                {
                    ConfigOptions = response.ConfigOptions
                }));

            var updatedDraft = MergeNewSessionDraftDelta(draft, delta);
            await _chatConnectionStore.Dispatch(new SetNewSessionDraftAction(updatedDraft)).ConfigureAwait(false);
            await ApplyNewSessionDraftProjectionAsync(
                await _chatConnectionStore.GetCurrentStateAsync().ConfigureAwait(false)).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to switch ACP new-session draft model.");
            await ApplyNewSessionDraftProjectionAsync(
                await _chatConnectionStore.GetCurrentStateAsync().ConfigureAwait(false)).ConfigureAwait(false);
        }
        finally
        {
            _newSessionDraftGate.Release();
        }
    }

    private async Task ApplyNewSessionDraftProjectionAsync(ChatConnectionState connectionState)
    {
        var draft = ResolveEffectiveNewSessionDraft(connectionState);
        var storeState = await _chatStore.GetCurrentStateAsync().ConfigureAwait(false);
        var connectionProjection = CreateProjection(storeState, connectionState);
        await PostToUiAsync(() =>
        {
            ApplyConversationStatusProjection(connectionProjection);
            ApplyConnectionAndAgentProjection(connectionProjection);

            IsNewSessionDraftLoading = draft?.Phase is NewSessionDraftPhase.Creating or NewSessionDraftPhase.Promoting or NewSessionDraftPhase.Closing;
            IsNewSessionDraftReady = draft?.Phase == NewSessionDraftPhase.Ready;
            NewSessionDraftErrorMessage = draft?.Phase == NewSessionDraftPhase.Faulted
                ? NormalizeNewSessionDraftError(draft.Error)
                : string.Empty;

            var availableModes = draft?.AvailableModes ?? ImmutableList<ConversationModeOptionSnapshot>.Empty;
            var projection = _sessionOptionsPresenter.Present(
                availableModes,
                draft?.SelectedModeId,
                draft?.ConfigOptions ?? ImmutableList<ConversationConfigOptionSnapshot>.Empty,
                draft?.ShowConfigOptionsPanel ?? false);

            if (!_sessionOptionsPresenter.ModeCollectionMatches(_newSessionDraftModeOptions, projection.AvailableModes))
            {
                _newSessionDraftModeOptions.Clear();
                foreach (var mode in projection.AvailableModes)
                {
                    _newSessionDraftModeOptions.Add(mode);
                }
            }

            SetSelectedNewSessionDraftModeWithoutDispatch(
                _sessionOptionsPresenter.ResolveSelectedMode(_newSessionDraftModeOptions, projection.SelectedModeId));
            if (!_sessionOptionsPresenter.OptionCollectionMatches(_newSessionDraftModelOptions, projection.ModelOptions))
            {
                _newSessionDraftModelOptions.Clear();
                foreach (var model in projection.ModelOptions)
                {
                    _newSessionDraftModelOptions.Add(model);
                }
            }

            _newSessionDraftModelConfigId = projection.ModelConfigId;
            SetSelectedNewSessionDraftModelWithoutDispatch(
                _sessionOptionsPresenter.ResolveSelectedModelOption(_newSessionDraftModelOptions, projection.SelectedModelValue));
            OnPropertyChanged(nameof(NewSessionDraftModeOptions));
            OnPropertyChanged(nameof(NewSessionDraftModelOptions));
            OnPropertyChanged(nameof(HasNewSessionDraftModelSelector));
        }).ConfigureAwait(false);
    }

    private async Task ClearNewSessionDraftStateAsync(bool clearDesiredRequest = true)
    {
        if (clearDesiredRequest)
        {
            ClearDesiredNewSessionDraftRequestKey();
        }

        var current = await _chatConnectionStore.GetCurrentStateAsync().ConfigureAwait(false);
        if (current.NewSessionDraft is null)
        {
            await ApplyNewSessionDraftProjectionAsync(current).ConfigureAwait(false);
            return;
        }

        await _chatConnectionStore.Dispatch(new ClearNewSessionDraftAction()).ConfigureAwait(false);
        await ApplyNewSessionDraftProjectionAsync(
            await _chatConnectionStore.GetCurrentStateAsync().ConfigureAwait(false)).ConfigureAwait(false);
    }

    private async Task PublishRequiredProfileConnectionFailureDraftAsync(
        string requiredProfileId,
        string? errorMessage)
    {
        var current = await _chatConnectionStore.GetCurrentStateAsync().ConfigureAwait(false);
        if (!string.Equals(current.SelectedProfileIntentId, requiredProfileId, StringComparison.Ordinal)
            || current.Phase is not (ConnectionPhase.Disconnected or ConnectionPhase.Error))
        {
            return;
        }

        var failed = CreateNewSessionDraftState(
            requiredProfileId,
            cwd: string.Empty,
            remoteSessionId: null,
            current.ConnectionInstanceId ?? string.Empty,
            NewSessionDraftPhase.Faulted,
            version: 0,
            AcpSessionUpdateDelta.Empty,
            isConfigAuthoritative: false) with
        {
            Error = string.IsNullOrWhiteSpace(errorMessage)
                ? current.Error
                : errorMessage
        };
        await _chatConnectionStore.Dispatch(new SetNewSessionDraftAction(failed)).ConfigureAwait(false);
        await ApplyNewSessionDraftProjectionAsync(
            await _chatConnectionStore.GetCurrentStateAsync().ConfigureAwait(false)).ConfigureAwait(false);
    }

    private void SetSelectedNewSessionDraftModeWithoutDispatch(SessionModeViewModel? mode)
    {
        _suppressNewSessionDraftModeSelectionDispatch = true;
        try
        {
            SelectedNewSessionDraftMode = mode;
        }
        finally
        {
            _suppressNewSessionDraftModeSelectionDispatch = false;
        }
    }

    private void SetSelectedNewSessionDraftModelWithoutDispatch(OptionValueViewModel? model)
    {
        _suppressNewSessionDraftModelSelectionDispatch = true;
        try
        {
            SelectedNewSessionDraftModelOption = model;
        }
        finally
        {
            _suppressNewSessionDraftModelSelectionDispatch = false;
        }
    }

    private async Task CloseNewSessionDraftQuietlyAsync(
        SalmonEgg.Application.Services.Chat.IChatService chatService,
        NewSessionDraftState draft,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(draft.RemoteSessionId))
        {
            return;
        }

        await CloseNewSessionIdQuietlyAsync(chatService, draft.RemoteSessionId!, cancellationToken).ConfigureAwait(false);
    }

    private async Task CloseNewSessionIdQuietlyAsync(
        SalmonEgg.Application.Services.Chat.IChatService chatService,
        string sessionId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        try
        {
            if (chatService.AgentCapabilities?.SupportsSessionClose == true)
            {
                await chatService.CloseSessionAsync(
                    new SessionCloseParams(sessionId),
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Failed to close ACP new-session draft. sessionId={SessionId}", sessionId);
        }
        finally
        {
            _sessionManager.RemoveSession(sessionId);
        }
    }

    private bool IsCurrentNewSessionDraft(ChatConnectionState connectionState, NewSessionDraftState draft)
    {
        var projectedProfileId = ResolveProjectedProfileId(connectionState);
        return (string.IsNullOrWhiteSpace(projectedProfileId)
                || string.Equals(projectedProfileId, draft.ProfileId, StringComparison.Ordinal))
            && string.Equals(connectionState.ConnectionInstanceId, draft.ConnectionInstanceId, StringComparison.Ordinal);
    }

    private static NewSessionDraftState? ResolveEffectiveNewSessionDraft(ChatConnectionState connectionState)
    {
        var draft = connectionState.NewSessionDraft;
        if (draft is null)
        {
            return null;
        }

        var projectedProfileId = ResolveProjectedProfileId(connectionState);
        if (!string.IsNullOrWhiteSpace(projectedProfileId)
            && !string.Equals(projectedProfileId, draft.ProfileId, StringComparison.Ordinal))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(connectionState.ConnectionInstanceId)
            && !string.Equals(connectionState.ConnectionInstanceId, draft.ConnectionInstanceId, StringComparison.Ordinal))
        {
            return null;
        }

        return draft;
    }

    private static string? ResolveProjectedProfileId(ChatConnectionState connectionState)
        => !string.IsNullOrWhiteSpace(connectionState.SelectedProfileIntentId)
            ? connectionState.SelectedProfileIntentId
            : connectionState.ForegroundTransportProfileId;

    private static bool IsReusableNewSessionDraft(
        NewSessionDraftState? draft,
        string profileId,
        string connectionInstanceId,
        string cwd)
        => draft is { Phase: NewSessionDraftPhase.Ready }
            && !string.IsNullOrWhiteSpace(draft.RemoteSessionId)
            && string.Equals(draft.ProfileId, profileId, StringComparison.Ordinal)
            && string.Equals(draft.ConnectionInstanceId, connectionInstanceId, StringComparison.Ordinal)
            && string.Equals(draft.Cwd, cwd, StringComparison.Ordinal);

    private static NewSessionDraftState CreateNewSessionDraftState(
        string profileId,
        string cwd,
        string? remoteSessionId,
        string connectionInstanceId,
        NewSessionDraftPhase phase,
        long version,
        AcpSessionUpdateDelta delta,
        bool isConfigAuthoritative)
    {
        var availableModes = delta.AvailableModes?
            .Select(ToConversationModeOptionSnapshot)
            .ToImmutableList() ?? ImmutableList<ConversationModeOptionSnapshot>.Empty;
        var configOptions = delta.ConfigOptions?
            .Select(ToConversationConfigOptionSnapshot)
            .ToImmutableList() ?? ImmutableList<ConversationConfigOptionSnapshot>.Empty;
        var availableCommands = delta.AvailableCommands?
            .Select(ToConversationAvailableCommandSnapshot)
            .ToImmutableList() ?? ImmutableList<ConversationAvailableCommandSnapshot>.Empty;
        var sessionInfo = ToConversationSessionInfoSnapshot(delta.SessionInfo);

        return new NewSessionDraftState(
            profileId,
            cwd,
            remoteSessionId,
            connectionInstanceId,
            phase,
            version,
            availableModes,
            string.IsNullOrWhiteSpace(delta.SelectedModeId)
                ? availableModes.FirstOrDefault()?.ModeId
                : delta.SelectedModeId,
            configOptions,
            delta.ShowConfigOptionsPanel ?? configOptions.Count > 0,
            availableCommands,
            sessionInfo,
            isConfigAuthoritative);
    }

    private static NewSessionDraftState MergeNewSessionDraftDelta(NewSessionDraftState draft, AcpSessionUpdateDelta delta)
    {
        var availableModes = delta.AvailableModes is null
            ? draft.AvailableModes
            : delta.AvailableModes.Select(ToConversationModeOptionSnapshot).ToImmutableList();
        var configOptions = delta.ConfigOptions is null
            ? draft.ConfigOptions
            : delta.ConfigOptions.Select(ToConversationConfigOptionSnapshot).ToImmutableList();
        var availableCommands = delta.AvailableCommands is null
            ? draft.AvailableCommands
            : delta.AvailableCommands.Select(ToConversationAvailableCommandSnapshot).ToImmutableList();
        var sessionInfo = ToConversationSessionInfoSnapshot(delta.SessionInfo);

        return draft with
        {
            AvailableModes = availableModes,
            SelectedModeId = string.IsNullOrWhiteSpace(delta.SelectedModeId)
                ? draft.SelectedModeId
                : delta.SelectedModeId,
            ConfigOptions = configOptions,
            ShowConfigOptionsPanel = delta.ShowConfigOptionsPanel ?? draft.ShowConfigOptionsPanel,
            AvailableCommands = availableCommands,
            SessionInfo = sessionInfo is null
                ? ConversationSessionInfoSnapshots.Clone(draft.SessionInfo)
                : ConversationSessionInfoSnapshots.Merge(draft.SessionInfo, sessionInfo)
        };
    }

    private static string? ResolveModeConfigId(IReadOnlyList<ConversationConfigOptionSnapshot> configOptions)
        => configOptions.FirstOrDefault(option =>
            string.Equals(option.Category, "mode", StringComparison.OrdinalIgnoreCase)
            || string.Equals(option.Id, "mode", StringComparison.OrdinalIgnoreCase))?.Id;

    private static string? ResolveModelConfigId(IReadOnlyList<ConversationConfigOptionSnapshot> configOptions)
        => configOptions.FirstOrDefault(option =>
            string.Equals(option.Category, "model", StringComparison.OrdinalIgnoreCase))?.Id;

    private static string? ResolveSelectedModelValue(IReadOnlyList<ConversationConfigOptionSnapshot> configOptions)
        => configOptions.FirstOrDefault(option =>
            string.Equals(option.Category, "model", StringComparison.OrdinalIgnoreCase))?.SelectedValue;

    private static string? NormalizeNewSessionDraftProfileId(string? profileId)
        => string.IsNullOrWhiteSpace(profileId)
            ? null
            : profileId.Trim();

    private static string NormalizeNewSessionDraftError(string? error)
    {
        if (string.Equals(error, AcpSessionNewCwdResolver.MissingRemoteCwdMessage, StringComparison.Ordinal))
        {
            return AcpSessionNewCwdResolver.MissingRemoteCwdMessage;
        }

        return string.IsNullOrWhiteSpace(error)
            ? "Unable to load session configuration. Check the connection and try again."
            : error.Trim();
    }

    private ServerConfiguration? ResolveNewSessionDraftProfile(string? profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return SelectedAcpProfile;
        }

        return AcpProfileList.FirstOrDefault(profile =>
                   string.Equals(profile.Id, profileId, StringComparison.Ordinal))
               ?? SelectedAcpProfile;
    }

    private Task StartInFlightNewSessionDraftRequest(PendingNewSessionDraftRequest request)
    {
        var task = ExecuteInFlightNewSessionDraftRequestAsync(request);
        RegisterInFlightNewSessionDraftRequest(request.RequestKey, task);
        Logger.LogInformation(
            "Started ACP new-session draft request. profileId={ProfileId} connectionInstanceId={ConnectionInstanceId} cwd={Cwd}",
            request.ProfileId,
            request.ConnectionInstanceId,
            request.Cwd);
        return task;
    }

    private async Task ExecuteInFlightNewSessionDraftRequestAsync(PendingNewSessionDraftRequest request)
    {
        try
        {
            var response = await CreateNewSessionDraftResponseAsync(request).ConfigureAwait(false);
            await CompleteSuccessfulNewSessionDraftRequestAsync(request, response).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_disposed)
        {
        }
        catch (Exception ex)
        {
            Logger.LogWarning(
                ex,
                "Failed to complete ACP new-session draft request. profileId={ProfileId} connectionInstanceId={ConnectionInstanceId} cwd={Cwd}",
                request.ProfileId,
                request.ConnectionInstanceId,
                request.Cwd);
            await CompleteFailedNewSessionDraftRequestAsync(request, ex).ConfigureAwait(false);
        }
        finally
        {
            RemoveInFlightNewSessionDraftRequest(request.RequestKey);
        }
    }

    private async Task<SessionNewResponse> CreateNewSessionDraftResponseAsync(
        PendingNewSessionDraftRequest request)
    {
        var operationCancellationToken = _disposed ? CancellationToken.None : _disposeCts.Token;

        try
        {
            var mcpServers = await ResolveCurrentMcpServersAsync(operationCancellationToken).ConfigureAwait(false);
            return await request.ChatService.CreateSessionAsync(
                new SessionNewParams(
                    request.Cwd,
                    McpServerJsonConverter.CloneServers(mcpServers))).ConfigureAwait(false);
        }
        catch (Exception ex) when (ChatAuthenticationCoordinator.IsAuthenticationRequiredError(ex))
        {
            var authenticated = await TryAuthenticateAsync(operationCancellationToken).ConfigureAwait(false);
            if (!authenticated)
            {
                throw new OperationCanceledException("Authentication was not completed.", operationCancellationToken);
            }

            var mcpServers = await ResolveCurrentMcpServersAsync(operationCancellationToken).ConfigureAwait(false);
            return await request.ChatService.CreateSessionAsync(
                new SessionNewParams(
                    request.Cwd,
                    McpServerJsonConverter.CloneServers(mcpServers))).ConfigureAwait(false);
        }
    }

    private async Task CompleteSuccessfulNewSessionDraftRequestAsync(
        PendingNewSessionDraftRequest request,
        SessionNewResponse response)
    {
        var shouldDiscardResponse = false;

        await _newSessionDraftGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            var connectionState = await _chatConnectionStore.GetCurrentStateAsync().ConfigureAwait(false);
            if (IsDesiredNewSessionDraftRequestCancellationRequested(request.RequestKey))
            {
                shouldDiscardResponse = true;
                ClearDesiredNewSessionDraftRequestKey();
                await _chatConnectionStore.Dispatch(new ClearNewSessionDraftAction()).ConfigureAwait(false);
                await ApplyNewSessionDraftProjectionAsync(
                    await _chatConnectionStore.GetCurrentStateAsync().ConfigureAwait(false)).ConfigureAwait(false);
            }
            else if (!ShouldAdoptNewSessionDraftRequestResponse(connectionState, request.RequestKey))
            {
                shouldDiscardResponse = true;
            }
            else
            {
                var readyDraft = CreateNewSessionDraftState(
                    request.ProfileId,
                    request.Cwd,
                    response.SessionId,
                    request.ConnectionInstanceId,
                    NewSessionDraftPhase.Ready,
                    request.RequestVersion,
                    _acpSessionUpdateProjector.ProjectSessionNew(response),
                    response.ConfigOptions is not null);
                await _chatConnectionStore.Dispatch(new SetNewSessionDraftAction(readyDraft)).ConfigureAwait(false);
                await ApplyNewSessionDraftProjectionAsync(
                    await _chatConnectionStore.GetCurrentStateAsync().ConfigureAwait(false)).ConfigureAwait(false);
                Logger.LogInformation(
                    "Applied ACP new-session draft response. profileId={ProfileId} connectionInstanceId={ConnectionInstanceId} remoteSessionId={RemoteSessionId} modeCount={ModeCount}",
                    request.ProfileId,
                    request.ConnectionInstanceId,
                    response.SessionId,
                    readyDraft.AvailableModes.Count);
            }
        }
        finally
        {
            _newSessionDraftGate.Release();
        }

        if (!shouldDiscardResponse)
        {
            return;
        }

        Logger.LogInformation(
            "Discarding superseded ACP new-session draft response. profileId={ProfileId} connectionInstanceId={ConnectionInstanceId} remoteSessionId={RemoteSessionId}",
            request.ProfileId,
            request.ConnectionInstanceId,
            response.SessionId);
        await CloseNewSessionIdQuietlyAsync(request.ChatService, response.SessionId, CancellationToken.None).ConfigureAwait(false);
    }

    private async Task CompleteFailedNewSessionDraftRequestAsync(
        PendingNewSessionDraftRequest request,
        Exception exception)
    {
        var appliedFailure = false;

        await _newSessionDraftGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            var connectionState = await _chatConnectionStore.GetCurrentStateAsync().ConfigureAwait(false);
            if (ShouldAdoptNewSessionDraftRequestResponse(connectionState, request.RequestKey))
            {
                var failed = connectionState.NewSessionDraft is null
                    ? CreateNewSessionDraftState(
                        request.ProfileId,
                        request.Cwd,
                        remoteSessionId: null,
                        request.ConnectionInstanceId,
                        NewSessionDraftPhase.Faulted,
                        request.RequestVersion,
                        AcpSessionUpdateDelta.Empty,
                        isConfigAuthoritative: false) with
                    {
                        Error = exception.Message
                    }
                    : connectionState.NewSessionDraft with
                    {
                        Phase = NewSessionDraftPhase.Faulted,
                        Error = exception.Message
                    };
                await _chatConnectionStore.Dispatch(new SetNewSessionDraftAction(failed)).ConfigureAwait(false);
                await ApplyNewSessionDraftProjectionAsync(
                    await _chatConnectionStore.GetCurrentStateAsync().ConfigureAwait(false)).ConfigureAwait(false);
                appliedFailure = true;
            }
        }
        finally
        {
            _newSessionDraftGate.Release();
        }

        if (appliedFailure)
        {
            Logger.LogDebug(exception, "Failed to prepare ACP new-session draft.");
            return;
        }

        Logger.LogInformation(
            "Ignoring superseded ACP new-session draft failure. profileId={ProfileId} connectionInstanceId={ConnectionInstanceId} error={Error}",
            request.ProfileId,
            request.ConnectionInstanceId,
            exception.Message);
    }

    private bool ShouldAdoptNewSessionDraftRequestResponse(
        ChatConnectionState connectionState,
        NewSessionDraftRequestKey requestKey)
    {
        if (!IsDesiredNewSessionDraftRequest(requestKey)
            || connectionState.Phase != ConnectionPhase.Connected)
        {
            return false;
        }

        return _authoritativeConnectionResolver.TryResolveReadyForegroundConnection(
                   _chatService,
                   connectionState,
                   requestKey.ProfileId,
                   out var snapshot)
               && string.Equals(snapshot.ProfileId, requestKey.ProfileId, StringComparison.Ordinal)
               && string.Equals(snapshot.ConnectionInstanceId, requestKey.ConnectionInstanceId, StringComparison.Ordinal);
    }

    private void RegisterInFlightNewSessionDraftRequest(NewSessionDraftRequestKey requestKey, Task task)
    {
        lock (_newSessionDraftRequestSync)
        {
            _inFlightNewSessionDraftRequests[requestKey] = task;
        }
    }

    private bool TryGetInFlightNewSessionDraftRequestTask(NewSessionDraftRequestKey requestKey, out Task? task)
    {
        lock (_newSessionDraftRequestSync)
        {
            return _inFlightNewSessionDraftRequests.TryGetValue(requestKey, out task);
        }
    }

    private void RemoveInFlightNewSessionDraftRequest(NewSessionDraftRequestKey requestKey)
    {
        lock (_newSessionDraftRequestSync)
        {
            _inFlightNewSessionDraftRequests.Remove(requestKey);
        }
    }

    private void SetDesiredNewSessionDraftRequest(
        NewSessionDraftRequestKey requestKey,
        CancellationToken cancellationToken)
    {
        lock (_newSessionDraftRequestSync)
        {
            _desiredNewSessionDraftRequestKey = requestKey;
            _desiredNewSessionDraftCancellationToken = cancellationToken;
        }
    }

    private void ClearDesiredNewSessionDraftRequestKey()
    {
        lock (_newSessionDraftRequestSync)
        {
            _desiredNewSessionDraftRequestKey = null;
            _desiredNewSessionDraftCancellationToken = default;
        }
    }

    private bool IsDesiredNewSessionDraftRequest(NewSessionDraftRequestKey requestKey)
    {
        lock (_newSessionDraftRequestSync)
        {
            return _desiredNewSessionDraftRequestKey is { } desired
                && desired.Equals(requestKey);
        }
    }

    private bool IsDesiredNewSessionDraftRequestCancellationRequested(NewSessionDraftRequestKey requestKey)
    {
        lock (_newSessionDraftRequestSync)
        {
            return _desiredNewSessionDraftRequestKey is { } desired
                && desired.Equals(requestKey)
                && _desiredNewSessionDraftCancellationToken.IsCancellationRequested;
        }
    }

    private readonly record struct NewSessionDraftRequestKey(
        string ProfileId,
        string ConnectionInstanceId,
        string Cwd);

    private sealed record PendingNewSessionDraftRequest(
        NewSessionDraftRequestKey RequestKey,
        string ProfileId,
        string ConnectionInstanceId,
        string Cwd,
        long RequestVersion,
        SalmonEgg.Application.Services.Chat.IChatService ChatService,
        CancellationToken CancellationToken);

    private enum RequiredProfileIdentityWaitStatus
    {
        Ready = 0,
        Superseded = 1,
        ConnectionFailed = 2,
        TimedOut = 3
    }

    private readonly record struct RequiredProfileIdentityWaitOutcome(
        RequiredProfileIdentityWaitStatus Status,
        string? ErrorMessage);
}
