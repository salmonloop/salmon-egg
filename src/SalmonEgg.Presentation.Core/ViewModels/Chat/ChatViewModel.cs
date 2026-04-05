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
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Interfaces;
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
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.ViewModels.Settings;

namespace SalmonEgg.Presentation.ViewModels.Chat;

/// <summary>
/// Main ViewModel for the Chat interface.
/// Orchestrates the lifecycle of conversations, ACP agent connectivity, and UI state projection.
/// Follows the MVVM pattern where the View is driven strictly by this ViewModel and its projected state.
/// </summary>
public partial class ChatViewModel : ViewModelBase, IDisposable, IConversationCatalog, IAcpChatCoordinatorSink, IConversationSessionSwitcher, IConversationActivationPreview
{
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
    private static readonly TimeSpan LoadResponseHydrationMinimumVisibleDuration = TimeSpan.FromMilliseconds(600);
    private const int RemoteReplayPollDelayMilliseconds = 50;
    private AcpHydrationCompletionMode _hydrationCompletionMode = AcpHydrationCompletionMode.StrictReplay;
    private readonly ChatServiceFactory _chatServiceFactory;
    private readonly ChatConversationWorkspace _conversationWorkspace;
    private readonly IConversationActivationCoordinator _conversationActivationCoordinator;
    private readonly IConversationBindingCommands _bindingCommands;
    private readonly IAcpConnectionCommands _acpConnectionCommands;
    private readonly IAcpConnectionCoordinator _acpConnectionCoordinator;
    private readonly IProjectAffinityResolver _projectAffinityResolver;
    private readonly IConfigurationService _configurationService;
    private readonly AppPreferencesViewModel _preferences;
    private readonly AcpProfilesViewModel _acpProfiles;
    private readonly ISessionManager _sessionManager;
    private readonly IMiniWindowCoordinator _miniWindowCoordinator;
    private readonly ConversationCatalogPresenter _conversationCatalogPresenter;
    private readonly IChatStateProjector _chatStateProjector;
    private readonly IAcpSessionUpdateProjector _acpSessionUpdateProjector;
    private readonly IChatConnectionStore _chatConnectionStore;
    private IChatService? _chatService;
    private readonly SynchronizationContext _syncContext;
    private bool _disposed;
    private bool _autoConnectAttempted;
    private bool _suppressAcpProfileConnect;
    private bool _suppressAutoConnectFromPreferenceChange;
    private CancellationTokenSource? _sendPromptCts;
    private CancellationTokenSource? _transientNotificationCts;
    private CancellationTokenSource? _storeStateCts;
    private readonly object _selectedProfileConnectSync = new();
    private readonly object _ambientConnectionRequestSync = new();
    private Task? _selectedProfileConnectTask;
    private ServerConfiguration? _pendingSelectedProfileConnect;
    private IDisposable? _storeStateSubscription;
    private IDisposable? _connectionStateSubscription;
    private string? _currentRemoteSessionId;
    private IReadOnlyList<AuthMethodDefinition>? _advertisedAuthMethods;
    private bool _suppressMiniWindowSessionSync;
    private bool _suppressStoreProfileProjection;
    private bool _suppressStorePromptProjection;
    private bool _suppressProfileSyncFromStore;
    private bool _suppressModeSelectionDispatch;
    private string? _selectedProfileIdFromStore;
    private int _storeProjectionSequence;
    private readonly object _restoreSync = new();
    private Task? _restoreTask;
    private readonly object _conversationActivationSync = new();
    private readonly SemaphoreSlim _conversationActivationGate = new(1, 1);
    private readonly SemaphoreSlim _remoteConversationActivationGate = new(1, 1);

