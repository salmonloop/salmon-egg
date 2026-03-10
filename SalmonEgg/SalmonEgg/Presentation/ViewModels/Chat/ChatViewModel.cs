using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Interfaces;
using SalmonEgg.Domain.Interfaces.Transport;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.Content;
using SalmonEgg.Domain.Models.Protocol;
using SalmonEgg.Domain.Models.Session;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Presentation.ViewModels.Chat;

/// <summary>
/// Chat ViewModel，管理会话、消息显示、权限请求等 UI 逻辑
/// 这是重构后的主要 ViewModel，使用新的 ACP 协议 API
/// </summary>
public partial class ChatViewModel : ViewModelBase, IDisposable
{
    private readonly ChatServiceFactory _chatServiceFactory;
    private IChatService? _chatService;
    private readonly SynchronizationContext _syncContext;
    private bool _disposed;
    private readonly SemaphoreSlim _sessionSwitchGate = new(1, 1);
    private bool _suppressSessionUpdatesToUi;

    [ObservableProperty]
    private ObservableCollection<ChatMessageViewModel> _messageHistory = new();

    [ObservableProperty]
    private string _currentPrompt = string.Empty;

    [ObservableProperty]
    private string? _currentSessionId;

    [ObservableProperty]
    private bool _isSessionActive;

    [ObservableProperty]
    private string? _agentName;

    [ObservableProperty]
    private string? _agentVersion;

    [ObservableProperty]
    private bool _isInitializing;

    [ObservableProperty]
    private bool _isConnecting;

    // 传输配置
    [ObservableProperty]
    private TransportConfigViewModel _transportConfig = new();

