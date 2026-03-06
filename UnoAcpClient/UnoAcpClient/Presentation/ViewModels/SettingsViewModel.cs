using System.Threading;
using System;
using System.Collections.ObjectModel;
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
    /// 设置页面 ViewModel，管理服务器配置列表和主题
    /// Requirements: 4.1, 4.7, 5.1, 5.3
    /// </summary>
    public partial class SettingsViewModel : ViewModelBase
    {
        private readonly IConfigurationService _configService;
        private readonly SynchronizationContext _syncContext;

        [ObservableProperty]
        private ObservableCollection<ServerConfiguration> _configurations = new();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasSelectedConfiguration))]
        private ServerConfiguration? _selectedConfiguration;

        public bool HasSelectedConfiguration => SelectedConfiguration != null;

        [ObservableProperty]
        private string _theme = "System";

        public SettingsViewModel(
            IConfigurationService configService,
            ILogger<SettingsViewModel> logger) : base(logger)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _syncContext = SynchronizationContext.Current ?? new SynchronizationContext();
        }

        [RelayCommand]
        public async Task LoadConfigurationsAsync()
        {
            try
            {
                IsBusy = true;
                ClearError();
                var configs = await _configService.ListConfigurationsAsync();
                Configurations.Clear();
                foreach (var cfg in configs)
                    Configurations.Add(cfg);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "加载配置列表失败");
                SetError("加载配置列表失败：" + ex.Message);
            }
            finally { IsBusy = false; }
        }

        [RelayCommand]
        public void AddConfiguration()
        {
            var newConfig = new ServerConfiguration
            {
                Id = Guid.NewGuid().ToString(),
                Name = "New Configuration",
                ServerUrl = "ws://localhost:8080",
                Transport = TransportType.WebSocket,
                HeartbeatInterval = 30,
                ConnectionTimeout = 10
            };
            Configurations.Add(newConfig);
            SelectedConfiguration = newConfig;
            OnEditRequested?.Invoke(this, newConfig);
        }

        [RelayCommand]
        public void EditConfiguration(object? config)
        {
            if (config is ServerConfiguration selected)
            {
                SelectedConfiguration = selected;
                OnEditRequested?.Invoke(this, selected);
            }
        }

        public event EventHandler<ServerConfiguration>? OnEditRequested;

        [RelayCommand]
        public async Task DeleteConfigurationAsync()
        {
            if (SelectedConfiguration == null) return;
            try
            {
                IsBusy = true;
                ClearError();
                await _configService.DeleteConfigurationAsync(SelectedConfiguration.Id);
                Configurations.Remove(SelectedConfiguration);
                SelectedConfiguration = null;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "删除配置失败");
                SetError("删除配置失败：" + ex.Message);
            }
            finally { IsBusy = false; }
        }

        [RelayCommand]
        public void ToggleTheme()
        {
            Theme = Theme switch
            {
                "Light" => "Dark",
                "Dark" => "System",
                _ => "Light"
            };
        }

        public event EventHandler? OnSaveRequested;
        public event EventHandler? OnCancelRequested;
    }
}