    private CancellationTokenSource? _conversationActivationCts;
    private CancellationTokenSource? _ambientConnectionRequestCts;
    private long _conversationActivationVersion;
    private long _connectionGeneration;
    private readonly Dictionary<string, BottomPanelState> _bottomPanelStateByConversation = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AskUserRequestViewModel> _pendingAskUserRequestsByConversation = new(StringComparer.Ordinal);
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
    private ObservableCollection<PlanEntryViewModel>? _observedPlanEntries;
    private AskUserRequestViewModel? _observedPendingAskUserRequest;
    private string? _sessionSwitchOverlayConversationId;
    private string? _connectionLifecycleOverlayConversationId;
    private string? _historyOverlayConversationId;
    private string? _pendingHistoryOverlayDismissConversationId;
    private string? _sessionSwitchPreviewConversationId;

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
    private MiniWindowConversationItemViewModel? _selectedMiniWindowSession;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSendPromptUi))]
    private bool _isSessionActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInputEnabled))]
    [NotifyPropertyChangedFor(nameof(CanSendPromptUi))]
    private bool _isPromptInFlight;

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

    private bool IsOverlayOwnedByCurrentSession(string? ownerConversationId)
        => !string.IsNullOrWhiteSpace(ownerConversationId)
            && string.Equals(ownerConversationId, CurrentSessionId, StringComparison.Ordinal);

    private bool IsSessionSwitchOverlayVisible
        => IsSessionSwitching && !string.IsNullOrWhiteSpace(_sessionSwitchOverlayConversationId);

    private bool IsSessionSwitchPreviewVisible
        => !string.IsNullOrWhiteSpace(_sessionSwitchPreviewConversationId);

    private bool ShouldShowConnectionLifecycleOverlay
        => IsOverlayOwnedByCurrentSession(_connectionLifecycleOverlayConversationId)
            && (IsConnecting || IsInitializing);

    private bool ShouldShowHistoryOverlay
        => IsOverlayOwnedByCurrentSession(_historyOverlayConversationId);

    private bool ShouldShowProjectedHydrationOverlay
        => !ShouldShowHistoryOverlay
            && IsHydrating
            && !string.IsNullOrWhiteSpace(CurrentSessionId);

    public bool IsOverlayVisible
        => ShouldShowConnectionLifecycleOverlay
            || ShouldShowHistoryOverlay
            || ShouldShowProjectedHydrationOverlay
            || IsSessionSwitchOverlayVisible
            || IsSessionSwitchPreviewVisible
            || IsLayoutLoading;

    public bool HasVisibleTranscriptContent => MessageHistory.Count > 0;

    public bool ShouldShowBlockingLoadingMask
        => IsOverlayVisible && !HasVisibleTranscriptContent;

    public bool ShouldShowLoadingOverlayStatusPill
        => IsOverlayVisible
            && !string.IsNullOrWhiteSpace(OverlayStatusText);

    public bool ShouldShowLoadingOverlayPresenter
        => ShouldShowBlockingLoadingMask || ShouldShowLoadingOverlayStatusPill;

    public LoadingOverlayStage OverlayLoadingStage =>
        ResolveOverlayLoadingStage();

    public string OverlayStatusText =>
        OverlayLoadingStage switch
        {
            LoadingOverlayStage.Connecting => "正在连接助手...",
            LoadingOverlayStage.InitializingProtocol => "正在准备聊天环境...",
            LoadingOverlayStage.HydratingHistory => BuildHydrationStatusText(),
            LoadingOverlayStage.PreparingSession => "正在切换聊天...",
            _ => string.Empty
        };

    private LoadingOverlayStage ResolveOverlayLoadingStage()
    {
        // ACP lifecycle precedence: transport connect -> protocol initialize -> session replay hydration.
        if (IsConnecting && ShouldShowConnectionLifecycleOverlay)
        {
            return LoadingOverlayStage.Connecting;
        }

        if (IsInitializing && ShouldShowConnectionLifecycleOverlay)
        {
            return LoadingOverlayStage.InitializingProtocol;
        }

        if (ShouldShowHistoryOverlay || ShouldShowProjectedHydrationOverlay)
        {
            return LoadingOverlayStage.HydratingHistory;
        }

        if (IsSessionSwitchOverlayVisible || IsSessionSwitchPreviewVisible)
        {
            return LoadingOverlayStage.PreparingSession;
        }

        return LoadingOverlayStage.None;
    }

    private string BuildHydrationStatusText()
    {
        var loadedCount = ResolveHydrationLoadedMessageCount();
        return _hydrationOverlayPhase switch
        {
            HydrationOverlayPhase.RequestingSessionLoad => FormatHydrationStatus("正在打开聊天记录", loadedCount),
            HydrationOverlayPhase.AwaitingReplayStart => FormatHydrationStatus("正在获取聊天记录", loadedCount),
            HydrationOverlayPhase.ReplayingSessionUpdates => FormatHydrationStatus("正在同步聊天记录", loadedCount),
            HydrationOverlayPhase.ProjectingTranscript => FormatHydrationStatus("正在整理聊天内容", loadedCount),
            HydrationOverlayPhase.SettlingReplay => FormatHydrationStatus("正在完成聊天加载", loadedCount),
            HydrationOverlayPhase.FinalizingProjection => FormatHydrationStatus("即将完成聊天加载", loadedCount),
            _ => FormatHydrationStatus("正在加载聊天记录", loadedCount)
        };
    }

    private static string FormatHydrationStatus(string baseText, long loadedCount)
        => loadedCount > 0
            ? $"{baseText}（已加载 {loadedCount} 条消息）"
            : $"{baseText}...";

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

    public bool IsInputEnabled => !IsBusy && !IsPromptInFlight && PendingAskUserRequest is null;

    public bool HasPendingAskUserRequest => PendingAskUserRequest is not null;

    public string AskUserPrompt => PendingAskUserRequest?.Prompt ?? string.Empty;

    public ObservableCollection<AskUserQuestionViewModel> AskUserQuestions => PendingAskUserRequest?.Questions ?? _emptyAskUserQuestions;

    public bool AskUserHasError => PendingAskUserRequest?.HasError ?? false;

    public string AskUserErrorMessage => PendingAskUserRequest?.ErrorMessage ?? string.Empty;

    public IAsyncRelayCommand? AskUserSubmitCommand => PendingAskUserRequest?.SubmitCommand;

    // UI-BOUND PROPERTIES: Handlers for WinUI/Uno property change notifications.
    // These ensure the View reflects internal state changes that might not trigger automatically.
    public bool CanSendPromptUi => CanSendPrompt();

    public bool HasPlanEntries => PlanEntries.Count > 0;

    public bool ShouldShowPlanList => ShowPlanPanel && HasPlanEntries;

    public bool ShouldShowPlanEmpty => !ShowPlanPanel || !HasPlanEntries;

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
        ILogger<ChatViewModel> logger,
        SynchronizationContext? syncContext = null,
        IAcpConnectionCommands? acpConnectionCommands = null,
        IConversationActivationCoordinator? conversationActivationCoordinator = null,
        IConversationBindingCommands? bindingCommands = null,
        IAcpConnectionCoordinator? acpConnectionCoordinator = null,
        IProjectAffinityResolver? projectAffinityResolver = null)
        : base(logger)
    {
        _chatStore = chatStore ?? throw new ArgumentNullException(nameof(chatStore));
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
        _conversationCatalogPresenter = conversationCatalogPresenter ?? throw new ArgumentNullException(nameof(conversationCatalogPresenter));
        _chatStateProjector = chatStateProjector ?? throw new ArgumentNullException(nameof(chatStateProjector));
        _acpSessionUpdateProjector = acpSessionUpdateProjector ?? new AcpSessionUpdateProjector();
        _chatConnectionStore = chatConnectionStore ?? throw new ArgumentNullException(nameof(chatConnectionStore));
        _projectAffinityResolver = projectAffinityResolver ?? new ProjectAffinityResolver();
        _syncContext = syncContext ?? SynchronizationContext.Current ?? new SynchronizationContext();
        ApplyProjectAffinityOverrideCommand = new RelayCommand(ApplyProjectAffinityOverride, () => CanApplyProjectAffinityOverride);
        ClearProjectAffinityOverrideCommand = new RelayCommand(ClearProjectAffinityOverride, () => CanClearProjectAffinityOverride);
        _acpConnectionCommands = acpConnectionCommands
            ?? new AcpChatCoordinator(
                new ChatServiceFactoryAdapter(chatServiceFactory),
                NullLogger<AcpChatCoordinator>.Instance);
        StartStoreProjection();

        _acpProfiles.PropertyChanged += OnAcpProfilesPropertyChanged;
        _acpProfiles.Profiles.CollectionChanged += OnAcpProfilesCollectionChanged;
        _preferences.PropertyChanged += OnPreferencesPropertyChanged;
        _hydrationCompletionMode = ResolveHydrationCompletionMode(_preferences.AcpHydrationCompletionMode);
        _preferences.Projects.CollectionChanged += OnProjectAffinityPreferencesCollectionChanged;
        _preferences.ProjectPathMappings.CollectionChanged += OnProjectAffinityPreferencesCollectionChanged;
        _conversationWorkspace.PropertyChanged += OnConversationWorkspacePropertyChanged;
        AttachPlanEntriesCollectionChanged(PlanEntries);

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
                _conversationCatalogPresenter.Refresh(_conversationWorkspace.GetCatalog());
                OnPropertyChanged(nameof(GetKnownConversationIds));
                RefreshMiniWindowSessions();
                RefreshProjectAffinityCorrectionState();
                break;
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
            var connectionState = await _chatConnectionStore.State ?? ChatConnectionState.Empty;

            await PostToUiAsync(() =>
            {
                if (token.IsCancellationRequested || _disposed)
                {
                    return;
                }

                if (projectionSequence != Volatile.Read(ref _storeProjectionSequence))
                {
                    return;
                }

                var projection = CreateProjection(state, connectionState);
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
    }

    private void OnAcpProfilesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AcpProfilesViewModel.SelectedProfile))
        {
            if (_suppressProfileSyncFromStore)
            {
                return;
            }

            // Sync UI selection without triggering another connect attempt.
            _suppressAcpProfileConnect = true;
            try
            {
                SelectedAcpProfile = _acpProfiles.SelectedProfile;
            }
            finally
            {
                _suppressAcpProfileConnect = false;
            }
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
            sessionChanged = !string.Equals(CurrentSessionId, projection.HydratedConversationId, StringComparison.Ordinal);
            if (sessionChanged)
            {
                // Set the session ID without triggering OnCurrentSessionIdChanged immediately
                // if we want to avoid multiple overlay state changes, but here we actually
                // WANT consistent behavior. By moving session clearing before other updates,
                // we ensure the "empty" state is correctly identified as "loading" if hydrating.
                CurrentSessionId = projection.HydratedConversationId;
                if (preparedTranscript is { Count: > 0 })
                {
                    ReplaceMessageHistory(preparedTranscript);
                }
                else
                {
                    ReplaceMessageHistory(projection.Transcript);
                }
                ReplacePlanEntries(projection.PlanEntries);
            }

            var draft = projection.CurrentPrompt;
            if (!string.Equals(CurrentPrompt, draft, StringComparison.Ordinal))
            {
                CurrentPrompt = draft;
            }

            ApplySelectedProfileFromStore(projection.SelectedProfileId);
            _currentRemoteSessionId = projection.RemoteSessionId;

            // CRITICAL: Sync transcript BEFORE notifying IsSessionActive/IsHydrating.
            // This ensures that when the UI thread reacts to the active session,
            // MessageHistory already reflects the projected state.
            if (!sessionChanged)
            {
                SyncMessageHistory(projection.Transcript);
            }

            // Update hydration state BEFORE session active so the UI thread's IsOverlayVisible
            // logic (which depends on IsHydrating) sees the hydration state as soon as
            // the session becomes active. This prevents the "flash of empty chat interface".
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

            ShowPlanPanel = projection.ShowPlanPanel;
            CurrentPlanTitle = projection.PlanTitle;
            if (!sessionChanged)
            {
                SyncPlanEntries(projection.PlanEntries);
            }

            RaiseOverlayStateChanged();
            Interlocked.Exchange(ref _connectionGeneration, projection.ConnectionGeneration);
            CurrentConnectionStatus = projection.ConnectionStatus;
            ConnectionErrorMessage = projection.ConnectionError;
            IsAuthenticationRequired = projection.IsAuthenticationRequired;
            AuthenticationHintMessage = projection.AuthenticationHintMessage;
            AgentName = projection.AgentName;
            AgentVersion = projection.AgentVersion;
            ApplySessionStateProjection(
                projection.AvailableModes,
                projection.SelectedModeId,
                projection.ConfigOptions,
                projection.ShowConfigOptionsPanel);
            TryCompletePendingHistoryOverlayDismissal(projection);
            RefreshProjectAffinityCorrectionState(projection.HydratedConversationId);
            ApplyProjectAffinityOverrideCommand.NotifyCanExecuteChanged();
            ClearProjectAffinityOverrideCommand.NotifyCanExecuteChanged();
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

    private void SyncPlanEntries(IReadOnlyList<ConversationPlanEntrySnapshot> planEntries)
    {
        var entries = planEntries ?? Array.Empty<ConversationPlanEntrySnapshot>();
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (i < PlanEntries.Count)
            {
                if (PlanEntries[i].Content != (entry.Content ?? string.Empty))
                {
                    while (PlanEntries.Count > i) PlanEntries.RemoveAt(i);
                    PlanEntries.Add(new PlanEntryViewModel
                    {
                        Content = entry.Content ?? string.Empty,
                        Status = entry.Status,
                        Priority = entry.Priority
                    });
                }
                else
                {
                    PlanEntries[i].Status = entry.Status;
                    PlanEntries[i].Priority = entry.Priority;
                }
            }
            else
            {
                PlanEntries.Add(new PlanEntryViewModel
                {
                    Content = entry.Content ?? string.Empty,
                    Status = entry.Status,
                    Priority = entry.Priority
                });
            }
        }

        while (PlanEntries.Count > entries.Count)
        {
            PlanEntries.RemoveAt(PlanEntries.Count - 1);
        }
    }

    private void ApplySelectedProfileFromStore(string? profileId)
    {
        _selectedProfileIdFromStore = profileId;

        var match = string.IsNullOrWhiteSpace(profileId)
            ? null
            : _acpProfiles.Profiles.FirstOrDefault(p => string.Equals(p.Id, profileId, StringComparison.Ordinal));

        if (!ReferenceEquals(SelectedAcpProfile, match))
        {
            _suppressAcpProfileConnect = true;
            _suppressProfileSyncFromStore = true;
            try
            {
                SelectedAcpProfile = match;
                _acpProfiles.SelectedProfile = match;
            }
            finally
            {
                _suppressProfileSyncFromStore = false;
                _suppressAcpProfileConnect = false;
            }
        }
    }

    private ServerConfiguration? ResolveLoadedProfileSelection(ServerConfiguration? profile)
    {
        if (profile is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(profile.Id))
        {
            return _acpProfiles.Profiles.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, profile.Id, StringComparison.Ordinal));
        }

        return _acpProfiles.Profiles.FirstOrDefault(candidate => ReferenceEquals(candidate, profile));
    }

    private void ApplySessionStateProjection(
        IReadOnlyList<ConversationModeOptionSnapshot> availableModes,
        string? selectedModeId,
        IReadOnlyList<ConversationConfigOptionSnapshot> configOptions,
        bool showConfigOptionsPanel)
    {
        if (!ModeCollectionMatches(AvailableModes, availableModes))
        {
            AvailableModes.Clear();
            foreach (var mode in availableModes)
            {
                AvailableModes.Add(new SessionModeViewModel
                {
                    ModeId = mode.ModeId,
                    ModeName = mode.ModeName,
                    Description = mode.Description
                });
            }
        }

        if (!ConfigOptionCollectionMatches(ConfigOptions, configOptions))
        {
            ConfigOptions.Clear();
            foreach (var option in configOptions)
            {
                ConfigOptions.Add(MapConfigOption(option));
            }
        }

        ShowConfigOptionsPanel = showConfigOptionsPanel;

        if (ConfigOptions.Count == 0)
        {
            _modeConfigId = null;
        }
        else
        {
            SyncModesFromConfigOptions();
        }

        if (!string.IsNullOrWhiteSpace(selectedModeId) && AvailableModes.Count > 0)
        {
            SetSelectedModeWithoutDispatch(
                AvailableModes.FirstOrDefault(m => m.ModeId == selectedModeId) ?? AvailableModes[0]);
            return;
        }

        SetSelectedModeWithoutDispatch(AvailableModes.FirstOrDefault());
    }

    private static bool ModeCollectionMatches(
        IReadOnlyList<SessionModeViewModel> current,
        IReadOnlyList<ConversationModeOptionSnapshot> projected)
    {
        if (current.Count != projected.Count)
        {
            return false;
        }

        for (var i = 0; i < current.Count; i++)
        {
            if (!string.Equals(current[i].ModeId, projected[i].ModeId, StringComparison.Ordinal)
                || !string.Equals(current[i].ModeName, projected[i].ModeName, StringComparison.Ordinal)
                || !string.Equals(current[i].Description, projected[i].Description, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ConfigOptionCollectionMatches(
        IReadOnlyList<ConfigOptionViewModel> current,
        IReadOnlyList<ConversationConfigOptionSnapshot> projected)
    {
        if (current.Count != projected.Count)
        {
            return false;
        }

        for (var i = 0; i < current.Count; i++)
        {
            var left = current[i];
            var right = projected[i];
            if (!string.Equals(left.Id, right.Id, StringComparison.Ordinal)
                || !string.Equals(left.Name, right.Name, StringComparison.Ordinal)
                || !string.Equals(left.Description, right.Description, StringComparison.Ordinal)
                || !string.Equals(left.Category, right.Category, StringComparison.Ordinal)
                || !string.Equals(left.ValueType, right.ValueType ?? "string", StringComparison.Ordinal)
                || !string.Equals(left.Value?.ToString(), right.SelectedValue ?? string.Empty, StringComparison.Ordinal))
            {
                return false;
            }

            if (left.Options.Count != right.Options.Count)
            {
                return false;
            }

            for (var optionIndex = 0; optionIndex < left.Options.Count; optionIndex++)
            {
                var leftOption = left.Options[optionIndex];
                var rightOption = right.Options[optionIndex];
                if (!string.Equals(leftOption.Value, rightOption.Value, StringComparison.Ordinal)
                    || !string.Equals(leftOption.Name, rightOption.Name, StringComparison.Ordinal)
                    || !string.Equals(leftOption.Description, rightOption.Description, StringComparison.Ordinal))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private void OnAcpProfilesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedProfileIdFromStore))
        {
            return;
        }

        ApplySelectedProfileFromStore(_selectedProfileIdFromStore);
    }

    partial void OnSelectedAcpProfileChanged(ServerConfiguration? value)
    {
        if (!_suppressStoreProfileProjection)
        {
            _ = _chatConnectionStore.Dispatch(new SetSelectedProfileAction(value?.Id));
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
            .Dispatch(new SetSelectedProfileAction(profile.Id))
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
        AttachPlanEntriesCollectionChanged(value);
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

        SyncMiniWindowSelectedSession();
        SyncBottomPanelState(value);
        SyncPendingAskUserRequestState(value);
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

        if (_bottomPanelStateByConversation.TryGetValue(conversationId, out var state))
        {
            state.Selected = value;
        }
    }

    partial void OnSelectedMiniWindowSessionChanged(MiniWindowConversationItemViewModel? value)
    {
        if (_suppressMiniWindowSessionSync || value == null)
        {
            return;
        }

        StartChatSurfaceConversationSwitch(value.ConversationId);
    }

    private void StartChatSurfaceConversationSwitch(string? conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId)
            || string.Equals(CurrentSessionId, conversationId, StringComparison.Ordinal))
        {
            return;
        }

        _ = ((IConversationSessionSwitcher)this).SwitchConversationAsync(conversationId);
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
        IsLayoutLoading = true;
        try
        {
            var startAt = DateTimeOffset.UtcNow;

            var activationResult = await _conversationActivationCoordinator
                .ActivateSessionAsync(conversationId)
                .ConfigureAwait(false);
            if (!activationResult.Succeeded)
            {
                return;
            }

            // 强制 Skeleton 遮罩停留至少 600ms，以提供 premium 的加载感并确保 UI 布局就绪
            var elapsed = DateTimeOffset.UtcNow - startAt;
            var minDur = TimeSpan.FromMilliseconds(600);
            if (elapsed < minDur)
            {
                await Task.Delay(minDur - elapsed).ConfigureAwait(false);
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
                IsLayoutLoading = false;
            }
        }
    }

    private async Task ApplyCurrentStoreProjectionAsync()
    {
        var state = await _chatStore.State ?? ChatState.Empty;
        var connectionState = await _chatConnectionStore.State ?? ChatConnectionState.Empty;
        var projection = CreateProjection(state, connectionState);
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
    private Task PostToUiAsync(Action action)
    {
        if (SynchronizationContext.Current == _syncContext)
        {
            action();
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _syncContext.Post(_ =>
        {
            try
            {
                action();
                tcs.TrySetResult(null);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }, null);
        return tcs.Task;
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
        if (!string.IsNullOrWhiteSpace(connectionState.SelectedProfileId))
        {
            return;
        }

        var binding = _conversationWorkspace.GetRemoteBinding(conversationId);
        if (string.IsNullOrWhiteSpace(binding?.BoundProfileId))
        {
            return;
        }

        await _chatConnectionStore.Dispatch(new SetSelectedProfileAction(binding.BoundProfileId)).ConfigureAwait(false);
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
                MiniWindowSessions.Add(new MiniWindowConversationItemViewModel(conversationId, displayName));
            }

            SyncMiniWindowSelectedSession();
        }
        catch
        {
            // Mini window list is best-effort and should never break the main chat experience.
        }
    }

    private void SyncMiniWindowSelectedSession()
    {
        if (_suppressMiniWindowSessionSync)
        {
            return;
        }

        try
        {
            _suppressMiniWindowSessionSync = true;

            if (string.IsNullOrWhiteSpace(CurrentSessionId))
            {
                SelectedMiniWindowSession = null;
                return;
            }

            var match = MiniWindowSessions.FirstOrDefault(s => string.Equals(s.ConversationId, CurrentSessionId, StringComparison.Ordinal));
            if (!ReferenceEquals(SelectedMiniWindowSession, match))
            {
                SelectedMiniWindowSession = match;
            }
        }
        finally
        {
            _suppressMiniWindowSessionSync = false;
        }
    }

    private void SyncMessageHistory(IImmutableList<ConversationMessageSnapshot> transcript)
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

        if (previousCount != MessageHistory.Count)
        {
            OnPropertyChanged(nameof(HasVisibleTranscriptContent));
            OnPropertyChanged(nameof(OverlayStatusText));
            OnPropertyChanged(nameof(ShouldShowBlockingLoadingMask));
            OnPropertyChanged(nameof(ShouldShowLoadingOverlayPresenter));
        }
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
            ToolCallId = vm.ToolCallId,
            ToolCallKind = vm.ToolCallKind,
            ToolCallStatus = vm.ToolCallStatus,
            ToolCallJson = vm.ToolCallJson,
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
            ToolCallId = snapshot.ToolCallId,
            ToolCallKind = snapshot.ToolCallKind,
            ToolCallStatus = snapshot.ToolCallStatus,
            ToolCallJson = snapshot.ToolCallJson,
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
            connectionState.SelectedProfileId);
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

    private void RefreshProjectAffinityCorrectionState(string? conversationId = null)
    {
        var activeConversationId = string.IsNullOrWhiteSpace(conversationId)
            ? CurrentSessionId
            : conversationId;
        var options = BuildProjectAffinityOverrideOptions();
        ProjectAffinityOverrideOptions = new ObservableCollection<ProjectAffinityOverrideOptionViewModel>(options);

        if (string.IsNullOrWhiteSpace(activeConversationId))
        {
            IsProjectAffinityCorrectionVisible = false;
            HasProjectAffinityOverride = false;
            EffectiveProjectAffinityProjectId = null;
            EffectiveProjectAffinitySource = ProjectAffinitySource.Unclassified;
            ProjectAffinityCorrectionMessage = string.Empty;
            SelectedProjectAffinityOverrideProjectId = null;
            OnPropertyChanged(nameof(CanApplyProjectAffinityOverride));
            OnPropertyChanged(nameof(CanClearProjectAffinityOverride));
            return;
        }

        var binding = _conversationWorkspace.GetRemoteBinding(activeConversationId);
        var remoteSessionId = binding?.RemoteSessionId;
        var boundProfileId = binding?.BoundProfileId;
        if (string.Equals(activeConversationId, CurrentSessionId, StringComparison.Ordinal))
        {
            remoteSessionId ??= _currentRemoteSessionId;
            boundProfileId ??= SelectedAcpProfile?.Id;
        }

        var overrideProjectId = _conversationWorkspace.GetProjectAffinityOverride(activeConversationId)?.ProjectId;
        var remoteCwd = _sessionManager.GetSession(activeConversationId)?.Cwd;
        var projects = _preferences.Projects.ToArray();
        var mappings = _preferences.ProjectPathMappings.ToArray();
        var resolution = _projectAffinityResolver.Resolve(new ProjectAffinityRequest(
            RemoteCwd: remoteCwd,
            BoundProfileId: boundProfileId,
            RemoteSessionId: remoteSessionId,
            OverrideProjectId: overrideProjectId,
            Projects: projects,
            PathMappings: mappings,
            UnclassifiedProjectId: NavigationProjectIds.Unclassified));

        var isRemoteBound = !string.IsNullOrWhiteSpace(remoteSessionId)
            || !string.IsNullOrWhiteSpace(boundProfileId);
        IsProjectAffinityCorrectionVisible = isRemoteBound && resolution.Source is
            ProjectAffinitySource.NeedsMapping or
            ProjectAffinitySource.Unclassified or
            ProjectAffinitySource.Override;
        HasProjectAffinityOverride = !string.IsNullOrWhiteSpace(overrideProjectId);
        EffectiveProjectAffinityProjectId = resolution.EffectiveProjectId;
        EffectiveProjectAffinitySource = resolution.Source;
        ProjectAffinityCorrectionMessage = resolution.Source switch
        {
            ProjectAffinitySource.Override => "已应用本地项目覆盖，可随时清除。",
            ProjectAffinitySource.NeedsMapping => "远程会话未匹配到本地项目，请手动更正。",
            _ => "当前会话归类为“未归类”，可手动更正。"
        };

        if (HasProjectAffinityOverride)
        {
            SelectedProjectAffinityOverrideProjectId = overrideProjectId;
        }
        else if (!string.IsNullOrWhiteSpace(SelectedProjectAffinityOverrideProjectId)
            && !options.Any(option => string.Equals(option.ProjectId, SelectedProjectAffinityOverrideProjectId, StringComparison.Ordinal)))
        {
            SelectedProjectAffinityOverrideProjectId = null;
        }

        OnPropertyChanged(nameof(CanApplyProjectAffinityOverride));
        OnPropertyChanged(nameof(CanClearProjectAffinityOverride));
    }

    private IReadOnlyList<ProjectAffinityOverrideOptionViewModel> BuildProjectAffinityOverrideOptions()
    {
        var options = new List<ProjectAffinityOverrideOptionViewModel>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var project in _preferences.Projects)
        {
            if (project is null
                || string.IsNullOrWhiteSpace(project.ProjectId)
                || string.IsNullOrWhiteSpace(project.Name)
                || !seen.Add(project.ProjectId))
            {
                continue;
            }

            options.Add(new ProjectAffinityOverrideOptionViewModel(project.ProjectId, project.Name));
        }

        options.Sort((left, right) => string.Compare(left.DisplayName, right.DisplayName, StringComparison.Ordinal));
        return options;
    }

    private void ApplyProjectAffinityOverride()
    {
        if (string.IsNullOrWhiteSpace(CurrentSessionId)
            || string.IsNullOrWhiteSpace(SelectedProjectAffinityOverrideProjectId))
        {
            return;
        }

        _conversationWorkspace.UpdateProjectAffinityOverride(
            CurrentSessionId,
            SelectedProjectAffinityOverrideProjectId);
        RefreshProjectAffinityCorrectionState(CurrentSessionId);
        ApplyProjectAffinityOverrideCommand.NotifyCanExecuteChanged();
        ClearProjectAffinityOverrideCommand.NotifyCanExecuteChanged();
    }

    private void ClearProjectAffinityOverride()
    {
        if (string.IsNullOrWhiteSpace(CurrentSessionId))
        {
            return;
        }

        _conversationWorkspace.UpdateProjectAffinityOverride(CurrentSessionId, null);
        SelectedProjectAffinityOverrideProjectId = null;
        RefreshProjectAffinityCorrectionState(CurrentSessionId);
        ApplyProjectAffinityOverrideCommand.NotifyCanExecuteChanged();
        ClearProjectAffinityOverrideCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void BeginEditSessionName()
    {
        if (!IsSessionActive || string.IsNullOrWhiteSpace(CurrentSessionId))
        {
            return;
        }

        EditingSessionName = CurrentSessionDisplayName;
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
        var sanitized = SessionNamePolicy.Sanitize(EditingSessionName);
        var finalName = string.IsNullOrEmpty(sanitized)
            ? SessionNamePolicy.CreateDefault(sessionId)
            : sanitized;

        RenameConversation(sessionId, finalName);

        CancelSessionNameEdit();
    }

    public void RenameConversation(string conversationId, string newDisplayName)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return;
        }

        var sanitized = SessionNamePolicy.Sanitize(newDisplayName);
        var finalName = string.IsNullOrWhiteSpace(sanitized)
            ? SessionNamePolicy.CreateDefault(conversationId)
            : sanitized;

        _sessionManager.UpdateSession(conversationId, session => session.DisplayName = finalName, updateActivity: false);
        NotifyConversationListChanged();

        if (string.Equals(CurrentSessionId, conversationId, StringComparison.Ordinal))
        {
            CurrentSessionDisplayName = finalName;
        }
    }

    public void ArchiveConversation(string conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return;
        }

        try
        {
            _ = _conversationActivationCoordinator
                .ArchiveConversationAsync(conversationId, CurrentSessionId)
                .GetAwaiter()
                .GetResult();

            RemoveBottomPanelState(conversationId);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Archive operation failed due to underlying exception: {ConversationId}", conversationId);
        }
    }

    public void DeleteConversation(string conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return;
        }

        try
        {
            _ = _conversationActivationCoordinator
                .DeleteConversationAsync(conversationId, CurrentSessionId)
                .GetAwaiter()
                .GetResult();

            RemoveBottomPanelState(conversationId);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Delete operation failed due to underlying exception: {ConversationId}", conversationId);
        }
    }

    public IReadOnlyList<ConversationProjectTargetOption> GetConversationProjectTargets()
    {
        var targets = new List<ConversationProjectTargetOption>
        {
            new(NavigationProjectIds.Unclassified, "未归类")
        };

        foreach (var option in BuildProjectAffinityOverrideOptions())
        {
            targets.Add(new ConversationProjectTargetOption(option.ProjectId, option.DisplayName));
        }

        return targets;
    }

    public void MoveConversationToProject(string conversationId, string projectId)
    {
        if (string.IsNullOrWhiteSpace(conversationId) || string.IsNullOrWhiteSpace(projectId))
        {
            return;
        }

        _conversationWorkspace.UpdateProjectAffinityOverride(conversationId, projectId);
        NotifyConversationListChanged();

        if (string.Equals(CurrentSessionId, conversationId, StringComparison.Ordinal))
        {
            RefreshProjectAffinityCorrectionState(conversationId);
        }
    }

    public string[] GetKnownConversationIds() => _conversationWorkspace.GetKnownConversationIds();

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
        var profileId = connectionState.SelectedProfileId;
        if (string.IsNullOrWhiteSpace(profileId))
        {
            profileId = _preferences.LastSelectedServerId;
            if (!string.IsNullOrWhiteSpace(profileId))
            {
                await _chatConnectionStore.Dispatch(new SetSelectedProfileAction(profileId)).ConfigureAwait(false);
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
            await PostToUiAsync(() =>
            {
                _suppressAutoConnectFromPreferenceChange = true;
                _acpProfiles.MarkLastConnected(profile);
                _acpProfiles.SelectedProfile = ResolveLoadedProfileSelection(profile);
            }).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var result = connectionContext.Equals(AcpConnectionContext.None)
                ? await _acpConnectionCommands
                    .ConnectToProfileAsync(profile, TransportConfig, this, cancellationToken)
                    .ConfigureAwait(false)
                : await _acpConnectionCommands
                    .ConnectToProfileAsync(profile, TransportConfig, this, connectionContext, cancellationToken)
                    .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            await PostToUiAsync(() =>
            {
                CacheAuthMethods(result.InitializeResponse);
                ClearAuthenticationRequirement();
                UpdateAgentInfo();
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

            CacheAuthMethods(result.InitializeResponse);
            ClearAuthenticationRequirement();
            UpdateAgentInfo();

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
        var delta = _acpSessionUpdateProjector.ProjectSessionNew(response);
        await ApplySessionUpdateDeltaAsync(conversationId, delta).ConfigureAwait(true);
        Logger.LogInformation(
            "Session modes loaded: {Count}",
            delta.AvailableModes?.Count ?? 0);
    }

    private async Task ApplySessionLoadResponseAsync(string conversationId, SessionLoadResponse response)
    {
        var delta = _acpSessionUpdateProjector.ProjectSessionLoad(response);
        await ApplySessionUpdateDeltaAsync(conversationId, delta).ConfigureAwait(true);
        Logger.LogInformation(
            "Session load state projected: {Count}",
            delta.AvailableModes?.Count ?? 0);
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
            chatService.AskUserRequestReceived += OnAskUserRequestReceived;
            chatService.ErrorOccurred += OnErrorOccurred;

           UpdateAgentInfo();
       }

       private void UnsubscribeFromChatService(IChatService chatService)
       {
           chatService.SessionUpdateReceived -= OnSessionUpdateReceived;
           chatService.PermissionRequestReceived -= OnPermissionRequestReceived;
            chatService.FileSystemRequestReceived -= OnFileSystemRequestReceived;
            chatService.TerminalRequestReceived -= OnTerminalRequestReceived;
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

              UpdateAgentInfo();

          }
      }

    private void OnSessionUpdateReceived(object? sender, SessionUpdateEventArgs e)
    {
        if (SynchronizationContext.Current == _syncContext)
        {
            RecordSessionUpdateObservation(e.SessionId);
            TrackPendingSessionUpdate(ProcessSessionUpdateAsync(e));
            return;
        }

        _syncContext.Post(static state =>
        {
            var (viewModel, args) = ((ChatViewModel ViewModel, SessionUpdateEventArgs Args))state!;
            viewModel.RecordSessionUpdateObservation(args.SessionId);
            viewModel.TrackPendingSessionUpdate(viewModel.ProcessSessionUpdateAsync(args));
        }, (this, e));
    }

    private async Task ProcessSessionUpdateAsync(SessionUpdateEventArgs e)
    {
        try
        {
            // SECURITY/PROTOCOL CHECK: Only store-authoritative bindings can project updates.
            // Projection targets the bound conversation slice (SSOT), while visible transcript
            // mutation still follows the currently hydrated conversation projection.
            var storeState = await _chatStore.State ?? ChatState.Empty;
            var activeConversationId = storeState.ActiveTurn?.ConversationId ?? storeState.HydratedConversationId;
            var activeBinding = storeState.ResolveBinding(activeConversationId);
            var boundConversationId = ResolveConversationIdForRemoteSession(storeState, e.SessionId)
                ?? (!string.IsNullOrWhiteSpace(activeConversationId)
                    && string.Equals(activeBinding?.RemoteSessionId, e.SessionId, StringComparison.Ordinal)
                        ? activeConversationId
                        : null);

            if (e.Update is SessionInfoUpdate sessionInfoUpdate)
            {
                if (!string.IsNullOrWhiteSpace(boundConversationId))
                {
                    await ApplySessionInfoUpdateAsync(boundConversationId!, sessionInfoUpdate).ConfigureAwait(true);
                }

                return;
            }

            if (string.IsNullOrWhiteSpace(boundConversationId))
            {
                return;
            }

            var targetConversationId = boundConversationId!;
            var isActiveTarget =
                !string.IsNullOrWhiteSpace(activeConversationId)
                && string.Equals(activeConversationId, targetConversationId, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(activeBinding?.RemoteSessionId)
                && string.Equals(e.SessionId, activeBinding.RemoteSessionId, StringComparison.Ordinal);
            var activeTurn = isActiveTarget
                ? ResolveSessionUpdateTurn(storeState, activeConversationId, e.SessionId)
                : null;

            if (e.Update is AgentMessageUpdate messageUpdate && messageUpdate.Content != null)
            {
                await AdvanceActiveTurnPhaseAsync(activeTurn, ChatTurnPhase.Responding).ConfigureAwait(true);
                await HandleAgentContentChunkAsync(targetConversationId, messageUpdate.Content).ConfigureAwait(true);
                RecordTranscriptProjectionObservation(e.SessionId);
            }
            else if (e.Update is AgentThoughtUpdate)
            {
                // Thought chunks are transient states; they trigger 'thinking' UI feedback.
                await AdvanceActiveTurnPhaseAsync(activeTurn, ChatTurnPhase.Thinking).ConfigureAwait(true);
            }
            else if (e.Update is UserMessageUpdate userMessageUpdate && userMessageUpdate.Content != null)
            {
                await AddMessageToHistoryAsync(targetConversationId, userMessageUpdate.Content, isOutgoing: true).ConfigureAwait(true);
                RecordTranscriptProjectionObservation(e.SessionId);
            }
            else if (e.Update is ToolCallUpdate toolCallUpdate)
            {
                await AdvanceActiveTurnPhaseAsync(
                    activeTurn,
                    ChatTurnPhase.ToolPending,
                    toolCallUpdate.ToolCallId,
                    toolCallUpdate.Title).ConfigureAwait(true);

                await UpsertTranscriptSnapshotAsync(targetConversationId, CreateToolCallSnapshot(toolCallUpdate)).ConfigureAwait(true);
                RecordTranscriptProjectionObservation(e.SessionId);
            }
            else if (e.Update is ToolCallStatusUpdate toolCallStatusUpdate)
            {
                var phase = toolCallStatusUpdate.Status switch
                {
                    Domain.Models.Tool.ToolCallStatus.InProgress => ChatTurnPhase.ToolRunning,
                    Domain.Models.Tool.ToolCallStatus.Completed => ChatTurnPhase.WaitingForAgent,
                    Domain.Models.Tool.ToolCallStatus.Failed => ChatTurnPhase.Failed,
                    Domain.Models.Tool.ToolCallStatus.Cancelled => ChatTurnPhase.Cancelled,
                    _ => ChatTurnPhase.ToolPending
                };
                await AdvanceActiveTurnPhaseAsync(activeTurn, phase, toolCallStatusUpdate.ToolCallId).ConfigureAwait(true);
                if (toolCallStatusUpdate.Status == Domain.Models.Tool.ToolCallStatus.Cancelled)
                {
                    await PreemptivelyCancelTurnAsync(expectedConversationId: targetConversationId).ConfigureAwait(true);
                }
                await UpdateToolCallStatusAsync(targetConversationId, toolCallStatusUpdate).ConfigureAwait(true);
                RecordTranscriptProjectionObservation(e.SessionId);
            }
            else if (e.Update is PlanUpdate planUpdate)
            {
                await ApplySessionUpdateDeltaAsync(
                    targetConversationId,
                    _acpSessionUpdateProjector.Project(new SessionUpdateEventArgs(e.SessionId, planUpdate))).ConfigureAwait(true);
            }
            else if (e.Update is CurrentModeUpdate modeChange)
            {
                await ApplySessionUpdateDeltaAsync(
                    targetConversationId,
                    _acpSessionUpdateProjector.Project(new SessionUpdateEventArgs(e.SessionId, modeChange))).ConfigureAwait(true);
            }
            else if (e.Update is ConfigUpdateUpdate configUpdate)
            {
                await ApplySessionUpdateDeltaAsync(
                    targetConversationId,
                    _acpSessionUpdateProjector.Project(new SessionUpdateEventArgs(e.SessionId, configUpdate))).ConfigureAwait(true);
            }
            else if (e.Update is ConfigOptionUpdate optionUpdate)
            {
                await ApplySessionUpdateDeltaAsync(
                    targetConversationId,
                    _acpSessionUpdateProjector.Project(new SessionUpdateEventArgs(e.SessionId, optionUpdate))).ConfigureAwait(true);
            }
            else if (e.Update is UsageUpdate)
            {
                // Known protocol extension: usage telemetry does not currently drive visible turn or transcript state.
            }
            else if (e.Update is AvailableCommandsUpdate commandsUpdate)
            {
                if (isActiveTarget)
                {
                    UpdateSlashCommands(commandsUpdate);
                }
            }
            else if (e.Update != null)
            {
                // FUTURE-PROOFING: Log unknown protocol extensions to detect agent version mismatches.
                Logger.LogInformation("Unhandled session update type: {UpdateType}", e.Update.GetType().Name);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error processing session update");
        }
    }

    private void RaiseOverlayStateChanged()
    {
        OnPropertyChanged(nameof(IsOverlayVisible));
        OnPropertyChanged(nameof(OverlayLoadingStage));
        OnPropertyChanged(nameof(OverlayStatusText));
        OnPropertyChanged(nameof(ShouldShowBlockingLoadingMask));
        OnPropertyChanged(nameof(ShouldShowLoadingOverlayStatusPill));
        OnPropertyChanged(nameof(ShouldShowLoadingOverlayPresenter));
    }

    private void TrackPendingSessionUpdate(Task task)
    {
        ArgumentNullException.ThrowIfNull(task);

        lock (_sessionUpdateTrackingSync)
        {
            if (_pendingSessionUpdateCount == 0)
            {
                _sessionUpdatesDrainedTcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            _pendingSessionUpdateCount++;
        }

        _ = ObservePendingSessionUpdateAsync(task);
    }

    private async Task ObservePendingSessionUpdateAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        finally
        {
            TaskCompletionSource<object?>? drained = null;

            lock (_sessionUpdateTrackingSync)
            {
                if (_pendingSessionUpdateCount > 0)
                {
                    _pendingSessionUpdateCount--;
                    if (_pendingSessionUpdateCount == 0)
                    {
                        drained = _sessionUpdatesDrainedTcs;
                    }
                }
            }

            drained?.TrySetResult(null);
        }
    }

    private Task WaitForPendingSessionUpdatesAsync()
    {
        lock (_sessionUpdateTrackingSync)
        {
            if (_pendingSessionUpdateCount == 0)
            {
                return Task.CompletedTask;
            }

            _sessionUpdatesDrainedTcs ??= new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            return _sessionUpdatesDrainedTcs.Task;
        }
    }

    private bool HasPendingSessionUpdates()
    {
        lock (_sessionUpdateTrackingSync)
        {
            return _pendingSessionUpdateCount > 0;
        }
    }

    private async Task AwaitBufferedSessionReplayProjectionAsync(
        CancellationToken cancellationToken,
        long? hydrationAttemptId = null)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await PostToUiAsync(static () => { }).ConfigureAwait(false);
        if (_chatService is IAcpSessionUpdateBufferController adapter
            && hydrationAttemptId.HasValue)
        {
            await adapter
                .WaitForBufferedUpdatesDrainedAsync(hydrationAttemptId.Value, cancellationToken)
                .WaitAsync(RemoteReplayDrainTimeout, cancellationToken)
                .ConfigureAwait(false);

            // Draining the adapter can synchronously raise SessionUpdateReceived, and the
            // ViewModel intentionally reposts those updates onto the UI context for ordered
            // projection. Yield one more UI turn so those reposted callbacks have a chance
            // to register their pending-work counters before we snapshot pending state.
            await PostToUiAsync(static () => { }).ConfigureAwait(false);
        }

        var pendingUpdates = WaitForPendingSessionUpdatesAsync();
        if (!pendingUpdates.IsCompleted)
        {
            await pendingUpdates.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        // Session-update handlers can enqueue a follow-up store projection onto the UI context.
        // Wait for one more UI turn so the replay is fully reflected in MessageHistory before
        // activation/loading considers itself complete.
        await PostToUiAsync(static () => { }).ConfigureAwait(false);
    }

    private void RecordSessionUpdateObservation(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        lock (_sessionUpdateObservationSync)
        {
            _sessionUpdateObservationCounts[sessionId] =
                _sessionUpdateObservationCounts.TryGetValue(sessionId, out var current)
                    ? checked(current + 1)
                    : 1;
            _sessionUpdateLastObservedAtUtc[sessionId] = DateTime.UtcNow;
        }

        if (OverlayLoadingStage != LoadingOverlayStage.HydratingHistory)
        {
            return;
        }

        if (TryResolveCurrentHydrationConversationForRemoteSession(sessionId, out var conversationId))
        {
            SetHydrationOverlayPhase(conversationId, HydrationOverlayPhase.ReplayingSessionUpdates);
            OnPropertyChanged(nameof(OverlayStatusText));
        }
    }

    private void RecordTranscriptProjectionObservation(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        lock (_sessionUpdateObservationSync)
        {
            _sessionTranscriptProjectionObservationCounts[sessionId] =
                _sessionTranscriptProjectionObservationCounts.TryGetValue(sessionId, out var current)
                    ? checked(current + 1)
                    : 1;
        }

        if (OverlayLoadingStage != LoadingOverlayStage.HydratingHistory)
        {
            return;
        }

        if (TryResolveCurrentHydrationConversationForRemoteSession(sessionId, out var conversationId))
        {
            SetHydrationOverlayPhase(conversationId, HydrationOverlayPhase.ProjectingTranscript);
            OnPropertyChanged(nameof(OverlayStatusText));
        }
    }

    private long GetSessionUpdateObservationCount(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return 0;
        }

        lock (_sessionUpdateObservationSync)
        {
            return _sessionUpdateObservationCounts.TryGetValue(sessionId, out var count)
                ? count
                : 0;
        }
    }

    private long GetTranscriptProjectionObservationCount(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return 0;
        }

        lock (_sessionUpdateObservationSync)
        {
            return _sessionTranscriptProjectionObservationCounts.TryGetValue(sessionId, out var count)
                ? count
                : 0;
        }
    }

    private DateTime? GetSessionUpdateLastObservedAtUtc(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return null;
        }

        lock (_sessionUpdateObservationSync)
        {
            return _sessionUpdateLastObservedAtUtc.TryGetValue(sessionId, out var observedAtUtc)
                ? observedAtUtc
                : null;
        }
    }

    private async Task AwaitRemoteReplaySettleQuietPeriodAsync(
        string remoteSessionId,
        long replayBaseline,
        CancellationToken cancellationToken)
    {
        if (GetSessionUpdateObservationCount(remoteSessionId) <= replayBaseline)
        {
            return;
        }

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var lastObservedAtUtc = GetSessionUpdateLastObservedAtUtc(remoteSessionId);
            if (!lastObservedAtUtc.HasValue)
            {
                return;
            }

            var quietRemaining = (lastObservedAtUtc.Value + RemoteReplaySettleQuietPeriod) - DateTime.UtcNow;
            if (quietRemaining <= TimeSpan.Zero)
            {
                return;
            }

            var delay = quietRemaining < TimeSpan.FromMilliseconds(RemoteReplayPollDelayMilliseconds)
                ? quietRemaining
                : TimeSpan.FromMilliseconds(RemoteReplayPollDelayMilliseconds);
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task AwaitRemoteReplayProjectionAsync(
        string conversationId,
        long? activationVersion,
        string remoteSessionId,
        long replayBaseline,
        long transcriptProjectionBaseline,
        long? hydrationAttemptId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await SetHydrationOverlayPhaseAsync(
                conversationId,
                activationVersion,
                HydrationOverlayPhase.AwaitingReplayStart)
            .ConfigureAwait(false);

        var replayStartTimeoutAt = DateTime.UtcNow + RemoteReplayStartTimeout;

        while (GetSessionUpdateObservationCount(remoteSessionId) <= replayBaseline
            && DateTime.UtcNow < replayStartTimeoutAt)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(RemoteReplayPollDelayMilliseconds, cancellationToken).ConfigureAwait(false);
        }

        if (GetSessionUpdateObservationCount(remoteSessionId) > replayBaseline)
        {
            await SetHydrationOverlayPhaseAsync(
                    conversationId,
                    activationVersion,
                    HydrationOverlayPhase.ReplayingSessionUpdates)
                .ConfigureAwait(false);
        }

        var transcriptTimeoutAt = DateTime.UtcNow + RemoteReplayStartTimeout;
        while (GetTranscriptProjectionObservationCount(remoteSessionId) <= transcriptProjectionBaseline
            && DateTime.UtcNow < transcriptTimeoutAt)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(RemoteReplayPollDelayMilliseconds, cancellationToken).ConfigureAwait(false);
        }

        if (GetTranscriptProjectionObservationCount(remoteSessionId) > transcriptProjectionBaseline)
        {
            await SetHydrationOverlayPhaseAsync(
                    conversationId,
                    activationVersion,
                    HydrationOverlayPhase.ProjectingTranscript)
                .ConfigureAwait(false);
            await SetHydrationOverlayPhaseAsync(
                    conversationId,
                    activationVersion,
                    HydrationOverlayPhase.SettlingReplay)
                .ConfigureAwait(false);
            await AwaitRemoteReplaySettleQuietPeriodAsync(remoteSessionId, replayBaseline, cancellationToken).ConfigureAwait(false);
        }

        await SetHydrationOverlayPhaseAsync(
                conversationId,
                activationVersion,
                HydrationOverlayPhase.FinalizingProjection)
            .ConfigureAwait(false);

#if DEBUG
        Logger.LogInformation(
            "Remote replay wait finished. remoteSessionId={RemoteSessionId} replayBaseline={ReplayBaseline} observedCount={ObservedCount} transcriptBaseline={TranscriptProjectionBaseline} transcriptObservedCount={TranscriptObservedCount} startTimedOut={StartTimedOut} transcriptTimedOut={TranscriptTimedOut}",
            remoteSessionId,
            replayBaseline,
            GetSessionUpdateObservationCount(remoteSessionId),
            transcriptProjectionBaseline,
            GetTranscriptProjectionObservationCount(remoteSessionId),
            DateTime.UtcNow >= replayStartTimeoutAt,
            DateTime.UtcNow >= transcriptTimeoutAt);
#endif
        await AwaitBufferedSessionReplayProjectionAsync(cancellationToken, hydrationAttemptId).ConfigureAwait(false);
    }

    private static ActiveTurnState? ResolveSessionUpdateTurn(ChatState storeState, string? activeConversationId, string remoteSessionId)
    {
        if (storeState.ActiveTurn is not { } activeTurn
            || string.IsNullOrWhiteSpace(activeConversationId)
            || !string.Equals(activeTurn.ConversationId, activeConversationId, StringComparison.Ordinal))
        {
            return null;
        }

        var turnBinding = storeState.ResolveBinding(activeTurn.ConversationId);
        return string.Equals(turnBinding?.RemoteSessionId, remoteSessionId, StringComparison.Ordinal)
            ? activeTurn
            : null;
    }

    private async Task AdvanceActiveTurnPhaseAsync(
        ActiveTurnState? activeTurn,
        ChatTurnPhase phase,
        string? toolCallId = null,
        string? toolTitle = null)
    {
        if (activeTurn is null)
        {
            return;
        }

        await _chatStore.Dispatch(
            new AdvanceTurnPhaseAction(
                activeTurn.ConversationId,
                activeTurn.TurnId,
                phase,
                ToolCallId: toolCallId,
                ToolTitle: toolTitle)).ConfigureAwait(true);
    }

    private async Task ApplyPromptDispatchResultAsync(
        string conversationId,
        string turnId,
        SessionPromptResponse response)
    {
        switch (response.StopReason)
        {
            case StopReason.Cancelled:
                await PreemptivelyCancelTurnAsync(conversationId, turnId).ConfigureAwait(true);
                break;

            case StopReason.Refusal:
                await _chatStore.Dispatch(new FailTurnAction(conversationId, turnId, StopReason.Refusal.ToString())).ConfigureAwait(true);
                break;

            case StopReason.EndTurn:
            case StopReason.MaxTokens:
            case StopReason.MaxTurnRequests:
                await _chatStore.Dispatch(new CompleteTurnAction(conversationId, turnId)).ConfigureAwait(true);
                break;
        }
    }

    private async Task ApplySessionInfoUpdateAsync(string conversationId, SessionInfoUpdate update)
    {
        if (string.IsNullOrWhiteSpace(conversationId) || update is null)
        {
            return;
        }

        await _conversationWorkspace
            .ApplySessionInfoUpdateAsync(conversationId, update.Title, updatedAtUtc: ParseSessionUpdatedAtUtc(update.UpdatedAt))
            .ConfigureAwait(true);

        if (string.Equals(CurrentSessionId, conversationId, StringComparison.Ordinal))
        {
            CurrentSessionDisplayName = ResolveSessionDisplayName(conversationId);
        }
    }

    private async Task HandleAgentContentChunkAsync(string? conversationId, ContentBlock content)
    {
        // ACP streams response content as an array of blocks. We coalesce adjacent text blocks
        // into a single UI element to mimic a natural typing effect.
        if (content is TextContentBlock text)
        {
            await AppendAgentTextChunkAsync(conversationId, text.Text ?? string.Empty).ConfigureAwait(true);
            return;
        }

        await AddMessageToHistoryAsync(conversationId, content, isOutgoing: false).ConfigureAwait(true);
    }

    private async Task AppendAgentTextChunkAsync(string? conversationId, string chunk)
    {
        if (string.IsNullOrWhiteSpace(chunk) || string.IsNullOrWhiteSpace(conversationId))
        {
            return;
        }

        await _chatStore.Dispatch(new AppendTextDeltaAction(conversationId, chunk)).ConfigureAwait(false);
    }

    private Task<bool> ActivateConversationAsync(string sessionId, CancellationToken cancellationToken = default)
        => ActivateConversationCoreAsync(sessionId, awaitRemoteHydration: true, cancellationToken);

    private async Task<bool> ActivateConversationCoreAsync(
        string sessionId,
        bool awaitRemoteHydration,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return false;
        }

        if (await CanReuseWarmCurrentConversationAsync(sessionId, cancellationToken).ConfigureAwait(false))
        {
            await SupersedePendingActivationForWarmConversationAsync(sessionId, cancellationToken).ConfigureAwait(false);
            Logger.LogInformation(
                "Skipping redundant conversation activation because the current session is already warm. ConversationId={ConversationId}",
                sessionId);
            return true;
        }

        await SetConversationRuntimeStateAsync(
                sessionId,
                ConversationRuntimePhase.Selecting,
                reason: "ActivationStarted",
                cancellationToken)
            .ConfigureAwait(false);
        var activationStopwatch = Stopwatch.StartNew();

        var activationLease = BeginConversationActivation(cancellationToken);
        var activationGateEntered = false;
        var completionOwnedByBackground = false;
        try
        {
            ((IConversationActivationPreview)this).ClearSessionSwitchPreview(sessionId);
            await PostToUiAsync(() =>
            {
                SetConversationOverlayOwners(
                    sessionSwitchConversationId: sessionId,
                    connectionLifecycleConversationId: null,
                    historyConversationId: null);
                IsSessionSwitching = true;
            }).ConfigureAwait(false);
            Logger.LogInformation(
                "Conversation activation phase completed. phase=OverlayPrimed conversationId={ConversationId} activationVersion={ActivationVersion} elapsedMs={ElapsedMs}",
                sessionId,
                activationLease.Version,
                activationStopwatch.ElapsedMilliseconds);
            await _conversationActivationGate.WaitAsync(activationLease.CancellationToken).ConfigureAwait(false);
            activationGateEntered = true;
            activationLease.CancellationToken.ThrowIfCancellationRequested();
            Logger.LogInformation(
                "Conversation activation phase completed. phase=ActivationGateEntered conversationId={ConversationId} activationVersion={ActivationVersion} elapsedMs={ElapsedMs}",
                sessionId,
                activationLease.Version,
                activationStopwatch.ElapsedMilliseconds);
            await EnsureConversationWorkspaceRestoredAsync(activationLease.CancellationToken).ConfigureAwait(false);
            activationLease.CancellationToken.ThrowIfCancellationRequested();
            Logger.LogInformation(
                "Conversation activation phase completed. phase=WorkspaceRestored conversationId={ConversationId} activationVersion={ActivationVersion} elapsedMs={ElapsedMs}",
                sessionId,
                activationLease.Version,
                activationStopwatch.ElapsedMilliseconds);

            var activationHydrationMode = await ResolveConversationActivationHydrationModeAsync(
                    sessionId,
                    activationLease.CancellationToken)
                .ConfigureAwait(false);
            var activationResult = activationHydrationMode == ConversationActivationHydrationMode.SelectionOnly
                ? await _conversationActivationCoordinator
                    .ActivateSessionAsync(sessionId, activationHydrationMode, activationLease.CancellationToken)
                    .ConfigureAwait(false)
                : await _conversationActivationCoordinator
                    .ActivateSessionAsync(sessionId, activationLease.CancellationToken)
                    .ConfigureAwait(false);
            if (!activationResult.Succeeded)
            {
                await SetConversationRuntimeStateAsync(
                        sessionId,
                        ConversationRuntimePhase.Faulted,
                        reason: activationResult.FailureReason,
                        cancellationToken)
                    .ConfigureAwait(false);
                return false;
            }
            Logger.LogInformation(
                "Conversation activation phase completed. phase=ActivationCoordinatorSelected conversationId={ConversationId} activationVersion={ActivationVersion} elapsedMs={ElapsedMs}",
                sessionId,
                activationLease.Version,
                activationStopwatch.ElapsedMilliseconds);

            if (IsActivationContextStale(activationLease.Version, activationLease.CancellationToken))
            {
                return false;
            }

            await SetConversationRuntimeStateAsync(
                    sessionId,
                    ConversationRuntimePhase.Selected,
                    reason: "WorkspaceProjectionReady",
                    cancellationToken)
                .ConfigureAwait(false);

            activationLease.CancellationToken.ThrowIfCancellationRequested();
            await ResetRemoteHydrationUiStateAsync(activationLease.Version).ConfigureAwait(false);
            activationLease.CancellationToken.ThrowIfCancellationRequested();
            if (IsActivationContextStale(activationLease.Version, activationLease.CancellationToken))
            {
                return false;
            }
            await ApplyCurrentStoreProjectionAsync().ConfigureAwait(false);
            Logger.LogInformation(
                "Conversation activation phase completed. phase=InitialProjectionApplied conversationId={ConversationId} activationVersion={ActivationVersion} elapsedMs={ElapsedMs}",
                sessionId,
                activationLease.Version,
                activationStopwatch.ElapsedMilliseconds);
            const int slowSelectionActivationThresholdMs = 1200;
            if (activationStopwatch.ElapsedMilliseconds >= slowSelectionActivationThresholdMs)
            {
                Logger.LogWarning(
                    "Slow conversation selection detected. conversationId={ConversationId} activationVersion={ActivationVersion} elapsedMs={ElapsedMs}",
                    sessionId,
                    activationLease.Version,
                    activationStopwatch.ElapsedMilliseconds);
            }
            Logger.LogInformation(
                "Conversation activation selected phase completed. ConversationId={ConversationId} ElapsedMs={ElapsedMs}",
                sessionId,
                activationStopwatch.ElapsedMilliseconds);
            if (activationHydrationMode == ConversationActivationHydrationMode.WorkspaceSnapshot)
            {
                await DismissSessionSwitchOverlayAsync(activationLease.Version, sessionId).ConfigureAwait(false);
            }
            _conversationActivationGate.Release();
            activationGateEntered = false;
            if (!awaitRemoteHydration)
            {
                completionOwnedByBackground = true;
                Logger.LogInformation(
                    "Conversation activation handed off to background remote phase. ConversationId={ConversationId} ElapsedMs={ElapsedMs}",
                    sessionId,
                    activationStopwatch.ElapsedMilliseconds);
                _ = ContinueConversationActivationAsync(sessionId, activationLease);
                return true;
            }

            var remoteActivationSucceeded = await CompleteConversationRemoteActivationAsync(
                    sessionId,
                    activationLease.Version,
                    activationLease.CancellationToken)
                .ConfigureAwait(false);
            if (!remoteActivationSucceeded)
            {
                return false;
            }

            activationLease.CancellationToken.ThrowIfCancellationRequested();
            NotifyConversationListChanged();
            Logger.LogInformation(
                "Conversation activation fully completed. ConversationId={ConversationId} ElapsedMs={ElapsedMs}",
                sessionId,
                activationStopwatch.ElapsedMilliseconds);

            return true;
        }
        catch (OperationCanceledException) when (activationLease.CancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex)
        {
            HandleConversationActivationException(sessionId, ex);
            return false;
        }
        finally
        {
            if (activationGateEntered)
            {
                _conversationActivationGate.Release();
            }

            if (!completionOwnedByBackground && EndConversationActivation(activationLease))
            {
                ScheduleSessionSwitchOverlayDismissal(activationLease.Version, sessionId);
            }
        }
    }

    private async Task ContinueConversationActivationAsync(string sessionId, ConversationActivationLease activationLease)
    {
        try
        {
            var remoteActivationSucceeded = await CompleteConversationRemoteActivationAsync(
                    sessionId,
                    activationLease.Version,
                    activationLease.CancellationToken)
                .ConfigureAwait(false);
            if (remoteActivationSucceeded)
            {
                activationLease.CancellationToken.ThrowIfCancellationRequested();
                NotifyConversationListChanged();
            }
        }
        catch (OperationCanceledException) when (activationLease.CancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            HandleConversationActivationException(sessionId, ex);
        }
        finally
        {
            if (EndConversationActivation(activationLease))
            {
                ScheduleSessionSwitchOverlayDismissal(activationLease.Version, sessionId);
            }
        }
    }

    private async Task<bool> CompleteConversationRemoteActivationAsync(
        string sessionId,
        long activationVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var remotePhaseStopwatch = Stopwatch.StartNew();
        await _remoteConversationActivationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var remoteConnectionReady = await EnsureActiveConversationRemoteConnectionReadyAsync(
                    sessionId,
                    activationVersion,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!remoteConnectionReady)
            {
                Logger.LogInformation(
                    "Conversation remote activation failed before hydration. ConversationId={ConversationId} ElapsedMs={ElapsedMs}",
                    sessionId,
                    remotePhaseStopwatch.ElapsedMilliseconds);
                await SetConversationRuntimeStateAsync(
                        sessionId,
                        ConversationRuntimePhase.Faulted,
                        reason: "RemoteConnectionNotReady",
                        cancellationToken)
                    .ConfigureAwait(false);
                return false;
            }

            await SetConversationRuntimeStateAsync(
                    sessionId,
                    ConversationRuntimePhase.RemoteConnectionReady,
                    reason: "RemoteConnectionReady",
                    cancellationToken)
                .ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            var hydrated = await EnsureActiveConversationRemoteHydratedAsync(sessionId, activationVersion, cancellationToken).ConfigureAwait(false);
            Logger.LogInformation(
                "Conversation remote activation completed. ConversationId={ConversationId} Succeeded={Succeeded} ElapsedMs={ElapsedMs}",
                sessionId,
                hydrated,
                remotePhaseStopwatch.ElapsedMilliseconds);
            return hydrated;
        }
        finally
        {
            _remoteConversationActivationGate.Release();
        }
    }

    private void HandleConversationActivationException(string sessionId, Exception ex)
    {
        Logger.LogError(ex, "Switching session failed (SessionId={SessionId})", sessionId);

        _syncContext.Post(_ =>
        {
            SetError($"Failed to switch session: {ex.Message}");
            IsSessionActive = !string.IsNullOrWhiteSpace(CurrentSessionId);
        }, null);
    }

    private ConversationActivationLease BeginConversationActivation(CancellationToken cancellationToken)
    {
        CancelAmbientConnectionRequest();

        CancellationTokenSource? previousCts;
        var currentCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var version = Interlocked.Increment(ref _conversationActivationVersion);

        lock (_conversationActivationSync)
        {
            previousCts = _conversationActivationCts;
            _conversationActivationCts = currentCts;
        }

        try
        {
            previousCts?.Cancel();
        }
        finally
        {
            previousCts?.Dispose();
        }

        return new ConversationActivationLease(version, currentCts);
    }
    private bool EndConversationActivation(ConversationActivationLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);

        var completedCurrentActivation = false;
        lock (_conversationActivationSync)
        {
            if (ReferenceEquals(_conversationActivationCts, lease.CancellationTokenSource)
                && Volatile.Read(ref _conversationActivationVersion) == lease.Version)
            {
                _conversationActivationCts = null;
                completedCurrentActivation = true;
            }
        }

        lease.CancellationTokenSource.Dispose();
        return completedCurrentActivation;
    }

    private bool IsLatestConversationActivationVersion(long activationVersion)
        => Volatile.Read(ref _conversationActivationVersion) == activationVersion;

    private async Task SupersedePendingActivationForWarmConversationAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        var lease = BeginConversationActivation(cancellationToken);
        try
        {
            await ResetRemoteHydrationUiStateAsync(lease.Version).ConfigureAwait(false);
            await PostToUiAsync(() =>
            {
                _sessionSwitchPreviewConversationId = null;
                IsSessionSwitching = false;
                SetConversationOverlayOwners(
                    sessionSwitchConversationId: null,
                    connectionLifecycleConversationId: null,
                    historyConversationId: null);
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (lease.CancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            EndConversationActivation(lease);
        }
    }

    private bool IsActivationContextStale(long? activationVersion, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return true;
        }

        if (!activationVersion.HasValue)
        {
            return false;
        }

        return !IsLatestConversationActivationVersion(activationVersion.Value);
    }

    private async Task ResetRemoteHydrationUiStateAsync(long activationVersion)
    {
        if (!IsLatestConversationActivationVersion(activationVersion))
        {
            return;
        }

        await _chatStore.Dispatch(new SetIsHydratingAction(false)).ConfigureAwait(false);
        await PostToUiAsync(() =>
        {
            IsRemoteHydrationPending = false;
            _remoteHydrationSessionUpdateBaselineCounts.Clear();
            _remoteHydrationKnownTranscriptBaselineCounts.Clear();
            _remoteHydrationKnownTranscriptGrowthGraceDeadlineUtc.Clear();
            SetConversationOverlayOwners(
                sessionSwitchConversationId: _sessionSwitchOverlayConversationId,
                connectionLifecycleConversationId: null,
                historyConversationId: null);
        }).ConfigureAwait(false);
    }

    private void UpdateSlashCommands(AvailableCommandsUpdate update)
    {
        AvailableSlashCommands.Clear();
        foreach (var cmd in update.AvailableCommands)
        {
            AvailableSlashCommands.Add(new SlashCommandViewModel
            {
                Name = cmd.Name,
                Description = cmd.Description,
                InputHint = cmd.Input?.Hint
            });
        }

        RefreshSlashCommandFilter();
    }

    private void RefreshSlashCommandFilter()
    {
        var trimmed = (CurrentPrompt ?? string.Empty).TrimStart();
        if (!trimmed.StartsWith("/"))
        {
            ShowSlashCommands = false;
            SlashGhostSuffix = string.Empty;
            FilteredSlashCommands.Clear();
            SelectedSlashCommand = null;
            return;
        }

        var afterSlash = trimmed.Length > 1 ? trimmed[1..] : string.Empty;
        var token = afterSlash.Split(new[] { ' ', '\t', '\r', '\n' }, 2)[0];
        var hasAny = AvailableSlashCommands.Count > 0;

        FilteredSlashCommands.Clear();
        if (hasAny)
        {
            foreach (var cmd in AvailableSlashCommands.Where(c => c.Name.StartsWith(token, StringComparison.OrdinalIgnoreCase)))
            {
                FilteredSlashCommands.Add(cmd);
            }
        }

        SelectedSlashCommand = FilteredSlashCommands.FirstOrDefault();
        ShowSlashCommands = FilteredSlashCommands.Count > 0;

        UpdateSlashGhostSuffix(token, trimmed);
    }

    private void UpdateSlashGhostSuffix(string token, string trimmedPrompt)
    {
        if (SelectedSlashCommand != null && token.Length <= SelectedSlashCommand.Name.Length)
        {
            var suffix = SelectedSlashCommand.Name[token.Length..];
            SlashGhostSuffix = suffix + (trimmedPrompt.Contains(' ') ? string.Empty : " ");
        }
        else
        {
            SlashGhostSuffix = string.Empty;
        }
    }

    public bool TryAcceptSelectedSlashCommand(bool commitWithInputPlaceholder = false)
    {
        if (!ShowSlashCommands || SelectedSlashCommand == null)
        {
            return false;
        }

        var trimmed = (CurrentPrompt ?? string.Empty).TrimStart();
        var prefixWhitespaceCount = (CurrentPrompt ?? string.Empty).Length - trimmed.Length;
        var afterSlash = trimmed.Length > 1 ? trimmed[1..] : string.Empty;
        var rest = afterSlash.Split(new[] { ' ', '\t', '\r', '\n' }, 2);
        var existingToken = rest.Length > 0 ? rest[0] : string.Empty;
        var remainder = afterSlash.Length >= existingToken.Length ? afterSlash[existingToken.Length..] : string.Empty;

        var completed = "/" + SelectedSlashCommand.Name;
        if (!remainder.StartsWith(" "))
        {
            completed += " ";
        }

        if (commitWithInputPlaceholder && string.IsNullOrWhiteSpace(remainder) && !string.IsNullOrWhiteSpace(SelectedSlashCommand.InputHint))
        {
            // no-op: hint is shown in list; we don't inject it into the prompt
        }

        CurrentPrompt = new string(' ', prefixWhitespaceCount) + completed + remainder.TrimStart();
        ShowSlashCommands = false;
        SlashGhostSuffix = string.Empty;
        return true;
    }

    public bool TryMoveSlashSelection(int delta)
    {
        if (!ShowSlashCommands || FilteredSlashCommands.Count == 0)
        {
            return false;
        }

        var index = SelectedSlashCommand != null ? FilteredSlashCommands.IndexOf(SelectedSlashCommand) : 0;
        if (index < 0) index = 0;
        index = Math.Clamp(index + delta, 0, FilteredSlashCommands.Count - 1);
        SelectedSlashCommand = FilteredSlashCommands[index];

        var trimmed = (CurrentPrompt ?? string.Empty).TrimStart();
        var afterSlash = trimmed.Length > 1 ? trimmed[1..] : string.Empty;
        var token = afterSlash.Split(new[] { ' ', '\t', '\r', '\n' }, 2)[0];
        UpdateSlashGhostSuffix(token, trimmed);
        return true;
    }

    private void SyncBottomPanelState(string? conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            BottomPanelTabs = new ObservableCollection<BottomPanelTabViewModel>();
            SelectedBottomPanelTab = null;
            return;
        }

        if (!_bottomPanelStateByConversation.TryGetValue(conversationId, out var state))
        {
            state = new BottomPanelState(CreateDefaultBottomPanelTabs());
            _bottomPanelStateByConversation[conversationId] = state;
        }

        EnsureSelectedBottomPanelTab(state);
        BottomPanelTabs = state.Tabs;
        SelectedBottomPanelTab = state.Selected;
    }

    private static ObservableCollection<BottomPanelTabViewModel> CreateDefaultBottomPanelTabs()
        => new()
        {
            new BottomPanelTabViewModel("terminal", "BottomPanelTerminalTab.Text"),
            new BottomPanelTabViewModel("output", "BottomPanelOutputTab.Text")
        };

    private static void EnsureSelectedBottomPanelTab(BottomPanelState state)
    {
        if (state.Selected != null && state.Tabs.Contains(state.Selected))
        {
            return;
        }

        state.Selected = state.Tabs.FirstOrDefault();
    }

    private void RemoveBottomPanelState(string conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return;
        }

        _bottomPanelStateByConversation.Remove(conversationId);
        RemovePendingAskUserRequestState(conversationId);
        _ = _chatStore.Dispatch(new ClearConversationRuntimeStateAction(conversationId));

        if (string.Equals(CurrentSessionId, conversationId, StringComparison.Ordinal))
        {
            BottomPanelTabs = new ObservableCollection<BottomPanelTabViewModel>();
            SelectedBottomPanelTab = null;
        }
    }

    public Task<bool> SwitchConversationAsync(string conversationId, CancellationToken cancellationToken = default)
        => ActivateConversationCoreAsync(conversationId, awaitRemoteHydration: true, cancellationToken);

    Task<bool> IConversationSessionSwitcher.SwitchConversationAsync(string conversationId, CancellationToken cancellationToken)
        => ActivateConversationCoreAsync(conversationId, awaitRemoteHydration: false, cancellationToken);

    private void ApplySessionSwitchPreview(string conversationId)
    {
        if (_disposed)
        {
            return;
        }

        _sessionSwitchPreviewConversationId = conversationId;
        RaiseOverlayStateChanged();
    }

    private void ApplySessionSwitchPreviewClear(string conversationId)
    {
        if (_disposed
            || !string.Equals(_sessionSwitchPreviewConversationId, conversationId, StringComparison.Ordinal))
        {
            return;
        }

        _sessionSwitchPreviewConversationId = null;
        RaiseOverlayStateChanged();
    }

    void IConversationActivationPreview.PrimeSessionSwitchPreview(string conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return;
        }

        if (SynchronizationContext.Current == _syncContext)
        {
            ApplySessionSwitchPreview(conversationId);
            return;
        }

        _syncContext.Post(_ =>
        {
            ApplySessionSwitchPreview(conversationId);
        }, null);
    }

    void IConversationActivationPreview.ClearSessionSwitchPreview(string conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return;
        }

        if (SynchronizationContext.Current == _syncContext)
        {
            ApplySessionSwitchPreviewClear(conversationId);
            return;
        }

        _syncContext.Post(_ =>
        {
            ApplySessionSwitchPreviewClear(conversationId);
        }, null);
    }

    private void OnAskUserRequestReceived(object? sender, AskUserRequestEventArgs e)
    {
        _ = ProcessAskUserRequestAsync(e);
    }

    private async Task ProcessAskUserRequestAsync(AskUserRequestEventArgs e)
    {
        try
        {
            var conversationId = await ResolveConversationIdForRemoteSessionAsync(e.SessionId).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(conversationId))
            {
                Logger.LogWarning("Ask-user request ignored because no bound conversation matched remote session {RemoteSessionId}", e.SessionId);
                return;
            }

            AskUserRequestViewModel? requestViewModel = null;
            requestViewModel = AskUserInteractionViewModelFactory.Create(
                e.Request,
                e.MessageId,
                async answers =>
                {
                    var succeeded = await e.Respond(answers).ConfigureAwait(true);
                    if (!succeeded)
                    {
                        return false;
                    }

                    await PostToUiAsync(() => RemovePendingAskUserRequestState(conversationId!)).ConfigureAwait(true);
                    return true;
                });

            await PostToUiAsync(() =>
            {
                _pendingAskUserRequestsByConversation[conversationId!] = requestViewModel;
                SyncPendingAskUserRequestState(CurrentSessionId);
            }).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error processing ask-user request");
        }
    }

    private async Task<string?> ResolveConversationIdForRemoteSessionAsync(string remoteSessionId)
    {
        if (string.IsNullOrWhiteSpace(remoteSessionId))
        {
            return null;
        }

        var state = await _chatStore.State ?? ChatState.Empty;
        return ResolveConversationIdForRemoteSession(state, remoteSessionId);
    }

    private string? ResolveConversationIdForRemoteSession(ChatState state, string remoteSessionId)
    {
        if (string.IsNullOrWhiteSpace(remoteSessionId))
        {
            return null;
        }

        if (state.Bindings != null)
        {
            foreach (var binding in state.Bindings)
            {
                if (string.Equals(binding.Value.RemoteSessionId, remoteSessionId, StringComparison.Ordinal))
                {
                    return binding.Key;
                }
            }
        }

        return string.Equals(_currentRemoteSessionId, remoteSessionId, StringComparison.Ordinal)
            ? CurrentSessionId
            : null;
    }

    private void SyncPendingAskUserRequestState(string? conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            PendingAskUserRequest = null;
            return;
        }

        PendingAskUserRequest = _pendingAskUserRequestsByConversation.TryGetValue(conversationId, out var request)
            ? request
            : null;
    }

    private void RemovePendingAskUserRequestState(string conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return;
        }

        _pendingAskUserRequestsByConversation.Remove(conversationId);
        if (string.Equals(CurrentSessionId, conversationId, StringComparison.Ordinal))
        {
            PendingAskUserRequest = null;
        }
    }

    private void OnPermissionRequestReceived(object? sender, PermissionRequestEventArgs e)
    {
        _syncContext.Post(_ =>
        {
            try
            {
                var viewModel = new PermissionRequestViewModel
                {
                    MessageId = e.MessageId,
                    SessionId = e.SessionId,
                    ToolCallJson = e.ToolCall?.ToString() ?? string.Empty,
                    Options = new ObservableCollection<PermissionOptionViewModel>(
                        e.Options.Select(opt => new PermissionOptionViewModel
                        {
                            OptionId = opt.OptionId,
                            Name = opt.Name,
                            Kind = opt.Kind
                        }))
                };

                // Set response callback
                viewModel.OnRespond = async (outcome, optionId) =>
                {
                    if (_chatService != null)
                    {
                        await _chatService.RespondToPermissionRequestAsync(e.MessageId, outcome, optionId);
                    }
                    ShowPermissionDialog = false;
                    PendingPermissionRequest = null;
                };

                PendingPermissionRequest = viewModel;
                ShowPermissionDialog = true;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error processing permission request");
            }
        }, null);
    }

    private void OnFileSystemRequestReceived(object? sender, FileSystemRequestEventArgs e)
    {
        _syncContext.Post(_ =>
        {
            try
            {
                var viewModel = new FileSystemRequestViewModel
                {
                    MessageId = e.MessageId,
                    SessionId = e.SessionId,
                    Operation = e.Operation,
                    Path = e.Path,
                    Encoding = e.Encoding,
                    Content = e.Content
                };

                // Set response callback
                viewModel.OnRespond = async (success, content, message) =>
                {
                    if (_chatService != null)
                    {
                        await _chatService.RespondToFileSystemRequestAsync(e.MessageId, success, content, message);
                    }
                    ShowFileSystemDialog = false;
                    PendingFileSystemRequest = null;
                };

                PendingFileSystemRequest = viewModel;
                ShowFileSystemDialog = true;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error processing file system request");
            }
        }, null);
    }

    private void OnTerminalRequestReceived(object? sender, TerminalRequestEventArgs e)
    {
        _syncContext.Post(_ =>
        {
            try
            {
                Logger.LogInformation("Terminal request received: Method={Method}, TerminalId={TerminalId}", e.Method, e.TerminalId);
                ShowTransientNotificationToast($"Terminal request: {e.Method}");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error processing terminal request");
            }
        }, null);
    }

    private void OnErrorOccurred(object? sender, string error)
    {
        _syncContext.Post(_ =>
        {
            SetError(error);
            Logger.LogError(error);
        }, null);
    }

    private void UpdateAgentInfo()
    {
        if (_chatService?.AgentInfo != null)
        {
            _ = _chatStore.Dispatch(new SetAgentIdentityAction(_chatService.AgentInfo.Name, _chatService.AgentInfo.Version));
        }
    }

    private void CacheAuthMethods(InitializeResponse initResponse)
    {
        _advertisedAuthMethods = initResponse.AuthMethods;
    }

    private AuthMethodDefinition? GetPrimaryAuthMethod() =>
        _advertisedAuthMethods?.FirstOrDefault(m => !string.IsNullOrWhiteSpace(m.Id));

    private void ClearAuthenticationRequirement()
    {
        _ = _acpConnectionCoordinator.ClearAuthenticationRequiredAsync();
    }

    private void MarkAuthenticationRequired(AuthMethodDefinition? method, string? messageOverride = null)
    {
        var message =
            messageOverride
            ?? method?.Description
            ?? "The agent requires authentication before it can respond.";

        _ = _acpConnectionCoordinator.SetAuthenticationRequiredAsync(message);

        if (method != null)
        {
            Logger.LogInformation(
                "Agent requires authentication. id={MethodId}, name={Name}, hint={Hint}",
                method.Id,
                method.Name,
                message);
        }
        else
        {
            Logger.LogInformation("Agent requires authentication but did not advertise a usable methodId. hint={Hint}", message);
        }

        ShowTransientNotificationToast(message);
    }

    private async Task<bool> TryAuthenticateAsync(CancellationToken cancellationToken)
    {
        if (_chatService is null || !IsInitialized)
        {
            return false;
        }

        var method = GetPrimaryAuthMethod();
        if (method == null || string.IsNullOrWhiteSpace(method.Id))
        {
            MarkAuthenticationRequired(method);
            return false;
        }

        // Mark as required (blocks prompt sending) until authenticate succeeds.
        MarkAuthenticationRequired(method);

        try
        {
            var response = await _chatService
                .AuthenticateAsync(new AuthenticateParams(method.Id), cancellationToken)
                .ConfigureAwait(false);

            if (response.Authenticated)
            {
                ClearAuthenticationRequirement();
                return true;
            }

            MarkAuthenticationRequired(method, response.Message);
            return false;
        }
        catch (AcpException ex) when (ex.ErrorCode == JsonRpcErrorCode.MethodNotFound)
        {
            // Agent advertised authMethods but does not implement `authenticate`. Fall back to informational hint.
            MarkAuthenticationRequired(method);
            return false;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Authenticate failed");
            MarkAuthenticationRequired(method, $"Authentication failed: {ex.Message}");
            return false;
        }
    }

    private static bool IsAuthenticationRequiredError(Exception ex) =>
        ex is AcpException acp && acp.ErrorCode == JsonRpcErrorCode.AuthenticationRequired;

    private Task AddMessageToHistoryAsync(string? conversationId, ContentBlock content, bool isOutgoing)
    {
        return UpsertTranscriptSnapshotAsync(conversationId, CreateContentSnapshot(content, isOutgoing));
    }

    private ConversationMessageSnapshot CreateContentSnapshot(ContentBlock content, bool isOutgoing)
    {
        var snapshot = new ConversationMessageSnapshot
        {
            Id = Guid.NewGuid().ToString(),
            Timestamp = DateTime.UtcNow,
            IsOutgoing = isOutgoing
        };

        switch (content)
        {
            case TextContentBlock text:
                snapshot.ContentType = "text";
                snapshot.TextContent = text.Text ?? string.Empty;
                break;
            case ImageContentBlock image:
                snapshot.ContentType = "image";
                snapshot.ImageData = image.Data ?? string.Empty;
                snapshot.ImageMimeType = image.MimeType ?? string.Empty;
                break;
            case AudioContentBlock audio:
                snapshot.ContentType = "audio";
                snapshot.AudioData = audio.Data ?? string.Empty;
                snapshot.AudioMimeType = audio.MimeType ?? string.Empty;
                break;
            case ResourceContentBlock resourceContent:
                snapshot.ContentType = "resource";
                snapshot.TextContent = resourceContent.Resource?.Uri?.ToString() ?? string.Empty;
                break;
            case ResourceLinkContentBlock resourceLink:
                snapshot.ContentType = "resource_link";
                snapshot.TextContent = resourceLink.Uri?.ToString() ?? string.Empty;
                break;
            default:
                snapshot.ContentType = "text";
                snapshot.TextContent = $"[{content.GetType().Name}]";
                break;
        }

        return snapshot;
    }

    private ConversationMessageSnapshot CreateToolCallSnapshot(ToolCallUpdate toolCall)
    {
        return new ConversationMessageSnapshot
        {
            Id = Guid.NewGuid().ToString(),
            Timestamp = DateTime.UtcNow,
            IsOutgoing = false,
            ContentType = "tool_call",
            Title = toolCall.Title ?? string.Empty,
            TextContent = ResolveToolCallOutput(toolCall.RawOutput, toolCall.Content, string.Empty),
            ToolCallId = toolCall.ToolCallId,
            ToolCallKind = toolCall.Kind,
            ToolCallStatus = toolCall.Status,
            ToolCallJson = TryGetRawJson(toolCall.RawInput)
        };
    }

    private async Task UpsertTranscriptSnapshotAsync(string? conversationId, ConversationMessageSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return;
        }

        await _chatStore.Dispatch(new UpsertTranscriptMessageAction(conversationId, snapshot)).ConfigureAwait(false);
    }

    private async Task UpdateToolCallStatusAsync(string? conversationId, ToolCallStatusUpdate toolCallStatusUpdate)
    {
        if (string.IsNullOrWhiteSpace(conversationId) || string.IsNullOrEmpty(toolCallStatusUpdate.ToolCallId))
        {
            return;
        }

        var state = await _chatStore.State ?? ChatState.Empty;
        var currentTranscript = state.ResolveContentSlice(conversationId)?.Transcript
            ?? ImmutableList<ConversationMessageSnapshot>.Empty;
        var existing = currentTranscript.LastOrDefault(message =>
            string.Equals(message.ToolCallId, toolCallStatusUpdate.ToolCallId, StringComparison.Ordinal)
            && string.Equals(message.ContentType, "tool_call", StringComparison.Ordinal));
        var merged = existing is null
            ? CreateToolCallSnapshot(toolCallStatusUpdate)
            : new ConversationMessageSnapshot
            {
                Id = existing.Id,
                Timestamp = DateTime.UtcNow,
                IsOutgoing = existing.IsOutgoing,
                ContentType = existing.ContentType,
                Title = string.IsNullOrWhiteSpace(toolCallStatusUpdate.Title) ? existing.Title : toolCallStatusUpdate.Title,
                TextContent = ResolveToolCallOutput(
                    toolCallStatusUpdate.RawOutput,
                    toolCallStatusUpdate.Content,
                    existing.TextContent),
                ImageData = existing.ImageData,
                ImageMimeType = existing.ImageMimeType,
                AudioData = existing.AudioData,
                AudioMimeType = existing.AudioMimeType,
                ToolCallId = existing.ToolCallId,
                ToolCallKind = toolCallStatusUpdate.Kind ?? existing.ToolCallKind,
                ToolCallStatus = toolCallStatusUpdate.Status ?? existing.ToolCallStatus,
                ToolCallJson = TryGetRawJson(toolCallStatusUpdate.RawInput) ?? existing.ToolCallJson,
                PlanEntry = ClonePlanEntrySnapshot(existing.PlanEntry),
                ModeId = existing.ModeId
            };

        await _chatStore.Dispatch(new UpsertTranscriptMessageAction(conversationId, merged)).ConfigureAwait(false);
    }

    private ConversationMessageSnapshot CreateToolCallSnapshot(ToolCallStatusUpdate toolCallStatusUpdate)
    {
        return new ConversationMessageSnapshot
        {
            Id = Guid.NewGuid().ToString(),
            Timestamp = DateTime.UtcNow,
            IsOutgoing = false,
            ContentType = "tool_call",
            Title = toolCallStatusUpdate.Title ?? string.Empty,
            TextContent = ResolveToolCallOutput(toolCallStatusUpdate.RawOutput, toolCallStatusUpdate.Content, string.Empty),
            ToolCallId = toolCallStatusUpdate.ToolCallId,
            ToolCallKind = toolCallStatusUpdate.Kind,
            ToolCallStatus = toolCallStatusUpdate.Status,
            ToolCallJson = TryGetRawJson(toolCallStatusUpdate.RawInput)
        };
    }

    private static string? TryGetRawJson(System.Text.Json.JsonElement? element)
        => element?.GetRawText();

    private static string ResolveToolCallOutput(
        System.Text.Json.JsonElement? rawOutput,
        IReadOnlyList<Domain.Models.Tool.ToolCallContent>? content,
        string? fallback)
    {
        var serializedOutput = TryGetRawJson(rawOutput);
        if (!string.IsNullOrWhiteSpace(serializedOutput))
        {
            return serializedOutput;
        }

        var flattened = FlattenToolCallContent(content);
        if (!string.IsNullOrWhiteSpace(flattened))
        {
            return flattened;
        }

        return fallback ?? string.Empty;
    }

    private static string FlattenToolCallContent(IReadOnlyList<Domain.Models.Tool.ToolCallContent>? content)
    {
        if (content == null || content.Count == 0)
        {
            return string.Empty;
        }

        var parts = new List<string>(content.Count);
        foreach (var item in content)
        {
            switch (item)
            {
                case Domain.Models.Tool.ContentToolCallContent { Content: TextContentBlock textBlock } when !string.IsNullOrWhiteSpace(textBlock.Text):
                    parts.Add(textBlock.Text);
                    break;
                case Domain.Models.Tool.DiffToolCallContent diff when !string.IsNullOrWhiteSpace(diff.NewText):
                    parts.Add(diff.NewText);
                    break;
                case Domain.Models.Tool.DiffToolCallContent diff when !string.IsNullOrWhiteSpace(diff.Path):
                    parts.Add(diff.Path);
                    break;
                case Domain.Models.Tool.TerminalToolCallContent terminal when !string.IsNullOrWhiteSpace(terminal.TerminalId):
                    parts.Add(terminal.TerminalId);
                    break;
            }
        }

        return parts.Count == 0 ? string.Empty : string.Join(Environment.NewLine, parts);
    }

    private async Task PreemptivelyCancelTurnAsync(string? expectedConversationId = null, string? expectedTurnId = null)
    {
        var state = await _chatStore.State ?? ChatState.Empty;
        var activeTurn = state.ActiveTurn;
        if (activeTurn is null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(expectedConversationId)
            && !string.Equals(activeTurn.ConversationId, expectedConversationId, StringComparison.Ordinal))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(expectedTurnId)
            && !string.Equals(activeTurn.TurnId, expectedTurnId, StringComparison.Ordinal))
        {
            return;
        }

        await PreemptivelyCancelOutstandingToolCallsAsync(state, activeTurn).ConfigureAwait(true);
        await _chatStore.Dispatch(new CancelTurnAction(activeTurn.ConversationId, activeTurn.TurnId)).ConfigureAwait(true);
    }

    private async Task PreemptivelyCancelOutstandingToolCallsAsync(ChatState state, ActiveTurnState activeTurn)
    {
        if (string.IsNullOrWhiteSpace(activeTurn.ConversationId))
        {
            return;
        }

        var transcript = state.ResolveContentSlice(activeTurn.ConversationId)?.Transcript
            ?? (string.Equals(state.HydratedConversationId, activeTurn.ConversationId, StringComparison.Ordinal)
                ? state.Transcript
                : null)
            ?? ImmutableList<ConversationMessageSnapshot>.Empty;
        var cutoff = activeTurn.StartedAtUtc == default ? DateTime.MinValue : activeTurn.StartedAtUtc;
        var pendingToolCalls = transcript
            .Where(message =>
                string.Equals(message.ContentType, "tool_call", StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(message.ToolCallId)
                && message.Timestamp >= cutoff
                && message.ToolCallStatus is null
                    or Domain.Models.Tool.ToolCallStatus.Pending
                    or Domain.Models.Tool.ToolCallStatus.InProgress)
            .ToArray();

        foreach (var existing in pendingToolCalls)
        {
            await _chatStore.Dispatch(new UpsertTranscriptMessageAction(activeTurn.ConversationId, new ConversationMessageSnapshot
            {
                Id = existing.Id,
                Timestamp = DateTime.UtcNow,
                IsOutgoing = existing.IsOutgoing,
                ContentType = existing.ContentType,
                Title = existing.Title,
                TextContent = existing.TextContent,
                ImageData = existing.ImageData,
                ImageMimeType = existing.ImageMimeType,
                AudioData = existing.AudioData,
                AudioMimeType = existing.AudioMimeType,
                ToolCallId = existing.ToolCallId,
                ToolCallKind = existing.ToolCallKind,
                ToolCallStatus = Domain.Models.Tool.ToolCallStatus.Cancelled,
                ToolCallJson = existing.ToolCallJson,
                PlanEntry = ClonePlanEntrySnapshot(existing.PlanEntry),
                ModeId = existing.ModeId
            })).ConfigureAwait(true);
        }
    }

    private async Task ApplySessionUpdateDeltaAsync(string conversationId, AcpSessionUpdateDelta delta)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return;
        }

        var nextModes = delta.AvailableModes != null
            ? delta.AvailableModes.Select(ToConversationModeOptionSnapshot).ToImmutableList()
            : null;
        var nextConfigOptions = delta.ConfigOptions != null
            ? delta.ConfigOptions.Select(ToConversationConfigOptionSnapshot).ToImmutableList()
            : null;
        var hasSelectedModeId = !string.IsNullOrWhiteSpace(delta.SelectedModeId)
            || delta.AvailableModes is { Count: 0 };
        var nextSelectedModeId = !string.IsNullOrWhiteSpace(delta.SelectedModeId)
            ? delta.SelectedModeId
            : null;
        var nextShowConfigOptionsPanel = delta.ShowConfigOptionsPanel;
        if (nextShowConfigOptionsPanel is null && nextConfigOptions != null)
        {
            nextShowConfigOptionsPanel = nextConfigOptions.Count > 0;
        }

        await _chatStore.Dispatch(new MergeConversationSessionStateAction(
            conversationId,
            nextModes,
            nextSelectedModeId,
            hasSelectedModeId,
            nextConfigOptions,
            nextShowConfigOptionsPanel)).ConfigureAwait(true);

        if (delta.PlanEntries != null)
        {
            await _chatStore.Dispatch(new ReplacePlanEntriesAction(
                conversationId,
                delta.PlanEntries.ToImmutableList(),
                delta.ShowPlanPanel ?? true,
                delta.PlanTitle)).ConfigureAwait(true);
        }
    }

    private static ConfigOptionViewModel MapConfigOption(ConversationConfigOptionSnapshot option)
    {
        var viewModel = new ConfigOptionViewModel
        {
            Id = option.Id,
            Name = option.Name,
            Description = option.Description,
            Category = option.Category,
            ValueType = option.ValueType ?? "string",
            IsRequired = true,
            Value = option.SelectedValue ?? string.Empty,
            TextValue = option.SelectedValue ?? string.Empty
        };

        if (option.Options.Count > 0)
        {
            viewModel.Options = new ObservableCollection<OptionValueViewModel>(
                option.Options.Select(static item => new OptionValueViewModel
                {
                    Value = item.Value,
                    Name = item.Name,
                    Description = item.Description
                }));
            viewModel.SelectedOption = viewModel.Options.FirstOrDefault(item => item.Value == option.SelectedValue);
        }

        return viewModel;
    }

    private static ConversationModeOptionSnapshot ToConversationModeOptionSnapshot(AcpModeOption option)
        => new()
        {
            ModeId = option.ModeId,
            ModeName = option.ModeName,
            Description = option.Description
        };

    private static ConversationConfigOptionSnapshot ToConversationConfigOptionSnapshot(AcpConfigOptionSnapshot option)
        => new()
        {
            Id = option.Id,
            Name = option.Name,
            Description = option.Description,
            Category = option.Category,
            ValueType = option.ValueType,
            SelectedValue = option.SelectedValue,
            Options = option.Options
                .Select(static item => new ConversationConfigOptionChoiceSnapshot
                {
                    Value = item.Value,
                    Name = item.Name,
                    Description = item.Description
                })
                .ToList()
        };

    private void SyncModesFromConfigOptions()
    {
        var modeOption = ConfigOptions.FirstOrDefault(o =>
            string.Equals(o.Category, "mode", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(o.Id, "mode", StringComparison.OrdinalIgnoreCase));

        if (modeOption == null || modeOption.Options.Count == 0)
        {
            _modeConfigId = null;
            return;
        }

        _modeConfigId = modeOption.Id;

        if (AvailableModes.Count == 0)
        {
            foreach (var opt in modeOption.Options)
            {
                AvailableModes.Add(new SessionModeViewModel
                {
                    ModeId = opt.Value,
                    ModeName = opt.Name,
                    Description = opt.Description ?? string.Empty
                });
            }
        }

        var current = modeOption.SelectedOption?.Value ?? modeOption.TextValue;
        if (!string.IsNullOrWhiteSpace(current) && AvailableModes.Count > 0)
        {
            SetSelectedModeWithoutDispatch(
                AvailableModes.FirstOrDefault(m => m.ModeId == current) ?? AvailableModes[0]);
        }
    }

    private void SetSelectedModeWithoutDispatch(SessionModeViewModel? mode)
    {
        _suppressModeSelectionDispatch = true;
        try
        {
            SelectedMode = mode;
        }
        finally
        {
            _suppressModeSelectionDispatch = false;
        }
    }

    private async Task ApplySessionConfigOptionResponseAsync(
        string conversationId,
        SessionSetConfigOptionResponse response,
        string remoteSessionId)
    {
        if (response?.ConfigOptions == null)
        {
            return;
        }

        await ApplySessionUpdateDeltaAsync(conversationId, _acpSessionUpdateProjector.Project(
            new SessionUpdateEventArgs(
                remoteSessionId,
                new ConfigOptionUpdate
                {
                    ConfigOptions = response.ConfigOptions
                }))).ConfigureAwait(true);
    }

    private async Task ApplySessionModeResponseAsync(
        string conversationId,
        SessionSetModeResponse response,
        string remoteSessionId)
    {
        if (response is null || string.IsNullOrWhiteSpace(response.ModeId))
        {
            return;
        }

        await ApplySessionUpdateDeltaAsync(conversationId, _acpSessionUpdateProjector.Project(
            new SessionUpdateEventArgs(
                remoteSessionId,
                new CurrentModeUpdate(response.ModeId)))).ConfigureAwait(true);
    }

   [RelayCommand]
    private async Task InitializeAndConnectAsync()
    {
        if (IsInitializing || IsConnecting)
            return;

       // If ChatService is not yet created, apply transport config first.
       if (_chatService == null)
       {
           Logger.LogInformation("ChatService not yet created; calling ApplyTransportConfigAsync");
           await ApplyTransportConfigCommand.ExecuteAsync(null);
           return;
       }

        try
        {
            ClearError();
            var selectedProfileId = await ResolveSelectedProfileIdAsync().ConfigureAwait(false);
            await _acpConnectionCoordinator
                .SetInitializingAsync(selectedProfileId)
                .ConfigureAwait(false);

            var initParams = AcpInitializeRequestFactory.CreateDefault();

            if (_chatService == null)
            {
               throw new InvalidOperationException("Chat service is not initialized");
           }

            var initResponse = await _chatService.InitializeAsync(initParams);
            await _acpConnectionCoordinator
                .SetConnectedAsync(selectedProfileId)
                .ConfigureAwait(false);
            UpdateAgentInfo();
            CacheAuthMethods(initResponse);
            await _acpConnectionCoordinator.ClearAuthenticationRequiredAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await _acpConnectionCoordinator.SetDisconnectedAsync(ex.Message).ConfigureAwait(false);
            Logger.LogError(ex, "Initialization failed");
            SetError($"Initialization failed: {ex.Message}");
        }
    }

    private async Task<string?> ResolveSelectedProfileIdAsync()
    {
        if (!string.IsNullOrWhiteSpace(SelectedAcpProfile?.Id))
        {
            return SelectedAcpProfile!.Id;
        }

        var connectionState = await _chatConnectionStore.State ?? ChatConnectionState.Empty;
        return connectionState.SelectedProfileId;
    }

     [RelayCommand]
     private async Task CreateNewSessionAsync()
     {
        if (IsConnecting)
            return;

        try
        {
            ClearError();

            var sessionParams = new SessionNewParams
            {
                Cwd = GetActiveSessionCwdOrDefault(),
                McpServers = new List<McpServer>() // Can add MCP servers based on configuration.
            };

            if (_chatService == null)
            {
                throw new InvalidOperationException("Chat service is not initialized");
            }

            SessionNewResponse response;
            try
            {
                response = await _chatService.CreateSessionAsync(sessionParams);
            }
            catch (Exception ex) when (IsAuthenticationRequiredError(ex))
            {
                var authenticated = await TryAuthenticateAsync(CancellationToken.None).ConfigureAwait(false);
                if (!authenticated)
                {
                    return;
                }

                response = await _chatService.CreateSessionAsync(sessionParams);
            }

            var localConversationId = Guid.NewGuid().ToString("N");
            await _sessionManager.CreateSessionAsync(localConversationId, sessionParams.Cwd).ConfigureAwait(false);
            await _conversationWorkspace.RegisterConversationAsync(
                localConversationId,
                createdAt: DateTime.UtcNow,
                lastUpdatedAt: DateTime.UtcNow).ConfigureAwait(false);

            var switched = await ActivateConversationAsync(localConversationId).ConfigureAwait(false);
            if (!switched)
            {
                throw new InvalidOperationException("Failed to activate local conversation before applying session response.");
            }

            var bindingResult = await _bindingCommands
                .UpdateBindingAsync(localConversationId, response.SessionId, SelectedProfileId)
                .ConfigureAwait(false);
            if (bindingResult.Status is not BindingUpdateStatus.Success)
            {
                throw new InvalidOperationException(
                    $"Failed to bind new conversation ({bindingResult.Status}): {bindingResult.ErrorMessage ?? "UnknownError"}");
            }

            await ApplyCurrentStoreProjectionAsync().ConfigureAwait(false);
            await ApplySessionNewResponseAsync(localConversationId, response).ConfigureAwait(true);
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
        if (string.IsNullOrWhiteSpace(CurrentPrompt) || !IsSessionActive)
            return;

        if (IsPromptInFlight)
            return;

        if (IsAuthenticationRequired)
        {
            var authenticated = await TryAuthenticateAsync(_sendPromptCts?.Token ?? CancellationToken.None).ConfigureAwait(false);
            if (!authenticated)
            {
                ShowTransientNotificationToast(AuthenticationHintMessage ?? "The agent requires authentication before it can respond.");
                return;
            }
        }

        var promptText = CurrentPrompt;
        var conversationId = CurrentSessionId!;
        var turnId = Guid.NewGuid().ToString();

        try
        {
            ClearError();
            await _chatStore.Dispatch(new SetPromptInFlightAction(true));
            await _chatStore.Dispatch(new BeginTurnAction(conversationId, turnId, ChatTurnPhase.CreatingRemoteSession));

            // Clear input immediately for better UX
            CurrentPrompt = string.Empty;

            // Add user message to history
            var userContent = new TextContentBlock { Text = promptText };
            await AddMessageToHistoryAsync(conversationId, userContent, isOutgoing: true).ConfigureAwait(true);

            if (_chatService != null)
            {
                _sendPromptCts?.Cancel();
                _sendPromptCts = new CancellationTokenSource();
                var token = _sendPromptCts.Token;

                // Step 1: Ensure remote session exists before dispatching (if not already bound)
                var sessionResult = await _acpConnectionCommands
                    .EnsureRemoteSessionAsync(this, TryAuthenticateAsync, token)
                    .ConfigureAwait(false);

                if (!sessionResult.UsedExistingBinding)
                {
                    await ApplySessionNewResponseAsync(conversationId, sessionResult.Session).ConfigureAwait(true);
                }

                // Step 2: Advance phase to waiting for agent response
                await _chatStore.Dispatch(new AdvanceTurnPhaseAction(conversationId, turnId, ChatTurnPhase.WaitingForAgent));

                // Step 3: Dispatch the prompt to the identified remote session
                var promptDispatchResult = await _acpConnectionCommands
                    .DispatchPromptToRemoteSessionAsync(sessionResult.RemoteSessionId, promptText, this, TryAuthenticateAsync, token)
                    .ConfigureAwait(false);

                await ApplyPromptDispatchResultAsync(conversationId, turnId, promptDispatchResult.Response).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // User-cancelled; keep input cleared.
            await PreemptivelyCancelTurnAsync(conversationId, turnId).ConfigureAwait(true);
        }
        catch (TimeoutException ex)
        {
            Logger.LogError(ex, "SendPrompt timed out");
            SetError("Send timed out: Agent did not respond for a long time.");

            await _chatStore.Dispatch(new FailTurnAction(conversationId, turnId, "Timed out"));

            if (string.IsNullOrWhiteSpace(CurrentPrompt))
            {
                CurrentPrompt = promptText;
            }

            ShowTransientNotificationToast("Agent no response (timeout). Please check if the agent needs login/initialization or try again later.");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "SendPrompt failed");
            SetError($"Send failed: {ex.Message}");

            await _chatStore.Dispatch(new FailTurnAction(conversationId, turnId, ex.Message));

            // Restore text so the user can retry quickly.
            if (string.IsNullOrWhiteSpace(CurrentPrompt))
            {
                CurrentPrompt = promptText;
            }

            ShowTransientNotificationToast("Send failed, please try again later.");
        }
        finally
        {
            try { _sendPromptCts?.Dispose(); } catch { }
            _sendPromptCts = null;
            await _chatStore.Dispatch(new SetPromptInFlightAction(false));
        }
    }

    private bool CanSendPrompt() =>
        IsSessionActive
        && _chatService is not null
        && IsInitialized
        && !string.IsNullOrWhiteSpace(CurrentSessionId)
        && !string.IsNullOrWhiteSpace(CurrentPrompt)
        && !IsBusy
        && !IsPromptInFlight
        && PendingAskUserRequest is null;

    [RelayCommand]
    private async Task CancelPromptAsync()
    {
        if (!IsPromptInFlight)
        {
            return;
        }

        try
        {
            _sendPromptCts?.Cancel();
        }
        catch
        {
        }

        if (!IsSessionActive)
        {
            return;
        }

        try
        {
            await PreemptivelyCancelTurnAsync().ConfigureAwait(false);
            var activeBinding = await ResolveActiveConversationBindingAsync().ConfigureAwait(false);
            await CancelPendingPermissionRequestAsync(activeBinding?.RemoteSessionId).ConfigureAwait(false);
            await _acpConnectionCommands.CancelPromptAsync(this, "User cancelled").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Cancel prompt failed");
            ShowTransientNotificationToast("Cancellation failed.");
        }
    }

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

        _syncContext.Post(_ =>
        {
            TransientNotificationMessage = message.Trim();
            ShowTransientNotification = true;
        }, null);

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

            _syncContext.Post(_ => { ShowTransientNotification = false; }, null);
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
                if (!string.IsNullOrWhiteSpace(_modeConfigId))
                {
                    var setParams = new SessionSetConfigOptionParams(
                        activeBinding.RemoteSessionId!,
                        _modeConfigId,
                        mode.ModeId ?? string.Empty);
                    var response = await _chatService.SetSessionConfigOptionAsync(setParams).ConfigureAwait(true);
                    await ApplySessionConfigOptionResponseAsync(
                        activeBinding.ConversationId,
                        response,
                        activeBinding.RemoteSessionId!).ConfigureAwait(true);
                }
                else
                {
                    var modeParams = new SessionSetModeParams
                    {
                        SessionId = activeBinding.RemoteSessionId!,
                        ModeId = mode.ModeId
                    };
                    var response = await _chatService.SetSessionModeAsync(modeParams).ConfigureAwait(true);
                    await ApplySessionModeResponseAsync(
                        activeBinding.ConversationId,
                        response,
                        activeBinding.RemoteSessionId!).ConfigureAwait(true);
                }
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
                false,
                null));
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
            _pendingAskUserRequestsByConversation.Clear();
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
        OnPropertyChanged(nameof(CanSendPromptUi));
        RefreshSlashCommandFilter();
    }

    partial void OnIsPromptInFlightChanged(bool value)
    {
        SendPromptCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsInputEnabled));
        OnPropertyChanged(nameof(CanSendPromptUi));


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
        OnPropertyChanged(nameof(IsInputEnabled));
        OnPropertyChanged(nameof(CanSendPromptUi));
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
        OnPropertyChanged(nameof(CanSendPromptUi));
    }

    partial void OnIsSessionActiveChanged(bool value)
    {
        SendPromptCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanSendPromptUi));
    }

    public Task RestoreAsync(CancellationToken cancellationToken = default)
        => EnsureConversationWorkspaceRestoredAsync(cancellationToken);

    public IChatService? CurrentChatService => _chatService;

    public long ConnectionGeneration => Interlocked.Read(ref _connectionGeneration);

    public SynchronizationContext SessionUpdateSynchronizationContext => _syncContext;

    public IConversationBindingCommands ConversationBindingCommands => _bindingCommands;

    public ValueTask<ConversationRemoteBindingState?> GetCurrentRemoteBindingAsync(CancellationToken cancellationToken = default)
        => ResolveActiveConversationBindingAsync(cancellationToken);

    public async ValueTask<ConversationRemoteBindingState?> GetConversationRemoteBindingAsync(
        string conversationId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var binding = await ResolveConversationBindingAsync(conversationId, cancellationToken).ConfigureAwait(false);
        return ToBindingState(binding);
    }

    public bool IsInitialized => IsConnected && !IsInitializing;

    public string? CurrentRemoteSessionId => _currentRemoteSessionId;

    public string? SelectedProfileId => _selectedProfileIdFromStore;

    public Task SetIsHydratingAsync(bool isHydrating, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _chatStore.Dispatch(new SetIsHydratingAction(isHydrating)).AsTask();
    }

    public async Task SetConversationHydratingAsync(
        string conversationId,
        bool isHydrating,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(CurrentSessionId, conversationId, StringComparison.Ordinal))
        {
            return;
        }

        await PostToUiAsync(() =>
        {
            if (isHydrating)
            {
                _pendingHistoryOverlayDismissConversationId = null;
                SetConversationOverlayOwners(
                    sessionSwitchConversationId: _sessionSwitchOverlayConversationId,
                    connectionLifecycleConversationId: _connectionLifecycleOverlayConversationId,
                    historyConversationId: conversationId);
            }
            else if (string.Equals(_historyOverlayConversationId, conversationId, StringComparison.Ordinal))
            {
                _pendingHistoryOverlayDismissConversationId = conversationId;
            }
        }).ConfigureAwait(false);

        await _chatStore.Dispatch(new SetIsHydratingAction(isHydrating)).ConfigureAwait(false);
    }

    public async Task MarkActiveConversationRemoteHydratedAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var conversationId = CurrentSessionId;
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return;
        }

        var binding = await ResolveConversationBindingAsync(conversationId, cancellationToken).ConfigureAwait(false);
        await SetConversationRuntimeStateAsync(
                conversationId!,
                ConversationRuntimePhase.Warm,
                binding,
                reason: "MarkedHydrated",
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task MarkConversationRemoteHydratedAsync(
        string conversationId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return;
        }

        var binding = await ResolveConversationBindingAsync(conversationId, cancellationToken).ConfigureAwait(false);
        await SetConversationRuntimeStateAsync(
                conversationId,
                ConversationRuntimePhase.Warm,
                binding,
                reason: "MarkedHydrated",
                cancellationToken)
            .ConfigureAwait(false);
    }

    public Task ApplyConversationSessionLoadResponseAsync(
        string conversationId,
        SessionLoadResponse response,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ApplySessionLoadResponseAsync(conversationId, response);
    }

    public async Task ResetConversationForResyncAsync(
        string conversationId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(CurrentSessionId, conversationId, StringComparison.Ordinal))
        {
            return;
        }

        var binding = await ResolveConversationBindingAsync(conversationId, cancellationToken).ConfigureAwait(false);
        await SetConversationRuntimeStateAsync(
                conversationId,
                ConversationRuntimePhase.RemoteHydrating,
                binding,
                reason: "ResyncResetStarted",
                cancellationToken: CancellationToken.None)
            .ConfigureAwait(false);

        await ResetConversationProjectionForResyncAsync(conversationId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> HydrateActiveConversationAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var state = await _chatStore.State ?? ChatState.Empty;
        var conversationId = ResolveActiveConversationId(state);
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            SetError("Failed to load session: no active conversation is selected.");
            return false;
        }

        var binding = await ResolveConversationBindingAsync(conversationId!, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(binding?.RemoteSessionId))
        {
            SetError("Failed to load session: no remote session binding is available for the active conversation.");
            return false;
        }

        return await HydrateConversationAsync(
                conversationId!,
                binding!,
                activationVersion: null,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<bool> HydrateConversationAsync(
        string conversationId,
        ConversationBindingSlice binding,
        long? activationVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_chatService is not { IsConnected: true, IsInitialized: true } chatService)
        {
            SetError("Failed to load session: ACP chat service is not connected and initialized.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(binding.RemoteSessionId))
        {
            SetError("Failed to load session: no remote session binding is available for the active conversation.");
            return false;
        }

        await SetConversationRuntimeStateAsync(
                conversationId,
                ConversationRuntimePhase.RemoteHydrating,
                binding,
                reason: "SessionLoadStarted",
                cancellationToken)
            .ConfigureAwait(false);
        var hydrationStopwatch = Stopwatch.StartNew();
        var adapter = chatService as IAcpSessionUpdateBufferController;
        long? hydrationAttemptId = null;
        Logger.LogInformation(
            "Starting conversation hydration. ConversationId={ConversationId} RemoteSessionId={RemoteSessionId} CompletionMode={CompletionMode}",
            conversationId,
            binding.RemoteSessionId,
            _hydrationCompletionMode);

        try
        {
            hydrationAttemptId = adapter?.BeginHydrationBufferingScope(binding.RemoteSessionId);
            var ownsRemoteHydrationUi = ShouldOwnRemoteHydrationUi(conversationId, activationVersion);
            var transcriptBaselineCount = await GetProjectedTranscriptCountAsync(conversationId).ConfigureAwait(false);
            var knownTranscriptGrowthGraceDeadlineUtc = DateTime.UtcNow + RemoteReplayKnownTranscriptGrowthGracePeriod;
            var replayBaseline = GetSessionUpdateObservationCount(binding.RemoteSessionId);
            var transcriptProjectionBaseline = GetTranscriptProjectionObservationCount(binding.RemoteSessionId);
            var requiresTranscriptGrowthObservation = adapter != null;
            var hasCachedTranscript = transcriptBaselineCount > 0;
            var shouldAwaitReplayProjection =
                requiresTranscriptGrowthObservation &&
                _hydrationCompletionMode == AcpHydrationCompletionMode.StrictReplay &&
                !hasCachedTranscript;
            Logger.LogInformation(
                "Hydration replay wait policy resolved. conversationId={ConversationId} remoteSessionId={RemoteSessionId} shouldAwaitReplayProjection={ShouldAwaitReplayProjection} cachedTranscriptCount={CachedTranscriptCount}",
                conversationId,
                binding.RemoteSessionId,
                shouldAwaitReplayProjection,
                transcriptBaselineCount);
            if (ownsRemoteHydrationUi)
            {
                await PostToUiAsync(() =>
                {
                    IsRemoteHydrationPending = true;
                    _pendingHistoryOverlayDismissConversationId = null;
                    _remoteHydrationSessionUpdateBaselineCounts[conversationId] = replayBaseline;
                    if (requiresTranscriptGrowthObservation)
                    {
                        _remoteHydrationKnownTranscriptBaselineCounts[conversationId] = transcriptBaselineCount;
                        _remoteHydrationKnownTranscriptGrowthGraceDeadlineUtc[conversationId] =
                            knownTranscriptGrowthGraceDeadlineUtc;
                    }
                    else
                    {
                        ClearKnownTranscriptGrowthRequirement(conversationId);
                    }
                    SetConversationOverlayOwners(
                        sessionSwitchConversationId: _sessionSwitchOverlayConversationId,
                        connectionLifecycleConversationId: _connectionLifecycleOverlayConversationId,
                        historyConversationId: conversationId);
                    SetHydrationOverlayPhase(conversationId, HydrationOverlayPhase.RequestingSessionLoad);
                }).ConfigureAwait(false);
#if DEBUG
                Logger.LogInformation(
                    "Remote hydration overlay acquired. conversationId={ConversationId} activationVersion={ActivationVersion} remoteSessionId={RemoteSessionId}",
                    conversationId,
                    activationVersion,
                    binding.RemoteSessionId);
#endif
                await _chatStore.Dispatch(new SetIsHydratingAction(true)).ConfigureAwait(false);
            }

            if (IsActivationContextStale(activationVersion, cancellationToken))
            {
                return false;
            }

            cancellationToken.ThrowIfCancellationRequested();
            await _chatStore.Dispatch(new SelectConversationAction(conversationId)).ConfigureAwait(false);
            if (IsActivationContextStale(activationVersion, cancellationToken))
            {
                return false;
            }

            cancellationToken.ThrowIfCancellationRequested();
            await RefreshRemoteSessionMetadataFromSsotAsync(conversationId, binding, chatService, cancellationToken).ConfigureAwait(false);
            await ResetConversationProjectionForResyncAsync(conversationId, cancellationToken).ConfigureAwait(false);
            await SetHydrationOverlayPhaseAsync(
                    conversationId,
                    activationVersion,
                    HydrationOverlayPhase.RequestingSessionLoad)
                .ConfigureAwait(false);
            var sessionLoadTask = chatService.LoadSessionAsync(
                new SessionLoadParams(binding.RemoteSessionId!, GetSessionCwdOrDefault(conversationId)),
                cancellationToken);
            var sessionLoadResponse = await sessionLoadTask
                .WaitAsync(RemoteSessionLoadTimeout, cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (adapter != null)
            {
                if (!adapter.TryMarkHydrated(hydrationAttemptId!.Value))
                {
                    Logger.LogWarning(
                        "Discarding remote hydration completion because buffering attempt is stale. ConversationId={ConversationId} RemoteSessionId={RemoteSessionId}",
                        conversationId,
                        binding.RemoteSessionId);
                    return false;
                }
            }
            await ApplySessionLoadResponseAsync(conversationId, sessionLoadResponse).ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();

            if (shouldAwaitReplayProjection)
            {
                await AwaitRemoteReplayProjectionAsync(
                        conversationId,
                        activationVersion,
                        binding.RemoteSessionId!,
                        replayBaseline,
                        transcriptProjectionBaseline,
                        hydrationAttemptId,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                await SetHydrationOverlayPhaseAsync(
                        conversationId,
                        activationVersion,
                        HydrationOverlayPhase.FinalizingProjection)
                    .ConfigureAwait(false);
            }

            if (adapter != null
                && !adapter.TryMarkHydrated(hydrationAttemptId!.Value, reason: "PostDrainVerification"))
            {
                Logger.LogWarning(
                    "Discarding remote hydration finalization because buffering attempt became stale after replay drain. ConversationId={ConversationId} RemoteSessionId={RemoteSessionId}",
                    conversationId,
                    binding.RemoteSessionId);
                return false;
            }

            if (shouldAwaitReplayProjection)
            {
                await AwaitKnownTranscriptGrowthRequirementAsync(
                        conversationId,
                        transcriptBaselineCount,
                        knownTranscriptGrowthGraceDeadlineUtc,
                        hydrationAttemptId,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            await RestoreCachedConversationProjectionIfReplayIsEmptyAsync(conversationId).ConfigureAwait(false);
            await SetConversationRuntimeStateAsync(
                    conversationId,
                    ConversationRuntimePhase.Warm,
                    binding,
                    reason: "SessionLoadCompleted",
                    cancellationToken)
                .ConfigureAwait(false);
            Logger.LogInformation(
                "Conversation hydration completed. ConversationId={ConversationId} RemoteSessionId={RemoteSessionId} ElapsedMs={ElapsedMs}",
                conversationId,
                binding.RemoteSessionId,
                hydrationStopwatch.ElapsedMilliseconds);

            if (_hydrationCompletionMode == AcpHydrationCompletionMode.LoadResponse)
            {
                await EnsureMinimumHydrationVisibleDurationAsync(hydrationStopwatch, cancellationToken).ConfigureAwait(false);
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            ReleaseBufferedUpdatesAfterInterruptedHydration(adapter, hydrationAttemptId, "RemoteHydrationCanceled");

            throw;
        }
        catch (Exception ex)
        {
            ReleaseBufferedUpdatesAfterInterruptedHydration(adapter, hydrationAttemptId, "DiscoverImportLoadSessionFailed");

            if (AcpErrorClassifier.IsRemoteSessionNotFound(ex))
            {
                Logger.LogWarning(
                    ex,
                    "Remote session binding became stale during hydration. Clearing binding. ConversationId={ConversationId} RemoteSessionId={RemoteSessionId}",
                    conversationId,
                    binding.RemoteSessionId);
                await ClearRemoteBindingAsync(conversationId, binding.ProfileId).ConfigureAwait(false);
                await SetConversationRuntimeStateAsync(
                        conversationId,
                        ConversationRuntimePhase.Stale,
                        binding,
                        reason: "RemoteSessionNotFound",
                        cancellationToken: CancellationToken.None)
                    .ConfigureAwait(false);
                var restoredFromWorkspace = await TryRestoreConversationProjectionFromWorkspaceSnapshotAsync(conversationId).ConfigureAwait(false);
                if (!restoredFromWorkspace)
                {
                    SetError($"Failed to load session: {ex.Message}");
                    return false;
                }

                SetError($"Failed to load session: {ex.Message}");
                return true;
            }
            else
            {
                await SetConversationRuntimeStateAsync(
                        conversationId,
                        ConversationRuntimePhase.Faulted,
                        binding,
                        reason: ex.Message,
                        cancellationToken: CancellationToken.None)
                    .ConfigureAwait(false);
            }

            Logger.LogError(ex, "Failed to hydrate active conversation from remote session");
            Logger.LogInformation(
                "Conversation hydration failed. ConversationId={ConversationId} RemoteSessionId={RemoteSessionId} ElapsedMs={ElapsedMs}",
                conversationId,
                binding.RemoteSessionId,
                hydrationStopwatch.ElapsedMilliseconds);
            SetError($"Failed to load session: {ex.Message}");
            return false;
        }
        finally
        {
            if (ShouldOwnRemoteHydrationUi(conversationId, activationVersion))
            {
                await _chatStore.Dispatch(new SetIsHydratingAction(false)).ConfigureAwait(false);
                await PostToUiAsync(() =>
                {
                    _pendingHistoryOverlayDismissConversationId = conversationId;
                    IsRemoteHydrationPending = false;
                }).ConfigureAwait(false);
#if DEBUG
                Logger.LogInformation(
                    "Remote hydration logical completion reached. conversationId={ConversationId} activationVersion={ActivationVersion} pendingHistoryDismiss={PendingHistoryDismissConversationId}",
                    conversationId,
                    activationVersion,
                    _pendingHistoryOverlayDismissConversationId);
#endif
                await AwaitBufferedSessionReplayProjectionAsync(cancellationToken, hydrationAttemptId).ConfigureAwait(false);
                await ApplyCurrentStoreProjectionAsync().ConfigureAwait(false);
            }
        }
    }

    private void ReplaceMessageHistory(IImmutableList<ConversationMessageSnapshot> transcript)
    {
        var messages = transcript ?? ImmutableList<ConversationMessageSnapshot>.Empty;
        MessageHistory = new ObservableCollection<ChatMessageViewModel>(messages.Select(FromSnapshot));
        OnPropertyChanged(nameof(HasVisibleTranscriptContent));
        OnPropertyChanged(nameof(OverlayStatusText));
        OnPropertyChanged(nameof(ShouldShowBlockingLoadingMask));
        OnPropertyChanged(nameof(ShouldShowLoadingOverlayPresenter));
    }

    private void ReplaceMessageHistory(IReadOnlyList<ChatMessageViewModel> transcript)
    {
        var messages = transcript ?? Array.Empty<ChatMessageViewModel>();
        MessageHistory = new ObservableCollection<ChatMessageViewModel>(messages);
        OnPropertyChanged(nameof(HasVisibleTranscriptContent));
        OnPropertyChanged(nameof(OverlayStatusText));
        OnPropertyChanged(nameof(ShouldShowBlockingLoadingMask));
        OnPropertyChanged(nameof(ShouldShowLoadingOverlayPresenter));
    }

    private void ReplacePlanEntries(IReadOnlyList<ConversationPlanEntrySnapshot> planEntries)
    {
        var entries = planEntries ?? Array.Empty<ConversationPlanEntrySnapshot>();
        PlanEntries = new ObservableCollection<PlanEntryViewModel>(
            entries.Select(entry => new PlanEntryViewModel
            {
                Content = entry.Content ?? string.Empty,
                Status = entry.Status,
                Priority = entry.Priority
            }));
    }

    private async Task<bool> CanReuseWarmCurrentConversationAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(CurrentSessionId, sessionId, StringComparison.Ordinal))
        {
            return false;
        }

        var state = await _chatStore.State ?? ChatState.Empty;
        var workspaceBinding = _conversationWorkspace.GetRemoteBinding(sessionId);
        var binding = state.ResolveBinding(sessionId)
            ?? (workspaceBinding is null
                ? null
                : new ConversationBindingSlice(
                    workspaceBinding.ConversationId,
                    workspaceBinding.RemoteSessionId,
                    workspaceBinding.BoundProfileId));
        if (string.IsNullOrWhiteSpace(binding?.RemoteSessionId))
        {
            return true;
        }

        if (_chatService?.AgentCapabilities?.LoadSession == false)
        {
            return true;
        }

        var runtimeState = state.ResolveRuntimeState(sessionId);
        return runtimeState is { } hydratedRuntime
            && hydratedRuntime.Phase == ConversationRuntimePhase.Warm
            && hydratedRuntime.ConnectionGeneration == ConnectionGeneration
            && string.Equals(hydratedRuntime.RemoteSessionId, binding.RemoteSessionId, StringComparison.Ordinal)
            && string.Equals(hydratedRuntime.ProfileId, binding.ProfileId, StringComparison.Ordinal);
    }

    private async Task CancelPendingPermissionRequestAsync(string? expectedRemoteSessionId = null)
    {
        var pending = PendingPermissionRequest;
        if (pending is null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(expectedRemoteSessionId)
            && !string.Equals(pending.SessionId, expectedRemoteSessionId, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            if (_chatService != null)
            {
                await _chatService.RespondToPermissionRequestAsync(pending.MessageId, "cancelled", null).ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            Logger.LogDebug(
                ex,
                "Failed to send permission cancellation response. SessionId={SessionId}",
                pending.SessionId);
        }
        finally
        {
            ShowPermissionDialog = false;
            if (ReferenceEquals(PendingPermissionRequest, pending))
            {
                PendingPermissionRequest = null;
            }
        }
    }

    private async Task ClearRemoteBindingAsync(string conversationId, string? boundProfileId)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return;
        }

        var result = await _bindingCommands
            .UpdateBindingAsync(conversationId, remoteSessionId: null, boundProfileId)
            .ConfigureAwait(false);
        if (result.Status is BindingUpdateStatus.Success)
        {
            return;
        }

        if (result.Status is BindingUpdateStatus.Error
            && string.Equals(result.ErrorMessage, "BindingProjectionTimeout", StringComparison.Ordinal))
        {
            Logger.LogWarning(
                "Binding projection timed out while clearing stale remote binding. Applying local fallback. ConversationId={ConversationId}",
                conversationId);
            await ApplyLocalBindingClearFallbackAsync(conversationId, boundProfileId).ConfigureAwait(false);
            return;
        }

        Logger.LogWarning(
            "Failed to clear stale remote binding after hydration error. ConversationId={ConversationId} Status={Status} Error={Error}",
            conversationId,
            result.Status,
            result.ErrorMessage);
    }

    private async Task ApplyLocalBindingClearFallbackAsync(string conversationId, string? boundProfileId)
    {
        try
        {
            var clearedBinding = new ConversationBindingSlice(conversationId, null, boundProfileId);
            await _chatStore.Dispatch(new SetBindingSliceAction(clearedBinding)).ConfigureAwait(false);
            _conversationWorkspace.UpdateRemoteBinding(conversationId, remoteSessionId: null, boundProfileId);
            _conversationWorkspace.ScheduleSave();
        }
        catch (Exception ex)
        {
            Logger.LogWarning(
                ex,
                "Failed to apply local fallback after stale binding clear timeout. ConversationId={ConversationId}",
                conversationId);
        }
    }

    private void ApplySelectedProfile(ServerConfiguration profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var selectedProfile = ResolveLoadedProfileSelection(profile);
        _suppressAcpProfileConnect = true;
        _suppressStoreProfileProjection = selectedProfile is null;
        try
        {
            SelectedAcpProfile = selectedProfile;
            _acpProfiles.SelectedProfile = selectedProfile;
        }
        finally
        {
            _suppressStoreProfileProjection = false;
            _suppressAcpProfileConnect = false;
        }

        if (selectedProfile is null)
        {
            _ = _chatConnectionStore.Dispatch(new SetSelectedProfileAction(profile.Id));
        }
    }

    public void SelectProfile(ServerConfiguration profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (SynchronizationContext.Current == _syncContext)
        {
            ApplySelectedProfile(profile);
            return;
        }

        _ = SelectProfileAsync(profile);
    }

    public Task SelectProfileAsync(ServerConfiguration profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        cancellationToken.ThrowIfCancellationRequested();
        return PostToUiAsync(() => ApplySelectedProfile(profile));
    }

    private void ApplyChatServiceReplacement(IChatService? chatService)
    {
        if (_chatService != null)
        {
            UnsubscribeFromChatService(_chatService);
        }

        _ = _chatStore.Dispatch(new ResetConversationRuntimeStatesAction());
        _remoteHydrationSessionUpdateBaselineCounts.Clear();
        _remoteHydrationKnownTranscriptBaselineCounts.Clear();
        _remoteHydrationKnownTranscriptGrowthGraceDeadlineUtc.Clear();
        _hydrationOverlayPhase = HydrationOverlayPhase.None;
        _hydrationOverlayPhaseConversationId = null;
        _pendingAskUserRequestsByConversation.Clear();
        PendingAskUserRequest = null;
        _chatService = chatService;
        if (chatService != null)
        {
            SubscribeToChatService(chatService);
        }

        OnPropertyChanged(nameof(OverlayStatusText));
        OnPropertyChanged(nameof(CurrentChatService));
        OnPropertyChanged(nameof(IsInitialized));
    }

    public void ReplaceChatService(IChatService? chatService)
    {
        if (SynchronizationContext.Current == _syncContext)
        {
            ApplyChatServiceReplacement(chatService);
            return;
        }

        _ = ReplaceChatServiceAsync(chatService);
    }

    public Task ReplaceChatServiceAsync(IChatService? chatService, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return PostToUiAsync(() => ApplyChatServiceReplacement(chatService));
    }

    public void UpdateConnectionState(bool isConnecting, bool isConnected, bool isInitialized, string? errorMessage)
    {
        var profileId = _selectedProfileIdFromStore;
        if (isConnecting)
        {
            _ = _acpConnectionCoordinator.SetConnectingAsync(profileId);
            return;
        }

        if (isConnected)
        {
            _ = _acpConnectionCoordinator.SetConnectedAsync(profileId);
            return;
        }

        _ = _acpConnectionCoordinator.SetDisconnectedAsync(errorMessage);
    }

    public void UpdateInitializationState(bool isInitializing)
    {
    }

    public void UpdateAuthenticationState(bool isRequired, string? hintMessage)
    {
        if (isRequired)
        {
            _ = _acpConnectionCoordinator.SetAuthenticationRequiredAsync(hintMessage);
            return;
        }

        _ = _acpConnectionCoordinator.ClearAuthenticationRequiredAsync();
    }

    public void UpdateAgentIdentity(string? agentName, string? agentVersion)
    {
        _ = _chatStore.Dispatch(new SetAgentIdentityAction(agentName, agentVersion));
    }

    public async Task ResetHydratedConversationForResyncAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(CurrentSessionId))
        {
            return;
        }

        var conversationId = CurrentSessionId!;
        await ResetConversationProjectionForResyncAsync(conversationId, cancellationToken).ConfigureAwait(false);
    }

    private async Task ResetConversationProjectionForResyncAsync(
        string conversationId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _chatStore.Dispatch(new ClearTurnAction(conversationId)).ConfigureAwait(false);
        await _chatStore.Dispatch(new HydrateConversationAction(
            conversationId,
            ImmutableList<ConversationMessageSnapshot>.Empty,
            ImmutableList<ConversationPlanEntrySnapshot>.Empty,
            ShowPlanPanel: false,
            PlanTitle: null)).ConfigureAwait(false);
        await _chatStore.Dispatch(new SetConversationSessionStateAction(
            conversationId,
            ImmutableList<ConversationModeOptionSnapshot>.Empty,
            SelectedModeId: null,
            ImmutableList<ConversationConfigOptionSnapshot>.Empty,
            ShowConfigOptionsPanel: false)).ConfigureAwait(false);
    }

    private async Task<bool> TryRestoreConversationProjectionFromWorkspaceSnapshotAsync(string conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return false;
        }

        var fallback = await _conversationActivationCoordinator
            .ActivateSessionAsync(
                conversationId,
                ConversationActivationHydrationMode.WorkspaceSnapshot,
                CancellationToken.None)
            .ConfigureAwait(false);
        if (fallback.Succeeded)
        {
            return true;
        }

        Logger.LogWarning(
            "Failed to restore workspace snapshot after stale remote session fallback. ConversationId={ConversationId} Reason={Reason}",
            conversationId,
            fallback.FailureReason);
        return false;
    }

    private async Task RestoreCachedConversationProjectionIfReplayIsEmptyAsync(string conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return;
        }

        var snapshot = _conversationWorkspace.GetConversationSnapshot(conversationId);
        if (snapshot is null || snapshot.Transcript.Count == 0)
        {
            return;
        }

        var projectedTranscriptCount = await GetProjectedTranscriptCountAsync(conversationId).ConfigureAwait(false);
        if (projectedTranscriptCount >= snapshot.Transcript.Count)
        {
            return;
        }

        Logger.LogInformation(
            "Remote hydration projected partial transcript. Restoring cached workspace snapshot. ConversationId={ConversationId} ProjectedCount={ProjectedCount} CachedCount={CachedCount}",
            conversationId,
            projectedTranscriptCount,
            snapshot.Transcript.Count);
        await _chatStore.Dispatch(new HydrateConversationAction(
            conversationId,
            snapshot.Transcript.ToImmutableList(),
            snapshot.Plan.ToImmutableList(),
            snapshot.ShowPlanPanel,
            snapshot.PlanTitle)).ConfigureAwait(false);
    }

    private async Task<bool> EnsureActiveConversationRemoteConnectionReadyAsync(
        string conversationId,
        long? activationVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_disposed || string.IsNullOrWhiteSpace(conversationId))
        {
            return false;
        }

        if (IsActivationContextStale(activationVersion, cancellationToken))
        {
            return false;
        }

        var ownsConnectionLifecycleOverlay = false;
        var binding = await ResolveConversationBindingAsync(conversationId, cancellationToken).ConfigureAwait(false);
        if (IsActivationContextStale(activationVersion, cancellationToken))
        {
            return false;
        }

        try
        {
            if (string.IsNullOrWhiteSpace(binding?.RemoteSessionId))
            {
                return true;
            }

            if (await IsRemoteConnectionReadyAsync(binding.ProfileId, cancellationToken).ConfigureAwait(false))
            {
                return true;
            }

            if (ShouldOwnRemoteHydrationUi(conversationId, activationVersion))
            {
                ownsConnectionLifecycleOverlay = true;
                await PostToUiAsync(() =>
                {
                    SetConversationOverlayOwners(
                        sessionSwitchConversationId: _sessionSwitchOverlayConversationId,
                        connectionLifecycleConversationId: conversationId,
                        historyConversationId: _historyOverlayConversationId);
                }).ConfigureAwait(false);
            }

            var connectionState = await _chatConnectionStore.State ?? ChatConnectionState.Empty;
            var hasPendingConnection = connectionState.Phase is ConnectionPhase.Connecting or ConnectionPhase.Initializing
                || IsConnecting
                || IsInitializing;
            var pendingProfileId = !string.IsNullOrWhiteSpace(connectionState.SelectedProfileId)
                ? connectionState.SelectedProfileId
                : !string.IsNullOrWhiteSpace(SelectedProfileId)
                    ? SelectedProfileId
                    : SelectedAcpProfile?.Id;
            if (!string.IsNullOrWhiteSpace(binding.ProfileId)
                && hasPendingConnection
                && (string.IsNullOrWhiteSpace(pendingProfileId)
                    || string.Equals(binding.ProfileId, pendingProfileId, StringComparison.Ordinal)))
            {
                var becameReady = await WaitForRemoteConnectionReadyAsync(binding.ProfileId, cancellationToken).ConfigureAwait(false);
                if (IsActivationContextStale(activationVersion, cancellationToken))
                {
                    return false;
                }

                if (becameReady)
                {
                    return true;
                }

                var finalConnectionState = await _chatConnectionStore.State ?? ChatConnectionState.Empty;
                if (!string.IsNullOrWhiteSpace(finalConnectionState.Error))
                {
                    await ApplyCurrentStoreProjectionAsync().ConfigureAwait(false);
                    SetError($"Failed to load session: {finalConnectionState.Error}");
                    return false;
                }
            }

            if (string.IsNullOrWhiteSpace(binding.ProfileId))
            {
                SetError("Failed to load session: no ACP profile is bound to the remote conversation.");
                return false;
            }

            var profile = await ResolveProfileConfigurationAsync(binding.ProfileId!, cancellationToken).ConfigureAwait(false);
            if (profile is null)
            {
                Logger.LogWarning(
                    "Skipping remote conversation connection because the bound profile could not be resolved. ConversationId={ConversationId} ProfileId={ProfileId}",
                    conversationId,
                    binding.ProfileId);
                SetError("Failed to load session: the bound ACP profile could not be resolved.");
                return false;
            }

            var connectionContext = CreateConversationConnectionContext(
                conversationId,
                binding,
                profile.Id,
                preserveConversation: true);
            await ConnectToAcpProfileCoreAsync(profile, connectionContext, cancellationToken).ConfigureAwait(false);
            if (IsActivationContextStale(activationVersion, cancellationToken))
            {
                return false;
            }

            var readyAfterConnect = await IsRemoteConnectionReadyAsync(binding.ProfileId, cancellationToken).ConfigureAwait(false);
            if (IsActivationContextStale(activationVersion, cancellationToken))
            {
                return false;
            }

            if (!readyAfterConnect)
            {
                await ApplyCurrentStoreProjectionAsync().ConfigureAwait(false);
                SetError("Failed to load session: ACP profile connection did not become ready.");
            }

            return readyAfterConnect;
        }
        finally
        {
            if (ownsConnectionLifecycleOverlay
                && string.Equals(_connectionLifecycleOverlayConversationId, conversationId, StringComparison.Ordinal))
            {
                await PostToUiAsync(() =>
                {
                    SetConversationOverlayOwners(
                        sessionSwitchConversationId: _sessionSwitchOverlayConversationId,
                        connectionLifecycleConversationId: null,
                        historyConversationId: _historyOverlayConversationId);
                }).ConfigureAwait(false);
            }
        }
    }

    private async Task<bool> EnsureActiveConversationRemoteHydratedAsync(
        string conversationId,
        long? activationVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_disposed || string.IsNullOrWhiteSpace(conversationId))
        {
            return false;
        }

        var binding = await ResolveConversationBindingAsync(conversationId, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(binding?.RemoteSessionId))
        {
            return true;
        }

        if (_chatService is not { IsConnected: true, IsInitialized: true } chatService)
        {
            SetError("Failed to load session: ACP chat service is not connected and initialized.");
            return false;
        }

        if (chatService.AgentCapabilities?.LoadSession != true)
        {
            return true;
        }

        var state = await _chatStore.State ?? ChatState.Empty;
        var runtimeState = state.ResolveRuntimeState(conversationId);

        if (runtimeState is { } hydratedRuntime
            && hydratedRuntime.Phase == ConversationRuntimePhase.Warm
            && hydratedRuntime.ConnectionGeneration == ConnectionGeneration
            && string.Equals(hydratedRuntime.RemoteSessionId, binding.RemoteSessionId, StringComparison.Ordinal)
            && string.Equals(hydratedRuntime.ProfileId, binding.ProfileId, StringComparison.Ordinal))
        {
            Logger.LogInformation(
                "Skipping remote hydration for conversation because runtime state is warm. ConversationId={ConversationId} RemoteSessionId={RemoteSessionId} ConnectionGeneration={ConnectionGeneration}",
                conversationId,
                binding.RemoteSessionId,
                ConnectionGeneration);
            return true;
        }

        return await HydrateConversationAsync(conversationId, binding!, activationVersion, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ConversationActivationHydrationMode> ResolveConversationActivationHydrationModeAsync(
        string conversationId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var binding = await ResolveConversationBindingAsync(conversationId, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(binding?.RemoteSessionId))
        {
            return ConversationActivationHydrationMode.WorkspaceSnapshot;
        }

        if (_chatService?.AgentCapabilities?.LoadSession == false)
        {
            return ConversationActivationHydrationMode.WorkspaceSnapshot;
        }

        return ConversationActivationHydrationMode.SelectionOnly;
    }

    private async Task<ConversationBindingSlice?> ResolveConversationBindingAsync(
        string conversationId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return null;
        }

        var state = await _chatStore.State ?? ChatState.Empty;
        var binding = state.ResolveBinding(conversationId);
        if (binding != null)
        {
            return binding;
        }

        var workspaceBinding = _conversationWorkspace.GetRemoteBinding(conversationId);
        return workspaceBinding is null
            ? null
            : new ConversationBindingSlice(
                workspaceBinding.ConversationId,
                workspaceBinding.RemoteSessionId,
                workspaceBinding.BoundProfileId);
    }

    private static AcpConnectionContext CreateConversationConnectionContext(
        string? conversationId,
        ConversationBindingSlice? binding,
        string? profileId,
        bool preserveConversation)
    {
        if (!preserveConversation || string.IsNullOrWhiteSpace(conversationId))
        {
            return new AcpConnectionContext(conversationId, PreserveConversation: false);
        }

        var hasMatchingRemoteBinding =
            !string.IsNullOrWhiteSpace(binding?.RemoteSessionId)
            && !string.IsNullOrWhiteSpace(binding?.ProfileId)
            && string.Equals(binding?.ProfileId, profileId, StringComparison.Ordinal);

        return new AcpConnectionContext(conversationId, hasMatchingRemoteBinding);
    }

    private async Task<int> GetProjectedTranscriptCountAsync(string conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return 0;
        }

        var state = await _chatStore.State ?? ChatState.Empty;
        var projectedTranscript = state.ResolveContentSlice(conversationId)?.Transcript
            ?? (string.Equals(state.HydratedConversationId, conversationId, StringComparison.Ordinal)
                ? state.Transcript
                : null);
        return projectedTranscript?.Count ?? 0;
    }

    private async Task AwaitKnownTranscriptGrowthRequirementAsync(
        string conversationId,
        int transcriptBaselineCount,
        DateTime graceDeadlineUtc,
        long? hydrationAttemptId,
        CancellationToken cancellationToken)
    {
        while (DateTime.UtcNow < graceDeadlineUtc)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await AwaitBufferedSessionReplayProjectionAsync(cancellationToken, hydrationAttemptId).ConfigureAwait(false);
            var projectedTranscriptCount = await GetProjectedTranscriptCountAsync(conversationId).ConfigureAwait(false);
            if (projectedTranscriptCount > transcriptBaselineCount)
            {
                return;
            }

            var remaining = graceDeadlineUtc - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                return;
            }

            var delay = remaining < TimeSpan.FromMilliseconds(RemoteReplayPollDelayMilliseconds)
                ? remaining
                : TimeSpan.FromMilliseconds(RemoteReplayPollDelayMilliseconds);
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    private static Task EnsureMinimumHydrationVisibleDurationAsync(Stopwatch hydrationStopwatch, CancellationToken cancellationToken)
    {
        var remaining = LoadResponseHydrationMinimumVisibleDuration - hydrationStopwatch.Elapsed;
        if (remaining <= TimeSpan.Zero)
        {
            return Task.CompletedTask;
        }

        return Task.Delay(remaining, cancellationToken);
    }

    private static void ReleaseBufferedUpdatesAfterInterruptedHydration(
        IAcpSessionUpdateBufferController? adapter,
        long? hydrationAttemptId,
        string reason)
    {
        if (adapter is null || !hydrationAttemptId.HasValue)
        {
            return;
        }

        adapter.SuppressBufferedUpdates(hydrationAttemptId.Value, reason);
        adapter.TryMarkHydrated(hydrationAttemptId.Value, lowTrust: true, reason: reason);
    }

    private async Task<ServerConfiguration?> ResolveProfileConfigurationAsync(
        string profileId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return null;
        }

        await EnsureAcpProfilesLoadedAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        return _acpProfiles.Profiles.FirstOrDefault(p => string.Equals(p.Id, profileId, StringComparison.Ordinal))
            ?? await _configurationService.LoadConfigurationAsync(profileId);
    }

    private async Task<bool> IsRemoteConnectionReadyAsync(
        string? requiredProfileId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_chatService is not { IsConnected: true, IsInitialized: true })
        {
            return false;
        }

        var connectionState = await _chatConnectionStore.State ?? ChatConnectionState.Empty;
        if (connectionState.Phase is ConnectionPhase.Connecting or ConnectionPhase.Initializing)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(connectionState.Error))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(requiredProfileId))
        {
            return true;
        }

        return string.Equals(requiredProfileId, connectionState.SelectedProfileId, StringComparison.Ordinal)
            || string.Equals(requiredProfileId, SelectedProfileId, StringComparison.Ordinal)
            || string.Equals(requiredProfileId, SelectedAcpProfile?.Id, StringComparison.Ordinal);
    }

    private async Task<bool> WaitForRemoteConnectionReadyAsync(
        string? requiredProfileId,
        CancellationToken cancellationToken)
    {
        while (!await IsRemoteConnectionReadyAsync(requiredProfileId, cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var connectionState = await _chatConnectionStore.State ?? ChatConnectionState.Empty;
            if (connectionState.Phase is not ConnectionPhase.Connecting
                && connectionState.Phase is not ConnectionPhase.Initializing)
            {
                return false;
            }

            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    private Task SetConversationRuntimeStateAsync(
        string conversationId,
        ConversationRuntimePhase phase,
        string? reason,
        CancellationToken cancellationToken)
        => SetConversationRuntimeStateAsync(
            conversationId,
            phase,
            binding: null,
            reason,
            cancellationToken);

    private async Task SetConversationRuntimeStateAsync(
        string conversationId,
        ConversationRuntimePhase phase,
        ConversationBindingSlice? binding,
        string? reason,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return;
        }

        var remoteSessionId = binding?.RemoteSessionId;
        var profileId = binding?.ProfileId;
        if (string.IsNullOrWhiteSpace(remoteSessionId) || string.IsNullOrWhiteSpace(profileId))
        {
            var currentState = await _chatStore.State ?? ChatState.Empty;
            var existingRuntime = currentState.ResolveRuntimeState(conversationId);
            remoteSessionId ??= existingRuntime?.RemoteSessionId ?? currentState.ResolveBinding(conversationId)?.RemoteSessionId;
            profileId ??= existingRuntime?.ProfileId ?? currentState.ResolveBinding(conversationId)?.ProfileId;
        }

        var runtimeState = new ConversationRuntimeSlice(
            ConversationId: conversationId,
            Phase: phase,
            ConnectionGeneration: ConnectionGeneration,
            RemoteSessionId: remoteSessionId,
            ProfileId: profileId,
            Reason: reason,
            UpdatedAtUtc: DateTime.UtcNow);
        await _chatStore.Dispatch(new SetConversationRuntimeStateAction(runtimeState)).ConfigureAwait(false);
        Logger.LogInformation(
            "Conversation runtime stage transitioned. ConversationId={ConversationId} Stage={Stage} ConnectionGeneration={ConnectionGeneration} RemoteSessionId={RemoteSessionId} ProfileId={ProfileId} Reason={Reason}",
            conversationId,
            phase,
            ConnectionGeneration,
            remoteSessionId,
            profileId,
            reason);
    }

    private bool ShouldOwnRemoteHydrationUi(string conversationId, long? activationVersion)
        => activationVersion.HasValue
            ? IsLatestConversationActivationVersion(activationVersion.Value)
            : string.Equals(CurrentSessionId, conversationId, StringComparison.Ordinal);

    private AcpHydrationCompletionMode ResolveHydrationCompletionMode(string? configuredMode)
    {
        if (Enum.TryParse<AcpHydrationCompletionMode>(configuredMode, ignoreCase: true, out var mode))
        {
            return mode;
        }

        if (!string.IsNullOrWhiteSpace(configuredMode))
        {
            Logger.LogWarning(
                "Unknown ACP hydration completion mode '{ConfiguredMode}'. Falling back to StrictReplay.",
                configuredMode);
        }

        return AcpHydrationCompletionMode.StrictReplay;
    }

    private async Task RefreshRemoteSessionMetadataFromSsotAsync(
        string conversationId,
        ConversationBindingSlice binding,
        IChatService chatService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(conversationId)
            || string.IsNullOrWhiteSpace(binding.RemoteSessionId)
            || chatService.AgentCapabilities?.SessionCapabilities?.List is null)
        {
            return;
        }

        AgentSessionInfo? sessionInfo;
        try
        {
            sessionInfo = await FindRemoteSessionInfoAsync(chatService, binding.RemoteSessionId!, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(
                ex,
                "Failed to refresh remote session metadata before hydration. ConversationId={ConversationId} RemoteSessionId={RemoteSessionId}",
                conversationId,
                binding.RemoteSessionId);
            return;
        }

        if (sessionInfo is null)
        {
            return;
        }

        await _conversationWorkspace
            .ApplySessionInfoUpdateAsync(
                conversationId,
                sessionInfo.Title,
                cwd: sessionInfo.Cwd,
                updatedAtUtc: ParseSessionUpdatedAtUtc(sessionInfo.UpdatedAt),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (string.Equals(CurrentSessionId, conversationId, StringComparison.Ordinal))
        {
            CurrentSessionDisplayName = ResolveSessionDisplayName(conversationId);
        }
    }

    private static DateTime? ParseSessionUpdatedAtUtc(string? updatedAt)
    {
        if (string.IsNullOrWhiteSpace(updatedAt)
            || !DateTimeOffset.TryParse(
                updatedAt,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsedUpdatedAt))
        {
            return null;
        }

        return parsedUpdatedAt.UtcDateTime;
    }

    private static async Task<AgentSessionInfo?> FindRemoteSessionInfoAsync(
        IChatService chatService,
        string remoteSessionId,
        CancellationToken cancellationToken)
    {
        string? cursor = null;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            var response = await chatService
                .ListSessionsAsync(new SessionListParams { Cursor = cursor }, cancellationToken)
                .ConfigureAwait(false);
            var match = response?.Sessions?.FirstOrDefault(session =>
                string.Equals(session.SessionId, remoteSessionId, StringComparison.Ordinal));
            if (match != null)
            {
                return match;
            }

            cursor = response?.NextCursor;
        }
        while (!string.IsNullOrWhiteSpace(cursor));

        return null;
    }

    private void SetConversationOverlayOwners(
        string? sessionSwitchConversationId,
        string? connectionLifecycleConversationId,
        string? historyConversationId)
    {
        if (string.Equals(_sessionSwitchOverlayConversationId, sessionSwitchConversationId, StringComparison.Ordinal)
            && string.Equals(_connectionLifecycleOverlayConversationId, connectionLifecycleConversationId, StringComparison.Ordinal)
            && string.Equals(_historyOverlayConversationId, historyConversationId, StringComparison.Ordinal))
        {
            return;
        }

        _sessionSwitchOverlayConversationId = sessionSwitchConversationId;
        _connectionLifecycleOverlayConversationId = connectionLifecycleConversationId;
        _historyOverlayConversationId = historyConversationId;
        if (string.IsNullOrWhiteSpace(historyConversationId)
            || !string.Equals(_pendingHistoryOverlayDismissConversationId, historyConversationId, StringComparison.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(historyConversationId))
            {
                _pendingHistoryOverlayDismissConversationId = null;
            }
        }

        ResetHydrationOverlayPhaseIfOwnerChanged(historyConversationId);
#if DEBUG
        Logger.LogInformation(
            "Overlay owners updated. currentSession={CurrentSessionId} sessionSwitch={SessionSwitchConversationId} connectionLifecycle={ConnectionLifecycleConversationId} history={HistoryConversationId} pendingHistoryDismiss={PendingHistoryDismissConversationId} isHydrating={IsHydrating} isRemoteHydrationPending={IsRemoteHydrationPending}",
            CurrentSessionId,
            _sessionSwitchOverlayConversationId,
            _connectionLifecycleOverlayConversationId,
            _historyOverlayConversationId,
            _pendingHistoryOverlayDismissConversationId,
            IsHydrating,
            IsRemoteHydrationPending);
#endif
        RaiseOverlayStateChanged();
    }

    private void ScheduleSessionSwitchOverlayDismissal(long activationVersion, string conversationId)
    {
        _syncContext.Post(_ =>
        {
            DismissSessionSwitchOverlay(activationVersion, conversationId);
        }, null);
    }

    private Task DismissSessionSwitchOverlayAsync(long activationVersion, string conversationId)
        => PostToUiAsync(() => DismissSessionSwitchOverlay(activationVersion, conversationId));

    private void DismissSessionSwitchOverlay(long activationVersion, string conversationId)
    {
        if (Volatile.Read(ref _conversationActivationVersion) != activationVersion
            || !string.Equals(_sessionSwitchOverlayConversationId, conversationId, StringComparison.Ordinal))
        {
            return;
        }

        IsSessionSwitching = false;
        SetConversationOverlayOwners(
            sessionSwitchConversationId: null,
            connectionLifecycleConversationId: _connectionLifecycleOverlayConversationId,
            historyConversationId: _historyOverlayConversationId);
    }

    private void TryCompletePendingHistoryOverlayDismissal(ChatUiProjection projection)
    {
        var hasVisibleTranscript = projection.Transcript.Count > 0;
        if (string.IsNullOrWhiteSpace(projection.HydratedConversationId)
            || !string.Equals(_historyOverlayConversationId, projection.HydratedConversationId, StringComparison.Ordinal)
            || !string.Equals(_pendingHistoryOverlayDismissConversationId, projection.HydratedConversationId, StringComparison.Ordinal)
            || projection.IsHydrating
            || IsRemoteHydrationPending
            || (!hasVisibleTranscript && HasPendingSessionUpdates())
            || (!hasVisibleTranscript && !HasSatisfiedKnownTranscriptGrowthRequirement(projection)))
        {
            return;
        }

#if DEBUG
        Logger.LogInformation(
            "Completing history overlay dismissal. conversationId={ConversationId} transcriptCount={TranscriptCount} hasVisibleTranscript={HasVisibleTranscript} turnStatusVisible={IsTurnStatusVisible}",
            projection.HydratedConversationId,
            projection.Transcript.Count,
            hasVisibleTranscript,
            projection.IsTurnStatusVisible);
#endif
        SetConversationOverlayOwners(
            sessionSwitchConversationId: _sessionSwitchOverlayConversationId,
            connectionLifecycleConversationId: _connectionLifecycleOverlayConversationId,
            historyConversationId: null);
        ClearKnownTranscriptGrowthRequirement(projection.HydratedConversationId);
    }

    private bool HasSatisfiedKnownTranscriptGrowthRequirement(ChatUiProjection projection)
    {
        if (string.IsNullOrWhiteSpace(projection.HydratedConversationId)
            || !_remoteHydrationKnownTranscriptBaselineCounts.TryGetValue(projection.HydratedConversationId, out var baselineCount))
        {
            return true;
        }

        if (projection.Transcript.Count > baselineCount)
        {
            return true;
        }

        return !_remoteHydrationKnownTranscriptGrowthGraceDeadlineUtc.TryGetValue(
                projection.HydratedConversationId,
                out var graceDeadlineUtc)
            || DateTime.UtcNow >= graceDeadlineUtc;
    }

    private void ClearKnownTranscriptGrowthRequirement(string? conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return;
        }

        _remoteHydrationSessionUpdateBaselineCounts.Remove(conversationId);
        _remoteHydrationKnownTranscriptBaselineCounts.Remove(conversationId);
        _remoteHydrationKnownTranscriptGrowthGraceDeadlineUtc.Remove(conversationId);
    }

    private sealed class NoopAcpConnectionCoordinator : IAcpConnectionCoordinator
    {
        public static NoopAcpConnectionCoordinator Instance { get; } = new();

        public Task SetConnectingAsync(string? profileId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetInitializingAsync(string? profileId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetConnectedAsync(string? profileId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetDisconnectedAsync(string? errorMessage = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetAuthenticationRequiredAsync(string? hintMessage, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ClearAuthenticationRequiredAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ResetAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ResyncAsync(IAcpChatCoordinatorSink sink, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class BottomPanelState
    {
        public ObservableCollection<BottomPanelTabViewModel> Tabs { get; }
        public BottomPanelTabViewModel? Selected { get; set; }

        public BottomPanelState(ObservableCollection<BottomPanelTabViewModel> tabs)
        {
            Tabs = tabs;
            Selected = tabs.FirstOrDefault();
        }
    }

    private sealed class ConversationActivationLease
    {
        public ConversationActivationLease(long version, CancellationTokenSource cancellationTokenSource)
        {
            Version = version;
            CancellationTokenSource = cancellationTokenSource ?? throw new ArgumentNullException(nameof(cancellationTokenSource));
            CancellationToken = cancellationTokenSource.Token;
        }

        public long Version { get; }

        public CancellationTokenSource CancellationTokenSource { get; }

        public CancellationToken CancellationToken { get; }
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
           AttachPlanEntriesCollectionChanged(null);
           if (_observedPendingAskUserRequest != null)
           {
               _observedPendingAskUserRequest.PropertyChanged -= OnPendingAskUserRequestPropertyChanged;
               _observedPendingAskUserRequest = null;
           }

           _sendPromptCts?.Cancel();
           _transientNotificationCts?.Cancel();
           StopStoreProjection();

            try { _sendPromptCts?.Dispose(); } catch { }
            try { _transientNotificationCts?.Dispose(); } catch { }
            try { _conversationActivationCts?.Cancel(); } catch { }
            try { _conversationActivationCts?.Dispose(); } catch { }
            try { _conversationActivationGate.Dispose(); } catch { }
            try { _remoteConversationActivationGate.Dispose(); } catch { }

            _selectedProfileConnectTask = null;
            _pendingSelectedProfileConnect = null;
            _sendPromptCts = null;
       _transientNotificationCts = null;
       _conversationActivationCts = null;
       }

    private void AttachPlanEntriesCollectionChanged(ObservableCollection<PlanEntryViewModel>? planEntries)
    {
        if (_observedPlanEntries != null)
        {
            _observedPlanEntries.CollectionChanged -= OnPlanEntriesCollectionChanged;
        }

        _observedPlanEntries = planEntries;

        if (_observedPlanEntries != null)
        {
            _observedPlanEntries.CollectionChanged += OnPlanEntriesCollectionChanged;
        }
    }

    private void OnPlanEntriesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => RaisePlanEntryDerivedPropertyNotifications();

    private void RaisePlanEntryDerivedPropertyNotifications()
    {
        OnPropertyChanged(nameof(HasPlanEntries));
        OnPropertyChanged(nameof(ShouldShowPlanList));
        OnPropertyChanged(nameof(ShouldShowPlanEmpty));
    }
}

public sealed class ProjectAffinityOverrideOptionViewModel
{
    public ProjectAffinityOverrideOptionViewModel(string projectId, string displayName)
    {
        ProjectId = projectId ?? string.Empty;
        DisplayName = displayName ?? string.Empty;
    }

    public string ProjectId { get; }

    public string DisplayName { get; }
}

/// <summary>
/// Session mode ViewModel
/// </summary>
public partial class SessionModeViewModel : ObservableObject
{
    [ObservableProperty]
    private string _modeId = string.Empty;

    [ObservableProperty]
    private string _modeName = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;
}

/// <summary>
/// Permission request ViewModel
/// </summary>
public partial class PermissionRequestViewModel : ObservableObject
{
    public object MessageId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string ToolCallJson { get; set; } = string.Empty;

    [ObservableProperty]
    private ObservableCollection<PermissionOptionViewModel> _options = new();

    public Func<string, string?, Task>? OnRespond { get; set; }

    [RelayCommand]
    private async Task RespondAsync(PermissionOptionViewModel? option)
    {
        if (OnRespond == null)
            return;

        if (option != null)
        {
            await OnRespond("selected", option.OptionId);
        }
        else
        {
            await OnRespond("cancelled", null);
        }
    }
}

/// <summary>
/// Permission option ViewModel
/// </summary>
public partial class PermissionOptionViewModel : ObservableObject
{
    [ObservableProperty]
    private string _optionId = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _kind = string.Empty;
}

/// <summary>
/// File system request ViewModel
/// </summary>
public partial class FileSystemRequestViewModel : ObservableObject
{
    public object MessageId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string? Encoding { get; set; }
    public string? Content { get; set; }

    public Func<bool, string?, string?, Task>? OnRespond { get; set; }

    [ObservableProperty]
    private string _responseContent = string.Empty;

    [RelayCommand]
    private async Task RespondAsync(bool success)
    {
        if (OnRespond == null)
            return;

        await OnRespond(success, success ? ResponseContent : null, success ? null : "Operation failed");
    }
}
