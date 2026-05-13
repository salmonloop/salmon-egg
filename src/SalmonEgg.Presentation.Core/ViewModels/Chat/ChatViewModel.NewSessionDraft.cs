using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
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

        await _newSessionDraftGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await TryAutoConnectAsync(cancellationToken).ConfigureAwait(false);
            var connectionState = await _chatConnectionStore.State ?? ChatConnectionState.Empty;
            var chatService = _chatService;
            var normalizedRequiredProfileId = NormalizeNewSessionDraftProfileId(requiredProfileId);
            var foregroundProfileId = connectionState.ForegroundTransportProfileId;
            var profileId = normalizedRequiredProfileId ?? foregroundProfileId ?? SelectedProfileId;
            var connectionInstanceId = connectionState.ConnectionInstanceId ?? ConnectionInstanceId;
            var normalizedCwd = NormalizeNewSessionDraftCwd(cwd);

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
                return;
            }

            if (chatService is not { IsConnected: true, IsInitialized: true }
                || connectionState.Phase != ConnectionPhase.Connected
                || string.IsNullOrWhiteSpace(profileId)
                || string.IsNullOrWhiteSpace(connectionInstanceId))
            {
                await ClearNewSessionDraftStateAsync().ConfigureAwait(false);
                return;
            }

            var existingDraft = connectionState.NewSessionDraft;
            if (IsReusableNewSessionDraft(existingDraft, profileId!, connectionInstanceId!, normalizedCwd))
            {
                await ApplyNewSessionDraftProjectionAsync(connectionState).ConfigureAwait(false);
                return;
            }

            if (existingDraft is not null)
            {
                await CloseNewSessionDraftQuietlyAsync(chatService, existingDraft, cancellationToken).ConfigureAwait(false);
                await ClearNewSessionDraftStateAsync().ConfigureAwait(false);
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
                (await _chatConnectionStore.State) ?? ChatConnectionState.Empty).ConfigureAwait(false);

            SessionNewResponse response;
            try
            {
                response = await chatService.CreateSessionAsync(
                    new SessionNewParams(normalizedCwd, new List<McpServer>())).ConfigureAwait(false);
            }
            catch (Exception ex) when (ChatAuthenticationCoordinator.IsAuthenticationRequiredError(ex))
            {
                var authenticated = await TryAuthenticateAsync(cancellationToken).ConfigureAwait(false);
                if (!authenticated)
                {
                    throw new OperationCanceledException("Authentication was not completed.", cancellationToken);
                }

                response = await chatService.CreateSessionAsync(
                    new SessionNewParams(normalizedCwd, new List<McpServer>())).ConfigureAwait(false);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                await CloseNewSessionIdQuietlyAsync(chatService, response.SessionId, CancellationToken.None).ConfigureAwait(false);
                await ClearNewSessionDraftStateAsync().ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }

            var readyDraft = CreateNewSessionDraftState(
                profileId!,
                normalizedCwd,
                response.SessionId,
                connectionInstanceId!,
                NewSessionDraftPhase.Ready,
                requestVersion,
                _acpSessionUpdateProjector.ProjectSessionNew(response),
                response.ConfigOptions is not null);
            await _chatConnectionStore.Dispatch(new SetNewSessionDraftAction(readyDraft)).ConfigureAwait(false);
            await ApplyNewSessionDraftProjectionAsync(
                (await _chatConnectionStore.State) ?? ChatConnectionState.Empty).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Failed to prepare ACP new-session draft.");
            var connectionState = await _chatConnectionStore.State ?? ChatConnectionState.Empty;
            var failed = connectionState.NewSessionDraft is null
                ? null
                : connectionState.NewSessionDraft with
                {
                    Phase = NewSessionDraftPhase.Faulted,
                    Error = ex.Message
                };
            await _chatConnectionStore.Dispatch(new SetNewSessionDraftAction(failed)).ConfigureAwait(false);
            await ApplyNewSessionDraftProjectionAsync(
                (await _chatConnectionStore.State) ?? ChatConnectionState.Empty).ConfigureAwait(false);
        }
        finally
        {
            _newSessionDraftGate.Release();
        }
    }

    public async Task DiscardNewSessionDraftAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _newSessionDraftGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var connectionState = await _chatConnectionStore.State ?? ChatConnectionState.Empty;
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
            var connectionState = await _chatConnectionStore.State ?? ChatConnectionState.Empty;
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

    private async Task SetNewSessionDraftModeAsync(string modeId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _newSessionDraftGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var connectionState = await _chatConnectionStore.State ?? ChatConnectionState.Empty;
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
                    new CurrentModeUpdate(response.ModeId)));
            }

            var updatedDraft = MergeNewSessionDraftDelta(draft, delta);
            await _chatConnectionStore.Dispatch(new SetNewSessionDraftAction(updatedDraft)).ConfigureAwait(false);
            await ApplyNewSessionDraftProjectionAsync(
                (await _chatConnectionStore.State) ?? ChatConnectionState.Empty).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to switch ACP new-session draft mode.");
            await ApplyNewSessionDraftProjectionAsync(
                (await _chatConnectionStore.State) ?? ChatConnectionState.Empty).ConfigureAwait(false);
        }
        finally
        {
            _newSessionDraftGate.Release();
        }
    }

    private async Task ApplyNewSessionDraftProjectionAsync(ChatConnectionState connectionState)
    {
        var draft = ResolveEffectiveNewSessionDraft(connectionState);
        await PostToUiAsync(() =>
        {
            IsNewSessionDraftLoading = draft?.Phase is NewSessionDraftPhase.Creating or NewSessionDraftPhase.Promoting or NewSessionDraftPhase.Closing;
            IsNewSessionDraftReady = draft?.Phase == NewSessionDraftPhase.Ready;

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
            OnPropertyChanged(nameof(NewSessionDraftModeOptions));
        }).ConfigureAwait(false);
    }

    private async Task ClearNewSessionDraftStateAsync()
    {
        var current = await _chatConnectionStore.State ?? ChatConnectionState.Empty;
        if (current.NewSessionDraft is null)
        {
            await ApplyNewSessionDraftProjectionAsync(current).ConfigureAwait(false);
            return;
        }

        await _chatConnectionStore.Dispatch(new ClearNewSessionDraftAction()).ConfigureAwait(false);
        await ApplyNewSessionDraftProjectionAsync(
            (await _chatConnectionStore.State) ?? ChatConnectionState.Empty).ConfigureAwait(false);
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

    private async Task CloseNewSessionDraftQuietlyAsync(
        SalmonEgg.Application.Services.Chat.IChatService chatService,
        NewSessionDraftState draft,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(draft.RemoteSessionId)
            || chatService.AgentCapabilities?.SupportsSessionClose != true)
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
        if (string.IsNullOrWhiteSpace(sessionId)
            || chatService.AgentCapabilities?.SupportsSessionClose != true)
        {
            return;
        }

        try
        {
            await chatService.CloseSessionAsync(
                new SessionCloseParams(sessionId),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Failed to close ACP new-session draft. sessionId={SessionId}", sessionId);
        }
    }

    private bool IsCurrentNewSessionDraft(ChatConnectionState connectionState, NewSessionDraftState draft)
        => string.Equals(connectionState.ForegroundTransportProfileId, draft.ProfileId, StringComparison.Ordinal)
            && (string.IsNullOrWhiteSpace(connectionState.SettingsSelectedProfileId)
                || string.Equals(connectionState.SettingsSelectedProfileId, draft.ProfileId, StringComparison.Ordinal))
            && string.Equals(connectionState.ConnectionInstanceId, draft.ConnectionInstanceId, StringComparison.Ordinal);

    private static NewSessionDraftState? ResolveEffectiveNewSessionDraft(ChatConnectionState connectionState)
    {
        var draft = connectionState.NewSessionDraft;
        if (draft is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(connectionState.SettingsSelectedProfileId)
            && !string.Equals(connectionState.SettingsSelectedProfileId, draft.ProfileId, StringComparison.Ordinal))
        {
            return null;
        }

        return draft;
    }

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

    private static string NormalizeNewSessionDraftCwd(string? cwd)
    {
        if (!string.IsNullOrWhiteSpace(cwd))
        {
            return cwd.Trim();
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

    private static string? NormalizeNewSessionDraftProfileId(string? profileId)
        => string.IsNullOrWhiteSpace(profileId)
            ? null
            : profileId.Trim();
}
