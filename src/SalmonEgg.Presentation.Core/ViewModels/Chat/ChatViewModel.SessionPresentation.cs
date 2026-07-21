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
using SalmonEgg.Presentation.Core.Diagnostics;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Acp.JsonRpc;
using SalmonEgg.Domain.Interfaces.Storage;
using SalmonEgg.Domain.Interfaces.Transport;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Domain.Models;
using SalmonEgg.Acp.Content;
using SalmonEgg.Acp.Mcp;
using SalmonEgg.Acp.Protocol;
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
    private readonly ConversationOperationFailureState _conversationOperationFailureState = new();

    /// <summary>Raised after authoritative transcript projection is applied for the active session.</summary>
    public event EventHandler? TranscriptContentChanged;

    // Temporary alias until views subscribe only to TranscriptContentChanged.
    public event EventHandler? ProjectionRestoreReady
    {
        add => TranscriptContentChanged += value;
        remove => TranscriptContentChanged -= value;
    }

    public string? SessionActivationFailureMessage
    {
        get
        {
            var activation = _shellNavigationRuntimeState?.ActiveSessionActivation;
            if (activation is not { Phase: SessionActivationPhase.Faulted }
                || !string.Equals(activation.SessionId, CurrentSessionId, StringComparison.Ordinal))
            {
                return null;
            }

            // Hydration failures publish FailureMessage; navigation-level faults often only
            // set Reason. Prefer the explicit message, then a localized reason fallback.
            if (!string.IsNullOrWhiteSpace(activation.FailureMessage))
            {
                return activation.FailureMessage;
            }

            return ResolveSessionActivationFailureReasonMessage(activation.Reason);
        }
    }

    private string? ResolveSessionActivationFailureReasonMessage(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Localize(
                "SessionActivation_FailedGeneric",
                "Failed to open this session. Please try again.");
        }

        // Superseded/canceled are expected races, not user-facing faults.
        if (reason.StartsWith("Superseded", StringComparison.Ordinal)
            || string.Equals(reason, "Canceled", StringComparison.Ordinal))
        {
            return null;
        }

        return reason switch
        {
            "ConversationSelectionFailed" => Localize(
                "SessionActivation_ConversationSelectionFailed",
                "Failed to open this session. Please try again."),
            "ChatShellNavigationFailed" => Localize(
                "SessionActivation_ChatShellNavigationFailed",
                "Failed to open the chat view for this session. Please try again."),
            _ => Localize(
                "SessionActivation_FailedGeneric",
                "Failed to open this session. Please try again.")
        };
    }

    public bool HasSessionActivationFailure
        => !string.IsNullOrWhiteSpace(SessionActivationFailureMessage);

    public string? ConversationOperationFailureMessage
    {
        get
        {
            return _conversationOperationFailureState.ResolveVisibleMessage(CurrentSessionId);
        }
    }

    public bool HasConversationOperationFailure
        => !string.IsNullOrWhiteSpace(ConversationOperationFailureMessage);

    private void NotifySessionActivationFailureProjectionChanged()
    {
        OnPropertyChanged(nameof(SessionActivationFailureMessage));
        OnPropertyChanged(nameof(HasSessionActivationFailure));
    }

    private void PublishConversationOperationFailure(string? conversationId, string message)
    {
        if (!_conversationOperationFailureState.Publish(conversationId, message, CurrentSessionId))
        {
            return;
        }

        NotifyConversationOperationFailureProjectionChanged();
    }

    private Task PublishConversationOperationFailureAsync(string? conversationId, string message)
    {
        if (_uiDispatcher.HasThreadAccess)
        {
            PublishConversationOperationFailure(conversationId, message);
            return Task.CompletedTask;
        }

        return _uiDispatcher.EnqueueAsync(() => PublishConversationOperationFailure(conversationId, message));
    }

    private ConversationFailurePublicationContext CaptureFailurePublicationContext(
        string conversationId,
        long? activationVersion,
        string? operationOwner)
    {
        var activeActivation = _shellNavigationRuntimeState?.ActiveSessionActivation;
        long? expectedShellSnapshotVersion = activationVersion.HasValue
            && activeActivation is not null
            && activeActivation.Matches(conversationId)
                ? activeActivation.Version
                : null;
        return new ConversationFailurePublicationContext(
            conversationId,
            activationVersion,
            operationOwner,
            expectedShellSnapshotVersion);
    }

    private Task PublishConversationFailureAsync(
        ConversationFailurePublicationContext context,
        string reason,
        string message)
    {
        if (context.ExpectedShellSnapshotVersion.HasValue)
        {
            return _conversationActivationOutcomePublisher.TryPublishFailureAsync(
                context.ConversationId,
                context.ActivationVersion,
                context.ExpectedShellSnapshotVersion.Value,
                reason,
                message);
        }

        return PublishConversationOperationFailureAsync(context.OperationOwner, message);
    }

    private Task PublishConversationActivationPhaseAsync(
        ConversationFailurePublicationContext context,
        SessionActivationPhase phase,
        string? reason = null)
    {
        return context.ExpectedShellSnapshotVersion.HasValue
            ? _conversationActivationOutcomePublisher.TryPublishPhaseAsync(
                context.ConversationId,
                context.ActivationVersion,
                context.ExpectedShellSnapshotVersion.Value,
                phase,
                reason)
            : Task.CompletedTask;
    }

    private void ClearConversationOperationFailure(string? conversationId)
    {
        if (!_conversationOperationFailureState.Clear(conversationId))
        {
            return;
        }

        NotifyConversationOperationFailureProjectionChanged();
    }

    private void QueueClearConversationOperationFailure(string? conversationId)
    {
        if (_uiDispatcher.HasThreadAccess)
        {
            ClearConversationOperationFailure(conversationId);
            return;
        }

        _uiDispatcher.Enqueue(() => ClearConversationOperationFailure(conversationId));
    }

    private void NotifyConversationOperationFailureProjectionChanged()
    {
        OnPropertyChanged(nameof(ConversationOperationFailureMessage));
        OnPropertyChanged(nameof(HasConversationOperationFailure));
    }

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

        _isSelectedAcpProfileDefaultProjection = false;
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
        WriteTranscriptProjectionBootFact(projection.HydratedConversationId, projection.Transcript.Count);
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

        NotifyTranscriptContentChanged();
        ShowPlanPanel = projection.ShowPlanPanel;
        if (!sessionChanged)
        {
            SyncPlanEntries(projection.PlanEntries);
        }

        RefreshTaskOverviewChanges(
            projection.HydratedConversationId,
            projection.Transcript,
            forceRefresh: false);
        WriteTranscriptProjectionBootFact(projection.HydratedConversationId, projection.Transcript.Count);
    }



    public TranscriptProjectionRestoreToken? CreateViewportProjectionRestoreToken(ChatMessageViewModel message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (string.IsNullOrWhiteSpace(CurrentSessionId)
            || string.IsNullOrWhiteSpace(message.ProjectionItemKey)
            || !TranscriptItemKey.IsRestorable(message.ProjectionItemKey))
        {
            return null;
        }

        return new TranscriptProjectionRestoreToken(CurrentSessionId, message.ProjectionItemKey);
    }

    private void NotifyTranscriptContentChanged()
        => TranscriptContentChanged?.Invoke(this, EventArgs.Empty);

    private void WriteTranscriptProjectionBootFact(string? conversationId, int transcriptCount)
    {
        if (string.IsNullOrWhiteSpace(conversationId) || transcriptCount <= 0)
        {
            return;
        }

        // DEBUG-only Skia Desktop smoke fact: seed restore and live projection both land
        // here after the authoritative transcript is applied to MessageHistory.
        DebugBootLog.Write(
            $"ChatTranscript: projected conversation={conversationId} count={transcriptCount} history={MessageHistory.Count}");
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

    private void ApplyConversationStatusProjection(ChatUiProjection projection)
    {
        IsHydrating = projection.IsHydrating;
        IsSessionActive = projection.IsSessionActive;
        IsPromptInFlight = projection.IsPromptInFlight;
        IsPromptSubmitInFlight = projection.IsPromptSubmitInFlight;
        IsTurnStatusVisible = projection.IsTurnStatusVisible;
        TurnStatusText = projection.TurnStatusText;
        IsTurnStatusRunning = projection.IsTurnStatusRunning;
        IsTurnStatusFaulted = projection.IsTurnStatusFaulted;
        TurnStatusSource = projection.TurnStatusSource;
        TurnPhase = projection.TurnPhase;
        IsTurnFailureVisible = projection.IsTurnFailureVisible;
        TurnFailureTitle = projection.TurnFailureTitle;
        TurnFailureMessage = projection.TurnFailureMessage;
        TurnFailureCopyActionText = projection.TurnFailureCopyActionText;
        TurnFailureDismissActionText = projection.TurnFailureDismissActionText;
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
            _isSelectedAcpProfileDefaultProjection = false;
            var nextProfileIntentId = value?.Id;
            _pendingSelectedProfileIntentId = nextProfileIntentId;
            _hasPendingSelectedProfileIntent = true;
            if (!string.Equals(_selectedProfileIntentIdFromStore, nextProfileIntentId, StringComparison.Ordinal))
            {
                _selectedProfileIntentIdFromStore = nextProfileIntentId;
                OnPropertyChanged(nameof(SelectedProfileIntentId));
                ClearNewSessionDraftProjection();
            }

            _ = DispatchSelectedProfileIntentAsync(value?.Id);
        }

        if (_suppressAcpProfileConnect || value == null)
        {
            return;
        }

        QueueSelectedProfileConnection(value);
    }

    private async Task DispatchSelectedProfileIntentAsync(string? profileId)
    {
        try
        {
            await _chatConnectionStore.Dispatch(new SetSelectedProfileIntentAction(profileId)).ConfigureAwait(false);
            await ApplyLatestNewSessionDraftProjectionAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to update selected ACP profile intent. ProfileId={ProfileId}", profileId);
        }
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

}


