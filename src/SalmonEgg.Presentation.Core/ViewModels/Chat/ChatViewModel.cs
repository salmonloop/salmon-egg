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
using SalmonEgg.Presentation.ViewModels.Settings;

namespace SalmonEgg.Presentation.ViewModels.Chat;

/// <summary>
/// Main ViewModel for the Chat interface.
/// Orchestrates the lifecycle of conversations, ACP agent connectivity, and UI state projection.
/// Follows the MVVM pattern where the View is driven strictly by this ViewModel and its projected state.
/// </summary>
public partial class ChatViewModel : ViewModelBase, IDisposable, IAcpChatCoordinatorSink, IConversationSessionSwitcher, IConversationActivationPreview, IConversationPanelCleanup, IConversationActivationOrchestratorSink
{
    private const int MiniWindowCompactDisplayNameMaxLength = 24;

    public enum LoadingOverlayStage
    {
        None = 0,
        Connecting = 1,
        InitializingProtocol = 2,
        HydratingHistory = 3,
        PreparingSession = 4
    }

    private enum HydrationOverlayPhase
    {
        None = 0,
        RequestingSessionLoad = 1,
        AwaitingReplayStart = 2,
        ReplayingSessionUpdates = 3,
        ProjectingTranscript = 4,
        SettlingReplay = 5,
        FinalizingProjection = 6
    }

    private static readonly TimeSpan RemoteReplayStartTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RemoteReplaySettleQuietPeriod = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RemoteReplayKnownTranscriptGrowthGracePeriod = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RemoteSessionLoadTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan RemoteReplayDrainTimeout = TimeSpan.FromSeconds(10);
    private const int RemoteReplayPollDelayMilliseconds = 50;
    private AcpHydrationCompletionMode _hydrationCompletionMode = AcpHydrationCompletionMode.StrictReplay;
    private readonly ChatServiceFactory _chatServiceFactory;
    private readonly ChatConversationWorkspace _conversationWorkspace;
    private readonly IConversationActivationCoordinator _conversationActivationCoordinator;
    private readonly IConversationActivationOrchestrator _conversationActivationOrchestrator;
    private readonly IConversationBindingCommands _bindingCommands;
    private readonly IAcpConnectionCommands _acpConnectionCommands;
    private readonly IAcpConnectionCoordinator _acpConnectionCoordinator;
    private readonly IAcpConnectionSessionRegistry? _connectionSessionRegistry;
    private readonly AcpAuthoritativeConnectionResolver _authoritativeConnectionResolver;
    private readonly ChatProjectAffinityCorrectionCoordinator _projectAffinityCorrectionCoordinator;
    private readonly ChatConversationSurfaceProjectionCoordinator _conversationSurfaceProjectionCoordinator;
    private readonly ChatInputStatePresenter _inputStatePresenter;
    private readonly ChatAskUserStatePresenter _askUserStatePresenter;
    private readonly ChatPlanPanelStatePresenter _planPanelStatePresenter;
    private readonly ChatPlanEntriesProjectionCoordinator _planEntriesProjectionCoordinator;
    private readonly ChatAcpProfileSelectionResolver _profileSelectionResolver;
    private readonly ChatConversationPanelStateCoordinator _panelStateCoordinator;
    private readonly ChatConversationPanelRuntimeCoordinator _panelRuntimeCoordinator;
    private readonly ChatSessionOptionsPresenter _sessionOptionsPresenter;
    private readonly ChatTerminalProjectionCoordinator _terminalProjectionCoordinator;
    private readonly ChatInteractionEventBridge _interactionEventBridge;
    private readonly ChatAuthenticationCoordinator _authenticationCoordinator;
    private readonly ChatSessionHeaderActionCoordinator _sessionHeaderActionCoordinator;
    private readonly ConversationCatalogFacade _conversationCatalogFacade;
    private readonly IConfigurationService _configurationService;
    private readonly AppPreferencesViewModel _preferences;
    private readonly AcpProfilesViewModel _acpProfiles;
    private readonly ISessionManager _sessionManager;
    private readonly IMiniWindowCoordinator _miniWindowCoordinator;
    private readonly ConversationCatalogPresenter _conversationCatalogPresenter;
    private readonly IChatStateProjector _chatStateProjector;
    private readonly IChatUiProjectionApplicationCoordinator _chatUiProjectionApplicationCoordinator;
    private readonly IAcpSessionUpdateProjector _acpSessionUpdateProjector;
    private readonly IChatConnectionStore _chatConnectionStore;
    private readonly IConversationAttentionStore? _conversationAttentionStore;
    private readonly SerialAsyncWorkQueue _sessionUpdateWorkQueue;
    private readonly SerialAsyncWorkQueue _previewSnapshotWorkQueue;
    private readonly LocalTerminalPanelCoordinator? _localTerminalPanelCoordinator;
    private IChatService? _chatService;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly IConversationPreviewStore _previewStore;
    private readonly ChatTranscriptProjectionCoordinator _transcriptProjectionCoordinator;
    private readonly ChatTranscriptProjectionContext _transcriptProjectionContext;
    private readonly ConversationHydrationCoordinator _hydrationCoordinator;
    private readonly ConversationHydrationContext _hydrationContext;
    private readonly IVoiceInputService _voiceInputService;
    private readonly IShellNavigationRuntimeState? _shellNavigationRuntimeState;
    private readonly ConversationActivationOutcomePublisher _conversationActivationOutcomePublisher;
    private bool _disposed;
    private bool _autoConnectAttempted;
    private bool _suppressAcpProfileConnect;
    private bool _suppressAutoConnectFromPreferenceChange;
    private CancellationTokenSource? _sendPromptCts;
    private CancellationTokenSource? _voiceInputCts;
    private CancellationTokenSource? _transientNotificationCts;
    private CancellationTokenSource? _storeStateCts;
    private readonly object _selectedProfileConnectSync = new();
    private readonly object _ambientConnectionRequestSync = new();
    private Task? _selectedProfileConnectTask;
    private ServerConfiguration? _pendingSelectedProfileConnect;
    private IDisposable? _storeStateSubscription;
    private IDisposable? _connectionStateSubscription;
    private string? _currentRemoteSessionId;
    private bool _suppressStoreProfileProjection;
    private bool _suppressStorePromptProjection;
    private bool _suppressProfileSyncFromStore;
    private bool _suppressModeSelectionDispatch;
    private string? _selectedProfileIdFromStore;
    private string? _settingsSelectedProfileId;
    private int _storeProjectionSequence;
    private readonly object _restoreSync = new();
    private Task? _restoreTask;
    private readonly SemaphoreSlim _remoteConversationActivationGate = new(1, 1);
    private long _localTerminalActivationVersion;

    private CancellationTokenSource? _ambientConnectionRequestCts;
    private long _connectionGeneration;
    private string? _connectionInstanceId;
    private readonly ObservableCollection<AskUserQuestionViewModel> _emptyAskUserQuestions = new();
    private readonly object _sessionUpdateTrackingSync = new();
    private TaskCompletionSource<object?>? _sessionUpdatesDrainedTcs;
    private readonly object _sessionUpdateObservationSync = new();
    private readonly Dictionary<string, long> _sessionUpdateObservationCounts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _sessionTranscriptProjectionObservationCounts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTime> _sessionUpdateLastObservedAtUtc = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _remoteHydrationKnownTranscriptBaselineCounts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTime> _remoteHydrationKnownTranscriptGrowthGraceDeadlineUtc = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _remoteHydrationSessionUpdateBaselineCounts = new(StringComparer.Ordinal);
    private HydrationOverlayPhase _hydrationOverlayPhase = HydrationOverlayPhase.None;
    private string? _hydrationOverlayPhaseConversationId;
    private int _pendingSessionUpdateCount;
    private AskUserRequestViewModel? _observedPendingAskUserRequest;
    private string? _sessionSwitchOverlayConversationId;
    private string? _connectionLifecycleOverlayConversationId;
    private string? _historyOverlayConversationId;
    private string? _pendingHistoryOverlayDismissConversationId;
    private string? _sessionSwitchPreviewConversationId;
    private string? _visibleTranscriptConversationId;
    private string? _activeVoiceInputRequestId;
    private string _voiceInputBasePrompt = string.Empty;

    /// <summary>
    /// Local conversation binding connects a stable UI ConversationId to a transient ACP RemoteSessionId.
    /// This allows the user to switch between tabs/histories without losing the underlying agent session context,
    /// and handles reconnections by re-binding new remote sessions to the same local ID.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<ChatMessageViewModel> _messageHistory = new();

    [ObservableProperty]
    private string _currentPrompt = string.Empty;

    private string? _currentSessionId;

    public string? CurrentSessionId
    {
        get => _currentSessionId;
        private set
        {
            if (SetProperty(ref _currentSessionId, value))
            {
                OnCurrentSessionIdChanged(value);
            }
        }
    }