    [ObservableProperty]
    private bool _showTransportConfigPanel = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasConnectionError))]
    private string? _connectionErrorMessage;

    public bool HasConnectionError => !string.IsNullOrWhiteSpace(ConnectionErrorMessage);

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private ObservableCollection<SessionModeViewModel> _availableModes = new();

    [ObservableProperty]
    private SessionModeViewModel? _selectedMode;

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

    public string? CurrentConnectionStatus { get; private set; }

    public ChatViewModel(
        ChatServiceFactory chatServiceFactory,
        ILogger<ChatViewModel> logger)
        : base(logger)
    {
        _chatServiceFactory = chatServiceFactory ?? throw new ArgumentNullException(nameof(chatServiceFactory));
        _syncContext = SynchronizationContext.Current ?? new SynchronizationContext();

        // 创建默认 ChatService 实例
        // 延迟创建 ChatService，等待用户配置
    // _chatService = _chatServiceFactory.CreateDefaultChatService();

        // 订阅事件
        SubscribeToEvents();
    }

    [RelayCommand]
        private async Task ApplyTransportConfigAsync()
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

               await initTask;
               UpdateAgentInfo();
               Logger.LogInformation("ACP 协议初始化完成，Agent: {Name} v{Version}", AgentName, AgentVersion);

               // 初始化成功后，自动创建新会话（ACP 标准流程）
               // 参考：https://agentclientprotocol.com/protocol/session-setup
               Logger.LogInformation("正在创建新会话...");
               var sessionParams = new SalmonEgg.Domain.Models.Protocol.SessionNewParams
               {
                   Cwd = Environment.CurrentDirectory
               };
                var response = await _chatService.CreateSessionAsync(sessionParams);
                Logger.LogInformation("会话创建成功，SessionId={SessionId}", response.SessionId);

                IsConnected = _chatService.IsConnected && _chatService.IsInitialized;
                CurrentSessionId = response.SessionId;
                IsSessionActive = !string.IsNullOrWhiteSpace(response.SessionId);

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

                if (IsSessionActive)
                {
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
                CurrentSessionId = null;
                IsSessionActive = false;
            }
           finally
           {
               IsConnecting = false;
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

                if (!string.IsNullOrWhiteSpace(CurrentSessionId) &&
                    !string.Equals(e.SessionId, CurrentSessionId, StringComparison.Ordinal))
                {
                    // Multi-session: only render updates for the active session.
                    return;
                }

                if (e.Update is AgentMessageUpdate messageUpdate && messageUpdate.Content != null)
                {
                    AddMessageToHistory(messageUpdate.Content, isOutgoing: false);
                }
                else if (e.Update is UserMessageUpdate userMessageUpdate && userMessageUpdate.Content != null)
                {
                    AddMessageToHistory(userMessageUpdate.Content, isOutgoing: true);
                }
                else if (e.Update is ToolCallUpdate toolCallUpdate)
                {
                    AddToolCallToHistory(toolCallUpdate);
                }
                else if (e.Update is PlanUpdate planUpdate)
                {
                    UpdatePlan(planUpdate);
                }
                else if (e.Update is ModeChangeUpdate modeChange)
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
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理会话更新时出错");
            }
        }, null);
    }

    public async Task<bool> TrySwitchToSessionAsync(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || _chatService == null || !_chatService.IsInitialized || !_chatService.IsConnected)
        {
            return false;
        }

        if (string.Equals(_chatService.CurrentSessionId, sessionId, StringComparison.Ordinal))
        {
            return true;
        }

        await _sessionSwitchGate.WaitAsync().ConfigureAwait(false);
        try
        {
            _suppressSessionUpdatesToUi = true;

            _syncContext.Post(_ =>
            {
                MessageHistory.Clear();
                CurrentPlan.Clear();
                ShowPlanPanel = false;
            }, null);

            await _chatService.LoadSessionAsync(new SalmonEgg.Domain.Models.Protocol.SessionLoadParams(
                sessionId,
                Environment.CurrentDirectory)).ConfigureAwait(false);

            _syncContext.Post(_ =>
            {
                CurrentSessionId = sessionId;
                IsSessionActive = true;
                LoadSessionHistory();
            }, null);

            return true;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Switching session failed (SessionId={SessionId})", sessionId);

            _syncContext.Post(_ =>
            {
                ConnectionErrorMessage = $"加载会话失败：{ex.Message}";
                // ChatService restores CurrentSessionId/history on failure; resync UI from it.
                CurrentSessionId = _chatService.CurrentSessionId;
                IsSessionActive = !string.IsNullOrWhiteSpace(CurrentSessionId);
                LoadSessionHistory();
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

    private void LoadSessionHistory()
    {
        MessageHistory.Clear();
        if (_chatService != null)
        {
            foreach (var entry in _chatService.SessionHistory)
            {
                AddEntryToMessageHistory(entry);
            }
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
                MessageHistory.Add(message);
            }
        }
        else if (!string.IsNullOrEmpty(entry.ModeId))
        {
            var message = ChatMessageViewModel.CreateFromModeChange(
                Guid.NewGuid().ToString(),
                entry.ModeId,
                entry.Title,
                isOutgoing: false);
            MessageHistory.Add(message);
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

        MessageHistory.Add(message);
    }

    private void AddToolCallToHistory(ToolCallUpdate toolCall)
    {
        var message = ChatMessageViewModel.CreateFromToolCall(
            Guid.NewGuid().ToString(),
            toolCall.ToolCallId,
            toolCall.ToolCall?.GetRawText(),
            toolCall.Kind,
            toolCall.Status,
            toolCall.Title,
            isOutgoing: false);
        MessageHistory.Add(message);
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
    }

    private void OnModeChanged(ModeChangeUpdate modeChange)
    {
        if (!string.IsNullOrEmpty(modeChange.ModeId))
        {
            // 更新当前模式
            var selectedMode = AvailableModes.FirstOrDefault(m => m.ModeId == modeChange.ModeId);
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

           var response = await _chatService.InitializeAsync(initParams);
           UpdateAgentInfo();
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
                Cwd = Environment.CurrentDirectory,
                McpServers = new object[0] // 可以根据配置添加 MCP 服务器
            };

            if (_chatService == null)
            {
                throw new InvalidOperationException("Chat service is not initialized");
            }

            var response = await _chatService.CreateSessionAsync(sessionParams);
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

        var promptText = CurrentPrompt;

        try
        {
            IsBusy = true;
            ClearError();

            // 添加用户消息到历史
            var userContent = new TextContentBlock { Text = promptText };
            AddMessageToHistory(userContent, isOutgoing: true);

            // Clear input immediately for better UX (even if the send is still in-flight).
            CurrentPrompt = string.Empty;

            var promptParams = new SessionPromptParams
            {
                SessionId = CurrentSessionId!,
                Prompt = new[] { new { type = "text", text = promptText } },
                MaxTokens = null,
                StopSequences = null
            };


            if (_chatService != null)
            {
                await _chatService.SendPromptAsync(promptParams);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "发送提示失败");
            SetError($"发送提示失败：{ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanSendPrompt() => IsSessionActive && !string.IsNullOrWhiteSpace(CurrentPrompt) && !IsBusy;

    [RelayCommand]
    private async Task SetModeAsync(SessionModeViewModel? mode)
    {
        if (mode == null || !IsSessionActive)
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
                        CurrentSessionId!,
                        _modeConfigId,
                        System.Text.Json.JsonSerializer.SerializeToElement(mode.ModeId));
                    await _chatService.SetSessionConfigOptionAsync(setParams);
                }
                else
                {
                    var modeParams = new SessionSetModeParams
                    {
                        SessionId = CurrentSessionId!,
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
        if (!IsSessionActive)
            return;

        try
        {
            IsBusy = true;
            ClearError();

            var cancelParams = new SessionCancelParams
            {
                SessionId = CurrentSessionId!,
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
        RefreshSlashCommandFilter();
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
