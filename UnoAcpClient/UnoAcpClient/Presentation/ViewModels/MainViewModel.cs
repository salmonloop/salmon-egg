using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using UnoAcpClient.Application.Services;
using UnoAcpClient.Domain.Models;
using UnoAcpClient.Domain.Services;

namespace UnoAcpClient.Presentation.ViewModels
{
    /// <summary>
    /// 主 ViewModel，管理连接、消息发送和历史记录
    /// Requirements: 4.1, 4.2, 4.3, 4.4, 4.5
    /// </summary>
    public partial class MainViewModel : ViewModelBase
    {
        private readonly IConnectionService _connectionService;
        private readonly IMessageService _messageService;
        private readonly IConfigurationService _configService;
        private readonly SynchronizationContext _syncContext;
        private IDisposable? _stateSubscription;
        private IDisposable? _notificationSubscription;

        [ObservableProperty]
        private ObservableCollection<ServerConfiguration> _servers = new();

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
        private ServerConfiguration? _selectedServer;

        [ObservableProperty]
        private ConnectionState _currentConnectionState = new() { Status = ConnectionStatus.Disconnected };

        [ObservableProperty]
        private ObservableCollection<MessageViewModel> _messageHistory = new();

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SendMessageCommand))]
        private string _method = string.Empty;

        [ObservableProperty]
        private string _parameters = string.Empty;

        public bool IsConnected => CurrentConnectionState?.Status == ConnectionStatus.Connected;

        public MainViewModel(
            IConnectionService connectionService,
            IMessageService messageService,
            IConfigurationService configService,
            ILogger<MainViewModel> logger) : base(logger)
        {
            _connectionService = connectionService ?? throw new ArgumentNullException(nameof(connectionService));
            _messageService = messageService ?? throw new ArgumentNullException(nameof(messageService));
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _syncContext = SynchronizationContext.Current ?? new SynchronizationContext();
            SubscribeToStateChanges();
        }

        private void SubscribeToStateChanges()
        {
            _stateSubscription = _connectionService.ConnectionStateChanges.Subscribe(state =>
            {
                _syncContext.Post(_ =>
                {
                    CurrentConnectionState = state;
                    OnPropertyChanged(nameof(IsConnected));
                    ConnectCommand.NotifyCanExecuteChanged();
                    DisconnectCommand.NotifyCanExecuteChanged();
                }, null);
            });

            _notificationSubscription = _messageService.Notifications.Subscribe(msg =>
            {
                _syncContext.Post(_ =>
                {
                    MessageHistory.Add(new MessageViewModel
                    {
                        Id = msg.Id,
                        Type = msg.Type ?? "notification",
                        Method = msg.Method ?? "notification",
                        Content = msg.Params?.ToString() ?? string.Empty,
                        Timestamp = msg.Timestamp,
                        IsOutgoing = false
                    });
                }, null);
            });
        }

        [RelayCommand]
        public async Task LoadServersAsync()
        {
            try
            {
                IsBusy = true;
                ClearError();
                var configs = await _configService.ListConfigurationsAsync();
                Servers.Clear();
                foreach (var cfg in configs)
                    Servers.Add(cfg);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "加载服务器配置失败");
                SetError("加载服务器配置失败：" + ex.Message);
            }
            finally { IsBusy = false; }
        }

        private bool CanConnect() => SelectedServer != null &&
            CurrentConnectionState?.Status != ConnectionStatus.Connected &&
            CurrentConnectionState?.Status != ConnectionStatus.Connecting;

        [RelayCommand(CanExecute = nameof(CanConnect))]
        public async Task ConnectAsync()
        {
            if (SelectedServer == null) return;
            try
            {
                IsBusy = true;
                ClearError();
                var result = await _connectionService.ConnectAsync(SelectedServer.Id);
                if (!result.IsSuccess)
                    SetError(result.Error ?? "连接失败");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "连接服务器失败");
                SetError("连接失败：" + ex.Message);
            }
            finally { IsBusy = false; }
        }

        private bool CanDisconnect() => CurrentConnectionState?.Status == ConnectionStatus.Connected
            || CurrentConnectionState?.Status == ConnectionStatus.Reconnecting;

        [RelayCommand(CanExecute = nameof(CanDisconnect))]
        public async Task DisconnectAsync()
        {
            try
            {
                IsBusy = true;
                ClearError();
                await _connectionService.DisconnectAsync();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "断开连接失败");
                SetError("断开连接失败：" + ex.Message);
            }
            finally { IsBusy = false; }
        }

        private bool CanSendMessage() => !string.IsNullOrWhiteSpace(Method)
            && CurrentConnectionState?.Status == ConnectionStatus.Connected;

        [RelayCommand(CanExecute = nameof(CanSendMessage))]
        public async Task SendMessageAsync()
        {
            if (string.IsNullOrWhiteSpace(Method)) return;
            try
            {
                IsBusy = true;
                ClearError();
                MessageHistory.Add(new MessageViewModel
                {
                    Id = Guid.NewGuid().ToString(),
                    Type = "request",
                    Method = Method,
                    Content = Parameters,
                    Timestamp = DateTime.Now,
                    IsOutgoing = true
                });

                var result = await
 _messageService.SendRequestAsync(Method, Parameters);

                if (result.IsSuccess && result.Value != null)
                {
                    MessageHistory.Add(new MessageViewModel
                    {
                        Id = result.Value.Id,
                        Type = "response",
                        Method = Method,
                        Content = result.Value.Result?.ToString()
                            ?? result.Value.Error?.Message
                            ?? string.Empty,
                        Timestamp = result.Value.Timestamp,
                        IsOutgoing = false
                    });
                }
                else if (!result.IsSuccess)
                {
                    SetError(result.Error ?? "发送失败");
                }

                Method = string.Empty;
                Parameters = string.Empty;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "发送消息失败");
                SetError("发送消息失败：" + ex.Message);
            }
            finally { IsBusy = false; }
        }

        [RelayCommand]
        public void ClearHistory() => MessageHistory.Clear();

        public void Dispose()
        {
            _stateSubscription?.Dispose();
            _notificationSubscription?.Dispose();
        }
    }
}
