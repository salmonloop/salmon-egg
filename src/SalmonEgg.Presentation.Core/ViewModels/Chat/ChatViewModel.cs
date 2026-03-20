using System;
using Uno.Extensions.Reactive;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
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
using SalmonEgg.Domain.Models.Session;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.ViewModels.Settings;

namespace SalmonEgg.Presentation.ViewModels.Chat;

/// <summary>
/// Main ViewModel for the Chat interface.
/// Orchestrates the lifecycle of conversations, ACP agent connectivity, and UI state projection.
/// Follows the MVVM pattern where the View is driven strictly by this ViewModel and its projected state.
/// </summary>
public partial class ChatViewModel : ViewModelBase, IDisposable, IConversationCatalog, IConversationSessionSwitcher, IAcpChatCoordinatorSink
{
    private readonly ChatServiceFactory _chatServiceFactory;
    private readonly ChatConversationWorkspace _conversationWorkspace;
    private readonly IAcpConnectionCommands _acpConnectionCommands;
    private readonly IConfigurationService _configurationService;
    private readonly AppPreferencesViewModel _preferences;
    private readonly AcpProfilesViewModel _acpProfiles;
    private readonly ISessionManager _sessionManager;
    private readonly IMiniWindowCoordinator _miniWindowCoordinator;
    private readonly ConversationCatalogPresenter _conversationCatalogPresenter;
    private readonly IChatStateProjector _chatStateProjector;
    private readonly IAcpSessionUpdateProjector _acpSessionUpdateProjector;
    private IChatService? _chatService;
    private readonly SynchronizationContext _syncContext;
    private bool _disposed;
    private bool _suppressSessionUpdatesToUi;
    private bool _autoConnectAttempted;
    private bool _suppressAcpProfileConnect;
    private bool _suppressAutoConnectFromPreferenceChange;
    private CancellationTokenSource? _sendPromptCts;
    private CancellationTokenSource? _transientNotificationCts;
    private CancellationTokenSource? _storeStateCts;
    private readonly object _selectedProfileConnectSync = new();
    private Task? _selectedProfileConnectTask;
    private ServerConfiguration? _pendingSelectedProfileConnect;
    private IDisposable? _storeStateSubscription;
    private string? _currentRemoteSessionId;
    private IReadOnlyList<AuthMethodDefinition>? _advertisedAuthMethods;
    private bool _suppressMiniWindowSessionSync;
    private bool _suppressStoreConversationProjection;
    private bool _suppressStoreProfileProjection;
    private bool _suppressStorePromptProjection;
    private bool _suppressProfileSyncFromStore;
    private string? _selectedProfileIdFromStore;
    private int _storeProjectionSequence;
    private readonly object _restoreSync = new();
    private Task? _restoreTask;

    /// <summary>
    /// Local conversation binding connects a stable UI ConversationId to a transient ACP RemoteSessionId.
    /// This allows the user to switch between tabs/histories without losing the underlying agent session context,
    /// and handles reconnections by re-binding new remote sessions to the same local ID.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<ChatMessageViewModel> _messageHistory = new();

    [ObservableProperty]
    private string _currentPrompt = string.Empty;

    [ObservableProperty]
    private string? _currentSessionId;

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
    private bool _isThinking;

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
    private string? _agentName;

    [ObservableProperty]
    private string? _agentVersion;

    [ObservableProperty]
    private bool _isInitializing;

    [ObservableProperty]
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

    public bool IsInputEnabled => !IsBusy && !IsPromptInFlight;

    // UI-BOUND PROPERTIES: Handlers for WinUI/Uno property change notifications.
    // These ensure the View reflects internal state changes that might not trigger automatically.
    public bool CanSendPromptUi => CanSendPrompt();

    public bool HasPlanEntries => PlanEntries.Count > 0;

    public bool ShouldShowPlanList => ShowPlanPanel && HasPlanEntries;

