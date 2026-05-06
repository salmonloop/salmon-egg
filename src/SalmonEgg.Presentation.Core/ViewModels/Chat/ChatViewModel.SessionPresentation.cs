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

    public event EventHandler<ProjectionRestoreReadyEventArgs>? ProjectionRestoreReady;

    private void SyncPlanEntries(IReadOnlyList<ConversationPlanEntrySnapshot> planEntries)
        => _planEntriesProjectionCoordinator.Sync(PlanEntries, planEntries);

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
        _selectedProfileIdFromStore = profileId;

        var match = _profileSelectionResolver.ResolveById(_acpProfiles.Profiles, profileId);
        ApplyResolvedProfileSelection(
            match,
            suppressStoreProjection: false,
            suppressProfileSyncFromStore: true);
    }

    private void ApplySettingsSelectedProfileFromStore(string? profileId)
    {
        _settingsSelectedProfileId = profileId;

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
        SetSelectedModeWithoutDispatch(_sessionOptionsPresenter.ResolveSelectedMode(AvailableModes, projection.SelectedModeId));
    }

    private void OnAcpProfilesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedProfileIdFromStore))
        {
            return;
        }

        ApplySelectedProfileFromStore(_selectedProfileIdFromStore);
    }

    private void ApplySessionIdentityProjection(
        ChatUiProjection projection,
        IReadOnlyList<ChatMessageViewModel>? preparedTranscript,
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
            preparedTranscript,
            sessionChanged: true);
        ReplacePlanEntries(projection.PlanEntries);
    }

    private void ApplyPromptAndProfileProjection(ChatUiProjection projection)
    {
        var draft = projection.CurrentPrompt;
        if (!string.Equals(CurrentPrompt, draft, StringComparison.Ordinal))
        {
            CurrentPrompt = draft;
        }

        ApplySettingsSelectedProfileFromStore(projection.SettingsSelectedProfileId);
        _selectedProfileIdFromStore = !string.IsNullOrWhiteSpace(projection.ChatOwnerProfileId)
            ? projection.ChatOwnerProfileId
            : projection.SettingsSelectedProfileId;
        _currentRemoteSessionId = projection.RemoteSessionId;
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
                preparedTranscript: null,
                sessionChanged: false);
        }

        PublishProjectionRestoreReady(projection);
        ShowPlanPanel = projection.ShowPlanPanel;
        CurrentPlanTitle = projection.PlanTitle;
        if (!sessionChanged)
        {
            SyncPlanEntries(projection.PlanEntries);
        }
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

    private void ApplyConversationStatusProjection(ChatUiProjection projection)
    {
        IsHydrating = projection.IsHydrating;
        IsSessionActive = projection.IsSessionActive;
        IsPromptInFlight = projection.IsPromptInFlight;
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
        RaiseOverlayStateChanged();
        Interlocked.Exchange(ref _connectionGeneration, projection.ConnectionGeneration);
        if (!string.Equals(_connectionInstanceId, projection.ConnectionInstanceId, StringComparison.Ordinal))
        {
            _connectionInstanceId = projection.ConnectionInstanceId;
            OnPropertyChanged(nameof(ConnectionInstanceId));
        }

        CurrentConnectionStatus = projection.ConnectionStatus;
        ConnectionErrorMessage = projection.ConnectionError;
        IsAuthenticationRequired = projection.IsAuthenticationRequired;
        AuthenticationHintMessage = projection.AuthenticationHintMessage;
        AgentName = projection.AgentName;
        AgentVersion = projection.AgentVersion;
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
            _ = _chatConnectionStore.Dispatch(new SetSettingsSelectedProfileAction(value?.Id));
        }

        if (_suppressAcpProfileConnect || value == null)
        {
            return;
        }

        QueueSelectedProfileConnection(value);
    }

    partial void OnSelectedModeChanged(SessionModeViewModel? value)
    {
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
            _preferences.ProjectPathMappings.ToArray());

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

    [RelayCommand]
    private void BeginEditSessionName()
    {
        if (!_sessionHeaderActionCoordinator.TryBeginEditSessionName(
            IsSessionActive,
            CurrentSessionId,
            CurrentSessionDisplayName,
            out var editingSessionName))
        {
            return;
        }

        EditingSessionName = editingSessionName;
        IsEditingSessionName = true;
    }

    [RelayCommand]
    private void CancelSessionNameEdit()
    {
        IsEditingSessionName = false;
        EditingSessionName = string.Empty;
    }

    [RelayCommand]
    private void CommitSessionNameEdit()
    {
        if (!IsSessionActive || string.IsNullOrWhiteSpace(CurrentSessionId))
        {
            CancelSessionNameEdit();
            return;
        }

        var sessionId = CurrentSessionId;
        var finalName = _sessionHeaderActionCoordinator.CommitSessionName(sessionId, EditingSessionName);

        RenameConversation(sessionId, finalName);

        CancelSessionNameEdit();
    }

    public void RenameConversation(string conversationId, string newDisplayName)
    {
        _conversationCatalogFacade.RenameConversation(conversationId, newDisplayName);

        if (string.Equals(CurrentSessionId, conversationId, StringComparison.Ordinal))
        {
            CurrentSessionDisplayName = ResolveSessionDisplayName(conversationId);
        }
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
