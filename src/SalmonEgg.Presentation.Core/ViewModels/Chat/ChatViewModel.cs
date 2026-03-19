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
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.ViewModels.Settings;

namespace SalmonEgg.Presentation.ViewModels.Chat;

/// <summary>
/// Main ViewModel for the Chat interface.
/// Orchestrates the lifecycle of conversations, ACP agent connectivity, and UI state projection.
/// Follows the MVVM pattern where the View is driven strictly by this ViewModel and its projected state.
/// </summary>
public partial class ChatViewModel : ViewModelBase, IDisposable
{
    private readonly ChatServiceFactory _chatServiceFactory;
    private readonly IConfigurationService _configurationService;
    private readonly AppPreferencesViewModel _preferences;
    private readonly AcpProfilesViewModel _acpProfiles;
    private readonly ISessionManager _sessionManager;
    private readonly IConversationStore _conversationStore;
    private readonly IMiniWindowCoordinator _miniWindowCoordinator;
    private IChatService? _chatService;
    private readonly SynchronizationContext _syncContext;
    private bool _disposed;
    private readonly SemaphoreSlim _sessionSwitchGate = new(1, 1);
    private bool _suppressSessionUpdatesToUi;
    private bool _autoConnectAttempted;
    private bool _suppressAcpProfileConnect;
    private bool _conversationsRestored;
    private CancellationTokenSource? _conversationSaveCts;
    private CancellationTokenSource? _sendPromptCts;
    private CancellationTokenSource? _transientNotificationCts;
    private CancellationTokenSource? _storeStateCts;
    private IDisposable? _storeStateSubscription;
    private string? _currentRemoteSessionId;
    private IReadOnlyList<AuthMethodDefinition>? _advertisedAuthMethods;
    private bool _suppressMiniWindowSessionSync;
    private bool _suppressStoreConversationProjection;
    private bool _suppressStoreProfileProjection;
    private bool _suppressStorePromptProjection;
    private bool _suppressProfileSyncFromStore;
    private string? _selectedProfileIdFromStore;

    /// <summary>
    /// Local conversation binding connects a stable UI ConversationId to a transient ACP RemoteSessionId.
    /// This allows the user to switch between tabs/histories without losing the underlying agent session context,
    /// and handles reconnections by re-binding new remote sessions to the same local ID.
    /// </summary>
    private readonly Dictionary<string, ConversationBinding> _conversationBindings = new(StringComparer.Ordinal);

    private sealed class ConversationBinding
    {
        public ConversationBinding(string conversationId)
        {
            ConversationId = conversationId;
            CreatedAt = DateTime.UtcNow;
            LastUpdatedAt = DateTime.UtcNow;
        }

        public string ConversationId { get; }
        public string? BoundProfileId { get; set; }
        public string? RemoteSessionId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime LastUpdatedAt { get; set; }

        public List<ChatMessageViewModel> Transcript { get; } = new();
        public List<PlanEntryViewModel> Plan { get; } = new();
        public bool ShowPlanPanel { get; set; }
        public string? PlanTitle { get; set; }
    }

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

    public ChatViewModel(
        IChatStore chatStore,
        ChatServiceFactory chatServiceFactory,
        IConfigurationService configurationService,
        AppPreferencesViewModel preferences,
        AcpProfilesViewModel acpProfiles,
        ISessionManager sessionManager,
        IConversationStore conversationStore,
        IMiniWindowCoordinator miniWindowCoordinator,
        ILogger<ChatViewModel> logger,
        SynchronizationContext? syncContext = null)
        : base(logger)
    {
        _chatStore = chatStore ?? throw new ArgumentNullException(nameof(chatStore));
        _chatServiceFactory = chatServiceFactory ?? throw new ArgumentNullException(nameof(chatServiceFactory));
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _acpProfiles = acpProfiles ?? throw new ArgumentNullException(nameof(acpProfiles));
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _conversationStore = conversationStore ?? throw new ArgumentNullException(nameof(conversationStore));
        _miniWindowCoordinator = miniWindowCoordinator ?? throw new ArgumentNullException(nameof(miniWindowCoordinator));
        _syncContext = syncContext ?? SynchronizationContext.Current ?? new SynchronizationContext();
        StartStoreProjection();

        _acpProfiles.PropertyChanged += OnAcpProfilesPropertyChanged;
        _acpProfiles.Profiles.CollectionChanged += OnAcpProfilesCollectionChanged;
        _preferences.PropertyChanged += OnPreferencesPropertyChanged;
        PlanEntries.CollectionChanged += OnCurrentPlanCollectionChanged;

        // Start restoring local conversation list immediately so the sidebar can show it ASAP.
        _ = RestoreConversationsAsync();
    }

