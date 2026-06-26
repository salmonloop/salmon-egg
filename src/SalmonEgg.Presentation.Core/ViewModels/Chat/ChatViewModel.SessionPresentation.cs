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
using SalmonEgg.Presentation.Core.ViewModels.Chat.ProfileSelection;
using SalmonEgg.Presentation.Core.ViewModels.Chat.ProjectAffinity;
using SalmonEgg.Presentation.Core.ViewModels.Chat.SessionOptions;
using SalmonEgg.Presentation.ViewModels.Chat.Activation;
using SalmonEgg.Presentation.ViewModels.Chat.Transcript;
using SalmonEgg.Presentation.ViewModels.Chat.Panels;
using SalmonEgg.Presentation.Models.Navigation;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.Utilities;
using SalmonEgg.Presentation.ViewModels.Settings;

namespace SalmonEgg.Presentation.ViewModels.Chat;

public partial class ChatViewModel
{
    private ProjectionRestoreReadyPublicationKey? _lastProjectionRestoreReadyKey;
    private long _currentRestoreProjectionEpoch = -1;
    private string? _currentRestoreProjectionConversationId;

    public event EventHandler<ProjectionRestoreReadyEventArgs>? ProjectionRestoreReady;

    private void SyncPlanEntries(IReadOnlyList<ConversationPlanEntrySnapshot> planEntries)
    {
        _planEntriesProjectionCoordinator.Sync(PlanEntries, planEntries);
        RaisePlanEntryDerivedPropertyNotifications();
    }

    private void ApplyResolvedProfileSelection(
        ServerConfiguration? selectedProfile,
        bool suppressStoreProjection,
        bool suppressProfileSyncFromStore)
    {
        if (ReferenceEquals(SelectedAcpProfile, selectedProfile)
            && ReferenceEquals(_acpProfiles.SelectedProfile, selectedProfile))
        {
            return;
        }

        var previousSuppressAcpProfileConnect = _suppressAcpProfileConnect;
        var previousSuppressStoreProfileProjection = _suppressStoreProfileProjection;
        var previousSuppressProfileSyncFromStore = _suppressProfileSyncFromStore;

        _suppressAcpProfileConnect = true;
        if (suppressStoreProjection)
        {
            _suppressStoreProfileProjection = true;
        }

        if (suppressProfileSyncFromStore)
        {
            _suppressProfileSyncFromStore = true;
        }

        try
        {
            SelectedAcpProfile = selectedProfile;
            _acpProfiles.SelectedProfile = selectedProfile;
        }
        finally
        {
            _suppressProfileSyncFromStore = previousSuppressProfileSyncFromStore;
            _suppressStoreProfileProjection = previousSuppressStoreProfileProjection;
            _suppressAcpProfileConnect = previousSuppressAcpProfileConnect;
        }
    }

    private void ApplySelectedProfileFromStore(string? profileId)
    {
        if (_hasPendingSelectedProfileIntent)
        {
            if (!string.Equals(_pendingSelectedProfileIntentId, profileId, StringComparison.Ordinal))
            {
                return;
            }

            _hasPendingSelectedProfileIntent = false;
            _pendingSelectedProfileIntentId = null;
        }

        if (!string.Equals(_selectedProfileIntentIdFromStore, profileId, StringComparison.Ordinal))
        {
            _selectedProfileIntentIdFromStore = profileId;
            OnPropertyChanged(nameof(SelectedProfileIntentId));
        }

        var match = _profileSelectionResolver.ResolveById(_acpProfiles.Profiles, profileId);
        ApplyResolvedProfileSelection(
            match,
            suppressStoreProjection: false,
            suppressProfileSyncFromStore: true);
    }

    private ServerConfiguration? ResolveLoadedProfileSelection(ServerConfiguration? profile)
        => _profileSelectionResolver.ResolveLoadedProfileSelection(_acpProfiles.Profiles, profile);

    private void ApplySessionStateProjection(
        IReadOnlyList<ConversationModeOptionSnapshot> availableModes,
        string? selectedModeId,
        IReadOnlyList<ConversationConfigOptionSnapshot> configOptions,
        bool showConfigOptionsPanel)
    {
        var projection = _sessionOptionsPresenter.Present(
            availableModes,
            selectedModeId,
            configOptions,
            showConfigOptionsPanel);

        if (!_sessionOptionsPresenter.ModeCollectionMatches(AvailableModes, projection.AvailableModes))
        {
            AvailableModes.Clear();
            foreach (var mode in projection.AvailableModes)
            {
                AvailableModes.Add(mode);
            }
        }

        if (!_sessionOptionsPresenter.ConfigOptionCollectionMatches(ConfigOptions, projection.ConfigOptions))
        {
            ConfigOptions.Clear();
            foreach (var option in projection.ConfigOptions)
            {
                ConfigOptions.Add(option);
            }
        }

        ShowConfigOptionsPanel = projection.ShowConfigOptionsPanel;
        _modeConfigId = projection.ModeConfigId;
        _modelOptions = projection.ModelOptions;
        _modelConfigId = projection.ModelConfigId;
        _selectedModelValue = projection.SelectedModelValue;
        SetSelectedModeWithoutDispatch(_sessionOptionsPresenter.ResolveSelectedMode(AvailableModes, projection.SelectedModeId));
        NotifyComposerProjectionChanged();
    }

