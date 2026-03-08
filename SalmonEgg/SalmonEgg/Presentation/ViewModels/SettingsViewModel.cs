using System.Threading;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SalmonEgg.Application.Services;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Presentation.ViewModels
{
    /// <summary>
    /// 设置页面 ViewModel，管理服务器配置列表和主题
    /// Requirements: 4.1, 4.7, 5.1, 5.3
    /// </summary>
    public partial class SettingsViewModel : ViewModelBase
    {
        private readonly IConfigurationService _configService;
        private readonly IAppSettingsService _appSettingsService;
        private readonly SynchronizationContext _syncContext;
        private CancellationTokenSource? _settingsSaveCts;
        private bool _suppressSettingsSave;

        [ObservableProperty]
        private ObservableCollection<ServerConfiguration> _configurations = new();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasSelectedConfiguration))]
        private ServerConfiguration? _selectedConfiguration;

        public bool HasSelectedConfiguration => SelectedConfiguration != null;

        [ObservableProperty]
        private string _theme = "System";

        [ObservableProperty]
        private bool _isAnimationEnabled = true;

        public SettingsViewModel(
            IConfigurationService configService,
            IAppSettingsService appSettingsService,
            ILogger<SettingsViewModel> logger) : base(logger)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _appSettingsService = appSettingsService ?? throw new ArgumentNullException(nameof(appSettingsService));
            _syncContext = SynchronizationContext.Current ?? new SynchronizationContext();
            _ = LoadAppSettingsAsync();
        }

        private async Task LoadAppSettingsAsync()
        {
            try
            {
                _suppressSettingsSave = true;
                var settings = await _appSettingsService.LoadAsync();
                Theme = settings.Theme;
                IsAnimationEnabled = settings.IsAnimationEnabled;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "加载 app.yaml 失败");
            }
            finally
            {
                _suppressSettingsSave = false;
            }
        }

        partial void OnThemeChanged(string value) => ScheduleSaveAppSettings();

        partial void OnIsAnimationEnabledChanged(bool value) => ScheduleSaveAppSettings();

        private void ScheduleSaveAppSettings()
        {
            if (_suppressSettingsSave)
            {
                return;
            }

            _settingsSaveCts?.Cancel();
            _settingsSaveCts?.Dispose();
            _settingsSaveCts = new CancellationTokenSource();
            var token = _settingsSaveCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), token);
                    await _appSettingsService.SaveAsync(new AppSettings
                    {
                        Theme = Theme,
                        IsAnimationEnabled = IsAnimationEnabled
                    });
                }
                catch (TaskCanceledException)
                {
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "保存 app.yaml 失败");
                }
            }, token);
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
        public async Task DeleteConfigurationAsync(ServerConfiguration? config)
        {
            if (config is not null)
            {
                SelectedConfiguration = config;
            }

            if (SelectedConfiguration == null)
            {
                return;
            }

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

    }
}