    private void StartStoreProjection()
    {
        // SINGLE SOURCE OF TRUTH (SSOT): We project the central store state into UI-bound properties.
        // This reactive pattern ensures the UI stays synchronized with the domain state regardless of which
        // thread triggered the update (e.g. background ACP streaming vs user interaction).
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
                await PostToUiAsync(() =>
                {
                    if (token.IsCancellationRequested || _disposed)
                    {
                        return;
                    }

                    IsPromptInFlight = state.IsPromptInFlight;
                    IsThinking = state.IsThinking;
                    IsConnected = state.ConnectionStatus == "Connected";
                    CurrentConnectionStatus = state.ConnectionStatus;
                    ConnectionErrorMessage = state.ConnectionError;
                    SyncMessageHistory(state.Messages, state.IsThinking);
                    ApplyStoreProjection(state);
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
        if (e.PropertyName == nameof(AppPreferencesViewModel.LastSelectedServerId))
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

    private void ApplyStoreProjection(ChatState state)
    {
        _suppressStoreConversationProjection = true;
        _suppressStoreProfileProjection = true;
        _suppressStorePromptProjection = true;
        try
        {
            if (!string.Equals(CurrentSessionId, state.SelectedConversationId, StringComparison.Ordinal))
            {
                CurrentSessionId = state.SelectedConversationId;
            }

            var draft = state.DraftText ?? string.Empty;
            if (!string.Equals(CurrentPrompt, draft, StringComparison.Ordinal))
            {
                CurrentPrompt = draft;
            }

            ApplySelectedProfileFromStore(state.SelectedAcpProfileId);
        }
        finally
        {
            _suppressStorePromptProjection = false;
            _suppressStoreProfileProjection = false;
            _suppressStoreConversationProjection = false;
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

        // Fire-and-forget; errors are surfaced via the existing ConnectionErrorMessage/Logger paths.
        _ = ConnectToAcpProfileCommand.ExecuteAsync(value);
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
            _ = _chatStore.Dispatch(new SelectConversationAction(value));
        }

        // Keep the header name stable and decouple it from ACP sessionId.
        CurrentSessionDisplayName = ResolveSessionDisplayName(value);

        if (!string.IsNullOrWhiteSpace(value))
        {
            // Ensure we have a conversation binding for this conversation id.
            var binding = GetOrCreateConversationBinding(value);

            binding.BoundProfileId ??= _preferences.LastSelectedServerId;

            _currentRemoteSessionId = binding.RemoteSessionId;

            RestoreConversation(binding);
        }
        else
        {
            _currentRemoteSessionId = null;
            CurrentPlanTitle = null;
            ShowPlanPanel = false;
            PlanEntries.Clear();
        }

        if (IsEditingSessionName)
        {
            IsEditingSessionName = false;
            EditingSessionName = string.Empty;
        }

        // Persist the active conversation state to ensure it survives application restarts.
        ScheduleConversationSave();

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
            CurrentSessionId = value.ConversationId;
        }
    }

    /// <summary>
    /// Retrieves or initializes a local conversation binding.
    /// Bindings act as a bridge between the persistent store and the live UI state.
    /// </summary>
    private ConversationBinding GetOrCreateConversationBinding(string conversationId)
    {
        if (_conversationBindings.TryGetValue(conversationId, out var existing))
        {
            return existing;
        }

        var created = new ConversationBinding(conversationId);
        _conversationBindings[conversationId] = created;
        return created;
    }

    private ConversationBinding? TryGetConversationBinding(string? conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return null;
        }

        return _conversationBindings.TryGetValue(conversationId, out var binding) ? binding : null;
    }

    private void RestoreConversation(ConversationBinding binding)
    {
        // Locally-persisted conversations are the source of truth for UI history playback.
        MessageHistory.Clear();
        foreach (var msg in binding.Transcript)
        {
            MessageHistory.Add(msg);
        }

        PlanEntries.Clear();
        foreach (var entry in binding.Plan)
        {
            PlanEntries.Add(entry);
        }
        ShowPlanPanel = binding.ShowPlanPanel;
        CurrentPlanTitle = binding.PlanTitle;
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

    public async Task RestoreConversationsAsync()
    {
        if (_conversationsRestored)
        {
            return;
        }

        _conversationsRestored = true;

        ConversationDocument doc;
        try
        {
            IsConversationListLoading = true;
            doc = await _conversationStore.LoadAsync().ConfigureAwait(false);
        }
        catch
        {
            _syncContext.Post(_ => { IsConversationListLoading = false; }, null);
            return;
        }

        foreach (var convo in doc.Conversations)
        {
            if (string.IsNullOrWhiteSpace(convo.ConversationId))
            {
                continue;
            }

            if (_sessionManager.GetSession(convo.ConversationId) != null)
            {
                continue;
            }

            try
            {
                await _sessionManager.CreateSessionAsync(convo.ConversationId, convo.Cwd).ConfigureAwait(false);
            }
            catch
            {
            }
        }

        _syncContext.Post(_ =>
        {
            try
            {
                foreach (var convo in doc.Conversations)
                {
                    if (string.IsNullOrWhiteSpace(convo.ConversationId))
                    {
                        continue;
                    }

                    var binding = GetOrCreateConversationBinding(convo.ConversationId);
                    binding.CreatedAt = convo.CreatedAt == default ? DateTime.UtcNow : convo.CreatedAt;
                    binding.LastUpdatedAt = convo.LastUpdatedAt == default ? DateTime.UtcNow : convo.LastUpdatedAt;

                    // Display name is persisted separately from ACP sessionId.
                    var displayName = string.IsNullOrWhiteSpace(convo.DisplayName)
                        ? SessionNamePolicy.CreateDefault(convo.ConversationId)
                        : convo.DisplayName.Trim();

                    _sessionManager.UpdateSession(
                        convo.ConversationId,
                        s =>
                        {
                            s.DisplayName = displayName;
                            s.CreatedAt = binding.CreatedAt;
                            s.LastActivityAt = binding.LastUpdatedAt;
                            // Restore Cwd from persisted conversation record.
                            if (!string.IsNullOrWhiteSpace(convo.Cwd))
                            {
                                s.Cwd = convo.Cwd;
                            }
                        },
                        updateActivity: false);

                    binding.Transcript.Clear();
                    foreach (var msg in convo.Messages)
                    {
                        binding.Transcript.Add(FromSnapshot(msg));
                    }
                }

                var last = doc.LastActiveConversationId;
                if (!string.IsNullOrWhiteSpace(last) &&
                    _conversationBindings.ContainsKey(last))
                {
                    CurrentSessionId = last;
                    IsSessionActive = true;
                }
                else if (_conversationBindings.Count > 0)
                {
                    // If last-active is missing, default to the most recent conversation for a better UX.
                    var fallback = _conversationBindings.Values
                        .OrderByDescending(c => c.LastUpdatedAt)
                        .Select(c => c.ConversationId)
                        .FirstOrDefault();

                    if (!string.IsNullOrWhiteSpace(fallback))
                    {
                        CurrentSessionId = fallback;
                        IsSessionActive = true;
                    }
                }
            }
            catch
            {
            }

            IsConversationListLoading = false;
            NotifyConversationListChanged();
        }, null);
    }

    private void NotifyConversationListChanged()
    {
        _syncContext.Post(_ =>
        {
            ConversationListVersion++;
            OnPropertyChanged(nameof(GetKnownConversationIds));
            RefreshMiniWindowSessions();
        }, null);
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

    private void SyncMessageHistory(IImmutableList<ChatMessage>? messages, bool isThinking)
    {
        if (messages == null)
        {
            MessageHistory.Clear();
            return;
        }

        // Incremental Reconciliation: We perform a targeted diff between the new state and the UI collection.
        // This preserves existing ViewModel instances where possible, which is critical for:
        // 1. Maintaining UI virtualization state (scroll position).
        // 2. Ensuring smooth text animation (e.g. streaming deltas).
        // 3. Avoiding expensive re-renders of complex message bubbles.
        for (int i = 0; i < messages.Count; i++)
        {
            var message = messages[i];
            if (i < MessageHistory.Count)
            {
                if (MessageHistory[i].Id != message.Id)
                {
                    // Re-sync from this point onwards if IDs drift
                    while (MessageHistory.Count > i) MessageHistory.RemoveAt(i);
                    MessageHistory.Add(MapToViewModel(message));
                }
                else
                {
                    // Same ID, update content if changed (e.g. streaming deltas)
                    var vm = MessageHistory[i];
                    var newContent = message.Parts?.OfType<TextPart>().LastOrDefault()?.Text ?? string.Empty;
                    if (vm.TextContent != newContent)
                    {
                        vm.TextContent = newContent;
                    }
                }
            }
            else
            {
                MessageHistory.Add(MapToViewModel(message));
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

    private ChatMessageViewModel MapToViewModel(ChatMessage message)
    {
        var content = message.Parts?.OfType<TextPart>().LastOrDefault()?.Text ?? string.Empty;
        return new ChatMessageViewModel
        {
            Id = message.Id,
            Timestamp = message.Timestamp.DateTime,
            IsOutgoing = message.IsOutgoing,
            ContentType = "text",
            TextContent = content
        };
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

    private void ScheduleConversationSave()
    {
        if (_preferences.SaveLocalHistory == false)
        {
            return;
        }

        _conversationSaveCts?.Cancel();
        _conversationSaveCts = new CancellationTokenSource();
        var token = _conversationSaveCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                // Debounce to avoid too frequent writes while streaming updates.
                await Task.Delay(400, token).ConfigureAwait(false);
                await SaveConversationsAsync(token).ConfigureAwait(false);
            }
            catch
            {
            }
        }, token);
    }

    private async Task SaveConversationsAsync(CancellationToken cancellationToken)
    {
        var doc = new ConversationDocument
        {
            Version = 1,
            LastActiveConversationId = CurrentSessionId
        };

        // Keep most-recent first.
        var ordered = _conversationBindings.Values
            .OrderByDescending(c => c.LastUpdatedAt)
            .ToArray();

        foreach (var binding in ordered)
        {
            var name = ResolveSessionDisplayName(binding.ConversationId);
            var session = _sessionManager.GetSession(binding.ConversationId);
            var record = new ConversationRecord
            {
                ConversationId = binding.ConversationId,
                DisplayName = name,
                CreatedAt = binding.CreatedAt,
                LastUpdatedAt = binding.LastUpdatedAt,
                Cwd = session?.Cwd
            };

            foreach (var msg in binding.Transcript)
            {
                record.Messages.Add(ToSnapshot(msg));
            }

            doc.Conversations.Add(record);
        }

        await _conversationStore.SaveAsync(doc, cancellationToken).ConfigureAwait(false);
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

        var sanitized = SessionNamePolicy.Sanitize(newDisplayName);
        var finalName = string.IsNullOrEmpty(sanitized)
            ? SessionNamePolicy.CreateDefault(conversationId)
            : sanitized;

        _sessionManager.UpdateSession(conversationId, s => s.DisplayName = finalName, updateActivity: false);

        if (string.Equals(CurrentSessionId, conversationId, StringComparison.Ordinal))
        {
            CurrentSessionDisplayName = finalName;
        }

        var binding = TryGetConversationBinding(conversationId);
        if (binding != null)
        {
            binding.LastUpdatedAt = DateTime.UtcNow;
        }

        ScheduleConversationSave();
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
            MessageHistory.Clear();
            PlanEntries.Clear();
            ShowPlanPanel = false;
            _suppressSessionUpdatesToUi = false;
        }

        _syncContext.Post(_ =>
        {
            try
            {
                _sessionManager.RemoveSession(conversationId);
                _conversationBindings.Remove(conversationId);

                ScheduleConversationSave();
                NotifyConversationListChanged();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Archive operation failed due to underlying exception: {ConversationId}", conversationId);
            }
        }, null);
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
            MessageHistory.Clear();
            PlanEntries.Clear();
            ShowPlanPanel = false;
            _suppressSessionUpdatesToUi = false;
        }

        _syncContext.Post(_ =>
        {
            try
            {
                _conversationBindings.Remove(conversationId);
                _sessionManager.RemoveSession(conversationId);

                ScheduleConversationSave();
                NotifyConversationListChanged();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Delete operation failed due to underlying exception: {ConversationId}", conversationId);
            }
        }, null);
    }

    public string[] GetKnownConversationIds()
    {
        return _conversationBindings.Values
            .OrderByDescending(c => c.LastUpdatedAt)
            .Select(c => c.ConversationId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToArray();
    }

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

    public async Task TryAutoConnectAsync()
    {
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

        await EnsureAcpProfilesLoadedAsync();

        ServerConfiguration? config;
        try
        {
            // The config may not be loaded into the list yet (e.g. first launch), so we load by id as fallback.
            config = _acpProfiles.Profiles.FirstOrDefault(p => p.Id == profileId)
                    ?? await _configurationService.LoadConfigurationAsync(profileId);
        }
        catch
        {
            return;
        }

        if (config == null)
        {
            return;
        }

        try
        {
            _suppressAcpProfileConnect = true;
            try
            {
                SelectedAcpProfile = config;
            }
            finally
            {
                _suppressAcpProfileConnect = false;
            }

            await ConnectToAcpProfileAsync(config);
        }
        catch
        {
        }
    }

    [RelayCommand]
    private async Task ConnectToAcpProfileAsync(ServerConfiguration? profile)
    {
        if (profile == null)
        {
            return;
        }

        try
        {
            _acpProfiles.MarkLastConnected(profile);
            _acpProfiles.SelectedProfile = profile;

            ApplyProfileToTransportConfig(profile);

            // Switching ACP should not reset the current conversation transcript.
            var preserveConversation = IsSessionActive && !string.IsNullOrWhiteSpace(CurrentSessionId);
            await ApplyTransportConfigCoreAsync(preserveConversation);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to connect to ACP profile {ProfileId}", profile.Id);
            throw;
        }
    }

    private void ApplyProfileToTransportConfig(ServerConfiguration profile)
    {
        TransportConfig.SelectedTransportType = profile.Transport;

        if (profile.Transport == TransportType.Stdio)
        {
            TransportConfig.StdioCommand = profile.StdioCommand ?? string.Empty;
            TransportConfig.StdioArgs = profile.StdioArgs ?? string.Empty;
            TransportConfig.RemoteUrl = string.Empty;
        }
        else
        {
            TransportConfig.RemoteUrl = profile.ServerUrl ?? string.Empty;
            TransportConfig.StdioCommand = string.Empty;
            TransportConfig.StdioArgs = string.Empty;
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
            var (isValid, errorMessage) = TransportConfig.Validate();
            if (!isValid)
            {
                ConnectionErrorMessage = errorMessage;
                return;
            }

            ConnectionErrorMessage = null;
            IsConnecting = true;
           try
           {
               Logger.LogInformation("Applying transport configuration: {TransportType}", TransportConfig.SelectedTransportType);
               Logger.LogInformation("TransportConfig current values - StdioCommand: '{StdioCommand}', StdioArgs: '{StdioArgs}', RemoteUrl: '{RemoteUrl}'",
                   TransportConfig.StdioCommand, TransportConfig.StdioArgs, TransportConfig.RemoteUrl);

                // Recreate ChatService using the factory based on user configuration.
                // 1. Instantiate the appropriate ChatService for the transport type.
                IChatService newChatService;
                switch (TransportConfig.SelectedTransportType)
               {
                   case TransportType.Stdio:
                       Logger.LogInformation("Stdio config - Command: {Command}, Args: {Args}", TransportConfig.StdioCommand, TransportConfig.StdioArgs);
                       newChatService = _chatServiceFactory.CreateChatService(
                           TransportType.Stdio,
                           TransportConfig.StdioCommand,
                           TransportConfig.StdioArgs,
                           null);
                       break;
                   case TransportType.WebSocket:
                       newChatService = _chatServiceFactory.CreateChatService(
                           TransportType.WebSocket,
                           null,
                           null,
                           TransportConfig.RemoteUrl);
                       break;
                   case TransportType.HttpSse:
                       newChatService = _chatServiceFactory.CreateChatService(
                           TransportType.HttpSse,
                           null,
                           null,
                           TransportConfig.RemoteUrl);
                       break;
                   default:
                       throw new InvalidOperationException($"Unsupported transport type: {TransportConfig.SelectedTransportType}");
               }

               // Best-effort: disconnect previous transport to avoid leaks (do not reset local conversation state).
                if (_chatService != null)
                {
                    try
                    {
                        await _chatService.DisconnectAsync();
                    }
                    catch (Exception ex)
                    {
                        // Failure to disconnect the previous transport is non-fatal.
                        Logger.LogDebug(ex, "Failed to disconnect previous transport");
                    }
                }

               // 2. Unsubscribe from events of the old service (if any).
               if (_chatService != null)
               {
                   UnsubscribeFromChatService(_chatService);
               }

               // 3. Subscribe to events of the new service.
               SubscribeToChatService(newChatService);

               // 4. Replace the old ChatService instance.
               _chatService = newChatService;

               // 5. Initialize ACP protocol (with timeout).
               Logger.LogInformation("Initializing ACP protocol...");
               var initParams = new InitializeParams
               {
                   ProtocolVersion = 1,
                   ClientInfo = new ClientInfo
                   {
                       Name = "SalmonEgg",
                       Title = "Uno Acp Client",
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

               // Use Task.WhenAny for timeout to avoid UI freeze.
               var initTask = _chatService.InitializeAsync(initParams);
               var timeoutTask = Task.Delay(TimeSpan.FromSeconds(30));
               var completedTask = await Task.WhenAny(initTask, timeoutTask);

               if (completedTask == timeoutTask)
               {
                   throw new TimeoutException("Initialization timeout: Agent did not respond within the allocated time. Please verify command and arguments.");
               }

               var initResponse = await initTask;
               UpdateAgentInfo();
               Logger.LogInformation("ACP protocol initialization complete, Agent: {Name} v{Version}", AgentName, AgentVersion);

               var isConnected = _chatService.IsConnected && _chatService.IsInitialized;
               _ = _chatStore.Dispatch(new UpdateConnectionStatusAction(isConnected));
               CacheAuthMethods(initResponse);
               ClearAuthenticationRequirement();

               if (string.IsNullOrWhiteSpace(CurrentSessionId))
               {
                   CurrentSessionId = Guid.NewGuid().ToString();
               }

               IsSessionActive = !string.IsNullOrWhiteSpace(CurrentSessionId);

               // Initialize success; auto-create new session (ACP standard flow).
               // Ref: https://agentclientprotocol.com/protocol/session-setup
               Logger.LogInformation("Creating new session...");
               var sessionParams = new SalmonEgg.Domain.Models.Protocol.SessionNewParams
               {
                   Cwd = GetActiveSessionCwdOrDefault()
               };

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
                        // Keep the transport connected; the user may complete auth externally and retry.
                        Logger.LogWarning("Authentication required before session creation.");
                        ShowTransportConfigPanel = false;
                        return;
                    }

                    response = await _chatService.CreateSessionAsync(sessionParams);
                }
                Logger.LogInformation("Session created successfully, SessionId={SessionId}", response.SessionId);

                var remoteSessionId = response.SessionId;
                var hasRemoteSession = !string.IsNullOrWhiteSpace(remoteSessionId);

                if (preserveConversation && IsSessionActive && !string.IsNullOrWhiteSpace(CurrentSessionId) && hasRemoteSession)
                {
                    var binding = GetOrCreateConversationBinding(CurrentSessionId!);
                    binding.RemoteSessionId = remoteSessionId;
                    binding.BoundProfileId = _preferences.LastSelectedServerId;
                    _currentRemoteSessionId = remoteSessionId;

                    // Keep the local transcript; just rebind the active remote session.
                    IsSessionActive = true;
                }
                else
                {
                    // Do NOT replace the local conversation id with the ACP session id.
                    // If there isn't a local conversation selected, create one and bind it to the new remote session.
                    if (string.IsNullOrWhiteSpace(CurrentSessionId))
                    {
                        CurrentSessionId = Guid.NewGuid().ToString();
                    }

                    IsSessionActive = !string.IsNullOrWhiteSpace(CurrentSessionId);
                    _currentRemoteSessionId = remoteSessionId;

                    if (!string.IsNullOrWhiteSpace(CurrentSessionId))
                    {
                        var binding = GetOrCreateConversationBinding(CurrentSessionId);
                        binding.RemoteSessionId = remoteSessionId;
                        binding.BoundProfileId = _preferences.LastSelectedServerId;
                        binding.LastUpdatedAt = DateTime.UtcNow;
                    }
                }

                ApplySessionNewResponse(response);

                if (IsSessionActive)
                {
                    // Local transcript is restored by conversation switch; keep remote replay separate.
                    LoadSessionHistory();
                }
                ShowTransportConfigPanel = false;
                Logger.LogInformation("Connected successfully");
            }
            catch (Exception ex)
            {
                _ = _chatStore.Dispatch(new UpdateConnectionStatusAction(false, $"Connection failed: {ex.Message}"));
                Logger.LogError(ex, "Error during connection");
                _currentRemoteSessionId = null;
                // Keep the local conversation visible; only clear the remote binding so we don't send to stale ids.
                ClearRemoteSessionBindingForCurrentConversation();
            }
           finally
           {
               IsConnecting = false;
           }
        }

    private void ApplySessionNewResponse(SessionNewResponse response)
    {
        // Load modes (some Agents may omit this field; deprecated in favor of configOptions)
        AvailableModes.Clear();
        SelectedMode = null;
        if (response.Modes?.AvailableModes != null)
        {
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

            if (AvailableModes.Count > 0)
            {
                var currentModeId = response.Modes.CurrentModeId;
                SelectedMode = string.IsNullOrWhiteSpace(currentModeId)
                    ? AvailableModes[0]
                    : AvailableModes.FirstOrDefault(m => m.ModeId == currentModeId) ?? AvailableModes[0];
            }
        }

        Logger.LogInformation("Session modes loaded: {Count}", AvailableModes.Count);

        // Load config options
        ConfigOptions.Clear();
        ShowConfigOptionsPanel = false;
        if (response.ConfigOptions != null)
        {
            foreach (var option in response.ConfigOptions)
            {
                ConfigOptions.Add(ConfigOptionViewModel.CreateFromAcp(option));
            }
            ShowConfigOptionsPanel = ConfigOptions.Count > 0;
            SyncModesFromConfigOptions();
        }
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

           // Listen for session status.
           if (chatService.CurrentSessionId != null)
           {
               CurrentSessionId = chatService.CurrentSessionId;
               IsSessionActive = true;
               LoadSessionHistory();
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

              // Listen for session status.
              if (_chatService.CurrentSessionId != null)
              {
                  CurrentSessionId = _chatService.CurrentSessionId;
                  IsSessionActive = true;
                  LoadSessionHistory();
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
                if (!string.IsNullOrWhiteSpace(_currentRemoteSessionId) &&
                    !string.Equals(e.SessionId, _currentRemoteSessionId, StringComparison.Ordinal))
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

                    var message = CreateToolCallMessage(toolCallUpdate);
                    AppendToTranscript(message);
                }
                else if (e.Update is ToolCallStatusUpdate toolCallStatusUpdate)
                {
                    _ = _chatStore.Dispatch(new SetIsThinkingAction(true));
                    UpdateToolCallStatus(toolCallStatusUpdate);
                }
                else if (e.Update is PlanUpdate planUpdate)
                {
                    UpdatePlan(planUpdate);
                }
                else if (e.Update is CurrentModeUpdate modeChange)
                {
                    OnModeChanged(modeChange);
                }
                else if (e.Update is ConfigUpdateUpdate configUpdate)
                {
                    UpdateConfigOptions(configUpdate);
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

        _ = _chatStore.Dispatch(new AppendTextDeltaAction(chunk));

        var binding = TryGetConversationBinding(CurrentSessionId);
        if (binding != null)
        {
            binding.LastUpdatedAt = DateTime.UtcNow;
            ScheduleConversationSave();
        }
    }

    public async Task<bool> TrySwitchToSessionAsync(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return false;
        }

        if (string.Equals(CurrentSessionId, sessionId, StringComparison.Ordinal))
        {
            return true;
        }

        await _sessionSwitchGate.WaitAsync().ConfigureAwait(false);
        try
        {
            _suppressSessionUpdatesToUi = true;

            // Switch local conversation first (UI stays stable even if not connected).
            await PostToUiAsync(() =>
            {
                CurrentSessionId = sessionId;
                IsSessionActive = true;
            }).ConfigureAwait(false);

            var binding = GetOrCreateConversationBinding(sessionId);
            binding.BoundProfileId ??= _preferences.LastSelectedServerId;

            // If the active ACP changed since this conversation was last used, defer remote session creation to the next send.
            // Switching conversations should be instant and offline-friendly.
            var currentProfileId = _preferences.LastSelectedServerId;
            if (!string.IsNullOrWhiteSpace(currentProfileId) &&
                !string.Equals(binding.BoundProfileId, currentProfileId, StringComparison.Ordinal))
            {
                binding.BoundProfileId = currentProfileId;
                binding.RemoteSessionId = null;
                binding.LastUpdatedAt = DateTime.UtcNow;
                _currentRemoteSessionId = null;
                ScheduleConversationSave();
            }
            else
            {
                _currentRemoteSessionId = binding.RemoteSessionId;
            }

            await PostToUiAsync(() =>
            {
                RestoreConversation(binding);
            }).ConfigureAwait(false);

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
            _sessionSwitchGate.Release();
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
            AgentName = _chatService.AgentInfo.Name;
            AgentVersion = _chatService.AgentInfo.Version;
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
        IsAuthenticationRequired = false;
        AuthenticationHintMessage = null;
    }

    private void MarkAuthenticationRequired(AuthMethodDefinition? method, string? messageOverride = null)
    {
        var message =
            messageOverride
            ?? method?.Description
            ?? "The agent requires authentication before it can respond.";

        IsAuthenticationRequired = true;
        AuthenticationHintMessage = message;

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

    private void LoadSessionHistory()
    {
        // Conversations are local. When a remote session replays history, it is appended through SessionUpdate events.
        // We keep transcript per conversation and restore it when switching conversations.
        var binding = TryGetConversationBinding(CurrentSessionId);
        if (binding != null)
        {
            RestoreConversation(binding);
        }
    }

    private void AddEntryToMessageHistory(SessionUpdateEntry entry)
    {
        if (entry.Content != null)
        {
            AddMessageToHistory(entry.Content, isOutgoing: false);
        }
        else if (entry.Entries != null)
        {
            foreach (var planEntry in entry.Entries)
            {
                var message = ChatMessageViewModel.CreateFromPlanEntry(
                    Guid.NewGuid().ToString(),
                    planEntry,
                    isOutgoing: false);
                AppendToTranscript(message);
            }
        }
        else if (!string.IsNullOrEmpty(entry.ModeId))
        {
            var message = ChatMessageViewModel.CreateFromModeChange(
                Guid.NewGuid().ToString(),
                entry.ModeId,
                entry.Title,
                isOutgoing: false);
            AppendToTranscript(message);
        }
    }

    private void AppendToTranscript(ChatMessageViewModel message)
    {
        MessageHistory.Add(message);

        var binding = TryGetConversationBinding(CurrentSessionId);
        if (binding != null)
        {
            binding.Transcript.Add(message);
            binding.LastUpdatedAt = DateTime.UtcNow;
            ScheduleConversationSave();
        }
    }



    private void RemoveMessageFromTranscript(ChatMessageViewModel message)
    {
        MessageHistory.Remove(message);
        var binding = TryGetConversationBinding(CurrentSessionId);
        if (binding != null)
        {
            binding.Transcript.Remove(message);
            binding.LastUpdatedAt = DateTime.UtcNow;
            ScheduleConversationSave();
        }
    }

    private void ReplaceMessageInTranscript(ChatMessageViewModel oldMessage, ChatMessageViewModel newMessage)
    {
        var index = MessageHistory.IndexOf(oldMessage);
        if (index >= 0)
        {
            MessageHistory[index] = newMessage;
        }
        else
        {
            MessageHistory.Add(newMessage);
        }

        var binding = TryGetConversationBinding(CurrentSessionId);
        if (binding != null)
        {
            var transcriptIndex = binding.Transcript.IndexOf(oldMessage);
            if (transcriptIndex >= 0)
            {
                binding.Transcript[transcriptIndex] = newMessage;
            }
            else
            {
                binding.Transcript.Add(newMessage);
            }
            binding.LastUpdatedAt = DateTime.UtcNow;
            ScheduleConversationSave();
        }
    }

    private ChatMessageViewModel CreateMessageFromContent(ContentBlock content, bool isOutgoing)
    {
        var id = Guid.NewGuid().ToString();
        switch (content)
        {
            case TextContentBlock text:
                return ChatMessageViewModel.CreateFromTextContent(id, content, isOutgoing);
            case ImageContentBlock image:
                return ChatMessageViewModel.CreateFromImageContent(id, content, isOutgoing);
            case AudioContentBlock audio:
                return ChatMessageViewModel.CreateFromAudioContent(id, content, isOutgoing);
            case ResourceContentBlock resourceContent:
                return ChatMessageViewModel.CreateFromResourceContent(id, resourceContent, isOutgoing);
            case ResourceLinkContentBlock resourceLink:
                return ChatMessageViewModel.CreateFromResourceLink(id, resourceLink, isOutgoing);
            default:
                return ChatMessageViewModel.CreateFromTextContent(id, content, isOutgoing);
        }
    }

    private ChatMessageViewModel CreateToolCallMessage(ToolCallUpdate toolCall)
    {
        var rawInput = toolCall.RawInput?.GetRawText();
        var rawOutput = toolCall.RawOutput?.GetRawText();

        return ChatMessageViewModel.CreateFromToolCall(
            Guid.NewGuid().ToString(),
            toolCall.ToolCallId,
            rawInput,
            rawOutput,
            toolCall.Kind,
            toolCall.Status,
            toolCall.Title,
            isOutgoing: false);
    }

    private void AddMessageToHistory(ContentBlock content, bool isOutgoing)
    {
        var id = Guid.NewGuid().ToString();
        var parts = ImmutableList<ChatContentPart>.Empty;

        if (content is TextContentBlock text)
        {
            parts = parts.Add(new TextPart(text.Text ?? string.Empty));
        }
        else
        {
            // For non-text blocks, fallback to a placeholder text for now in the Store
            // Future steps will add specialized ChatContentPart types
            parts = parts.Add(new TextPart($"[{content.GetType().Name}]"));
        }

        var message = new ChatMessage(id, DateTimeOffset.Now, isOutgoing, Parts: parts);
        _ = _chatStore.Dispatch(new AddMessageAction(message));

        var binding = TryGetConversationBinding(CurrentSessionId);
        if (binding != null)
        {
            binding.LastUpdatedAt = DateTime.UtcNow;
            ScheduleConversationSave();
        }
    }

    private void AddToolCallToHistory(ToolCallUpdate toolCall)
    {
        var rawInput = toolCall.RawInput?.GetRawText();
        var rawOutput = toolCall.RawOutput?.GetRawText();

        var message = ChatMessageViewModel.CreateFromToolCall(
            Guid.NewGuid().ToString(),
            toolCall.ToolCallId,
            rawInput,
            rawOutput,
            toolCall.Kind,
            toolCall.Status,
            toolCall.Title,
            isOutgoing: false);
        AppendToTranscript(message);
    }

    private void UpdateToolCallStatus(ToolCallStatusUpdate toolCallStatusUpdate)
    {
        if (string.IsNullOrEmpty(toolCallStatusUpdate.ToolCallId))
        {
            return;
        }

        var toolCallId = toolCallStatusUpdate.ToolCallId;
        var status = toolCallStatusUpdate.Status;
        var content = toolCallStatusUpdate.Content;

        var existingMessage = MessageHistory.FirstOrDefault(m =>
            m.ToolCallId == toolCallId && m.ContentType == "tool_call");

        if (existingMessage != null)
        {
            if (status.HasValue)
            {
                existingMessage.ToolCallStatus = status;
            }

            if (content != null && content.Count > 0)
            {
                foreach (var item in content)
                {
                    if (item is Domain.Models.Tool.ContentToolCallContent contentItem)
                    {
                        if (contentItem.Content is TextContentBlock textBlock && !string.IsNullOrEmpty(textBlock.Text))
                        {
                            existingMessage.TextContent = string.IsNullOrEmpty(existingMessage.TextContent)
                                ? textBlock.Text
                                : existingMessage.TextContent + textBlock.Text;
                        }
                    }
                }
            }

            var binding = TryGetConversationBinding(CurrentSessionId);
            if (binding != null)
            {
                binding.LastUpdatedAt = DateTime.UtcNow;
                ScheduleConversationSave();
            }
        }
    }

    private void UpdatePlan(PlanUpdate planUpdate)
    {
        ShowPlanPanel = true;
        PlanEntries.Clear();
        CurrentPlanTitle = string.IsNullOrWhiteSpace(planUpdate.Title) ? null : planUpdate.Title.Trim();

        if (planUpdate.Entries != null)
        {
            foreach (var entry in planUpdate.Entries)
            {
                PlanEntries.Add(new PlanEntryViewModel
                {
                    Content = entry.Content ?? string.Empty,
                    Status = entry.Status,
                    Priority = entry.Priority
                });
            }
        }

        var binding = TryGetConversationBinding(CurrentSessionId);
        if (binding != null)
        {
            binding.Plan.Clear();
            foreach (var item in PlanEntries)
            {
                binding.Plan.Add(item);
            }
            binding.ShowPlanPanel = ShowPlanPanel;
            binding.PlanTitle = CurrentPlanTitle;
            binding.LastUpdatedAt = DateTime.UtcNow;
            ScheduleConversationSave();
        }
    }

    private void OnModeChanged(CurrentModeUpdate modeChange)
    {
        if (!string.IsNullOrEmpty(modeChange.CurrentModeId))
        {
            // Update current mode.
            var selectedMode = AvailableModes.FirstOrDefault(m => m.ModeId == modeChange.CurrentModeId);
            if (selectedMode != null)
            {
                SelectedMode = selectedMode;
            }
        }
    }

    private void UpdateConfigOptions(ConfigUpdateUpdate configUpdate)
    {
        if (configUpdate.ConfigOptions != null)
        {
            ConfigOptions.Clear();
            foreach (var option in configUpdate.ConfigOptions)
            {
                ConfigOptions.Add(ConfigOptionViewModel.CreateFromAcp(option));
            }
            ShowConfigOptionsPanel = ConfigOptions.Count > 0;
            SyncModesFromConfigOptions();
        }
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
           IsInitializing = true;
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
           IsInitializing = false;
       }
   }

    [RelayCommand]
    private async Task CreateNewSessionAsync()
    {
        if (IsConnecting)
            return;

        try
        {
            IsConnecting = true;
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
            CurrentSessionId = response.SessionId;
            IsSessionActive = true;

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


            MessageHistory.Clear();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to create session");
            SetError($"Failed to create session: {ex.Message}");
        }
        finally
        {
            IsConnecting = false;
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

                var remoteSessionId = await EnsureRemoteSessionAsync(_sendPromptCts.Token);
                var promptParams = new SessionPromptParams
                {
                    SessionId = remoteSessionId,
                    Prompt = new List<ContentBlock> { new TextContentBlock { Text = promptText } },
                    MaxTokens = null,
                    StopSequences = null
                };

                try
                {
                    await _chatService.SendPromptAsync(promptParams, _sendPromptCts.Token);
                }
                catch (Exception ex) when (IsAuthenticationRequiredError(ex))
                {
                    var authenticated = await TryAuthenticateAsync(_sendPromptCts.Token).ConfigureAwait(false);
                    if (!authenticated)
                    {
                        throw;
                    }

                    await _chatService.SendPromptAsync(promptParams, _sendPromptCts.Token);
                }
                catch (Exception ex) when (IsRemoteSessionNotFound(ex))
                {
                    // Error Recovery: If the agent process was restarted or the remote session expired,
                    // we clear the stale binding, create a new remote session, and retry the prompt once.
                    // This provides a seamless "auto-recovery" experience for the user.
                    ClearRemoteSessionBindingForCurrentConversation();
                    remoteSessionId = await EnsureRemoteSessionAsync(_sendPromptCts.Token);
                    promptParams.SessionId = remoteSessionId;
                    await _chatService.SendPromptAsync(promptParams, _sendPromptCts.Token);
                }
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
            var remoteSessionId = _currentRemoteSessionId;
            if (string.IsNullOrWhiteSpace(remoteSessionId) && !string.IsNullOrWhiteSpace(CurrentSessionId))
            {
                remoteSessionId = TryGetConversationBinding(CurrentSessionId)?.RemoteSessionId;
            }

            if (string.IsNullOrWhiteSpace(remoteSessionId))
            {
                return;
            }

            var cancelParams = new SessionCancelParams
            {
                SessionId = remoteSessionId!,
                Reason = "User cancelled"
            };

            if (_chatService != null)
            {
                await _chatService.CancelSessionAsync(cancelParams);
            }
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

    private string GetActiveSessionCwdOrDefault()
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

    /// <summary>
    /// Ensures that a remote ACP session exists and is bound to the current local conversation.
    /// Remote sessions are created lazily to avoid unnecessary resource consumption
    /// until the user actually sends a message or a feature requires an active session.
    /// </summary>
    private async Task<string> EnsureRemoteSessionAsync(CancellationToken cancellationToken = default)
    {
        if (_chatService is not { IsConnected: true, IsInitialized: true })
        {
            throw new InvalidOperationException("Not connected to ACP agent.");
        }

        if (!IsSessionActive || string.IsNullOrWhiteSpace(CurrentSessionId))
        {
            throw new InvalidOperationException("No session selected.");
        }

        var binding = GetOrCreateConversationBinding(CurrentSessionId!);
        if (!string.IsNullOrWhiteSpace(binding.RemoteSessionId))
        {
            _currentRemoteSessionId = binding.RemoteSessionId;
            return binding.RemoteSessionId!;
        }

        var sessionParams = new SessionNewParams
        {
            Cwd = GetActiveSessionCwdOrDefault(),
            McpServers = new List<McpServer>()
        };

        SessionNewResponse response;
        try
        {
            response = await _chatService.CreateSessionAsync(sessionParams).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsAuthenticationRequiredError(ex))
        {
            var authenticated = await TryAuthenticateAsync(cancellationToken).ConfigureAwait(false);
            if (!authenticated)
            {
                throw new InvalidOperationException(AuthenticationHintMessage ?? "The agent requires authentication before it can respond.");
            }

            response = await _chatService.CreateSessionAsync(sessionParams).ConfigureAwait(false);
        }
        binding.RemoteSessionId = response.SessionId;
        binding.BoundProfileId = _preferences.LastSelectedServerId;
        binding.LastUpdatedAt = DateTime.UtcNow;
        _currentRemoteSessionId = response.SessionId;
        ScheduleConversationSave();

        // Apply agent-advertised capabilities (modes/config) on the UI thread so the rest of the page can update.
        var activeConversationId = binding.ConversationId;
        _syncContext.Post(_ =>
        {
            if (string.Equals(CurrentSessionId, activeConversationId, StringComparison.Ordinal))
            {
                ApplySessionNewResponse(response);
            }
        }, null);

        return response.SessionId;
    }

    private void ClearRemoteSessionBindingForCurrentConversation()
    {
        if (string.IsNullOrWhiteSpace(CurrentSessionId))
        {
            _currentRemoteSessionId = null;
            return;
        }

        var binding = GetOrCreateConversationBinding(CurrentSessionId);
        binding.RemoteSessionId = null;
        binding.LastUpdatedAt = DateTime.UtcNow;
        _currentRemoteSessionId = null;
        ScheduleConversationSave();
    }

    private static bool IsRemoteSessionNotFound(Exception ex) =>
        ex is AcpException acp
        && (acp.ErrorCode == JsonRpcErrorCode.ResourceNotFound
            || (acp.Message.Contains("Session", StringComparison.OrdinalIgnoreCase)
                && acp.Message.Contains("not found", StringComparison.OrdinalIgnoreCase)));

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
        MessageHistory.Clear();
        PlanEntries.Clear();
        ShowPlanPanel = false;
        CurrentPlanTitle = null;

        var binding = TryGetConversationBinding(CurrentSessionId);
        if (binding != null)
        {
            binding.Transcript.Clear();
            binding.Plan.Clear();
            binding.ShowPlanPanel = false;
            binding.PlanTitle = null;
            binding.LastUpdatedAt = DateTime.UtcNow;
            ScheduleConversationSave();
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
            CurrentSessionId = null;
            _currentRemoteSessionId = null;
            IsSessionActive = false;
            MessageHistory.Clear();
            PlanEntries.Clear();
            CurrentPlanTitle = null;
            AvailableModes.Clear();
            SelectedMode = null;
            AgentName = null;
            AgentVersion = null;
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

           _conversationSaveCts?.Cancel();
           _sendPromptCts?.Cancel();
           _transientNotificationCts?.Cancel();
           StopStoreProjection();

            try
            {
                // Best-effort flush so the latest transcript survives restarts without blocking UI thread.
                _ = Task.Run(async () =>
                {
                   try { await SaveConversationsAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
               });
           }
           catch
           {
           }

            try { _conversationSaveCts?.Dispose(); } catch { }
            try { _sendPromptCts?.Dispose(); } catch { }
            try { _transientNotificationCts?.Dispose(); } catch { }

            _conversationSaveCts = null;
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