    public bool ShouldShowPlanEmpty => !ShowPlanPanel || !HasPlanEntries;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSendPromptUi))]
    private bool _isConnected;

    [ObservableProperty]
    private ObservableCollection<SessionModeViewModel> _availableModes = new();

    [ObservableProperty]
    private SessionModeViewModel? _selectedMode;

    public ObservableCollection<ServerConfiguration> AcpProfileList => _acpProfiles.Profiles;

    [ObservableProperty]
    private ServerConfiguration? _selectedAcpProfile;

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
    private bool _showPermissionDialog;

    [ObservableProperty]
    private PermissionRequestViewModel? _pendingPermissionRequest;

    [ObservableProperty]
    private bool _showFileSystemDialog;

    [ObservableProperty]
    private FileSystemRequestViewModel? _pendingFileSystemRequest;

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
        ILogger<ChatViewModel> logger,
        SynchronizationContext? syncContext = null,
        IAcpConnectionCommands? acpConnectionCommands = null)
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
        _conversationCatalogPresenter = conversationCatalogPresenter ?? throw new ArgumentNullException(nameof(conversationCatalogPresenter));
        _chatStateProjector = chatStateProjector ?? new ChatStateProjector();
        _acpSessionUpdateProjector = acpSessionUpdateProjector ?? new AcpSessionUpdateProjector();
        _syncContext = syncContext ?? SynchronizationContext.Current ?? new SynchronizationContext();
        _acpConnectionCommands = acpConnectionCommands
            ?? new AcpChatCoordinator(
                new ChatServiceFactoryAdapter(chatServiceFactory),
                NullLogger<AcpChatCoordinator>.Instance);
        StartStoreProjection();

        _acpProfiles.PropertyChanged += OnAcpProfilesPropertyChanged;
        _acpProfiles.Profiles.CollectionChanged += OnAcpProfilesCollectionChanged;
        _preferences.PropertyChanged += OnPreferencesPropertyChanged;
        _conversationWorkspace.PropertyChanged += OnConversationWorkspacePropertyChanged;
        PlanEntries.CollectionChanged += OnCurrentPlanCollectionChanged;

        IsConversationListLoading = _conversationWorkspace.IsConversationListLoading;
        ConversationListVersion = _conversationWorkspace.ConversationListVersion;
        _conversationCatalogPresenter.SetLoading(IsConversationListLoading);
        _conversationCatalogPresenter.Refresh(_conversationWorkspace.GetCatalog());

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
                break;
        }
    }

    private void StartStoreProjection()
    {
        _storeStateCts = new CancellationTokenSource();
        var token = _storeStateCts.Token;

        _chatStore.State.ForEach(async (state, ct) =>
        {
            if (state is null || token.IsCancellationRequested || ct.IsCancellationRequested || _disposed)
            {
                return;
            }

            try
            {
                var projectionSequence = Interlocked.Increment(ref _storeProjectionSequence);

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

                    var projection = CreateProjection(state);
                    SyncMessageHistory(projection.Transcript, projection.IsThinking);
                    ApplyStoreProjection(projection);
                    PersistConversationState(state, scheduleSave: true);
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
        }, out _storeStateSubscription);
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

        _storeStateSubscription = null;

        try
        {
            _storeStateCts?.Dispose();
        }
        catch
        {
        }

        _storeStateCts = null;
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

    private void ApplyStoreProjection(ChatUiProjection projection)
    {
        _suppressStoreConversationProjection = true;
        _suppressStoreProfileProjection = true;
        _suppressStorePromptProjection = true;
        try
        {
            if (!string.Equals(CurrentSessionId, projection.SelectedConversationId, StringComparison.Ordinal))
            {
                CurrentSessionId = projection.SelectedConversationId;
            }

            var draft = projection.CurrentPrompt;
            if (!string.Equals(CurrentPrompt, draft, StringComparison.Ordinal))
            {
                CurrentPrompt = draft;
            }

            ApplySelectedProfileFromStore(projection.SelectedProfileId);
            _currentRemoteSessionId = projection.RemoteSessionId;
            IsSessionActive = projection.IsSessionActive;
            IsPromptInFlight = projection.IsPromptInFlight;
            IsThinking = projection.IsThinking;
            IsConnecting = projection.IsConnecting;
            IsConnected = projection.IsConnected;
            IsInitializing = projection.IsInitializing;
            CurrentConnectionStatus = projection.ConnectionStatus;
            ConnectionErrorMessage = projection.ConnectionError;
            IsAuthenticationRequired = projection.IsAuthenticationRequired;
            AuthenticationHintMessage = projection.AuthenticationHintMessage;
            AgentName = projection.AgentName;
            AgentVersion = projection.AgentVersion;
            ShowPlanPanel = projection.ShowPlanPanel;
            CurrentPlanTitle = projection.PlanTitle;
            SyncPlanEntries(projection.PlanEntries);
        }
        finally
        {
            _suppressStorePromptProjection = false;
            _suppressStoreProfileProjection = false;
            _suppressStoreConversationProjection = false;
        }
    }

    private void SyncPlanEntries(IReadOnlyList<ConversationPlanEntrySnapshot> planEntries)
    {
        PlanEntries.Clear();
        foreach (var entry in planEntries)
        {
            PlanEntries.Add(new PlanEntryViewModel
            {
                Content = entry.Content ?? string.Empty,
                Status = entry.Status,
                Priority = entry.Priority
            });
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
            _ = _chatStore.Dispatch(new SelectProfileAction(value?.Id));
        }

        if (_suppressAcpProfileConnect || value == null)
        {
            return;
        }

        QueueSelectedProfileConnection(value);
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
            catch
            {
            }
        }
    }

    // Partial method implementations called by source-generated code.
    partial void OnShowPlanPanelChanged(bool value)
    {
        OnPropertyChanged(nameof(ShouldShowPlanList));
        OnPropertyChanged(nameof(ShouldShowPlanEmpty));
    }

    partial void OnCurrentSessionIdChanged(string? value)
    {
        if (!_suppressStoreConversationProjection)
        {
            _ = SelectAndHydrateConversationAsync(value);
        }

        // Keep the header name stable and decouple it from ACP sessionId.
        CurrentSessionDisplayName = ResolveSessionDisplayName(value);

        if (string.IsNullOrWhiteSpace(value))
        {
            _currentRemoteSessionId = null;
        }

        if (IsEditingSessionName)
        {
            IsEditingSessionName = false;
            EditingSessionName = string.Empty;
        }

        SyncMiniWindowSelectedSession();
    }

    partial void OnSelectedMiniWindowSessionChanged(MiniWindowConversationItemViewModel? value)
    {
        if (_suppressMiniWindowSessionSync || value == null)
        {
            return;
        }

        if (!string.Equals(CurrentSessionId, value.ConversationId, StringComparison.Ordinal))
        {
            _ = TrySwitchToSessionAsync(value.ConversationId);
        }
    }

    private async Task SelectAndHydrateConversationAsync(string? conversationId)
    {
        await _chatStore.Dispatch(new SelectConversationAction(conversationId));
        await DispatchConversationHydrationAsync(conversationId).ConfigureAwait(false);
        await NormalizeConversationBindingForSelectedProfileAsync(conversationId).ConfigureAwait(false);
        await ApplyCurrentStoreProjectionAsync().ConfigureAwait(false);
    }

    private async Task DispatchConversationHydrationAsync(string? conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return;
        }

        var snapshot = _conversationWorkspace.GetConversationSnapshot(conversationId);

        await _chatStore.Dispatch(new HydrateConversationAction(
            conversationId,
            snapshot?.Transcript.ToImmutableList() ?? ImmutableList<ConversationMessageSnapshot>.Empty,
            snapshot?.Plan.ToImmutableList() ?? ImmutableList<ConversationPlanEntrySnapshot>.Empty,
            snapshot?.ShowPlanPanel ?? false,
            snapshot?.PlanTitle)).ConfigureAwait(false);
    }

    private async Task NormalizeConversationBindingForSelectedProfileAsync(string? conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return;
        }

        var selectedProfileId = _preferences.LastSelectedServerId;
        if (string.IsNullOrWhiteSpace(selectedProfileId))
        {
            return;
        }

        var currentState = await _chatStore.State ?? ChatState.Empty;
        if (!string.Equals(currentState.HydratedConversationId, conversationId, StringComparison.Ordinal))
        {
            return;
        }

        var binding = _conversationWorkspace.GetRemoteBinding(conversationId);
        var boundProfileId = binding?.BoundProfileId;
        if (string.IsNullOrWhiteSpace(boundProfileId)
            || string.Equals(boundProfileId, selectedProfileId, StringComparison.Ordinal))
        {
            return;
        }

        _conversationWorkspace.UpdateRemoteBinding(conversationId, remoteSessionId: null, boundProfileId: selectedProfileId);
        _conversationWorkspace.ScheduleSave();
        await ApplyCurrentStoreProjectionAsync().ConfigureAwait(false);
    }

    private async Task ApplyCurrentStoreProjectionAsync()
    {
        var state = await _chatStore.State ?? ChatState.Empty;
        var projection = CreateProjection(state);

        await PostToUiAsync(() =>
        {
            if (_disposed)
            {
                return;
            }

            SyncMessageHistory(projection.Transcript, projection.IsThinking);
            ApplyStoreProjection(projection);
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
            await PostToUiAsync(() =>
            {
                var restoredConversationId = _conversationWorkspace.LastActiveConversationId;
                if (!string.IsNullOrWhiteSpace(restoredConversationId))
                {
                    _suppressStoreConversationProjection = true;
                    CurrentSessionId = restoredConversationId;
                    IsSessionActive = true;
                    _suppressStoreConversationProjection = false;
                }
                else
                {
                    _suppressStoreConversationProjection = true;
                    CurrentSessionId = null;
                    IsSessionActive = false;
                    _suppressStoreConversationProjection = false;
                }
            }).ConfigureAwait(false);

            await SelectAndHydrateConversationAsync(_conversationWorkspace.LastActiveConversationId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Conversation workspace restore failed");
        }
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

    private void SyncMessageHistory(IReadOnlyList<ConversationMessageSnapshot> transcript, bool isThinking)
    {
        var messages = transcript ?? Array.Empty<ConversationMessageSnapshot>();
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

        var expectedCount = messages.Count + (isThinking ? 1 : 0);
        while (MessageHistory.Count > expectedCount)
        {
            MessageHistory.RemoveAt(MessageHistory.Count - 1);
        }

        if (isThinking)
        {
            if (MessageHistory.Count < expectedCount)
            {
                MessageHistory.Add(ChatMessageViewModel.CreateThinkingPlaceholder(Guid.NewGuid().ToString()));
            }
            else if (!MessageHistory[^1].IsThinkingPlaceholder)
            {
                MessageHistory.RemoveAt(MessageHistory.Count - 1);
                MessageHistory.Add(ChatMessageViewModel.CreateThinkingPlaceholder(Guid.NewGuid().ToString()));
            }
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

    private ConversationRemoteBindingState? TryGetRemoteBinding(string? conversationId)
        => _conversationWorkspace.GetRemoteBinding(conversationId);

    private ChatUiProjection CreateProjection(ChatState state)
        => _chatStateProjector.Apply(state, CurrentSessionId, TryGetRemoteBinding(CurrentSessionId));

    private void PersistConversationState(ChatState state, bool scheduleSave)
    {
        if (string.IsNullOrWhiteSpace(state.HydratedConversationId))
        {
            return;
        }

        if (state.Transcript is null && state.PlanEntries is null)
        {
            return;
        }

        var conversationId = state.HydratedConversationId!;
        var existing = _conversationWorkspace.GetConversationSnapshot(conversationId);
        var transcript = (state.Transcript ?? ImmutableList<ConversationMessageSnapshot>.Empty)
            .Where(static message => !IsThinkingPlaceholder(message))
            .Select(CloneSnapshot)
            .ToArray();
        var planEntries = (state.PlanEntries ?? ImmutableList<ConversationPlanEntrySnapshot>.Empty)
            .Select(ClonePlanEntrySnapshot)
            .ToArray();

        _conversationWorkspace.UpsertConversationSnapshot(new ConversationWorkspaceSnapshot(
            conversationId,
            transcript,
            planEntries,
            state.ShowPlanPanel,
            state.PlanTitle,
            existing?.CreatedAt ?? DateTime.UtcNow,
            DateTime.UtcNow));

        if (scheduleSave)
        {
            _conversationWorkspace.ScheduleSave();
        }
    }

    private void ScheduleConversationSave()
    {
        _conversationWorkspace.ScheduleSave();
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

        _conversationWorkspace.RenameConversation(conversationId, newDisplayName);
        var finalName = ResolveSessionDisplayName(conversationId);

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

        if (string.Equals(CurrentSessionId, conversationId, StringComparison.Ordinal))
        {
            _suppressSessionUpdatesToUi = true;
            CurrentSessionId = null;
            _currentRemoteSessionId = null;
            IsSessionActive = false;
            _suppressSessionUpdatesToUi = false;
        }

        try
        {
            _conversationWorkspace.ArchiveConversation(conversationId);
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

        if (string.Equals(CurrentSessionId, conversationId, StringComparison.Ordinal))
        {
            _suppressSessionUpdatesToUi = true;
            CurrentSessionId = null;
            _currentRemoteSessionId = null;
            IsSessionActive = false;
            _suppressSessionUpdatesToUi = false;
        }

        try
        {
            _conversationWorkspace.DeleteConversation(conversationId);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Delete operation failed due to underlying exception: {ConversationId}", conversationId);
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
            try
            {
                SelectedAcpProfile = _acpProfiles.SelectedProfile;
            }
            finally
            {
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

        var profileId = _preferences.LastSelectedServerId;
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
            try
            {
                SelectedAcpProfile = config;
            }
            finally
            {
                _suppressAcpProfileConnect = false;
            }

            cancellationToken.ThrowIfCancellationRequested();
            await ConnectToAcpProfileCoreAsync(config, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _autoConnectAttempted = false;
            throw;
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

        try
        {
            _suppressAutoConnectFromPreferenceChange = true;
            _acpProfiles.MarkLastConnected(profile);
            _acpProfiles.SelectedProfile = profile;

            await _chatStore.Dispatch(new SetConnectionLifecycleAction(true, IsConnected, IsInitializing, null));
            var result = await _acpConnectionCommands
                .ConnectToProfileAsync(profile, TransportConfig, this, cancellationToken)
                .ConfigureAwait(false);

            CacheAuthMethods(result.InitializeResponse);
            ClearAuthenticationRequirement();
            UpdateAgentInfo();
            _ = _chatStore.Dispatch(new UpdateConnectionStatusAction(IsConnected, null));
            ShowTransportConfigPanel = false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to connect to ACP profile {ProfileId}", profile.Id);
            throw;
        }
        finally
        {
            _suppressAutoConnectFromPreferenceChange = false;
            await _chatStore.Dispatch(new SetConnectionLifecycleAction(false, IsConnected, IsInitializing, ConnectionErrorMessage));
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
            await _chatStore.Dispatch(new SetConnectionLifecycleAction(
                IsConnecting: true,
                IsConnected: IsConnected,
                IsInitialized: IsInitializing,
                ErrorMessage: null));
            try
            {
                var result = await _acpConnectionCommands
                    .ApplyTransportConfigurationAsync(TransportConfig, this, preserveConversation)
                    .ConfigureAwait(false);

                CacheAuthMethods(result.InitializeResponse);
                ClearAuthenticationRequirement();
                UpdateAgentInfo();

                if (string.IsNullOrWhiteSpace(CurrentSessionId))
                {
                    var sessionId = Guid.NewGuid().ToString("N");
                    await _sessionManager.CreateSessionAsync(sessionId, GetActiveSessionCwdOrDefault()).ConfigureAwait(false);
                    await _conversationWorkspace.TrySwitchToSessionAsync(sessionId).ConfigureAwait(false);
                    await PostToUiAsync(() =>
                    {
                        CurrentSessionId = sessionId;
                        IsSessionActive = true;
                    }).ConfigureAwait(false);
                }

                _ = _chatStore.Dispatch(new UpdateConnectionStatusAction(IsConnected, null));
                ShowTransportConfigPanel = false;
            }
            catch (Exception ex)
            {
                _ = _chatStore.Dispatch(new UpdateConnectionStatusAction(false, $"Connection failed: {ex.Message}"));
                Logger.LogError(ex, "Error during connection");
                ClearRemoteSessionBindingForCurrentConversation();
            }
            finally
            {
                await _chatStore.Dispatch(new SetConnectionLifecycleAction(
                    IsConnecting: false,
                    IsConnected: IsConnected,
                    IsInitialized: IsInitializing,
                    ErrorMessage: ConnectionErrorMessage));
            }
        }

    private void ApplySessionNewResponse(SessionNewResponse response)
    {
        ApplySessionUpdateDelta(_acpSessionUpdateProjector.ProjectSessionNew(response));
        Logger.LogInformation("Session modes loaded: {Count}", AvailableModes.Count);
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
            chatService.ErrorOccurred += OnErrorOccurred;

           // Listen for initialization status changes.
           if (chatService.IsInitialized)
           {
               UpdateAgentInfo();
           }
       }

       private void UnsubscribeFromChatService(IChatService chatService)
       {
           chatService.SessionUpdateReceived -= OnSessionUpdateReceived;
           chatService.PermissionRequestReceived -= OnPermissionRequestReceived;
            chatService.FileSystemRequestReceived -= OnFileSystemRequestReceived;
            chatService.TerminalRequestReceived -= OnTerminalRequestReceived;
            chatService.ErrorOccurred -= OnErrorOccurred;
        }

        private void SubscribeToEvents()
      {
          // Only subscribe if _chatService is not null. 
          // In constructor, _chatService might be null; it will be created in ApplyTransportConfigAsync.
          if (_chatService != null)
          {
              SubscribeToChatService(_chatService);

              // Listen for initialization status changes.
              if (_chatService.IsInitialized)
              {
                  UpdateAgentInfo();
              }

          }
      }

    private void OnSessionUpdateReceived(object? sender, SessionUpdateEventArgs e)
    {
        _syncContext.Post(_ =>
        {
            try
            {
                if (_suppressSessionUpdatesToUi)
                {
                    return;
                }

                // SECURITY/PROTOCOL CHECK: Ensure updates only affect the currently active remote session.
                // This prevents cross-talk if multiple agents or sessions are running.
                var activeRemoteSessionId = _currentRemoteSessionId;
                if (string.IsNullOrWhiteSpace(activeRemoteSessionId) && !string.IsNullOrWhiteSpace(CurrentSessionId))
                {
                    activeRemoteSessionId = _conversationWorkspace.GetRemoteBinding(CurrentSessionId)?.RemoteSessionId;
                }

                if (!string.IsNullOrWhiteSpace(activeRemoteSessionId) &&
                    !string.Equals(e.SessionId, activeRemoteSessionId, StringComparison.Ordinal))
                {
                    return;
                }

                if (e.Update is AgentMessageUpdate messageUpdate && messageUpdate.Content != null)
                {
                    _ = _chatStore.Dispatch(new SetIsThinkingAction(false));
                    HandleAgentContentChunk(messageUpdate.Content);
                }
                else if (e.Update is AgentThoughtUpdate)
                {
                    // Thought chunks are transient states; they trigger 'thinking' UI feedback.
                    _ = _chatStore.Dispatch(new SetIsThinkingAction(true));
                }
                else if (e.Update is UserMessageUpdate userMessageUpdate && userMessageUpdate.Content != null)
                {
                    AddMessageToHistory(userMessageUpdate.Content, isOutgoing: true);
                }
                else if (e.Update is ToolCallUpdate toolCallUpdate)
                {
                    _ = _chatStore.Dispatch(new SetIsThinkingAction(true));

                    _ = UpsertTranscriptSnapshotAsync(CreateToolCallSnapshot(toolCallUpdate));
                }
                else if (e.Update is ToolCallStatusUpdate toolCallStatusUpdate)
                {
                    _ = _chatStore.Dispatch(new SetIsThinkingAction(true));
                    _ = UpdateToolCallStatusAsync(toolCallStatusUpdate);
                }
                else if (e.Update is PlanUpdate planUpdate)
                {
                    ApplySessionUpdateDelta(_acpSessionUpdateProjector.Project(new SessionUpdateEventArgs(e.SessionId, planUpdate)));
                }
                else if (e.Update is CurrentModeUpdate modeChange)
                {
                    ApplySessionUpdateDelta(_acpSessionUpdateProjector.Project(new SessionUpdateEventArgs(e.SessionId, modeChange)));
                }
                else if (e.Update is ConfigUpdateUpdate configUpdate)
                {
                    ApplySessionUpdateDelta(_acpSessionUpdateProjector.Project(new SessionUpdateEventArgs(e.SessionId, configUpdate)));
                }
                else if (e.Update is AvailableCommandsUpdate commandsUpdate)
                {
                    UpdateSlashCommands(commandsUpdate);
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
        }, null);
    }

    private void HandleAgentContentChunk(ContentBlock content)
    {
        // ACP streams response content as an array of blocks. We coalesce adjacent text blocks
        // into a single UI element to mimic a natural typing effect.
        if (content is TextContentBlock text)
        {
            AppendAgentTextChunk(text.Text ?? string.Empty);
            return;
        }

        AddMessageToHistory(content, isOutgoing: false);
    }

    private void AppendAgentTextChunk(string chunk)
    {
        if (string.IsNullOrWhiteSpace(chunk))
        {
            return;
        }

        _ = _chatStore.Dispatch(new AppendTextDeltaAction(CurrentSessionId, chunk));
    }

    public async Task<bool> TrySwitchToSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return false;
        }

        await EnsureConversationWorkspaceRestoredAsync(cancellationToken).ConfigureAwait(false);

        if (string.Equals(CurrentSessionId, sessionId, StringComparison.Ordinal))
        {
            var currentState = await _chatStore.State ?? ChatState.Empty;
            if (currentState.Transcript is null)
            {
                await SelectAndHydrateConversationAsync(sessionId).ConfigureAwait(false);
            }
            else
            {
                await NormalizeConversationBindingForSelectedProfileAsync(sessionId).ConfigureAwait(false);
                await ApplyCurrentStoreProjectionAsync().ConfigureAwait(false);
            }

            return true;
        }

        try
        {
            _suppressSessionUpdatesToUi = true;

            var switched = await _conversationWorkspace
                .TrySwitchToSessionAsync(sessionId, cancellationToken)
                .ConfigureAwait(false);
            if (!switched)
            {
                return false;
            }

            // Switch local conversation first (UI stays stable even if not connected).
            await PostToUiAsync(() =>
            {
                _suppressStoreConversationProjection = true;
                CurrentSessionId = sessionId;
                IsSessionActive = true;
                _suppressStoreConversationProjection = false;
            }).ConfigureAwait(false);

            await SelectAndHydrateConversationAsync(sessionId).ConfigureAwait(false);
            NotifyConversationListChanged();

            return true;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Switching session failed (SessionId={SessionId})", sessionId);

            _syncContext.Post(_ =>
            {
                ConnectionErrorMessage = $"Failed to switch session: {ex.Message}";
                IsSessionActive = !string.IsNullOrWhiteSpace(CurrentSessionId);
            }, null);

            return false;
        }
        finally
        {
            _suppressSessionUpdatesToUi = false;
        }
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
        _ = _chatStore.Dispatch(new SetAuthenticationStateAction(false, null));
    }

    private void MarkAuthenticationRequired(AuthMethodDefinition? method, string? messageOverride = null)
    {
        var message =
            messageOverride
            ?? method?.Description
            ?? "The agent requires authentication before it can respond.";

        _ = _chatStore.Dispatch(new SetAuthenticationStateAction(true, message));

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
        if (_chatService is not { IsConnected: true, IsInitialized: true })
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

    private void AddMessageToHistory(ContentBlock content, bool isOutgoing)
    {
        _ = UpsertTranscriptSnapshotAsync(CreateContentSnapshot(content, isOutgoing));
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
            TextContent = toolCall.RawOutput?.GetRawText() ?? string.Empty,
            ToolCallId = toolCall.ToolCallId,
            ToolCallKind = toolCall.Kind,
            ToolCallStatus = toolCall.Status,
            ToolCallJson = toolCall.RawInput?.GetRawText()
        };
    }

    private async Task UpsertTranscriptSnapshotAsync(ConversationMessageSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(CurrentSessionId))
        {
            return;
        }

        await _chatStore.Dispatch(new UpsertTranscriptMessageAction(CurrentSessionId, snapshot)).ConfigureAwait(false);
    }

    private async Task UpdateToolCallStatusAsync(ToolCallStatusUpdate toolCallStatusUpdate)
    {
        if (string.IsNullOrWhiteSpace(CurrentSessionId) || string.IsNullOrEmpty(toolCallStatusUpdate.ToolCallId))
        {
            return;
        }

        var state = await _chatStore.State ?? ChatState.Empty;
        var currentTranscript = state.Transcript ?? ImmutableList<ConversationMessageSnapshot>.Empty;
        var existing = currentTranscript.LastOrDefault(message =>
            string.Equals(message.ToolCallId, toolCallStatusUpdate.ToolCallId, StringComparison.Ordinal)
            && string.Equals(message.ContentType, "tool_call", StringComparison.Ordinal));
        if (existing is null)
        {
            return;
        }

        var appendedOutput = existing.TextContent ?? string.Empty;
        if (toolCallStatusUpdate.Content != null)
        {
            foreach (var item in toolCallStatusUpdate.Content.OfType<Domain.Models.Tool.ContentToolCallContent>())
            {
                if (item.Content is TextContentBlock textBlock && !string.IsNullOrEmpty(textBlock.Text))
                {
                    appendedOutput += textBlock.Text;
                }
            }
        }

        await _chatStore.Dispatch(new UpsertTranscriptMessageAction(CurrentSessionId, new ConversationMessageSnapshot
        {
            Id = existing.Id,
            Timestamp = DateTime.UtcNow,
            IsOutgoing = existing.IsOutgoing,
            ContentType = existing.ContentType,
            Title = existing.Title,
            TextContent = appendedOutput,
            ImageData = existing.ImageData,
            ImageMimeType = existing.ImageMimeType,
            AudioData = existing.AudioData,
            AudioMimeType = existing.AudioMimeType,
            ToolCallId = existing.ToolCallId,
            ToolCallKind = existing.ToolCallKind,
            ToolCallStatus = toolCallStatusUpdate.Status ?? existing.ToolCallStatus,
            ToolCallJson = existing.ToolCallJson,
            PlanEntry = ClonePlanEntrySnapshot(existing.PlanEntry),
            ModeId = existing.ModeId
        })).ConfigureAwait(false);
    }

    private void ApplySessionUpdateDelta(AcpSessionUpdateDelta delta)
    {
        if (delta.AvailableModes != null)
        {
            AvailableModes.Clear();
            foreach (var mode in delta.AvailableModes)
            {
                AvailableModes.Add(new SessionModeViewModel
                {
                    ModeId = mode.ModeId,
                    ModeName = mode.ModeName,
                    Description = mode.Description
                });
            }
        }

        if (delta.ConfigOptions != null)
        {
            ConfigOptions.Clear();
            foreach (var option in delta.ConfigOptions)
            {
                ConfigOptions.Add(MapConfigOption(option));
            }

            ShowConfigOptionsPanel = delta.ShowConfigOptionsPanel ?? ConfigOptions.Count > 0;
            SyncModesFromConfigOptions();
        }
        else if (delta.ShowConfigOptionsPanel.HasValue)
        {
            ShowConfigOptionsPanel = delta.ShowConfigOptionsPanel.Value;
        }

        if (!string.IsNullOrWhiteSpace(delta.SelectedModeId) && AvailableModes.Count > 0)
        {
            SelectedMode = AvailableModes.FirstOrDefault(m => m.ModeId == delta.SelectedModeId) ?? AvailableModes[0];
        }

        if (delta.PlanEntries != null)
        {
            _ = _chatStore.Dispatch(new ReplacePlanEntriesAction(
                CurrentSessionId,
                delta.PlanEntries.ToImmutableList(),
                delta.ShowPlanPanel ?? true,
                delta.PlanTitle));
        }
    }

    private static ConfigOptionViewModel MapConfigOption(AcpConfigOptionSnapshot option)
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
            SelectedMode = AvailableModes.FirstOrDefault(m => m.ModeId == current) ?? SelectedMode ?? AvailableModes[0];
        }
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
           await _chatStore.Dispatch(new SetConnectionLifecycleAction(IsConnecting, IsConnected, true, ConnectionErrorMessage));
           ClearError();

           // Initialize ACP client.
           var initParams = new InitializeParams
           {
               ProtocolVersion = 1,
               ClientInfo = new ClientInfo
               {
                   Name = "SalmonEgg",
                   Title = "SalmonEgg",
                   Version = "1.0.0"
               },
               ClientCapabilities = new ClientCapabilities
               {
                   Fs = new FsCapability
                   {
                       ReadTextFile = true,
                       WriteTextFile = true
                   }
               }
           };

           if (_chatService == null)
           {
               throw new InvalidOperationException("Chat service is not initialized");
           }

           var initResponse = await _chatService.InitializeAsync(initParams);
           UpdateAgentInfo();
           CacheAuthMethods(initResponse);
           ClearAuthenticationRequirement();
       }
       catch (Exception ex)
       {
           Logger.LogError(ex, "Initialization failed");
           SetError($"Initialization failed: {ex.Message}");
       }
       finally
       {
           await _chatStore.Dispatch(new SetConnectionLifecycleAction(IsConnecting, IsConnected, false, ConnectionErrorMessage));
       }
   }

    [RelayCommand]
    private async Task CreateNewSessionAsync()
    {
        if (IsConnecting)
            return;

        try
        {
            await _chatStore.Dispatch(new SetConnectionLifecycleAction(true, IsConnected, IsInitializing, ConnectionErrorMessage));
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
            var switched = await TrySwitchToSessionAsync(localConversationId).ConfigureAwait(false);
            if (!switched)
            {
                throw new InvalidOperationException("Failed to activate local conversation before binding remote session.");
            }

            BindRemoteSession(response.SessionId, _preferences.LastSelectedServerId, response, preserveConversation: true);

            // Load available modes (deprecated in favor of configOptions).
            if (response.Modes?.AvailableModes != null)
            {
                AvailableModes.Clear();
                foreach (var mode in response.Modes.AvailableModes)
                {
                    if (mode != null)
                    {
                        AvailableModes.Add(new SessionModeViewModel
                        {
                            ModeId = mode.Id ?? string.Empty,
                            ModeName = mode.Name ?? string.Empty,
                            Description = mode.Description ?? string.Empty
                        });
                    }
                }

                // Select the first mode as default
                if (AvailableModes.Count > 0)
                {
                    var currentModeId = response.Modes.CurrentModeId;
                    SelectedMode = string.IsNullOrWhiteSpace(currentModeId)
                        ? AvailableModes[0]
                        : AvailableModes.FirstOrDefault(m => m.ModeId == currentModeId) ?? AvailableModes[0];
                }
            }


            // Load configuration options
            if (response.ConfigOptions != null)
            {
                ConfigOptions.Clear();
                foreach (var option in response.ConfigOptions)
                {
                    ConfigOptions.Add(ConfigOptionViewModel.CreateFromAcp(option));
                }
                ShowConfigOptionsPanel = ConfigOptions.Count > 0;
                SyncModesFromConfigOptions();
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to create session");
            SetError($"Failed to create session: {ex.Message}");
        }
        finally
        {
            await _chatStore.Dispatch(new SetConnectionLifecycleAction(false, IsConnected, IsInitializing, ConnectionErrorMessage));
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

        try
        {
            _ = _chatStore.Dispatch(new UpdateConnectionStatusAction(IsConnected, null));
            await _chatStore.Dispatch(new SetPromptInFlightAction(true));
            await _chatStore.Dispatch(new SetIsThinkingAction(false));

            // Clear input immediately for better UX (agents may stream without returning a response for a while).
            // We'll restore it on failure so the user can retry.
            CurrentPrompt = string.Empty;

            // Add user message to history
            var userContent = new TextContentBlock { Text = promptText };
            AddMessageToHistory(userContent, isOutgoing: true);

            if (_chatService != null)
            {
                _sendPromptCts?.Cancel();
                _sendPromptCts = new CancellationTokenSource();
                await _acpConnectionCommands
                    .SendPromptAsync(promptText, this, TryAuthenticateAsync, _sendPromptCts.Token)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // User-cancelled; keep input cleared.
        }
        catch (TimeoutException ex)
        {
            Logger.LogError(ex, "SendPrompt timed out");
            _ = _chatStore.Dispatch(new UpdateConnectionStatusAction(IsConnected, "Send timed out: Agent did not respond for a long time."));

            if (string.IsNullOrWhiteSpace(CurrentPrompt))
            {
                CurrentPrompt = promptText;
            }

            ShowTransientNotificationToast("Agent no response (timeout). Please check if the agent needs login/initialization or try again later.");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "SendPrompt failed");
            _ = _chatStore.Dispatch(new UpdateConnectionStatusAction(IsConnected, $"Send failed: {ex.Message}"));

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
            await _chatStore.Dispatch(new SetIsThinkingAction(false));
        }
    }

    private bool CanSendPrompt() =>
        IsSessionActive
        && _chatService is { IsConnected: true, IsInitialized: true }
        && !string.IsNullOrWhiteSpace(CurrentSessionId)
        && !string.IsNullOrWhiteSpace(CurrentPrompt)
        && !IsBusy
        && !IsPromptInFlight;

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
    {
        try
        {
            var sessionId = CurrentSessionId;
            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                var session = _sessionManager.GetSession(sessionId);
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

    private void ClearRemoteSessionBindingForCurrentConversation()
    {
        if (string.IsNullOrWhiteSpace(CurrentSessionId))
        {
            _currentRemoteSessionId = null;
            return;
        }

        _conversationWorkspace.UpdateRemoteBinding(CurrentSessionId, remoteSessionId: null, boundProfileId: _preferences.LastSelectedServerId);
        _conversationWorkspace.ScheduleSave();
        _currentRemoteSessionId = null;
        _ = ApplyCurrentStoreProjectionAsync();
    }

    [RelayCommand]
    private async Task SetModeAsync(SessionModeViewModel? mode)
    {
        if (mode == null || !IsSessionActive || string.IsNullOrWhiteSpace(_currentRemoteSessionId))
            return;

        try
        {
            IsBusy = true;
            ClearError();

            if (_chatService != null)
            {
                if (!string.IsNullOrWhiteSpace(_modeConfigId))
                {
                    var setParams = new SessionSetConfigOptionParams(
                        _currentRemoteSessionId!,
                        _modeConfigId,
                        mode.ModeId ?? string.Empty);
                    await _chatService.SetSessionConfigOptionAsync(setParams);
                }
                else
                {
                    var modeParams = new SessionSetModeParams
                    {
                        SessionId = _currentRemoteSessionId!,
                        ModeId = mode.ModeId
                    };
                    await _chatService.SetSessionModeAsync(modeParams);
                }
            }
            SelectedMode = mode;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to switch mode");
            SetError($"Failed to switch mode: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CancelSessionAsync()
    {
        if (!IsSessionActive || string.IsNullOrWhiteSpace(_currentRemoteSessionId))
            return;

        try
        {
            IsBusy = true;
            ClearError();

            var cancelParams = new SessionCancelParams
            {
                SessionId = _currentRemoteSessionId!,
                Reason = "User cancelled"
            };

            if (_chatService != null)
            {
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

            if (_chatService != null)
            {
                await _chatService.DisconnectAsync();
            }
            ClearRemoteSessionBindingForCurrentConversation();
            await _chatStore.Dispatch(new SetConnectionLifecycleAction(false, false, false, null));
            await _chatStore.Dispatch(new SetAgentIdentityAction(null, null));
            AvailableModes.Clear();
            SelectedMode = null;
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

    public string? CurrentConversationId => CurrentSessionId;

    public Task RestoreAsync(CancellationToken cancellationToken = default)
        => EnsureConversationWorkspaceRestoredAsync(cancellationToken);

    public IChatService? CurrentChatService => _chatService;

    public bool IsInitialized => _chatService?.IsInitialized == true;

    public string? CurrentRemoteSessionId => _currentRemoteSessionId;

    public string? SelectedProfileId => _preferences.LastSelectedServerId;

    public void SelectProfile(ServerConfiguration profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        _suppressAcpProfileConnect = true;
        try
        {
            SelectedAcpProfile = profile;
            _acpProfiles.SelectedProfile = profile;
        }
        finally
        {
            _suppressAcpProfileConnect = false;
        }
    }

    public void ReplaceChatService(IChatService? chatService)
    {
        if (_chatService != null)
        {
            UnsubscribeFromChatService(_chatService);
        }

        _chatService = chatService;
        if (chatService != null)
        {
            SubscribeToChatService(chatService);
        }
        OnPropertyChanged(nameof(CurrentChatService));
        OnPropertyChanged(nameof(IsInitialized));
    }

    public void UpdateConnectionState(bool isConnecting, bool isConnected, bool isInitialized, string? errorMessage)
    {
        _ = _chatStore.Dispatch(new SetConnectionLifecycleAction(isConnecting, isConnected, isInitialized, errorMessage));
    }

    public void UpdateInitializationState(bool isInitializing)
    {
        _ = _chatStore.Dispatch(new SetConnectionLifecycleAction(IsConnecting, IsConnected, isInitializing, ConnectionErrorMessage));
    }

    public void UpdateAuthenticationState(bool isRequired, string? hintMessage)
    {
        _ = _chatStore.Dispatch(new SetAuthenticationStateAction(isRequired, hintMessage));
    }

    public void UpdateAgentIdentity(string? agentName, string? agentVersion)
    {
        _ = _chatStore.Dispatch(new SetAgentIdentityAction(agentName, agentVersion));
    }

    public void BindRemoteSession(string remoteSessionId, string? profileId, SessionNewResponse response, bool preserveConversation)
    {
        if (string.IsNullOrWhiteSpace(CurrentSessionId))
        {
            throw new InvalidOperationException("Cannot bind a remote ACP session without an active local conversation.");
        }

        _conversationWorkspace.UpdateRemoteBinding(CurrentSessionId!, remoteSessionId, profileId);
        _conversationWorkspace.ScheduleSave();
        _currentRemoteSessionId = remoteSessionId;
        ApplySessionNewResponse(response);

        if (!preserveConversation)
        {
            _ = DispatchConversationHydrationAsync(CurrentSessionId);
            return;
        }

        _ = ApplyCurrentStoreProjectionAsync();
    }

    public void ClearRemoteSessionBinding()
    {
        ClearRemoteSessionBindingForCurrentConversation();
    }


    private void OnCurrentPlanCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasPlanEntries));
        OnPropertyChanged(nameof(ShouldShowPlanList));
        OnPropertyChanged(nameof(ShouldShowPlanEmpty));
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

           PlanEntries.CollectionChanged -= OnCurrentPlanCollectionChanged;
           _acpProfiles.PropertyChanged -= OnAcpProfilesPropertyChanged;
           _acpProfiles.Profiles.CollectionChanged -= OnAcpProfilesCollectionChanged;
           _preferences.PropertyChanged -= OnPreferencesPropertyChanged;
           _conversationWorkspace.PropertyChanged -= OnConversationWorkspacePropertyChanged;

           _sendPromptCts?.Cancel();
           _transientNotificationCts?.Cancel();
           StopStoreProjection();

            try { _sendPromptCts?.Dispose(); } catch { }
            try { _transientNotificationCts?.Dispose(); } catch { }

            _selectedProfileConnectTask = null;
            _pendingSelectedProfileConnect = null;
            _sendPromptCts = null;
            _transientNotificationCts = null;
       }
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