internal sealed record ConversationOperationFailure(string? ConversationId, string Message);

internal readonly record struct ConversationFailurePublicationContext(
    string ConversationId,
    long? ActivationVersion,
    string? OperationOwner,
    long? ExpectedShellSnapshotVersion);

internal sealed class ConversationOperationFailureState
{
    private ConversationOperationFailure? _failure;

    public bool Publish(string? conversationId, string message, string? currentConversationId)
    {
        var incomingOwnerMatchesCurrent = OwnerMatches(conversationId, currentConversationId);
        if (!incomingOwnerMatchesCurrent
            && _failure is { } existingFailure
            && OwnerMatches(existingFailure.ConversationId, currentConversationId))
        {
            return false;
        }

        _failure = new ConversationOperationFailure(conversationId, message);
        return true;
    }

    public bool Clear(string? conversationId)
    {
        if (_failure is not { } failure
            || !string.Equals(failure.ConversationId, conversationId, StringComparison.Ordinal))
        {
            return false;
        }

        _failure = null;
        return true;
    }

    public string? ResolveVisibleMessage(string? currentConversationId)
    {
        if (_failure is not { } failure)
        {
            return null;
        }

        return OwnerMatches(failure.ConversationId, currentConversationId)
            ? failure.Message
            : null;
    }

    private static bool OwnerMatches(string? ownerConversationId, string? currentConversationId)
        => string.IsNullOrWhiteSpace(ownerConversationId)
            ? string.IsNullOrWhiteSpace(currentConversationId)
            : string.Equals(ownerConversationId, currentConversationId, StringComparison.Ordinal);
}
