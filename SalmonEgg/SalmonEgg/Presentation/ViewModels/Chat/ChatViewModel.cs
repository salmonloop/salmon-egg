using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
using SalmonEgg.Presentation.ViewModels.Settings;

namespace SalmonEgg.Presentation.ViewModels.Chat;

/// <summary>
/// Chat ViewModel，管理会话、消息显示、权限请求等 UI 逻辑
/// 这是重构后的主要 ViewModel，使用新的 ACP 协议 API
/// </summary>
public partial class ChatViewModel : ViewModelBase, IDisposable
{
    private readonly ChatServiceFactory _chatServiceFactory;
    private readonly IConfigurationService _configurationService;
    private readonly AppPreferencesViewModel _preferences;
    private readonly AcpProfilesViewModel _acpProfiles;
    private readonly ISessionManager _sessionManager;
    private readonly IConversationStore _conversationStore;
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
    private string? _currentRemoteSessionId;
    private ChatMessageViewModel? _activeAgentTextStreamMessage;
    private IReadOnlyList<AuthMethodDefinition>? _advertisedAuthMethods;

    // Local conversation binding: ConversationId (stable for sidebar/UI) -> active remote session id (per ACP) + transcript.
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
    }

    [ObservableProperty]
    private ObservableCollection<ChatMessageViewModel> _messageHistory = new();

    [ObservableProperty]
    private string _currentPrompt = string.Empty;

    [ObservableProperty]
    private string? _currentSessionId;

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

    // 传输配置
    [ObservableProperty]
    private TransportConfigViewModel _transportConfig = new();

    [ObservableProperty]
    private bool _showTransportConfigPanel = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasConnectionError))]
    private string? _connectionErrorMessage;

    public bool HasConnectionError => !string.IsNullOrWhiteSpace(ConnectionErrorMessage);

    public bool IsInputEnabled => !IsBusy && !IsPromptInFlight;

    // WinUI/Uno sometimes won't reflect ICommand.CanExecute into IsEnabled consistently across targets,
    // so expose a stable property for UI bindings.
    public bool CanSendPromptUi => CanSendPrompt();

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

    // 配置选项
    [ObservableProperty]
    private ObservableCollection<ConfigOptionViewModel> _configOptions = new();

    [ObservableProperty]
    private bool _showConfigOptionsPanel;

    private string? _modeConfigId;

    // Slash commands
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

    [ObservableProperty]
    private ObservableCollection<PlanEntryViewModel> _currentPlan = new();

    [ObservableProperty]
    private bool _showPlanPanel;

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

    public ChatViewModel(
        ChatServiceFactory chatServiceFactory,
        IConfigurationService configurationService,
        AppPreferencesViewModel preferences,
        AcpProfilesViewModel acpProfiles,
        ISessionManager sessionManager,
        IConversationStore conversationStore,
        ILogger<ChatViewModel> logger)
        : base(logger)
    {
        _chatServiceFactory = chatServiceFactory ?? throw new ArgumentNullException(nameof(chatServiceFactory));
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _acpProfiles = acpProfiles ?? throw new ArgumentNullException(nameof(acpProfiles));
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _conversationStore = conversationStore ?? throw new ArgumentNullException(nameof(conversationStore));
        _syncContext = SynchronizationContext.Current ?? new SynchronizationContext();

        // 创建默认 ChatService 实例
        // 延迟创建 ChatService，等待用户配置
    // _chatService = _chatServiceFactory.CreateDefaultChatService();

        // 订阅事件
        SubscribeToEvents();
        _acpProfiles.PropertyChanged += OnAcpProfilesPropertyChanged;
        _preferences.PropertyChanged += OnPreferencesPropertyChanged;

        // Start restoring local conversation list immediately so the sidebar can show it ASAP.
        _ = RestoreConversationsAsync();
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

    partial void OnSelectedAcpProfileChanged(ServerConfiguration? value)
    {
        if (_suppressAcpProfileConnect || value == null)
        {
            return;
        }

        // Fire-and-forget; errors are surfaced via the existing ConnectionErrorMessage/Logger paths.
        _ = ConnectToAcpProfileCommand.ExecuteAsync(value);
    }

    partial void OnCurrentSessionIdChanged(string? value)
    {
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
        }

        if (IsEditingSessionName)
        {
            IsEditingSessionName = false;
            EditingSessionName = string.Empty;
        }

        // Persist "last active" selection.
        ScheduleConversationSave();
    }

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
        // Restore transcript (do not pull from the remote session; conversations are local).
        _activeAgentTextStreamMessage = null;
        MessageHistory.Clear();
        foreach (var msg in binding.Transcript)
        {
            MessageHistory.Add(msg);
        }

        CurrentPlan.Clear();
        foreach (var entry in binding.Plan)
        {
            CurrentPlan.Add(entry);
        }
        ShowPlanPanel = binding.ShowPlanPanel;
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

                    if (_sessionManager.GetSession(convo.ConversationId) == null)
                    {
                        try { _sessionManager.CreateSessionAsync(convo.ConversationId).GetAwaiter().GetResult(); } catch { }
                    }
                    _sessionManager.UpdateSession(convo.ConversationId, s => s.DisplayName = displayName);

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
        }, null);
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
            var record = new ConversationRecord
            {
                ConversationId = binding.ConversationId,
                DisplayName = name,
                CreatedAt = binding.CreatedAt,
                LastUpdatedAt = binding.LastUpdatedAt
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

        _sessionManager.UpdateSession(conversationId, s => s.DisplayName = finalName);

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

    public void DeleteConversation(string conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return;
        }

        _conversationBindings.Remove(conversationId);
        _sessionManager.RemoveSession(conversationId);

        if (string.Equals(CurrentSessionId, conversationId, StringComparison.Ordinal))
        {
            CurrentSessionId = null;
            _currentRemoteSessionId = null;
            IsSessionActive = false;
            MessageHistory.Clear();
            CurrentPlan.Clear();
            ShowPlanPanel = false;
        }

        ScheduleConversationSave();
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
               Logger.LogInformation("应用传输配置：{TransportType}", TransportConfig.SelectedTransportType);
               Logger.LogInformation("TransportConfig 当前值 - StdioCommand: '{StdioCommand}', StdioArgs: '{StdioArgs}', RemoteUrl: '{RemoteUrl}'",
                   TransportConfig.StdioCommand, TransportConfig.StdioArgs, TransportConfig.RemoteUrl);

               // 使用 ChatServiceFactory 根据用户配置重新创建 ChatService
               // 1. 根据传输类型创建新的 ChatService 实例
               IChatService newChatService;
               switch (TransportConfig.SelectedTransportType)
               {
                   case TransportType.Stdio:
                       Logger.LogInformation("Stdio 配置 - 命令：{Command}, 参数：{Args}", TransportConfig.StdioCommand, TransportConfig.StdioArgs);
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
                       throw new InvalidOperationException($"不支持的传输类型：{TransportConfig.SelectedTransportType}");
               }

               // Best-effort: disconnect previous transport to avoid leaks (do not reset local conversation state).
               if (_chatService != null)
               {
                   try
                   {
                       await _chatService.DisconnectAsync();
                   }
                   catch
                   {
                   }
               }

               // 2. 先取消订阅旧服务的事件（如果存在）
               if (_chatService != null)
               {
                   UnsubscribeFromChatService(_chatService);
               }

               // 3. 订阅新服务的事件
               SubscribeToChatService(newChatService);

               // 4. 替换旧的 ChatService 实例
               _chatService = newChatService;

               // 4. 初始化 ACP 协议 (带超时)
               Logger.LogInformation("正在初始化 ACP 协议...");
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

               // 使用 Task.WhenAny 实现超时，避免 UI 卡死
               var initTask = _chatService.InitializeAsync(initParams);
               var timeoutTask = Task.Delay(TimeSpan.FromSeconds(30));
               var completedTask = await Task.WhenAny(initTask, timeoutTask);

               if (completedTask == timeoutTask)
               {
                   throw new TimeoutException("初始化超时：Agent 未在规定时间内响应。请检查命令和参数是否正确。");
               }

               var initResponse = await initTask;
               UpdateAgentInfo();
               Logger.LogInformation("ACP 协议初始化完成，Agent: {Name} v{Version}", AgentName, AgentVersion);

               IsConnected = _chatService.IsConnected && _chatService.IsInitialized;
               CacheAuthMethods(initResponse);
               ClearAuthenticationRequirement();

               if (string.IsNullOrWhiteSpace(CurrentSessionId))
               {
                   CurrentSessionId = Guid.NewGuid().ToString();
               }

               IsSessionActive = !string.IsNullOrWhiteSpace(CurrentSessionId);

               // 初始化成功后，自动创建新会话（ACP 标准流程）
               // 参考：https://agentclientprotocol.com/protocol/session-setup
               Logger.LogInformation("正在创建新会话...");
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
                Logger.LogInformation("会话创建成功，SessionId={SessionId}", response.SessionId);

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
                Logger.LogInformation("连接成功");
            }
            catch (Exception ex)
            {
                ConnectionErrorMessage = $"连接失败：{ex.Message}";
                Logger.LogError(ex, "连接时出错");
                IsConnected = false;
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

           // 监听初始化状态变化
           if (chatService.IsInitialized)
           {
               UpdateAgentInfo();
           }

           // 监听会话状态
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
          // 只有当 _chatService 不为 null 时才订阅事件
          // 在构造函数中 _chatService 可能为 null，将在 ApplyTransportConfigAsync 中创建
          if (_chatService != null)
          {
              SubscribeToChatService(_chatService);

              // 监听初始化状态变化
              if (_chatService.IsInitialized)
              {
                  UpdateAgentInfo();
              }

              // 监听会话状态
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

                if (!string.IsNullOrWhiteSpace(_currentRemoteSessionId) &&
                    !string.Equals(e.SessionId, _currentRemoteSessionId, StringComparison.Ordinal))
                {
                    // Only render updates for the active remote session bound to this conversation.
                    return;
                }

                if (e.Update is AgentMessageUpdate messageUpdate && messageUpdate.Content != null)
                {
                    IsThinking = false;
                    HandleAgentContentChunk(messageUpdate.Content);
                }
                else if (e.Update is AgentThoughtUpdate)
                {
                    // Agents may stream thought chunks. We don't render them, but can use them as a "thinking" signal.
                    IsThinking = true;
                }
                else if (e.Update is UserMessageUpdate userMessageUpdate && userMessageUpdate.Content != null)
                {
                    _activeAgentTextStreamMessage = null;
                    AddMessageToHistory(userMessageUpdate.Content, isOutgoing: true);
                }
                else if (e.Update is ToolCallUpdate toolCallUpdate)
                {
                    IsThinking = true;
                    _activeAgentTextStreamMessage = null;
                    AddToolCallToHistory(toolCallUpdate);
                }
                else if (e.Update is PlanUpdate planUpdate)
                {
                    _activeAgentTextStreamMessage = null;
                    UpdatePlan(planUpdate);
                }
                else if (e.Update is CurrentModeUpdate modeChange)
                {
                    _activeAgentTextStreamMessage = null;
                    OnModeChanged(modeChange);
                }
                else if (e.Update is ConfigUpdateUpdate configUpdate)
                {
                    _activeAgentTextStreamMessage = null;
                    UpdateConfigOptions(configUpdate);
                }
                else if (e.Update is AvailableCommandsUpdate commandsUpdate)
                {
                    _activeAgentTextStreamMessage = null;
                    UpdateSlashCommands(commandsUpdate);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理会话更新时出错");
            }
        }, null);
    }

    private void HandleAgentContentChunk(ContentBlock content)
    {
        // ACP streams assistant responses via session/update (agent_message_chunk). Coalesce text chunks into a
        // single chat bubble to match protocol intent and common client behavior.
        if (content is TextContentBlock text)
        {
            AppendAgentTextChunk(text.Text ?? string.Empty);
            return;
        }

        _activeAgentTextStreamMessage = null;
        AddMessageToHistory(content, isOutgoing: false);
    }

    private void AppendAgentTextChunk(string chunk)
    {
        if (string.IsNullOrEmpty(chunk))
        {
            return;
        }

        if (_activeAgentTextStreamMessage == null ||
            _activeAgentTextStreamMessage.IsOutgoing ||
            !string.Equals(_activeAgentTextStreamMessage.ContentType, "text", StringComparison.Ordinal))
        {
            var id = Guid.NewGuid().ToString();
            _activeAgentTextStreamMessage = ChatMessageViewModel.CreateFromTextContent(
                id,
                new TextContentBlock { Text = chunk },
                isOutgoing: false);
            AppendToTranscript(_activeAgentTextStreamMessage);
            return;
        }

        _activeAgentTextStreamMessage.TextContent += chunk;

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
            _syncContext.Post(_ =>
            {
                CurrentSessionId = sessionId;
                IsSessionActive = true;
            }, null);

            var binding = GetOrCreateConversationBinding(sessionId);
            binding.RemoteSessionId ??= sessionId;
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

            _syncContext.Post(_ =>
            {
                RestoreConversation(binding);
            }, null);

            return true;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Switching session failed (SessionId={SessionId})", sessionId);

            _syncContext.Post(_ =>
            {
                ConnectionErrorMessage = $"切换会话失败：{ex.Message}";
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

                // 设置响应回调
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
                Logger.LogError(ex, "处理权限请求时出错");
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

                // 设置响应回调
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
                Logger.LogError(ex, "处理文件系统请求时出错");
            }
        }, null);
    }

    private void OnTerminalRequestReceived(object? sender, TerminalRequestEventArgs e)
    {
        _syncContext.Post(_ =>
        {
            try
            {
                Logger.LogInformation("收到终端请求: Method={Method}, TerminalId={TerminalId}", e.Method, e.TerminalId);
                ShowTransientNotificationToast($"终端请求: {e.Method}");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理终端请求时出错");
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
            ?? "该 Agent 需要先完成登录/认证后才能正常回复。";

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
            MarkAuthenticationRequired(method, $"认证失败：{ex.Message}");
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

    private void AddMessageToHistory(ContentBlock content, bool isOutgoing)
    {
        var id = Guid.NewGuid().ToString();
        ChatMessageViewModel message;

        switch (content)
        {
            case TextContentBlock text:
                message = ChatMessageViewModel.CreateFromTextContent(id, content, isOutgoing);
                break;
            case ImageContentBlock image:
                message = ChatMessageViewModel.CreateFromImageContent(id, content, isOutgoing);
                break;
            case AudioContentBlock audio:
                message = ChatMessageViewModel.CreateFromAudioContent(id, content, isOutgoing);
                break;
            case ResourceContentBlock resourceContent:
                message = ChatMessageViewModel.CreateFromResourceContent(id, resourceContent, isOutgoing);
                break;
            case ResourceLinkContentBlock resourceLink:
                message = ChatMessageViewModel.CreateFromResourceLink(id, resourceLink, isOutgoing);
                break;
            default:
                message = ChatMessageViewModel.CreateFromTextContent(id, content, isOutgoing);
                break;
        }

        AppendToTranscript(message);
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

    private void UpdatePlan(PlanUpdate planUpdate)
    {
        ShowPlanPanel = true;
        CurrentPlan.Clear();

        if (planUpdate.Entries != null)
        {
            foreach (var entry in planUpdate.Entries)
            {
                CurrentPlan.Add(new PlanEntryViewModel
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
            foreach (var item in CurrentPlan)
            {
                binding.Plan.Add(item);
            }
            binding.ShowPlanPanel = ShowPlanPanel;
            binding.LastUpdatedAt = DateTime.UtcNow;
            ScheduleConversationSave();
        }
    }

    private void OnModeChanged(CurrentModeUpdate modeChange)
    {
        if (!string.IsNullOrEmpty(modeChange.CurrentModeId))
        {
            // 更新当前模式
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

       // 如果还没有创建 ChatService，先应用配置
       if (_chatService == null)
       {
           Logger.LogInformation("ChatService 尚未创建，调用 ApplyTransportConfigAsync");
           await ApplyTransportConfigCommand.ExecuteAsync(null);
           return;
       }

       try
       {
           IsInitializing = true;
           ClearError();

           // 初始化 ACP 客户端
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
           Logger.LogError(ex, "初始化失败");
           SetError($"初始化失败：{ex.Message}");
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
                McpServers = new List<McpServer>() // 可以根据配置添加 MCP 服务器
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

            // 加载可用模式（deprecated in favor of configOptions）
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

                // 选择第一个模式作为默认
                if (AvailableModes.Count > 0)
                {
                    var currentModeId = response.Modes.CurrentModeId;
                    SelectedMode = string.IsNullOrWhiteSpace(currentModeId)
                        ? AvailableModes[0]
                        : AvailableModes.FirstOrDefault(m => m.ModeId == currentModeId) ?? AvailableModes[0];
                }
            }


            // 加载配置选项
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
            Logger.LogError(ex, "创建会话失败");
            SetError($"创建会话失败：{ex.Message}");
        }
        finally
        {
            IsConnecting = false;
        }
    }

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
                ShowTransientNotificationToast(AuthenticationHintMessage ?? "该 Agent 需要先完成登录/认证后才能回复。");
                return;
            }
        }

        var promptText = CurrentPrompt;

        try
        {
            ClearError();
            IsPromptInFlight = true;
            IsThinking = false;

            // Clear input immediately for better UX (agents may stream without returning a response for a while).
            // We'll restore it on failure so the user can retry.
            CurrentPrompt = string.Empty;

            // 添加用户消息到历史
            var userContent = new TextContentBlock { Text = promptText };
            AddMessageToHistory(userContent, isOutgoing: true);
            _activeAgentTextStreamMessage = null;

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
                    // The agent process might have restarted; create a new remote session and retry once.
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
            SetError("发送超时：Agent 长时间无响应。");

            if (string.IsNullOrWhiteSpace(CurrentPrompt))
            {
                CurrentPrompt = promptText;
            }

            ShowTransientNotificationToast("Agent 无响应（超时）。请检查 Agent 是否需要先登录/初始化，或稍后重试。");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "SendPrompt failed");
            SetError($"发送失败：{ex.Message}");

            // Restore text so the user can retry quickly.
            if (string.IsNullOrWhiteSpace(CurrentPrompt))
            {
                CurrentPrompt = promptText;
            }

            ShowTransientNotificationToast("发送失败，请稍后重试。");
        }
        finally
        {
            try { _sendPromptCts?.Dispose(); } catch { }
            _sendPromptCts = null;
            IsPromptInFlight = false;
            IsThinking = false;
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
            ShowTransientNotificationToast("取消失败。");
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

    private string? GetActiveSessionCwdOrDefault()
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
            return null;
        }
    }

    private async Task<string> EnsureRemoteSessionAsync(CancellationToken cancellationToken = default)
    {
        if (_chatService is not { IsConnected: true, IsInitialized: true })
        {
            throw new InvalidOperationException("尚未连接到 ACP Agent。");
        }

        if (!IsSessionActive || string.IsNullOrWhiteSpace(CurrentSessionId))
        {
            throw new InvalidOperationException("未选择会话。");
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
                throw new InvalidOperationException(AuthenticationHintMessage ?? "该 Agent 需要先完成登录/认证后才能回复。");
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
            Logger.LogError(ex, "切换模式失败");
            SetError($"切换模式失败：{ex.Message}");
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
            Logger.LogError(ex, "取消会话失败");
            SetError($"取消会话失败：{ex.Message}");
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
        CurrentPlan.Clear();
        ShowPlanPanel = false;

        var binding = TryGetConversationBinding(CurrentSessionId);
        if (binding != null)
        {
            binding.Transcript.Clear();
            binding.Plan.Clear();
            binding.ShowPlanPanel = false;
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
            CurrentPlan.Clear();
            AvailableModes.Clear();
            SelectedMode = null;
            AgentName = null;
            AgentVersion = null;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "断开连接失败");
            SetError($"断开连接失败：{ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnCurrentPromptChanged(string value)
    {
        SendPromptCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanSendPromptUi));
        RefreshSlashCommandFilter();
    }

    partial void OnIsPromptInFlightChanged(bool value)
    {
        SendPromptCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsInputEnabled));
        OnPropertyChanged(nameof(CanSendPromptUi));

        if (!value)
        {
            // End-of-turn: stop coalescing into the current assistant bubble.
            _activeAgentTextStreamMessage = null;
        }
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

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

       protected virtual void Dispose(bool disposing)
       {
           if (!_disposed)
           {
               if (disposing && _chatService != null)
               {
                   UnsubscribeFromChatService(_chatService);
               }

               if (disposing)
               {
                   _acpProfiles.PropertyChanged -= OnAcpProfilesPropertyChanged;
                   _preferences.PropertyChanged -= OnPreferencesPropertyChanged;

                   _conversationSaveCts?.Cancel();
                   _sendPromptCts?.Cancel();
                   _transientNotificationCts?.Cancel();
                   try
                   {
                       // Best-effort flush so the latest transcript survives restarts.
                       SaveConversationsAsync(CancellationToken.None).GetAwaiter().GetResult();
                   }
                   catch
                   {
                   }

                   try { _conversationSaveCts?.Dispose(); } catch { }
                   try { _sendPromptCts?.Dispose(); } catch { }
                   try { _transientNotificationCts?.Dispose(); } catch { }
               }

               _disposed = true;
           }
       }
}

/// <summary>
/// 会话模式 ViewModel
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
/// 权限请求 ViewModel
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
/// 权限选项 ViewModel
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
/// 文件系统请求 ViewModel
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