    private void OnAcpProfilesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var profileId = _selectedProfileIntentIdFromStore;
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return;
        }

        _uiDispatcher.Enqueue(() =>
        {
            if (_disposed
                || !string.Equals(_selectedProfileIntentIdFromStore, profileId, StringComparison.Ordinal))
            {
                return;
            }

            ApplySelectedProfileFromStore(profileId);
        });
    }

    private void ApplySessionIdentityProjection(
        ChatUiProjection projection,
        out bool sessionChanged)
    {
        sessionChanged = !string.Equals(CurrentSessionId, projection.HydratedConversationId, StringComparison.Ordinal);
        if (!sessionChanged)
        {
            return;
        }

        // Set the session ID before subsequent projection so loading state is derived
        // from the active conversation that the UI is about to display.
        CurrentSessionId = projection.HydratedConversationId;
        _transcriptProjectionCoordinator.ApplyProjection(
            _transcriptProjectionContext,
            projection.HydratedConversationId,
            projection.Transcript,
            sessionChanged: true);
        ReplacePlanEntries(projection.PlanEntries);
        RefreshTaskOverviewChanges(
            projection.HydratedConversationId,
            projection.Transcript,
            forceRefresh: true);
    }

    private void ApplyPromptAndProfileProjection(ChatUiProjection projection, bool sessionChanged)
    {
        ApplyCurrentPromptProjection(projection, sessionChanged);

        ApplySelectedProfileFromStore(projection.SelectedProfileIntentId);
        var selectedProfileId = !string.IsNullOrWhiteSpace(projection.ChatOwnerProfileId)
            ? projection.ChatOwnerProfileId
            : projection.SelectedProfileIntentId;
        if (!string.Equals(_selectedProfileIdFromStore, selectedProfileId, StringComparison.Ordinal))
        {
            _selectedProfileIdFromStore = selectedProfileId;
            NotifyComposerProjectionChanged();
        }

        _currentRemoteSessionId = projection.RemoteSessionId;
    }

    private void ApplyCurrentPromptProjection(ChatUiProjection projection, bool sessionChanged)
    {
        var draft = projection.CurrentPrompt;

        if (sessionChanged)
        {
            ClearPendingLocalPromptProjection();
            _minimumPromptDraftRevision = projection.DraftRevision;
        }
        else if (projection.DraftRevision < _minimumPromptDraftRevision)
        {
            return;
        }
        else if (_hasPendingLocalPromptProjection)
        {
            var sameConversation = string.Equals(
                _pendingLocalPromptConversationId,
                projection.HydratedConversationId,
                StringComparison.Ordinal);

            if (sameConversation && string.Equals(draft, _pendingLocalPromptText, StringComparison.Ordinal))
            {
                ClearPendingLocalPromptProjection();
            }
            else if (sameConversation)
            {
                return;
            }
        }

        if (!string.Equals(CurrentPrompt, draft, StringComparison.Ordinal))
        {
            CurrentPrompt = draft;
        }

        _minimumPromptDraftRevision = Math.Max(_minimumPromptDraftRevision, projection.DraftRevision);
    }

    private void ApplyTranscriptAndPlanProjection(
        ChatUiProjection projection,
        bool sessionChanged)
    {
        // Transcript must be synchronized before activation/loading properties change
        // so the rendered surface never observes a newer active conversation with stale rows.
        if (!sessionChanged)
        {
            _transcriptProjectionCoordinator.ApplyProjection(
                _transcriptProjectionContext,
                projection.HydratedConversationId,
                projection.Transcript,
                sessionChanged: false);
        }

        UpdateRestoreProjectionMetadata(projection);
        PublishProjectionRestoreReady(projection);
        ShowPlanPanel = projection.ShowPlanPanel;
        if (!sessionChanged)
        {
            SyncPlanEntries(projection.PlanEntries);
        }

        RefreshTaskOverviewChanges(
            projection.HydratedConversationId,
            projection.Transcript,
            forceRefresh: false);
    }

    private void PublishProjectionRestoreReady(ChatUiProjection projection)
    {
        if (string.IsNullOrWhiteSpace(projection.HydratedConversationId)
            || !projection.RestoreProjection.IsReady
            || projection.RestoreProjection.Token is not { } token)
        {
            _lastProjectionRestoreReadyKey = null;
            return;
        }

        var publicationKey = new ProjectionRestoreReadyPublicationKey(
            projection.HydratedConversationId,
            projection.RestoreProjection.ProjectionEpoch,
            token);
        if (_lastProjectionRestoreReadyKey == publicationKey)
        {
            return;
        }

        _lastProjectionRestoreReadyKey = publicationKey;

        ProjectionRestoreReady?.Invoke(
            this,
            new ProjectionRestoreReadyEventArgs(
                projection.HydratedConversationId,
                projection.RestoreProjection.ProjectionEpoch,
                token));
    }

    public TranscriptProjectionRestoreToken? CreateViewportProjectionRestoreToken(ChatMessageViewModel message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (string.IsNullOrWhiteSpace(CurrentSessionId)
            || !string.Equals(CurrentSessionId, _currentRestoreProjectionConversationId, StringComparison.Ordinal)
            || _currentRestoreProjectionEpoch < 0
            || string.IsNullOrWhiteSpace(message.ProjectionItemKey))
        {
            return null;
        }

        return new TranscriptProjectionRestoreToken(
            CurrentSessionId,
            _currentRestoreProjectionEpoch,
            message.ProjectionItemKey);
    }

    public async ValueTask<IReadOnlyList<ConversationMessageSnapshot>> GetCurrentSessionTranscriptSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        var conversationId = CurrentSessionId;
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return Array.Empty<ConversationMessageSnapshot>();
        }

        cancellationToken.ThrowIfCancellationRequested();
        var state = await _chatStore.GetCurrentStateAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var transcript = state.ResolveContentSlice(conversationId)?.Transcript
            ?? (string.Equals(state.HydratedConversationId, conversationId, StringComparison.Ordinal)
                ? state.Transcript
                : null)
            ?? ImmutableList<ConversationMessageSnapshot>.Empty;
        return transcript.Select(CloneSnapshot).ToArray();
    }

    private void UpdateRestoreProjectionMetadata(ChatUiProjection projection)
    {
        _currentRestoreProjectionConversationId = projection.HydratedConversationId;
        _currentRestoreProjectionEpoch = projection.RestoreProjection.IsReady
            ? projection.RestoreProjection.ProjectionEpoch
            : -1;
        RefreshCurrentSessionDisplayName();
    }

    private void ApplyConversationStatusProjection(ChatUiProjection projection)
    {
        IsHydrating = projection.IsHydrating;
        IsSessionActive = projection.IsSessionActive;
        IsPromptInFlight = projection.IsPromptInFlight;
        IsPromptSubmitInFlight = projection.IsPromptSubmitInFlight;
        IsTurnStatusVisible = projection.IsTurnStatusVisible;
        TurnStatusText = projection.TurnStatusText;
        IsTurnStatusRunning = projection.IsTurnStatusRunning;
        TurnPhase = projection.TurnPhase;
        IsConnecting = projection.IsConnecting;
        IsConnected = projection.IsConnected;
        IsInitializing = projection.IsInitializing;
    }

    private void ApplyConnectionAndAgentProjection(ChatUiProjection projection)
    {
        Interlocked.Exchange(ref _connectionGeneration, projection.ConnectionGeneration);
        if (!string.Equals(_connectionInstanceId, projection.ConnectionInstanceId, StringComparison.Ordinal))
        {
            _connectionInstanceId = projection.ConnectionInstanceId;
            OnPropertyChanged(nameof(ConnectionInstanceId));
        }

        if (!string.Equals(_foregroundTransportProfileIdFromStore, projection.ForegroundTransportProfileId, StringComparison.Ordinal))
        {
            _foregroundTransportProfileIdFromStore = projection.ForegroundTransportProfileId;
            OnPropertyChanged(nameof(ForegroundTransportProfileId));
        }

        CurrentConnectionStatus = projection.ConnectionStatus;
        ConnectionErrorMessage = projection.ConnectionError;
        IsAuthenticationRequired = projection.IsAuthenticationRequired;
        AuthenticationHintMessage = projection.AuthenticationHintMessage;
        AgentName = projection.AgentName;
        AgentVersion = projection.AgentVersion;
        RaiseOverlayStateChanged();
    }

    private void ApplySessionToolingProjection(ChatUiProjection projection)
    {
        ApplySessionStateProjection(
            projection.AvailableModes,
            projection.SelectedModeId,
            projection.ConfigOptions,
            projection.ShowConfigOptionsPanel);
        ApplySlashCommandProjection(projection.AvailableCommands);
    }

    private void ApplyConversationChromeProjection(ChatUiProjection projection)
    {
        TryCompletePendingHistoryOverlayDismissal(projection);
        RefreshProjectAffinityCorrectionState(projection.HydratedConversationId);
        ApplyProjectAffinityOverrideCommand.NotifyCanExecuteChanged();
        ClearProjectAffinityOverrideCommand.NotifyCanExecuteChanged();
        QueuePreviewSnapshotPersistence(projection);
    }

    partial void OnSelectedAcpProfileChanged(ServerConfiguration? value)
    {
        if (!_suppressStoreProfileProjection)
        {
            var nextProfileIntentId = value?.Id;
            _pendingSelectedProfileIntentId = nextProfileIntentId;
            _hasPendingSelectedProfileIntent = true;
            if (!string.Equals(_selectedProfileIntentIdFromStore, nextProfileIntentId, StringComparison.Ordinal))
            {
                _selectedProfileIntentIdFromStore = nextProfileIntentId;
                OnPropertyChanged(nameof(SelectedProfileIntentId));
            }

            _ = _chatConnectionStore.Dispatch(new SetSelectedProfileIntentAction(value?.Id));
        }

        if (_suppressAcpProfileConnect || value == null)
        {
            return;
        }

        QueueSelectedProfileConnection(value);
    }

    partial void OnSelectedModeChanged(SessionModeViewModel? value)
    {
        NotifyComposerProjectionChanged();

        if (_suppressModeSelectionDispatch || value is null || _disposed)
        {
            return;
        }

        _ = SetModeAsync(value);
    }

    private void RefreshProjectAffinityCorrectionState(string? conversationId = null)
    {
        var presentedState = _projectAffinityCorrectionCoordinator.Present(
            _conversationWorkspace,
            _sessionManager,
            conversationId,
            CurrentSessionId,
            _currentRemoteSessionId,
            SelectedAcpProfile?.Id,
            SelectedProjectAffinityOverrideProjectId,
            _preferences.Projects.ToArray(),
            _preferences.AgentRemoteDirectories.ToArray());

        ProjectAffinityOverrideOptions = new ObservableCollection<ProjectAffinityOverrideOptionViewModel>(presentedState.Options);
        IsProjectAffinityCorrectionVisible = presentedState.IsVisible;
        HasProjectAffinityOverride = presentedState.HasOverride;
        EffectiveProjectAffinityProjectId = presentedState.EffectiveProjectId;
        EffectiveProjectAffinitySource = presentedState.EffectiveSource;
        ProjectAffinityCorrectionMessage = presentedState.Message;
        SelectedProjectAffinityOverrideProjectId = presentedState.SelectedOverrideProjectId;

        OnPropertyChanged(nameof(CanApplyProjectAffinityOverride));
        OnPropertyChanged(nameof(CanClearProjectAffinityOverride));
    }

    private void ApplyProjectAffinityOverride()
    {
        if (!_sessionHeaderActionCoordinator.TryApplyProjectAffinityOverride(
            _conversationWorkspace,
            CurrentSessionId,
            SelectedProjectAffinityOverrideProjectId))
        {
            return;
        }

        RefreshProjectAffinityCorrectionState(CurrentSessionId);
        ApplyProjectAffinityOverrideCommand.NotifyCanExecuteChanged();
        ClearProjectAffinityOverrideCommand.NotifyCanExecuteChanged();
    }

    private void ClearProjectAffinityOverride()
    {
        if (!_sessionHeaderActionCoordinator.TryClearProjectAffinityOverride(
            _conversationWorkspace,
            CurrentSessionId))
        {
            return;
        }

        SelectedProjectAffinityOverrideProjectId = null;
        RefreshProjectAffinityCorrectionState(CurrentSessionId);
        ApplyProjectAffinityOverrideCommand.NotifyCanExecuteChanged();
        ClearProjectAffinityOverrideCommand.NotifyCanExecuteChanged();
    }

    public IReadOnlyList<ConversationProjectTargetOption> GetConversationProjectTargets()
        => _conversationCatalogFacade.GetConversationProjectTargets();

    public void MoveConversationToProject(string conversationId, string projectId)
    {
        _conversationCatalogFacade.MoveConversationToProject(conversationId, projectId);

        if (string.Equals(CurrentSessionId, conversationId, StringComparison.Ordinal))
        {
            RefreshProjectAffinityCorrectionState(conversationId);
        }
    }
}

public sealed class ProjectionRestoreReadyEventArgs : EventArgs
{
    public ProjectionRestoreReadyEventArgs(
        string conversationId,
        long projectionEpoch,
        TranscriptProjectionRestoreToken restoreToken)
    {
        ConversationId = conversationId;
        ProjectionEpoch = projectionEpoch;
        RestoreToken = restoreToken;
    }

    public string ConversationId { get; }

    public long ProjectionEpoch { get; }

    public TranscriptProjectionRestoreToken RestoreToken { get; }
}

internal readonly record struct ProjectionRestoreReadyPublicationKey(
    string ConversationId,
    long ProjectionEpoch,
    TranscriptProjectionRestoreToken RestoreToken);