    [ObservableProperty]
    private ObservableCollection<MiniWindowConversationItemViewModel> _miniWindowSessions = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSendPromptUi))]
    private bool _isSessionActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInputEnabled))]
    [NotifyPropertyChangedFor(nameof(CanSendPromptUi))]
    private bool _isPromptInFlight;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInputEnabled))]
    [NotifyPropertyChangedFor(nameof(CanStartVoiceInput))]
    [NotifyPropertyChangedFor(nameof(CanStopVoiceInput))]
    private bool _isVoiceInputListening;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInputEnabled))]
    [NotifyPropertyChangedFor(nameof(CanStartVoiceInput))]
    [NotifyPropertyChangedFor(nameof(CanStopVoiceInput))]
    private bool _isVoiceInputBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartVoiceInput))]
    [NotifyPropertyChangedFor(nameof(CanStopVoiceInput))]
    private bool _isVoiceInputSupported;

    [ObservableProperty]
    private string? _voiceInputErrorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOverlayVisible))]
    [NotifyPropertyChangedFor(nameof(OverlayLoadingStage))]
    [NotifyPropertyChangedFor(nameof(OverlayStatusText))]
    private bool _isHydrating;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOverlayVisible))]
    [NotifyPropertyChangedFor(nameof(OverlayLoadingStage))]
    [NotifyPropertyChangedFor(nameof(OverlayStatusText))]
    private bool _isRemoteHydrationPending;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOverlayVisible))]
    [NotifyPropertyChangedFor(nameof(OverlayLoadingStage))]
    [NotifyPropertyChangedFor(nameof(OverlayStatusText))]
    private bool _isSessionSwitching;

    private bool IsChatShellVisibleForRemoteUi
        => _shellNavigationRuntimeState is null
            || _shellNavigationRuntimeState.CurrentShellContent == ShellNavigationContent.Chat;

    private string? PendingShellActivationConversationId
        => _shellNavigationRuntimeState?.IsSessionActivationInProgress == true
            ? _shellNavigationRuntimeState.ActiveSessionActivation?.SessionId
                ?? _shellNavigationRuntimeState.DesiredSessionId
            : null;

    public bool IsActivationOverlayVisible
        => ResolveConversationSurfaceProjection().IsActivationOverlayVisible;

    public bool IsOverlayVisible
        => ResolveConversationSurfaceProjection().IsOverlayVisible;

    public bool HasVisibleTranscriptContent => MessageHistory.Count > 0;

    public bool ShouldShowActiveConversationRoot
        => ResolveConversationSurfaceProjection().ShouldShowActiveConversationRoot;

    public bool ShouldLoadActiveConversationRoot
        => ResolveConversationSurfaceProjection().ShouldLoadActiveConversationRoot;

    public bool ShouldShowSessionHeader
        => ResolveConversationSurfaceProjection().ShouldShowSessionHeader;

    public bool ShouldShowTranscriptSurface
        => ResolveConversationSurfaceProjection().ShouldShowTranscriptSurface;

    public bool ShouldLoadTranscriptSurface
        => ResolveConversationSurfaceProjection().ShouldLoadTranscriptSurface;

    public bool ShouldShowConversationInputSurface
        => ResolveConversationSurfaceProjection().ShouldShowConversationInputSurface;

    public bool ShouldShowBlockingLoadingMask
        => ResolveConversationSurfaceProjection().ShouldShowBlockingLoadingMask;

    public bool ShouldShowLoadingOverlayStatusPill
        => ResolveConversationSurfaceProjection().ShouldShowLoadingOverlayStatusPill;

    public bool ShouldShowLoadingOverlayPresenter
        => ResolveConversationSurfaceProjection().ShouldShowLoadingOverlayPresenter;

    public LoadingOverlayStage OverlayLoadingStage =>
        ResolveConversationSurfaceProjection().OverlayLoadingStage;

    public string OverlayStatusText => ResolveConversationSurfaceProjection().OverlayStatusText;

    private ChatConversationSurfaceState ResolveConversationSurfaceState()
        => ChatConversationSurfaceStatePresenter.Resolve(new ChatConversationSurfaceStateInput(
            IsSessionActive,
            CurrentSessionId,
            MessageHistory.Count,
            _visibleTranscriptConversationId,
            IsChatShellVisibleForRemoteUi,
            IsConnecting,
            IsInitializing,
            IsHydrating,
            IsLayoutLoading,
            IsSessionSwitching,
            _sessionSwitchOverlayConversationId,
            _sessionSwitchPreviewConversationId,
            _connectionLifecycleOverlayConversationId,
            _historyOverlayConversationId,
            PendingShellActivationConversationId,
            ResolveHydrationLoadedMessageCount()));

    private ChatConversationSurfaceProjection ResolveConversationSurfaceProjection()
        => _conversationSurfaceProjectionCoordinator.Project(new ChatConversationSurfaceStateInput(
            IsSessionActive,
            CurrentSessionId,
            MessageHistory.Count,
            _visibleTranscriptConversationId,
            IsChatShellVisibleForRemoteUi,
            IsConnecting,
            IsInitializing,
            IsHydrating,
            IsLayoutLoading,
            IsSessionSwitching,
            _sessionSwitchOverlayConversationId,
            _sessionSwitchPreviewConversationId,
            _connectionLifecycleOverlayConversationId,
            _historyOverlayConversationId,
            PendingShellActivationConversationId,
            ResolveHydrationLoadedMessageCount()));

    private long ResolveHydrationLoadedMessageCount()
    {
        var renderedCount = MessageHistory.Count;
        var conversationId = !string.IsNullOrWhiteSpace(_historyOverlayConversationId)
            ? _historyOverlayConversationId
            : CurrentSessionId;
        if (string.IsNullOrWhiteSpace(conversationId)
            || !_remoteHydrationSessionUpdateBaselineCounts.TryGetValue(conversationId, out var replayBaseline))
        {
            return renderedCount;
        }

        var remoteSessionId = _conversationWorkspace.GetRemoteBinding(conversationId)?.RemoteSessionId;
        if (string.IsNullOrWhiteSpace(remoteSessionId))
        {
            return renderedCount;
        }

        var observedCount = GetSessionUpdateObservationCount(remoteSessionId);
        var replayLoadedCount = Math.Max(0L, observedCount - replayBaseline);
        return Math.Max(renderedCount, replayLoadedCount);
    }

    private bool TryResolveCurrentHydrationConversationForRemoteSession(
        string? remoteSessionId,
        out string conversationId)
    {
        conversationId = string.Empty;
        if (string.IsNullOrWhiteSpace(remoteSessionId))
        {
            return false;
        }

        var currentConversationId = CurrentSessionId;
        if (string.IsNullOrWhiteSpace(currentConversationId)
            || !string.Equals(_historyOverlayConversationId, currentConversationId, StringComparison.Ordinal))
        {
            return false;
        }

        var activeBinding = _conversationWorkspace.GetRemoteBinding(currentConversationId);
        if (!string.Equals(activeBinding?.RemoteSessionId, remoteSessionId, StringComparison.Ordinal))
        {
            return false;
        }

        conversationId = currentConversationId;
        return true;
    }

    private void ResetHydrationOverlayPhaseIfOwnerChanged(string? historyConversationId)
    {
        var shouldReset =
            string.IsNullOrWhiteSpace(historyConversationId)
            || !string.Equals(_hydrationOverlayPhaseConversationId, historyConversationId, StringComparison.Ordinal);

        if (!shouldReset
            || _hydrationOverlayPhase == HydrationOverlayPhase.None)
        {
            return;
        }

        _hydrationOverlayPhase = HydrationOverlayPhase.None;
        _hydrationOverlayPhaseConversationId = historyConversationId;
        OnPropertyChanged(nameof(OverlayStatusText));
    }

    private void SetHydrationOverlayPhase(string conversationId, HydrationOverlayPhase phase)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return;
        }

        if (!string.Equals(_historyOverlayConversationId, conversationId, StringComparison.Ordinal))
        {
            return;
        }

        if (!string.Equals(_hydrationOverlayPhaseConversationId, conversationId, StringComparison.Ordinal))
        {
            _hydrationOverlayPhaseConversationId = conversationId;
            _hydrationOverlayPhase = HydrationOverlayPhase.None;
        }

        if (phase != HydrationOverlayPhase.None
            && phase <= _hydrationOverlayPhase)
        {
            return;
        }

        if (_hydrationOverlayPhase == phase)
        {
            return;
        }

        _hydrationOverlayPhase = phase;
        OnPropertyChanged(nameof(OverlayStatusText));
    }

    private Task SetHydrationOverlayPhaseAsync(
        string conversationId,
        long? activationVersion,
        HydrationOverlayPhase phase)
    {
        if (!ShouldOwnRemoteHydrationUi(conversationId, activationVersion))
        {
            return Task.CompletedTask;
        }

        return PostToUiAsync(() => SetHydrationOverlayPhase(conversationId, phase));
    }

    private Task SetConversationHydrationPhaseAsync(
        string conversationId,
        long? activationVersion,
        ConversationHydrationPhase phase)
        => SetHydrationOverlayPhaseAsync(
            conversationId,
            activationVersion,
            phase switch
            {
                ConversationHydrationPhase.AwaitingReplayStart => HydrationOverlayPhase.AwaitingReplayStart,
                ConversationHydrationPhase.ReplayingSessionUpdates => HydrationOverlayPhase.ReplayingSessionUpdates,
                ConversationHydrationPhase.ProjectingTranscript => HydrationOverlayPhase.ProjectingTranscript,
                ConversationHydrationPhase.SettlingReplay => HydrationOverlayPhase.SettlingReplay,
                ConversationHydrationPhase.FinalizingProjection => HydrationOverlayPhase.FinalizingProjection,
                _ => HydrationOverlayPhase.None
            });

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOverlayVisible))]
    private bool _isLayoutLoading;

    [ObservableProperty]
    private bool _isTurnStatusVisible;

    [ObservableProperty]
    private string _turnStatusText = string.Empty;

    [ObservableProperty]
    private bool _isTurnStatusRunning;

    [ObservableProperty]
    private ChatTurnPhase? _turnPhase;

    [ObservableProperty]
    private bool _isConversationListLoading = true;

    [ObservableProperty]
    private int _conversationListVersion;

    [ObservableProperty]
    private string _currentSessionDisplayName = string.Empty;

    [ObservableProperty]
    private bool _isEditingSessionName;

    [ObservableProperty]
    private string _editingSessionName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentAgentDisplayText))]
    private string? _agentName;

    [ObservableProperty]
    private string? _agentVersion;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInitialized))]
    [NotifyPropertyChangedFor(nameof(CanSendPromptUi))]
    [NotifyPropertyChangedFor(nameof(IsOverlayVisible))]
    [NotifyPropertyChangedFor(nameof(OverlayLoadingStage))]
    [NotifyPropertyChangedFor(nameof(OverlayStatusText))]
    private bool _isInitializing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOverlayVisible))]
    [NotifyPropertyChangedFor(nameof(OverlayLoadingStage))]
    [NotifyPropertyChangedFor(nameof(OverlayStatusText))]
    private bool _isConnecting;

    [ObservableProperty]
    private bool _showTransientNotification;

    [ObservableProperty]
    private string _transientNotificationMessage = string.Empty;

    // Transport and Connection configuration
    [ObservableProperty]
    private TransportConfigViewModel _transportConfig = new();

    [ObservableProperty]
    private bool _showTransportConfigPanel = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasConnectionError))]
    private string? _connectionErrorMessage;

    public bool HasConnectionError => !string.IsNullOrWhiteSpace(ConnectionErrorMessage);

    public bool IsInputEnabled => ResolveInputState().IsInputEnabled;

    public bool HasPendingAskUserRequest => ResolveAskUserState().HasPendingRequest;

    public string AskUserPrompt => ResolveAskUserState().Prompt;

    public ObservableCollection<AskUserQuestionViewModel> AskUserQuestions => ResolveAskUserState().Questions;

    public bool AskUserHasError => ResolveAskUserState().HasError;

    public string AskUserErrorMessage => ResolveAskUserState().ErrorMessage;

    public IAsyncRelayCommand? AskUserSubmitCommand => ResolveAskUserState().SubmitCommand;

    // UI-BOUND PROPERTIES: Handlers for WinUI/Uno property change notifications.
    // These ensure the View reflects internal state changes that might not trigger automatically.
    public bool CanSendPromptUi => ResolveInputState().CanSendPrompt;

    public bool CanStartVoiceInput => ResolveInputState().CanStartVoiceInput;

    public bool CanStopVoiceInput => ResolveInputState().CanStopVoiceInput;

    public bool ShowVoiceInputStartButton => ResolveInputState().ShowVoiceInputStartButton;

    public bool ShowVoiceInputStopButton => ResolveInputState().ShowVoiceInputStopButton;

    public bool HasPlanEntries => ResolvePlanPanelState().HasPlanEntries;

    public bool ShouldShowPlanList => ResolvePlanPanelState().ShouldShowPlanList;

    public bool ShouldShowPlanEmpty => ResolvePlanPanelState().ShouldShowPlanEmpty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSendPromptUi))]
    [NotifyPropertyChangedFor(nameof(IsInitialized))]
    [NotifyPropertyChangedFor(nameof(IsOverlayVisible))]
    private bool _isConnected;

    [ObservableProperty]
    private ObservableCollection<SessionModeViewModel> _availableModes = new();

    [ObservableProperty]
    private SessionModeViewModel? _selectedMode;

    public ObservableCollection<ServerConfiguration> AcpProfileList => _acpProfiles.Profiles;

    public string CurrentAgentDisplayText
    {
        get
        {
            var agentName = AgentName?.Trim();
            if (!string.IsNullOrWhiteSpace(agentName))
            {
                return agentName;
            }

            var profileName = SelectedAcpProfile?.Name?.Trim();
            if (!string.IsNullOrWhiteSpace(profileName))
            {
                return profileName;
            }

            return "—";
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentAgentDisplayText))]
    private ServerConfiguration? _selectedAcpProfile;

    private bool _isProjectAffinityCorrectionVisible;

    private string _projectAffinityCorrectionMessage = string.Empty;

    private bool _hasProjectAffinityOverride;

    private string? _selectedProjectAffinityOverrideProjectId;

    private ObservableCollection<ProjectAffinityOverrideOptionViewModel> _projectAffinityOverrideOptions = new();

    private string? _effectiveProjectAffinityProjectId;

    private ProjectAffinitySource _effectiveProjectAffinitySource = ProjectAffinitySource.Unclassified;

    public bool IsProjectAffinityCorrectionVisible
    {
        get => _isProjectAffinityCorrectionVisible;
        private set => SetProperty(ref _isProjectAffinityCorrectionVisible, value);
    }

    public string ProjectAffinityCorrectionMessage
    {
        get => _projectAffinityCorrectionMessage;
        private set => SetProperty(ref _projectAffinityCorrectionMessage, value);
    }

    public bool HasProjectAffinityOverride
    {
        get => _hasProjectAffinityOverride;
        private set
        {
            if (SetProperty(ref _hasProjectAffinityOverride, value))
            {
                OnPropertyChanged(nameof(CanClearProjectAffinityOverride));
                ClearProjectAffinityOverrideCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string? SelectedProjectAffinityOverrideProjectId
    {
        get => _selectedProjectAffinityOverrideProjectId;
        set
        {
            if (SetProperty(ref _selectedProjectAffinityOverrideProjectId, value))
            {
                OnPropertyChanged(nameof(CanApplyProjectAffinityOverride));
                ApplyProjectAffinityOverrideCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public ObservableCollection<ProjectAffinityOverrideOptionViewModel> ProjectAffinityOverrideOptions
    {
        get => _projectAffinityOverrideOptions;
        private set => SetProperty(ref _projectAffinityOverrideOptions, value);
    }

    public string? EffectiveProjectAffinityProjectId
    {
        get => _effectiveProjectAffinityProjectId;
        private set => SetProperty(ref _effectiveProjectAffinityProjectId, value);
    }

    public ProjectAffinitySource EffectiveProjectAffinitySource
    {
        get => _effectiveProjectAffinitySource;
        private set => SetProperty(ref _effectiveProjectAffinitySource, value);
    }

    public bool CanApplyProjectAffinityOverride
        => !string.IsNullOrWhiteSpace(CurrentSessionId)
            && !string.IsNullOrWhiteSpace(SelectedProjectAffinityOverrideProjectId);

    public bool CanClearProjectAffinityOverride
        => !string.IsNullOrWhiteSpace(CurrentSessionId)
            && HasProjectAffinityOverride;

    public IRelayCommand ApplyProjectAffinityOverrideCommand { get; }

    public IRelayCommand ClearProjectAffinityOverrideCommand { get; }

    // Agent Configuration options (as defined by the protocol)
    [ObservableProperty]
    private ObservableCollection<ConfigOptionViewModel> _configOptions = new();

    [ObservableProperty]
    private bool _showConfigOptionsPanel;

    private readonly HashSet<string> _configAuthoritativeConversationIds = new(StringComparer.Ordinal);
    private string? _modeConfigId;

    // Slash command completion
    [ObservableProperty]
    private ObservableCollection<SlashCommandViewModel> _availableSlashCommands = new();

    [ObservableProperty]
    private ObservableCollection<SlashCommandViewModel> _filteredSlashCommands = new();

    [ObservableProperty]
    private SlashCommandViewModel? _selectedSlashCommand;

    [ObservableProperty]
    private bool _showSlashCommands;

    [ObservableProperty]
    private string _slashGhostSuffix = string.Empty;

    // Agent Planning panel: dynamically shows the agent's current task list.
    [ObservableProperty]
    private ObservableCollection<PlanEntryViewModel> _planEntries = new();

    [ObservableProperty]
    private bool _showPlanPanel;

    [ObservableProperty]
    private string? _currentPlanTitle;

    [ObservableProperty]
    private ObservableCollection<BottomPanelTabViewModel> _bottomPanelTabs = new();

    [ObservableProperty]
    private BottomPanelTabViewModel? _selectedBottomPanelTab;

    [ObservableProperty]
    private ObservableCollection<TerminalPanelSessionViewModel> _terminalSessions = new();

    [ObservableProperty]
    private TerminalPanelSessionViewModel? _selectedTerminalSession;

    [ObservableProperty]
    private LocalTerminalPanelSessionViewModel? _activeLocalTerminalSession;

    [ObservableProperty]
    private bool _showPermissionDialog;

    [ObservableProperty]
    private PermissionRequestViewModel? _pendingPermissionRequest;

    [ObservableProperty]
    private bool _showFileSystemDialog;

    [ObservableProperty]
    private FileSystemRequestViewModel? _pendingFileSystemRequest;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingAskUserRequest))]
    [NotifyPropertyChangedFor(nameof(AskUserPrompt))]
    [NotifyPropertyChangedFor(nameof(AskUserQuestions))]
    [NotifyPropertyChangedFor(nameof(AskUserHasError))]
    [NotifyPropertyChangedFor(nameof(AskUserErrorMessage))]
    [NotifyPropertyChangedFor(nameof(AskUserSubmitCommand))]
    [NotifyPropertyChangedFor(nameof(IsInputEnabled))]
    [NotifyPropertyChangedFor(nameof(CanSendPromptUi))]
    private AskUserRequestViewModel? _pendingAskUserRequest;

    [ObservableProperty]
    private bool _isAuthenticationRequired;

    [ObservableProperty]
    private string? _authenticationHintMessage;

    public string? CurrentConnectionStatus { get; private set; }

    private readonly IChatStore _chatStore;
    private readonly IAuthoritativeRemoteSessionRouter _authoritativeRemoteSessionRouter;

    private sealed class ChatServiceFactoryAdapter : IAcpChatServiceFactory
    {
        private readonly ChatServiceFactory _inner;

        public ChatServiceFactoryAdapter(ChatServiceFactory inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public IChatService CreateChatService(
            TransportType transportType,
            string? command = null,
            string? args = null,
            string? url = null)
            => _inner.CreateChatService(transportType, command, args, url);
    }

    public ChatViewModel(
        IChatStore chatStore,
        ChatServiceFactory chatServiceFactory,
        IConfigurationService configurationService,
        AppPreferencesViewModel preferences,
        AcpProfilesViewModel acpProfiles,
        ISessionManager sessionManager,
        IMiniWindowCoordinator miniWindowCoordinator,
        ChatConversationWorkspace conversationWorkspace,
        ConversationCatalogPresenter conversationCatalogPresenter,
        IChatStateProjector? chatStateProjector,
        IAcpSessionUpdateProjector? acpSessionUpdateProjector,
        IChatConnectionStore chatConnectionStore,
        IUiDispatcher uiDispatcher,
        IConversationPreviewStore previewStore,
        ILogger<ChatViewModel> logger,
        IConversationAttentionStore? conversationAttentionStore = null,
        IAcpConnectionCommands? acpConnectionCommands = null,
        IConversationActivationCoordinator? conversationActivationCoordinator = null,
        IConversationBindingCommands? bindingCommands = null,
        IAcpConnectionCoordinator? acpConnectionCoordinator = null,
        IProjectAffinityResolver? projectAffinityResolver = null,
        IShellNavigationRuntimeState? shellNavigationRuntimeState = null,
        IVoiceInputService? voiceInputService = null,
        LocalTerminalPanelCoordinator? localTerminalPanelCoordinator = null,
        IAuthoritativeRemoteSessionRouter? authoritativeRemoteSessionRouter = null,
        ConversationCatalogFacade? conversationCatalogFacade = null,
        IAcpConnectionSessionRegistry? connectionSessionRegistry = null,
        SerialAsyncWorkQueue? sessionUpdateWorkQueue = null,
        IChatUiProjectionApplicationCoordinator? chatUiProjectionApplicationCoordinator = null,
        IConversationActivationOrchestrator? conversationActivationOrchestrator = null)
        : base(logger)
    {
        _chatStore = chatStore ?? throw new ArgumentNullException(nameof(chatStore));
        _authoritativeRemoteSessionRouter = authoritativeRemoteSessionRouter ?? new AuthoritativeRemoteSessionRouter(chatStore);
        _chatServiceFactory = chatServiceFactory ?? throw new ArgumentNullException(nameof(chatServiceFactory));
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _acpProfiles = acpProfiles ?? throw new ArgumentNullException(nameof(acpProfiles));
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _miniWindowCoordinator = miniWindowCoordinator ?? throw new ArgumentNullException(nameof(miniWindowCoordinator));
        _conversationWorkspace = conversationWorkspace ?? throw new ArgumentNullException(nameof(conversationWorkspace));
        _bindingCommands = bindingCommands ?? new BindingCoordinator(conversationWorkspace, chatStore);
        _acpConnectionCoordinator = acpConnectionCoordinator ?? NoopAcpConnectionCoordinator.Instance;
        _conversationActivationCoordinator = conversationActivationCoordinator
            ?? new ConversationActivationCoordinator(
                conversationWorkspace,
                _bindingCommands,
                chatStore,
                chatConnectionStore,
                NullLogger<ConversationActivationCoordinator>.Instance);
        _conversationActivationOrchestrator = conversationActivationOrchestrator
            ?? new ConversationActivationOrchestrator(NullLogger<ConversationActivationOrchestrator>.Instance);
        _conversationCatalogPresenter = conversationCatalogPresenter ?? throw new ArgumentNullException(nameof(conversationCatalogPresenter));
        _conversationCatalogFacade = conversationCatalogFacade ?? throw new ArgumentNullException(nameof(conversationCatalogFacade));
        _chatStateProjector = chatStateProjector ?? throw new ArgumentNullException(nameof(chatStateProjector));
        _chatUiProjectionApplicationCoordinator = chatUiProjectionApplicationCoordinator ?? new ChatUiProjectionApplicationCoordinator();
        _acpSessionUpdateProjector = acpSessionUpdateProjector ?? new AcpSessionUpdateProjector();
        _chatConnectionStore = chatConnectionStore ?? throw new ArgumentNullException(nameof(chatConnectionStore));
        _conversationAttentionStore = conversationAttentionStore;
        _connectionSessionRegistry = connectionSessionRegistry;
        _authoritativeConnectionResolver = new AcpAuthoritativeConnectionResolver(connectionSessionRegistry);
        _sessionUpdateWorkQueue = sessionUpdateWorkQueue ?? new SerialAsyncWorkQueue();
        _previewSnapshotWorkQueue = new SerialAsyncWorkQueue();
        _projectAffinityCorrectionCoordinator = new ChatProjectAffinityCorrectionCoordinator(projectAffinityResolver ?? new ProjectAffinityResolver());
        _conversationSurfaceProjectionCoordinator = new ChatConversationSurfaceProjectionCoordinator();
        _inputStatePresenter = new ChatInputStatePresenter();
        _askUserStatePresenter = new ChatAskUserStatePresenter();
        _planPanelStatePresenter = new ChatPlanPanelStatePresenter();
        _planEntriesProjectionCoordinator = new ChatPlanEntriesProjectionCoordinator();
        _profileSelectionResolver = new ChatAcpProfileSelectionResolver();
        _panelStateCoordinator = new ChatConversationPanelStateCoordinator();
        _panelRuntimeCoordinator = new ChatConversationPanelRuntimeCoordinator();
        _sessionOptionsPresenter = new ChatSessionOptionsPresenter();
        _terminalProjectionCoordinator = new ChatTerminalProjectionCoordinator();
        _interactionEventBridge = new ChatInteractionEventBridge(_authoritativeRemoteSessionRouter, _terminalProjectionCoordinator);
        _authenticationCoordinator = new ChatAuthenticationCoordinator();
        _sessionHeaderActionCoordinator = new ChatSessionHeaderActionCoordinator();
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
        _previewStore = previewStore ?? throw new ArgumentNullException(nameof(previewStore));
        _transcriptProjectionCoordinator = new ChatTranscriptProjectionCoordinator(_previewStore);
        _transcriptProjectionContext = new ChatTranscriptProjectionContext
        {
            GetMessageHistory = () => MessageHistory,
            SetMessageHistory = history => MessageHistory = history,
            FromSnapshot = FromSnapshot,
            MatchesSnapshot = MatchesSnapshot,
            UpdateVisibleTranscriptConversationId = UpdateVisibleTranscriptConversationId,
            RaiseTranscriptStateChanged = RaiseTranscriptProjectionStateChanged
        };
        _hydrationCoordinator = new ConversationHydrationCoordinator(
            new ConversationHydrationCoordinatorOptions(
                ReplayStartTimeout: RemoteReplayStartTimeout,
                ReplaySettleQuietPeriod: RemoteReplaySettleQuietPeriod,
                PollDelay: TimeSpan.FromMilliseconds(RemoteReplayPollDelayMilliseconds),
                ReplayDrainTimeout: RemoteReplayDrainTimeout));
        _hydrationContext = new ConversationHydrationContext
        {
            SetHydrationPhaseAsync = SetConversationHydrationPhaseAsync,
            GetSessionUpdateObservationCount = GetSessionUpdateObservationCount,
            GetTranscriptProjectionObservationCount = GetTranscriptProjectionObservationCount,
            GetSessionUpdateLastObservedAtUtc = GetSessionUpdateLastObservedAtUtc,
            AwaitBufferedReplayProjectionAsync = AwaitBufferedSessionReplayProjectionAsync,
            GetProjectedTranscriptCountAsync = GetProjectedTranscriptCountAsync,
            YieldToUiAsync = () => PostToUiAsync(static () => { }),
            WaitForAdapterDrainAsync = WaitForAdapterReplayDrainAsync,
            WaitForPendingSessionUpdatesAsync = WaitForPendingSessionUpdatesAsync
        };
        _voiceInputService = voiceInputService ?? NoOpVoiceInputService.Instance;
        _shellNavigationRuntimeState = shellNavigationRuntimeState;
        _conversationActivationOutcomePublisher = new ConversationActivationOutcomePublisher(
            _shellNavigationRuntimeState,
            _uiDispatcher,
            Logger,
            () => IsChatShellVisibleForRemoteUi,
            _conversationActivationOrchestrator.IsLatestActivationVersion,
            SetError);
        _localTerminalPanelCoordinator = localTerminalPanelCoordinator;
        ApplyProjectAffinityOverrideCommand = new RelayCommand(ApplyProjectAffinityOverride, () => CanApplyProjectAffinityOverride);
        ClearProjectAffinityOverrideCommand = new RelayCommand(ClearProjectAffinityOverride, () => CanClearProjectAffinityOverride);
        _acpConnectionCommands = acpConnectionCommands
            ?? new AcpChatCoordinator(
                new ChatServiceFactoryAdapter(chatServiceFactory),
                NullLogger<AcpChatCoordinator>.Instance);
        _voiceInputService.PartialResultReceived += OnVoiceInputPartialResultReceived;
        _voiceInputService.FinalResultReceived += OnVoiceInputFinalResultReceived;
        _voiceInputService.SessionEnded += OnVoiceInputSessionEnded;
        _voiceInputService.ErrorOccurred += OnVoiceInputErrorOccurred;
        IsVoiceInputSupported = _voiceInputService.IsSupported;
        StartStoreProjection();

        _acpProfiles.PropertyChanged += OnAcpProfilesPropertyChanged;
        _acpProfiles.Profiles.CollectionChanged += OnAcpProfilesCollectionChanged;
        _preferences.PropertyChanged += OnPreferencesPropertyChanged;
        _hydrationCompletionMode = ResolveHydrationCompletionMode(_preferences.AcpHydrationCompletionMode);
        _preferences.Projects.CollectionChanged += OnProjectAffinityPreferencesCollectionChanged;
        _preferences.ProjectPathMappings.CollectionChanged += OnProjectAffinityPreferencesCollectionChanged;
        _conversationWorkspace.PropertyChanged += OnConversationWorkspacePropertyChanged;
        if (_shellNavigationRuntimeState is not null)
        {
            _shellNavigationRuntimeState.PropertyChanged += OnShellNavigationRuntimeStatePropertyChanged;
        }
        _planEntriesProjectionCoordinator.Observe(PlanEntries, RaisePlanEntryDerivedPropertyNotifications);

        IsConversationListLoading = _conversationWorkspace.IsConversationListLoading;
        ConversationListVersion = _conversationWorkspace.ConversationListVersion;
        _conversationCatalogPresenter.SetLoading(IsConversationListLoading);
        _conversationCatalogPresenter.Refresh(_conversationWorkspace.GetCatalog());
        RefreshProjectAffinityCorrectionState();

    }

    private void OnConversationWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ChatConversationWorkspace.IsConversationListLoading):
                IsConversationListLoading = _conversationWorkspace.IsConversationListLoading;
                _conversationCatalogPresenter.SetLoading(IsConversationListLoading);
                RefreshMiniWindowSessions();
                break;

            case nameof(ChatConversationWorkspace.ConversationListVersion):
                ConversationListVersion = _conversationWorkspace.ConversationListVersion;
                var refreshedCatalog = _conversationWorkspace.GetCatalog();
                _conversationCatalogPresenter.Refresh(refreshedCatalog);
                OnPropertyChanged(nameof(GetKnownConversationIds));
                RefreshMiniWindowSessions();
                RefreshProjectAffinityCorrectionState();
                break;
        }
    }

    private void OnShellNavigationRuntimeStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IShellNavigationRuntimeState.CurrentShellContent)
            || e.PropertyName == nameof(IShellNavigationRuntimeState.DesiredSessionId)
            || e.PropertyName == nameof(IShellNavigationRuntimeState.IsSessionActivationInProgress)
            || e.PropertyName == nameof(IShellNavigationRuntimeState.ActiveSessionActivation))
        {
            RaiseOverlayStateChanged();
        }
    }

    private void OnProjectAffinityPreferencesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => RefreshProjectAffinityCorrectionState();

    private void StartStoreProjection()
    {
        _storeStateCts = new CancellationTokenSource();
        var token = _storeStateCts.Token;

        _chatStore.State.ForEach(async (state, ct) =>
        {
            await RefreshProjectionAsync(state, token, ct).ConfigureAwait(false);
        }, out _storeStateSubscription);

        _chatConnectionStore.State.ForEach(async (_, ct) =>
        {
            if (token.IsCancellationRequested || ct.IsCancellationRequested || _disposed)
            {
                return;
            }

            ChatState? state = null;
            try
            {
                state = await _chatStore.State;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested || ct.IsCancellationRequested)
            {
                return;
            }

            await RefreshProjectionAsync(state, token, ct).ConfigureAwait(false);
        }, out _connectionStateSubscription);
    }

    private void StopStoreProjection()
    {
        _storeStateCts?.Cancel();

        try
        {
            _storeStateSubscription?.Dispose();
        }
        catch
        {
        }

        try
        {
            _connectionStateSubscription?.Dispose();
        }
        catch
        {
        }

        _storeStateSubscription = null;
        _connectionStateSubscription = null;

        try
        {
            _storeStateCts?.Dispose();
        }
        catch
        {
        }

        _storeStateCts = null;
    }

    private async Task RefreshProjectionAsync(ChatState? state, CancellationToken token, CancellationToken ct)
    {
        if (state is null || token.IsCancellationRequested || ct.IsCancellationRequested || _disposed)
        {
            return;
        }

        try
        {
            var projectionSequence = Interlocked.Increment(ref _storeProjectionSequence);
            var latestConnectionState = await _chatConnectionStore.State ?? ChatConnectionState.Empty;
            var projection = CreateProjection(state, latestConnectionState);
            await PostToUiAsync(async () =>
            {
                if (token.IsCancellationRequested || _disposed)
                {
                    return;
                }

                if (projectionSequence != Volatile.Read(ref _storeProjectionSequence))
                {
                    return;
                }

                if (!ShouldApplyStoreProjection(
                        projection,
                        _conversationActivationOrchestrator.CurrentActivationVersion))
                {
                    return;
                }

                ApplyStoreProjection(projection);
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested || ct.IsCancellationRequested)
        {
            // Graceful cancellation on disposal
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Chat store projection failed");
        }
    }

    [RelayCommand]
    private async Task OpenMiniWindowAsync()
    {
        try
        {
            await _miniWindowCoordinator.OpenMiniWindowAsync();
        }
        catch (Exception ex)
        {
            // Mini window failure is usually due to missing MSIX context or Skia runtime issues.
            Logger.LogError(ex, "Failed to open mini window");
            ShowTransientNotificationToast("Failed to open mini window. Please ensure you are running as an MSIX package.");
        }
    }

    [RelayCommand]
    private async Task ReturnToMainWindowAsync()
    {
        try
        {
            await _miniWindowCoordinator.ReturnToMainWindowAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to return to main window");
            ShowTransientNotificationToast("Failed to return to main window.");
        }
    }

    private void OnPreferencesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppPreferencesViewModel.LastSelectedServerId)
            && !_suppressAutoConnectFromPreferenceChange)
        {
            // Preferences load is async; on some targets (e.g., Skia) TryAutoConnectAsync may run before
            // LastSelectedServerId is hydrated. Re-attempt once it becomes available.
            _ = TryAutoConnectAsync();
        }

        if (e.PropertyName == nameof(AppPreferencesViewModel.AcpHydrationCompletionMode))
        {
            _hydrationCompletionMode = ResolveHydrationCompletionMode(_preferences.AcpHydrationCompletionMode);
        }
    }

    protected override void OnIsBusyChangedCore(bool value)
    {
        // Keep send button enabled state accurate when IsBusy toggles (we rely on command CanExecute).
        SendPromptCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsInputEnabled));
        OnPropertyChanged(nameof(CanSendPromptUi));
        OnPropertyChanged(nameof(CanStartVoiceInput));
        OnPropertyChanged(nameof(CanStopVoiceInput));
    }

    private void OnAcpProfilesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AcpProfilesViewModel.SelectedProfile))
        {
            if (_suppressProfileSyncFromStore)
            {
                return;
            }

            ApplyResolvedProfileSelection(
                _acpProfiles.SelectedProfile,
                suppressStoreProjection: false,
                suppressProfileSyncFromStore: false);
        }
    }

    private void ApplyStoreProjection(
        ChatUiProjection projection,
        IReadOnlyList<ChatMessageViewModel>? preparedTranscript = null)
    {
        var projectionApplyStopwatch = Stopwatch.StartNew();
        var sessionChanged = false;
        _suppressStoreProfileProjection = true;
        _suppressStorePromptProjection = true;
        try
        {
            ApplySessionIdentityProjection(projection, preparedTranscript, out sessionChanged);
            ApplyPromptAndProfileProjection(projection);
            ApplyTranscriptAndPlanProjection(projection, sessionChanged);
            ApplyConversationStatusProjection(projection);
            ApplyConnectionAndAgentProjection(projection);
            ApplySessionToolingProjection(projection);
            ApplyConversationChromeProjection(projection);
        }
        finally
        {
            projectionApplyStopwatch.Stop();
            _suppressStorePromptProjection = false;
            _suppressStoreProfileProjection = false;

            const int slowUiProjectionThresholdMs = 800;
            if (projectionApplyStopwatch.ElapsedMilliseconds >= slowUiProjectionThresholdMs)
            {
                Logger.LogWarning(
                    "Slow UI projection detected. elapsedMs={ElapsedMs} sessionChanged={SessionChanged} conversationId={ConversationId} transcriptCount={TranscriptCount} isHydrating={IsHydrating} isRemoteHydrationPending={IsRemoteHydrationPending}",
                    projectionApplyStopwatch.ElapsedMilliseconds,
                    sessionChanged,
                    projection.HydratedConversationId,
                    projection.Transcript.Count,
                    IsHydrating,
                    IsRemoteHydrationPending);
            }
        }
    }

    private void QueueSelectedProfileConnection(ServerConfiguration profile)
    {
        lock (_selectedProfileConnectSync)
        {
            _pendingSelectedProfileConnect = profile;
            if (_selectedProfileConnectTask is { IsCompleted: false })
            {
                return;
            }

            _selectedProfileConnectTask = ProcessSelectedProfileConnectionQueueAsync();
        }
    }

    private async Task ProcessSelectedProfileConnectionQueueAsync()
    {
        while (true)
        {
            ServerConfiguration? profile;
            lock (_selectedProfileConnectSync)
            {
                profile = _pendingSelectedProfileConnect;
                _pendingSelectedProfileConnect = null;
                if (profile == null)
                {
                    _selectedProfileConnectTask = null;
                    return;
                }
            }

            try
            {
                await ConnectToAcpProfileCoreAsync(profile, CancellationToken.None).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Queued ACP profile switch failed (ProfileId={ProfileId})", profile.Id);
            }
        }
    }

    private CancellationTokenSource BeginAmbientConnectionRequest(CancellationToken cancellationToken)
    {
        CancellationTokenSource? previousRequest;
        var currentRequest = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (_ambientConnectionRequestSync)
        {
            previousRequest = _ambientConnectionRequestCts;
            _ambientConnectionRequestCts = currentRequest;
        }

        try
        {
            previousRequest?.Cancel();
        }
        finally
        {
            previousRequest?.Dispose();
        }

        return currentRequest;
    }

    private void EndAmbientConnectionRequest(CancellationTokenSource request)
    {
        lock (_ambientConnectionRequestSync)
        {
            if (ReferenceEquals(_ambientConnectionRequestCts, request))
            {
                _ambientConnectionRequestCts = null;
            }
        }

        request.Dispose();
    }

    private void CancelAmbientConnectionRequest()
    {
        CancellationTokenSource? currentRequest;
        lock (_ambientConnectionRequestSync)
        {
            currentRequest = _ambientConnectionRequestCts;
            _ambientConnectionRequestCts = null;
        }

        try
        {
            currentRequest?.Cancel();
        }
        finally
        {
            currentRequest?.Dispose();
        }
    }

    private async Task PrepareSelectedProfileConnectionAsync(
        ServerConfiguration profile,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);

        await _chatConnectionStore
            .Dispatch(new SetSettingsSelectedProfileAction(profile.Id))
            .ConfigureAwait(false);
        await ApplyCurrentStoreProjectionAsync().ConfigureAwait(false);
    }

    // Partial method implementations called by source-generated code.
    partial void OnShowPlanPanelChanged(bool value)
    {
        OnPropertyChanged(nameof(ShouldShowPlanList));
        OnPropertyChanged(nameof(ShouldShowPlanEmpty));
    }

    partial void OnIsHydratingChanged(bool value) => RaiseOverlayStateChanged();

    partial void OnIsRemoteHydrationPendingChanged(bool value) => RaiseOverlayStateChanged();

    partial void OnIsSessionSwitchingChanged(bool value) => RaiseOverlayStateChanged();

    partial void OnIsLayoutLoadingChanged(bool value) => RaiseOverlayStateChanged();

    partial void OnPlanEntriesChanged(ObservableCollection<PlanEntryViewModel> value)
    {
        _planEntriesProjectionCoordinator.Observe(value, RaisePlanEntryDerivedPropertyNotifications);
        RaisePlanEntryDerivedPropertyNotifications();
    }

    private void OnCurrentSessionIdChanged(string? value)
    {
        // Keep the header name stable and decouple it from ACP sessionId.
        CurrentSessionDisplayName = ResolveSessionDisplayName(value);

        if (string.IsNullOrWhiteSpace(value))
        {
            _currentRemoteSessionId = null;
            var sessionSwitchConversationId = IsSessionSwitching ? _sessionSwitchOverlayConversationId : null;
            var overlayOwnersAlreadyMatch =
                string.Equals(_sessionSwitchOverlayConversationId, sessionSwitchConversationId, StringComparison.Ordinal)
                && string.IsNullOrWhiteSpace(_connectionLifecycleOverlayConversationId)
                && string.IsNullOrWhiteSpace(_historyOverlayConversationId);
            SetConversationOverlayOwners(
                sessionSwitchConversationId: sessionSwitchConversationId,
                connectionLifecycleConversationId: null,
                historyConversationId: null);
            if (overlayOwnersAlreadyMatch)
            {
                RaiseOverlayStateChanged();
            }
        }
        else
        {
            RaiseOverlayStateChanged();
        }

        if (IsEditingSessionName)
        {
            IsEditingSessionName = false;
            EditingSessionName = string.Empty;
        }

        SyncConversationPanelState(value);
        _ = ActivateLocalTerminalPanelAsync(value);
        RefreshProjectAffinityCorrectionState(value);
        ApplyProjectAffinityOverrideCommand.NotifyCanExecuteChanged();
        ClearProjectAffinityOverrideCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedBottomPanelTabChanged(BottomPanelTabViewModel? value)
    {
        var conversationId = CurrentSessionId;
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return;
        }

        _panelStateCoordinator.UpdateSelectedTab(conversationId, value);
    }

    private async Task SelectAndHydrateConversationAsync(string? conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            await _chatStore.Dispatch(new SelectConversationAction(null));
            await ApplyCurrentStoreProjectionAsync().ConfigureAwait(false);
            return;
        }

        var keepLayoutLoading = false;
        await SetLayoutLoadingAsync(true).ConfigureAwait(false);
        try
        {
            var activationResult = await _conversationActivationCoordinator
                .ActivateSessionAsync(conversationId)
                .ConfigureAwait(false);
            if (!activationResult.Succeeded)
            {
                return;
            }

            await ApplyCurrentStoreProjectionAsync().ConfigureAwait(false);

            // Re-evaluate if we should keep loading after projection applied.
            // Layout loading is only a bridge until the active conversation is fully
            // projected. Once hydration/pending replay has ended, visible transcript
            // content should not keep the layout overlay alive on its own.
            keepLayoutLoading = IsSessionActive && (IsHydrating || IsRemoteHydrationPending);
        }
        finally
        {
            if (!keepLayoutLoading)
            {
                await SetLayoutLoadingAsync(false).ConfigureAwait(false);
            }
        }
    }

    private Task SetLayoutLoadingAsync(bool value)
    {
        if (_uiDispatcher.HasThreadAccess)
        {
            IsLayoutLoading = value;
            return Task.CompletedTask;
        }

        return PostToUiAsync(() => IsLayoutLoading = value);
    }

    private async Task ApplyCurrentStoreProjectionAsync(long? activationVersion = null)
    {
        var state = await _chatStore.State ?? ChatState.Empty;
        var connectionState = await _chatConnectionStore.State ?? ChatConnectionState.Empty;
        var projection = CreateProjection(state, connectionState);
        if (activationVersion.HasValue
            && !ShouldApplyStoreProjection(projection, activationVersion.Value))
        {
            return;
        }

        var shouldPrebuildTranscript =
            !string.Equals(CurrentSessionId, projection.HydratedConversationId, StringComparison.Ordinal)
            && projection.Transcript.Count >= 200;
        IReadOnlyList<ChatMessageViewModel>? preparedTranscript = null;
        if (shouldPrebuildTranscript)
        {
            var prebuildStopwatch = Stopwatch.StartNew();
            preparedTranscript = projection.Transcript.Select(FromSnapshot).ToArray();
            Logger.LogInformation(
                "Prebuilt transcript view-models off UI thread. conversationId={ConversationId} messageCount={MessageCount} elapsedMs={ElapsedMs}",
                projection.HydratedConversationId,
                preparedTranscript.Count,
                prebuildStopwatch.ElapsedMilliseconds);
        }

        await PostToUiAsync(() =>
        {
            if (_disposed)
            {
                return;
            }

            if (activationVersion.HasValue && !_conversationActivationOrchestrator.IsLatestActivationVersion(activationVersion.Value))
            {
                return;
            }

            // Note: ApplyStoreProjection now internally calls SyncMessageHistory
            // before notifying state changes to avoid race conditions on the UI thread.
            ApplyStoreProjection(projection, preparedTranscript);
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes an action on the UI thread.
    /// WinUI and Uno collections (like MessageHistory) MUST be updated on the main thread
    /// to avoid random "The application called an interface that was marshalled for a different thread" crashes.
    /// </summary>
    private Task PostToUiAsync(Action action) => _uiDispatcher.EnqueueAsync(action);
    private Task PostToUiAsync(Func<Task> function) => _uiDispatcher.EnqueueAsync(function);

    private bool ShouldApplyStoreProjection(ChatUiProjection projection, long activationVersion)
        => _chatUiProjectionApplicationCoordinator.ShouldApply(projection, activationVersion);

    private void QueuePreviewSnapshotPersistence(ChatUiProjection projection)
    {
        var snapshot = _transcriptProjectionCoordinator.PreparePreviewSnapshotSave(
            projection.HydratedConversationId,
            projection.Transcript,
            projection.IsHydrating);
        if (snapshot is null)
        {
            return;
        }

        _ = _previewSnapshotWorkQueue.Enqueue(() =>
        {
            _transcriptProjectionCoordinator.SavePreviewSnapshot(snapshot);
            return Task.CompletedTask;
        });
    }

    private void ClearCurrentPromptOnUiThread()
    {
        if (_uiDispatcher.HasThreadAccess)
        {
            CurrentPrompt = string.Empty;
            return;
        }

        _ = PostToUiAsync(() => CurrentPrompt = string.Empty);
    }

    private void RestoreCurrentPromptOnUiThread(string promptText)
    {
        if (_uiDispatcher.HasThreadAccess)
        {
            if (string.IsNullOrWhiteSpace(CurrentPrompt))
            {
                CurrentPrompt = promptText;
            }

            return;
        }

        _ = PostToUiAsync(() =>
        {
            if (string.IsNullOrWhiteSpace(CurrentPrompt))
            {
                CurrentPrompt = promptText;
            }
        });
    }

    public Task RestoreConversationsAsync()
        => RestoreAsync();

    private Task EnsureConversationWorkspaceRestoredAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_restoreSync)
        {
            _restoreTask ??= RestoreConversationsCoreAsync();
            return _restoreTask;
        }
    }

    private async Task RestoreConversationsCoreAsync()
    {
        try
        {
            await _conversationWorkspace.RestoreAsync().ConfigureAwait(false);
            await RestoreConversationBindingsFromWorkspaceAsync().ConfigureAwait(false);
            var restoredConversationId = _conversationWorkspace.LastActiveConversationId;
            await BootstrapSelectedProfileFromWorkspaceAsync(restoredConversationId).ConfigureAwait(false);
            await SelectAndHydrateConversationAsync(restoredConversationId).ConfigureAwait(false);
            await TryAutoConnectAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Conversation workspace restore failed");
        }
    }

    private async Task RestoreConversationBindingsFromWorkspaceAsync()
    {
        foreach (var conversationId in _conversationWorkspace.GetKnownConversationIds())
        {
            var binding = _conversationWorkspace.GetRemoteBinding(conversationId);
            if (binding is null)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(binding.RemoteSessionId) && string.IsNullOrWhiteSpace(binding.BoundProfileId))
            {
                continue;
            }

            await _chatStore.Dispatch(new SetBindingSliceAction(
                new ConversationBindingSlice(
                    binding.ConversationId,
                    binding.RemoteSessionId,
                    binding.BoundProfileId))).ConfigureAwait(false);
        }
    }

    private async Task BootstrapSelectedProfileFromWorkspaceAsync(string? conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return;
        }

        var connectionState = await _chatConnectionStore.State ?? ChatConnectionState.Empty;
        if (!string.IsNullOrWhiteSpace(connectionState.SettingsSelectedProfileId))
        {
            return;
        }

        var binding = _conversationWorkspace.GetRemoteBinding(conversationId);
        if (string.IsNullOrWhiteSpace(binding?.BoundProfileId))
        {
            return;
        }

        await _chatConnectionStore.Dispatch(new SetSettingsSelectedProfileAction(binding.BoundProfileId)).ConfigureAwait(false);
    }

    private void NotifyConversationListChanged()
    {
        ConversationListVersion = _conversationWorkspace.ConversationListVersion;
        _conversationCatalogPresenter.Refresh(_conversationWorkspace.GetCatalog());
        OnPropertyChanged(nameof(GetKnownConversationIds));
        RefreshMiniWindowSessions();
    }

    private void RefreshMiniWindowSessions()
    {
        try
        {
            MiniWindowSessions.Clear();
            foreach (var conversationId in GetKnownConversationIds())
            {
                var displayName = ResolveSessionDisplayName(conversationId);
                MiniWindowSessions.Add(new MiniWindowConversationItemViewModel(
                    conversationId,
                    displayName,
                    CreateMiniWindowCompactDisplayName(displayName)));
            }
        }
        catch
        {
            // Mini window list is best-effort and should never break the main chat experience.
        }
    }

    private void SyncMessageHistory(string? conversationId, IImmutableList<ConversationMessageSnapshot> transcript)
    {
        var previousCount = MessageHistory.Count;
        var messages = transcript ?? ImmutableList<ConversationMessageSnapshot>.Empty;
        for (int i = 0; i < messages.Count; i++)
        {
            var message = messages[i];
            if (i < MessageHistory.Count)
            {
                if (MessageHistory[i].Id != message.Id)
                {
                    while (MessageHistory.Count > i) MessageHistory.RemoveAt(i);
                    MessageHistory.Add(FromSnapshot(message));
                }
                else if (!MatchesSnapshot(MessageHistory[i], message))
                {
                    MessageHistory[i] = FromSnapshot(message);
                }
            }
            else
            {
                MessageHistory.Add(FromSnapshot(message));
            }
        }

        while (MessageHistory.Count > messages.Count)
        {
            MessageHistory.RemoveAt(MessageHistory.Count - 1);
        }

        var transcriptOwnerChanged = UpdateVisibleTranscriptConversationId(conversationId, MessageHistory.Count > 0);
        if (previousCount != MessageHistory.Count || transcriptOwnerChanged)
        {
            OnPropertyChanged(nameof(HasVisibleTranscriptContent));
            OnPropertyChanged(nameof(OverlayStatusText));
            OnPropertyChanged(nameof(ShouldShowBlockingLoadingMask));
            OnPropertyChanged(nameof(ShouldShowLoadingOverlayPresenter));
        }
    }

    private void RaiseTranscriptProjectionStateChanged()
    {
        OnPropertyChanged(nameof(HasVisibleTranscriptContent));
        OnPropertyChanged(nameof(OverlayStatusText));
        OnPropertyChanged(nameof(ShouldShowBlockingLoadingMask));
        OnPropertyChanged(nameof(ShouldShowLoadingOverlayPresenter));
    }

    private static ConversationMessageSnapshot ToSnapshot(ChatMessageViewModel vm)
    {
        return new ConversationMessageSnapshot
        {
            Id = vm.Id,
            Timestamp = vm.Timestamp.ToUniversalTime(),
            IsOutgoing = vm.IsOutgoing,
            ContentType = vm.ContentType ?? string.Empty,
            Title = vm.Title ?? string.Empty,
            TextContent = vm.TextContent ?? string.Empty,
            ImageData = vm.ImageData ?? string.Empty,
            ImageMimeType = vm.ImageMimeType ?? string.Empty,
            AudioData = vm.AudioData ?? string.Empty,
            AudioMimeType = vm.AudioMimeType ?? string.Empty,
            ProtocolMessageId = null,
            ToolCallId = vm.ToolCallId,
            ToolCallKind = vm.ToolCallKind,
            ToolCallStatus = vm.ToolCallStatus,
            ToolCallJson = vm.ToolCallJson,
            ToolCallContent = CloneToolCallContentList(vm.ToolCallContent),
            PlanEntry = vm.PlanEntry != null
                ? new ConversationPlanEntrySnapshot
                {
                    Content = vm.PlanEntry.Content,
                    Status = vm.PlanEntry.Status,
                    Priority = vm.PlanEntry.Priority
                }
                : null,
            ModeId = vm.ModeId
        };
    }

    private static ChatMessageViewModel FromSnapshot(ConversationMessageSnapshot s)
    {
        // Minimal, stable restoration for persisted transcripts.
        var vm = new ChatMessageViewModel
        {
            Id = string.IsNullOrWhiteSpace(s.Id) ? Guid.NewGuid().ToString() : s.Id,
            Timestamp = s.Timestamp.ToLocalTime(),
            IsOutgoing = s.IsOutgoing,
            ContentType = s.ContentType ?? string.Empty,
            Title = s.Title ?? string.Empty,
            TextContent = s.TextContent ?? string.Empty,
            ImageData = s.ImageData ?? string.Empty,
            ImageMimeType = s.ImageMimeType ?? string.Empty,
            AudioData = s.AudioData ?? string.Empty,
            AudioMimeType = s.AudioMimeType ?? string.Empty,
            ToolCallId = s.ToolCallId,
            ToolCallKind = s.ToolCallKind,
            ToolCallStatus = s.ToolCallStatus,
            ToolCallJson = s.ToolCallJson,
            ToolCallContent = CloneToolCallContentList(s.ToolCallContent),
            ModeId = s.ModeId
        };

        if (s.PlanEntry != null)
        {
            vm.PlanEntry = new PlanEntryViewModel
            {
                Content = s.PlanEntry.Content ?? string.Empty,
                Status = s.PlanEntry.Status,
                Priority = s.PlanEntry.Priority
            };
        }

        return vm;
    }

    private static bool MatchesSnapshot(ChatMessageViewModel viewModel, ConversationMessageSnapshot snapshot)
    {
        return string.Equals(viewModel.Id, snapshot.Id, StringComparison.Ordinal)
            && viewModel.Timestamp == snapshot.Timestamp.ToLocalTime()
            && viewModel.IsOutgoing == snapshot.IsOutgoing
            && string.Equals(viewModel.ContentType ?? string.Empty, snapshot.ContentType ?? string.Empty, StringComparison.Ordinal)
            && string.Equals(viewModel.Title ?? string.Empty, snapshot.Title ?? string.Empty, StringComparison.Ordinal)
            && string.Equals(viewModel.TextContent ?? string.Empty, snapshot.TextContent ?? string.Empty, StringComparison.Ordinal)
            && string.Equals(viewModel.ImageData ?? string.Empty, snapshot.ImageData ?? string.Empty, StringComparison.Ordinal)
            && string.Equals(viewModel.ImageMimeType ?? string.Empty, snapshot.ImageMimeType ?? string.Empty, StringComparison.Ordinal)
            && string.Equals(viewModel.AudioData ?? string.Empty, snapshot.AudioData ?? string.Empty, StringComparison.Ordinal)
            && string.Equals(viewModel.AudioMimeType ?? string.Empty, snapshot.AudioMimeType ?? string.Empty, StringComparison.Ordinal)
            && string.Equals(viewModel.ToolCallId, snapshot.ToolCallId, StringComparison.Ordinal)
            && viewModel.ToolCallKind == snapshot.ToolCallKind
            && viewModel.ToolCallStatus == snapshot.ToolCallStatus
            && string.Equals(viewModel.ToolCallJson, snapshot.ToolCallJson, StringComparison.Ordinal)
            && ToolCallContentSnapshots.SequenceEquals(viewModel.ToolCallContent, snapshot.ToolCallContent)
            && string.Equals(viewModel.ModeId, snapshot.ModeId, StringComparison.Ordinal)
            && PlanEntryMatches(viewModel.PlanEntry, snapshot.PlanEntry);
    }

    private static bool PlanEntryMatches(PlanEntryViewModel? viewModel, ConversationPlanEntrySnapshot? snapshot)
    {
        if (viewModel is null && snapshot is null)
        {
            return true;
        }

        if (viewModel is null || snapshot is null)
        {
            return false;
        }

        return string.Equals(viewModel.Content ?? string.Empty, snapshot.Content ?? string.Empty, StringComparison.Ordinal)
            && viewModel.Status == snapshot.Status
            && viewModel.Priority == snapshot.Priority;
    }

    private static ConversationMessageSnapshot CloneSnapshot(ConversationMessageSnapshot snapshot)
    {
        return new ConversationMessageSnapshot
        {
            Id = snapshot.Id,
            Timestamp = snapshot.Timestamp,
            IsOutgoing = snapshot.IsOutgoing,
            ContentType = snapshot.ContentType,
            Title = snapshot.Title,
            TextContent = snapshot.TextContent,
            ImageData = snapshot.ImageData,
            ImageMimeType = snapshot.ImageMimeType,
            AudioData = snapshot.AudioData,
            AudioMimeType = snapshot.AudioMimeType,
            ProtocolMessageId = snapshot.ProtocolMessageId,
            ToolCallId = snapshot.ToolCallId,
            ToolCallKind = snapshot.ToolCallKind,
            ToolCallStatus = snapshot.ToolCallStatus,
            ToolCallJson = snapshot.ToolCallJson,
            ToolCallContent = CloneToolCallContentList(snapshot.ToolCallContent),
            PlanEntry = ClonePlanEntrySnapshot(snapshot.PlanEntry),
            ModeId = snapshot.ModeId
        };
    }

    private static ConversationPlanEntrySnapshot? ClonePlanEntrySnapshot(ConversationPlanEntrySnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return null;
        }

        return new ConversationPlanEntrySnapshot
        {
            Content = snapshot.Content,
            Status = snapshot.Status,
            Priority = snapshot.Priority
        };
    }

    private static bool IsThinkingPlaceholder(ConversationMessageSnapshot message)
        => string.Equals(message.ContentType, "thinking", StringComparison.OrdinalIgnoreCase);

    private ConversationWorkspaceSnapshot? TryGetConversationSnapshot(string? conversationId)
        => _conversationWorkspace.GetConversationSnapshot(conversationId);

    private ChatUiProjection CreateProjection(ChatState state, ChatConnectionState connectionState)
        => _chatStateProjector.Apply(
            state,
            connectionState,
            state.HydratedConversationId,
            ToBindingState(state.Binding));

    private static ConversationRemoteBindingState? ToBindingState(ConversationBindingSlice? binding)
        => binding is null || string.IsNullOrWhiteSpace(binding.ConversationId)
            ? null
            : new ConversationRemoteBindingState(
                binding.ConversationId!,
                binding.RemoteSessionId,
                binding.ProfileId);

    private static string? ResolveActiveConversationId(ChatState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return string.IsNullOrWhiteSpace(state.HydratedConversationId)
            ? null
            : state.HydratedConversationId;
    }

    private async ValueTask<ConversationRemoteBindingState?> ResolveActiveConversationBindingAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var state = await _chatStore.State ?? ChatState.Empty;
        var conversationId = ResolveActiveConversationId(state);
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return null;
        }

        var binding = state.ResolveBinding(conversationId);
        if (binding != null)
        {
            return ToBindingState(binding);
        }

        var connectionState = await _chatConnectionStore.State ?? ChatConnectionState.Empty;
        return new ConversationRemoteBindingState(
            conversationId,
            null,
            connectionState.ForegroundTransportProfileId);
    }

    private string ResolveSessionDisplayName(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return string.Empty;
        }

        var session = _sessionManager.GetSession(sessionId);
        var name = session?.DisplayName;
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name.Trim();
        }

        return SessionNamePolicy.CreateDefault(sessionId);
    }

    private static string CreateMiniWindowCompactDisplayName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return string.Empty;
        }

        var normalized = NormalizeMiniWindowDisplayName(displayName);
        var normalizedInfo = new StringInfo(normalized);
        if (normalizedInfo.LengthInTextElements <= MiniWindowCompactDisplayNameMaxLength)
        {
            return normalized;
        }

        return normalizedInfo.SubstringByTextElements(0, MiniWindowCompactDisplayNameMaxLength - 3) + "...";
    }

    private static string NormalizeMiniWindowDisplayName(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(trimmed.Length);
        var previousWasWhitespace = false;
        foreach (var character in trimmed)
        {
            if (char.IsWhiteSpace(character))
            {
                if (previousWasWhitespace)
                {
                    continue;
                }

                builder.Append(' ');
                previousWasWhitespace = true;
                continue;
            }

            builder.Append(character);
            previousWasWhitespace = false;
        }

        return builder.ToString();
    }

    public async Task<ConversationMutationResult> ArchiveConversationAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        var result = await _conversationCatalogFacade
            .ArchiveConversationAsync(conversationId, cancellationToken, CurrentSessionId)
            .ConfigureAwait(true);

        if (result.Succeeded && result.ClearedActiveConversation)
        {
            await _chatStore.Dispatch(new SelectConversationAction(null)).ConfigureAwait(false);
        }

        return result;
    }

    public async Task<ConversationMutationResult> DeleteConversationAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        var result = await _conversationCatalogFacade
            .DeleteConversationAsync(conversationId, cancellationToken, CurrentSessionId)
            .ConfigureAwait(true);

        if (result.Succeeded && result.ClearedActiveConversation)
        {
            await _chatStore.Dispatch(new SelectConversationAction(null)).ConfigureAwait(false);
        }

        return result;
    }

    public string[] GetKnownConversationIds() => _conversationCatalogFacade.GetKnownConversationIds();

    public async Task EnsureAcpProfilesLoadedAsync()
    {
        if (_acpProfiles.IsLoading || _acpProfiles.Profiles.Count > 0)
        {
            return;
        }

        try
        {
            await _acpProfiles.RefreshCommand.ExecuteAsync(null);
            _suppressAcpProfileConnect = true;
            _suppressStoreProfileProjection = true;
            try
            {
                SelectedAcpProfile = _acpProfiles.SelectedProfile;
            }
            finally
            {
                _suppressStoreProfileProjection = false;
                _suppressAcpProfileConnect = false;
            }
        }
        catch
        {
        }
    }

    public async Task TryAutoConnectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_autoConnectAttempted)
        {
            return;
        }

        var connectionState = await _chatConnectionStore.State ?? ChatConnectionState.Empty;
        var profileId = connectionState.SettingsSelectedProfileId;
        if (string.IsNullOrWhiteSpace(profileId))
        {
            profileId = _preferences.LastSelectedServerId;
            if (!string.IsNullOrWhiteSpace(profileId))
            {
                await _chatConnectionStore.Dispatch(new SetSettingsSelectedProfileAction(profileId)).ConfigureAwait(false);
            }
        }

        if (string.IsNullOrWhiteSpace(profileId))
        {
            return;
        }

        if (IsConnected || IsConnecting || IsInitializing)
        {
            return;
        }

        // Only mark as attempted once we actually have enough information to try.
        _autoConnectAttempted = true;

        try
        {
            await EnsureAcpProfilesLoadedAsync();
            cancellationToken.ThrowIfCancellationRequested();

            ServerConfiguration? config;
            // The config may not be loaded into the list yet (e.g. first launch), so we load by id as fallback.
            config = _acpProfiles.Profiles.FirstOrDefault(p => p.Id == profileId)
                    ?? await _configurationService.LoadConfigurationAsync(profileId);

            if (config == null)
            {
                return;
            }

            _suppressAcpProfileConnect = true;
            _suppressStoreProfileProjection = true;
            try
            {
                await PostToUiAsync(() =>
                {
                    var selectedProfile = ResolveLoadedProfileSelection(config);
                    SelectedAcpProfile = selectedProfile;
                    _acpProfiles.SelectedProfile = selectedProfile;
                }).ConfigureAwait(false);
            }
            finally
            {
                _suppressStoreProfileProjection = false;
                _suppressAcpProfileConnect = false;
            }

            cancellationToken.ThrowIfCancellationRequested();
            await ConnectToAcpProfileCoreAsync(config, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _autoConnectAttempted = false;
            return;
        }
        catch
        {
        }
    }

    [RelayCommand]
    private Task ConnectToAcpProfileAsync(ServerConfiguration? profile)
        => ConnectToAcpProfileCoreAsync(profile, CancellationToken.None);

    private async Task ConnectToAcpProfileCoreAsync(ServerConfiguration? profile, CancellationToken cancellationToken)
    {
        if (profile == null)
        {
            return;
        }

        var ambientConnectionRequest = BeginAmbientConnectionRequest(cancellationToken);
        try
        {
            await ConnectToAcpProfileCoreAsync(
                    profile,
                    AcpConnectionContext.None,
                    ambientConnectionRequest.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ambientConnectionRequest.IsCancellationRequested)
        {
            if (_chatService is not { IsConnected: true })
            {
                await _chatConnectionStore
                    .Dispatch(new SetConnectionPhaseAction(ConnectionPhase.Disconnected))
                    .ConfigureAwait(false);
            }

            throw;
        }
        finally
        {
            EndAmbientConnectionRequest(ambientConnectionRequest);
        }
    }

    private async Task ConnectToAcpProfileCoreAsync(
        ServerConfiguration? profile,
        AcpConnectionContext connectionContext,
        CancellationToken cancellationToken)
    {
        if (profile == null)
        {
            return;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await PrepareSelectedProfileConnectionAsync(profile, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var scopedSink = CreateScopedAcpCoordinatorSink(connectionContext);
            await PostToUiAsync(() =>
            {
                _suppressAutoConnectFromPreferenceChange = true;
                _acpProfiles.MarkLastConnected(profile);
                _acpProfiles.SelectedProfile = ResolveLoadedProfileSelection(profile);
            }).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var result = connectionContext.Equals(AcpConnectionContext.None)
                ? await _acpConnectionCommands
                    .ConnectToProfileAsync(profile, TransportConfig, scopedSink, cancellationToken)
                    .ConfigureAwait(false)
                : await _acpConnectionCommands
                    .ConnectToProfileAsync(profile, TransportConfig, scopedSink, connectionContext, cancellationToken)
                    .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsCurrentConnectionContext(connectionContext))
            {
                return;
            }

            await PostToUiAsync(() =>
            {
                _authenticationCoordinator.CacheAuthMethods(result.InitializeResponse);
                _authenticationCoordinator.ClearAuthenticationRequirement(_acpConnectionCoordinator);
                _ = _authenticationCoordinator.UpdateAgentInfoAsync(_chatService, _chatStore, SelectedProfileId);
                ShowTransportConfigPanel = false;
            }).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            await _chatConnectionStore
                .Dispatch(new SetConnectionPhaseAction(ConnectionPhase.Disconnected, ex.Message))
                .ConfigureAwait(false);
            await ApplyCurrentStoreProjectionAsync().ConfigureAwait(false);
            Logger.LogError(ex, "Failed to connect to ACP profile {ProfileId}", profile.Id);
            throw;
        }
        finally
        {
            await PostToUiAsync(() =>
            {
                _suppressAutoConnectFromPreferenceChange = false;
            }).ConfigureAwait(false);
        }
    }

    [RelayCommand]
        private async Task ApplyTransportConfigAsync()
        {
            await ApplyTransportConfigCoreAsync(preserveConversation: false);
        }

        /// <summary>
        /// Core logic for applying a new transport configuration.
        /// This method tears down the existing connection and establishes a new one,
        /// optionally preserving the active conversation's local state.
        /// </summary>
        private async Task ApplyTransportConfigCoreAsync(bool preserveConversation)
        {
            try
            {
                var result = preserveConversation
                    ? await _acpConnectionCommands
                        .ApplyTransportConfigurationAsync(
                            TransportConfig,
                            this,
                            new AcpConnectionContext(CurrentSessionId, PreserveConversation: true),
                            CancellationToken.None)
                        .ConfigureAwait(false)
                    : await _acpConnectionCommands
                        .ApplyTransportConfigurationAsync(TransportConfig, this, preserveConversation)
                        .ConfigureAwait(false);

            _authenticationCoordinator.CacheAuthMethods(result.InitializeResponse);
            _authenticationCoordinator.ClearAuthenticationRequirement(_acpConnectionCoordinator);
            _ = _authenticationCoordinator.UpdateAgentInfoAsync(_chatService, _chatStore, SelectedProfileId);

                if (string.IsNullOrWhiteSpace(CurrentSessionId))
                {
                    var sessionId = Guid.NewGuid().ToString("N");
                    await _sessionManager.CreateSessionAsync(sessionId, GetActiveSessionCwdOrDefault()).ConfigureAwait(false);
                    await ActivateConversationAsync(sessionId).ConfigureAwait(false);
                }

                ShowTransportConfigPanel = false;
            }
        catch (OperationCanceledException)
        {
            Logger.LogDebug("Transport apply was superseded by a newer request.");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error during connection");
        }
        }

    private async Task ApplySessionNewResponseAsync(string conversationId, SessionNewResponse response)
    {
        SetConversationConfigAuthority(conversationId, response.ConfigOptions != null);

        var delta = _acpSessionUpdateProjector.ProjectSessionNew(response);
        await ApplySessionUpdateDeltaAsync(conversationId, delta).ConfigureAwait(true);
        Logger.LogInformation(
            "Session modes loaded: {Count}",
            delta.AvailableModes?.Count ?? 0);
    }

    private async Task ApplySessionLoadResponseAsync(string conversationId, SessionLoadResponse response)
    {
        var latestActivationVersion = _conversationActivationOrchestrator.CurrentActivationVersion;
        SetConversationConfigAuthority(conversationId, response.ConfigOptions != null);

        var delta = _acpSessionUpdateProjector.ProjectSessionLoad(response);
        await ApplySessionUpdateDeltaAsync(conversationId, delta).ConfigureAwait(true);
        
        Logger.LogInformation(
            "Session load state projected. ConversationId={ConversationId} ModeCount={Count} LatestActivationVersion={ActivationVersion}",
            conversationId,
            delta.AvailableModes?.Count ?? 0,
            latestActivationVersion);
    }

    [RelayCommand]
    private void ToggleTransportConfigPanel()
    {
        ShowTransportConfigPanel = !ShowTransportConfigPanel;
    }

       private void SubscribeToChatService(IChatService chatService)
       {
           chatService.SessionUpdateReceived += OnSessionUpdateReceived;
           chatService.PermissionRequestReceived += OnPermissionRequestReceived;
            chatService.FileSystemRequestReceived += OnFileSystemRequestReceived;
            chatService.TerminalRequestReceived += OnTerminalRequestReceived;
            chatService.TerminalStateChangedReceived += OnTerminalStateChangedReceived;
            chatService.AskUserRequestReceived += OnAskUserRequestReceived;
            chatService.ErrorOccurred += OnErrorOccurred;

           _ = _authenticationCoordinator.UpdateAgentInfoAsync(_chatService, _chatStore, SelectedProfileId);
       }

       private void UnsubscribeFromChatService(IChatService chatService)
       {
           chatService.SessionUpdateReceived -= OnSessionUpdateReceived;
           chatService.PermissionRequestReceived -= OnPermissionRequestReceived;
            chatService.FileSystemRequestReceived -= OnFileSystemRequestReceived;
            chatService.TerminalRequestReceived -= OnTerminalRequestReceived;
            chatService.TerminalStateChangedReceived -= OnTerminalStateChangedReceived;
            chatService.AskUserRequestReceived -= OnAskUserRequestReceived;
            chatService.ErrorOccurred -= OnErrorOccurred;
        }

        private void SubscribeToEvents()
      {
          // Only subscribe if _chatService is not null. 
          // In constructor, _chatService might be null; it will be created in ApplyTransportConfigAsync.
          if (_chatService != null)
          {
              SubscribeToChatService(_chatService);

              _ = _authenticationCoordinator.UpdateAgentInfoAsync(_chatService, _chatStore, SelectedProfileId);

          }
      }

    private sealed class ScopedAcpChatCoordinatorSink : IAcpChatCoordinatorSink
    {
        private readonly ChatViewModel _owner;
        private readonly AcpConnectionContext _connectionContext;
        private readonly ScopedBindingCommands _bindingCommands;

        public ScopedAcpChatCoordinatorSink(ChatViewModel owner, AcpConnectionContext connectionContext)
        {
            _owner = owner;
            _connectionContext = connectionContext;
            _bindingCommands = new ScopedBindingCommands(owner, connectionContext);
        }

        public event PropertyChangedEventHandler? PropertyChanged
        {
            add => _owner.PropertyChanged += value;
            remove => _owner.PropertyChanged -= value;
        }

        public IChatService? CurrentChatService => _owner.CurrentChatService;
        public bool IsConnected => _owner.IsConnected;
        public bool IsConnecting => _owner.IsConnecting;
        public bool IsInitializing => _owner.IsInitializing;
        public bool IsSessionActive => _owner.IsSessionActive;
        public bool IsAuthenticationRequired => _owner.IsAuthenticationRequired;
        public string? ConnectionErrorMessage => _owner.ConnectionErrorMessage;
        public string? AuthenticationHintMessage => _owner.AuthenticationHintMessage;
        public string? AgentName => _owner.AgentName;
        public string? AgentVersion => _owner.AgentVersion;
        public string? CurrentSessionId => _owner.CurrentSessionId;
        public bool IsHydrating => _owner.IsHydrating;
        public bool IsInitialized => _owner.IsInitialized;
        public string? CurrentRemoteSessionId => _owner.CurrentRemoteSessionId;
        public string? SelectedProfileId => _owner.SelectedProfileId;
        public string? ConnectionInstanceId => _owner.ConnectionInstanceId;
        public long ConnectionGeneration => _owner.ConnectionGeneration;
        public IUiDispatcher Dispatcher => _owner.Dispatcher;
        public IConversationBindingCommands ConversationBindingCommands => _bindingCommands;

        public ValueTask<ConversationRemoteBindingState?> GetCurrentRemoteBindingAsync(CancellationToken cancellationToken = default)
            => _owner.GetCurrentRemoteBindingAsync(cancellationToken);

        public ValueTask<ConversationRemoteBindingState?> GetConversationRemoteBindingAsync(
            string conversationId,
            CancellationToken cancellationToken = default)
            => _owner.GetConversationRemoteBindingAsync(conversationId, cancellationToken);

        public void SelectProfile(ServerConfiguration profile)
        {
            if (CanMutate())
            {
                _owner.SelectProfile(profile);
            }
        }

        public Task SelectProfileAsync(ServerConfiguration profile, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return CanMutate()
                ? _owner.SelectProfileAsync(profile, cancellationToken)
                : Task.CompletedTask;
        }

        public void ReplaceChatService(IChatService? chatService)
        {
            if (CanMutate())
            {
                _owner.ReplaceChatService(chatService);
            }
        }

        public Task ReplaceChatServiceAsync(IChatService? chatService, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return CanMutate()
                ? _owner.ReplaceChatServiceAsync(chatService, cancellationToken)
                : Task.CompletedTask;
        }

        public Task ReplaceChatServiceAsync(IChatService? chatService, ServiceReplaceIntent intent, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return CanMutate()
                ? _owner.ReplaceChatServiceWithIntentAsync(chatService, intent, cancellationToken)
                : Task.CompletedTask;
        }

        public void UpdateConnectionState(bool isConnecting, bool isConnected, bool isInitialized, string? errorMessage)
        {
            if (CanMutate())
            {
                _owner.UpdateConnectionState(isConnecting, isConnected, isInitialized, errorMessage);
            }
        }

        public void UpdateInitializationState(bool isInitializing)
        {
            if (CanMutate())
            {
                _owner.UpdateInitializationState(isInitializing);
            }
        }

        public void UpdateAuthenticationState(bool isRequired, string? hintMessage)
        {
            if (CanMutate())
            {
                _owner.UpdateAuthenticationState(isRequired, hintMessage);
            }
        }

        public void UpdateAgentIdentity(string? agentName, string? agentVersion)
        {
            if (CanMutate())
            {
                _owner.UpdateAgentIdentity(agentName, agentVersion);
            }
        }

        public Task ResetHydratedConversationForResyncAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return CanMutate()
                ? _owner.ResetHydratedConversationForResyncAsync(cancellationToken)
                : Task.CompletedTask;
        }

        public Task ResetConversationForResyncAsync(string conversationId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return CanMutate()
                ? _owner.ResetConversationForResyncAsync(conversationId, cancellationToken)
                : Task.CompletedTask;
        }

        public string GetActiveSessionCwdOrDefault() => _owner.GetActiveSessionCwdOrDefault();

        public string GetSessionCwdOrDefault(string conversationId) => _owner.GetSessionCwdOrDefault(conversationId);

        public Task SetIsHydratingAsync(bool isHydrating, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return CanMutate()
                ? _owner.SetIsHydratingAsync(isHydrating, cancellationToken)
                : Task.CompletedTask;
        }

        public Task SetConversationHydratingAsync(
            string conversationId,
            bool isHydrating,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return CanMutate()
                ? _owner.SetConversationHydratingAsync(conversationId, isHydrating, cancellationToken)
                : Task.CompletedTask;
        }

        public Task MarkActiveConversationRemoteHydratedAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return CanMutate()
                ? _owner.MarkActiveConversationRemoteHydratedAsync(cancellationToken)
                : Task.CompletedTask;
        }

        public Task MarkConversationRemoteHydratedAsync(
            string conversationId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return CanMutate()
                ? _owner.MarkConversationRemoteHydratedAsync(conversationId, cancellationToken)
                : Task.CompletedTask;
        }

        public Task ApplyConversationSessionLoadResponseAsync(
            string conversationId,
            SessionLoadResponse response,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return CanMutate()
                ? _owner.ApplyConversationSessionLoadResponseAsync(conversationId, response, cancellationToken)
                : Task.CompletedTask;
        }

        private bool CanMutate() => _owner.IsCurrentConnectionContext(_connectionContext);
    }

    private sealed class ScopedBindingCommands : IConversationBindingCommands
    {
        private readonly ChatViewModel _owner;
        private readonly AcpConnectionContext _connectionContext;

        public ScopedBindingCommands(ChatViewModel owner, AcpConnectionContext connectionContext)
        {
            _owner = owner;
            _connectionContext = connectionContext;
        }

        public ValueTask<BindingUpdateResult> UpdateBindingAsync(string conversationId, string? remoteSessionId, string? boundProfileId)
            => _owner.IsCurrentConnectionContext(_connectionContext)
                ? _owner.ConversationBindingCommands.UpdateBindingAsync(conversationId, remoteSessionId, boundProfileId)
                : ValueTask.FromResult(BindingUpdateResult.Success());
    }

    private sealed class NoopAcpConnectionCoordinator : IAcpConnectionCoordinator
    {
        public static NoopAcpConnectionCoordinator Instance { get; } = new();

        public Task SetConnectingAsync(string? profileId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetInitializingAsync(string? profileId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetConnectedAsync(string? profileId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetConnectionInstanceIdAsync(string? connectionInstanceId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetDisconnectedAsync(string? errorMessage = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetAuthenticationRequiredAsync(string? hintMessage, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ClearAuthenticationRequiredAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ResetAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ResyncAsync(IAcpChatCoordinatorSink sink, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

       public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
       {
           if (_disposed)
           {
               return;
           }

           _disposed = true;

           if (!disposing)
           {
               return;
           }

           if (_chatService != null)
           {
               UnsubscribeFromChatService(_chatService);
           }

           _acpProfiles.PropertyChanged -= OnAcpProfilesPropertyChanged;
           _acpProfiles.Profiles.CollectionChanged -= OnAcpProfilesCollectionChanged;
           _preferences.PropertyChanged -= OnPreferencesPropertyChanged;
           _preferences.Projects.CollectionChanged -= OnProjectAffinityPreferencesCollectionChanged;
           _preferences.ProjectPathMappings.CollectionChanged -= OnProjectAffinityPreferencesCollectionChanged;
           _conversationWorkspace.PropertyChanged -= OnConversationWorkspacePropertyChanged;
           _voiceInputService.PartialResultReceived -= OnVoiceInputPartialResultReceived;
           _voiceInputService.FinalResultReceived -= OnVoiceInputFinalResultReceived;
           _voiceInputService.SessionEnded -= OnVoiceInputSessionEnded;
           _voiceInputService.ErrorOccurred -= OnVoiceInputErrorOccurred;
           if (_shellNavigationRuntimeState is not null)
           {
               _shellNavigationRuntimeState.PropertyChanged -= OnShellNavigationRuntimeStatePropertyChanged;
           }
           _planEntriesProjectionCoordinator.Observe(null, null);
           if (_observedPendingAskUserRequest != null)
           {
               _observedPendingAskUserRequest.PropertyChanged -= OnPendingAskUserRequestPropertyChanged;
               _observedPendingAskUserRequest = null;
           }

           _sendPromptCts?.Cancel();
           _voiceInputCts?.Cancel();
           _transientNotificationCts?.Cancel();
           try { _voiceInputService.StopAsync().GetAwaiter().GetResult(); } catch { }
           StopStoreProjection();

            try { _sendPromptCts?.Dispose(); } catch { }
            try { _voiceInputCts?.Dispose(); } catch { }
            try { _transientNotificationCts?.Dispose(); } catch { }
            try { _conversationActivationOrchestrator.Dispose(); } catch { }
            try { _localTerminalPanelCoordinator?.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { }
            try { _remoteConversationActivationGate.Dispose(); } catch { }

            _selectedProfileConnectTask = null;
            _pendingSelectedProfileConnect = null;
            _sendPromptCts = null;
       _voiceInputCts = null;
       _transientNotificationCts = null;
       }

    private void RaisePlanEntryDerivedPropertyNotifications()
    {
        OnPropertyChanged(nameof(HasPlanEntries));
        OnPropertyChanged(nameof(ShouldShowPlanList));
        OnPropertyChanged(nameof(ShouldShowPlanEmpty));
    }
}
