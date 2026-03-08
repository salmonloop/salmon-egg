using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using UnoAcpClient.Application.Services.Chat;
using UnoAcpClient.Domain.Interfaces;
using UnoAcpClient.Domain.Interfaces.Transport;
using UnoAcpClient.Domain.Models;
using UnoAcpClient.Domain.Models.Content;
using UnoAcpClient.Domain.Models.Protocol;
using UnoAcpClient.Domain.Models.Session;
using UnoAcpClient.Domain.Services;

namespace UnoAcpClient.Presentation.ViewModels.Chat
{
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
                           Name = "UnoAcpClient",
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
                   var sessionParams = new UnoAcpClient.Domain.Models.Protocol.SessionNewParams
                   {
                       Cwd = Environment.CurrentDirectory
                   };
                    await _chatService.CreateSessionAsync(sessionParams);
                    Logger.LogInformation("会话创建成功，SessionId={SessionId}", _chatService.CurrentSessionId);

                    IsConnected = _chatService.IsConnected && _chatService.IsInitialized;
                    CurrentSessionId = _chatService.CurrentSessionId;
                    IsSessionActive = !string.IsNullOrWhiteSpace(CurrentSessionId);
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
                    if (e.Update is AgentMessageUpdate messageUpdate && messageUpdate.Content != null)
                    {
                        AddMessageToHistory(messageUpdate.Content, isOutgoing: false);
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
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "处理会话更新时出错");
                }
            }, null);
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
            if (configUpdate.ConfigOptions is System.Text.Json.JsonElement element && element.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                ConfigOptions.Clear();
                foreach (var item in element.EnumerateArray())
                {
                    if (item.TryGetProperty("id", out var idProp))
                    {
                        var id = idProp.GetString() ?? string.Empty;
                        ConfigOptions.Add(ConfigOptionViewModel.CreateFromJson(id, item));
                    }
                }
                ShowConfigOptionsPanel = ConfigOptions.Count > 0;
            }
            else if (configUpdate.ConfigOptions is System.Text.Json.JsonElement objElement && objElement.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                ConfigOptions.Clear();
                foreach (var prop in objElement.EnumerateObject())
                {
                    ConfigOptions.Add(ConfigOptionViewModel.CreateFromJson(prop.Name, prop.Value));
                }
                ShowConfigOptionsPanel = ConfigOptions.Count > 0;
            }
            else if (configUpdate.ConfigOptions is System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<string, object>> dict)
            {
                ConfigOptions.Clear();
                foreach (var kvp in dict)
                {
                    ConfigOptions.Add(ConfigOptionViewModel.CreateFromJson(kvp.Key, kvp.Value));
                }
                ShowConfigOptionsPanel = ConfigOptions.Count > 0;
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
                       Name = "UnoAcpClient",
                       Title = "Uno ACP Client",
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

                // 加载可用模式
                if (response.Modes != null)
                {
                    AvailableModes.Clear();
                    foreach (var mode in response.Modes)
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
                        SelectedMode = AvailableModes[0];
                    }
                }


                // 加载配置选项
                if (response.ConfigOptions is System.Text.Json.JsonElement element && element.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    ConfigOptions.Clear();
                    foreach (var item in element.EnumerateArray())
                    {
                        if (item.TryGetProperty("id", out var idProp))
                        {
                            var id = idProp.GetString() ?? string.Empty;
                            ConfigOptions.Add(ConfigOptionViewModel.CreateFromJson(id, item));
                        }
                    }
                    ShowConfigOptionsPanel = ConfigOptions.Count > 0;
                }
                else if (response.ConfigOptions is System.Text.Json.JsonElement objElement && objElement.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    ConfigOptions.Clear();
                    foreach (var prop in objElement.EnumerateObject())
                    {
                        ConfigOptions.Add(ConfigOptionViewModel.CreateFromJson(prop.Name, prop.Value));
                    }
                    ShowConfigOptionsPanel = ConfigOptions.Count > 0;
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

            try
            {
                IsBusy = true;
                ClearError();

                // 添加用户消息到历史
                var userContent = new TextContentBlock { Text = CurrentPrompt };
                AddMessageToHistory(userContent, isOutgoing: true);


                var promptParams = new SessionPromptParams
                {
                    SessionId = CurrentSessionId!,
                    Prompt = new[] { new { type = "text", text = CurrentPrompt } },
                    MaxTokens = null,
                    StopSequences = null
                };


                if (_chatService != null)
                {
                    await _chatService.SendPromptAsync(promptParams);
                }
                CurrentPrompt = string.Empty;
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

                var modeParams = new SessionSetModeParams
                {
                    SessionId = CurrentSessionId!,
                    ModeId = mode.ModeId
                };

                if (_chatService != null)
                {
                    await _chatService.SetSessionModeAsync(modeParams);
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
}
