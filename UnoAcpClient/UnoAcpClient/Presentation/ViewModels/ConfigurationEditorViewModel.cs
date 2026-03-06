using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentValidation;
using Microsoft.Extensions.Logging;
using UnoAcpClient.Application.Validators;
using UnoAcpClient.Domain.Models;

namespace UnoAcpClient.Presentation.ViewModels
{
    /// <summary>
    /// 配置编辑器 ViewModel，用于添加/编辑服务器配置
    /// Requirements: 4.1, 5.1, 5.3
    /// </summary>
    public partial class ConfigurationEditorViewModel : ViewModelBase
    {
        private readonly IValidator<ServerConfiguration> _validator;

        [ObservableProperty]
        private string _name = string.Empty;

        [ObservableProperty]
        private string _serverUrl = string.Empty;

        [ObservableProperty]
        private TransportType _transport;

        [ObservableProperty]
        private string _token = string.Empty;

        [ObservableProperty]
        private string _apiKey = string.Empty;

        [ObservableProperty]
        private bool _proxyEnabled;

        [ObservableProperty]
        private string _proxyUrl = string.Empty;

        [ObservableProperty]
        private int _heartbeatInterval = 30;

        [ObservableProperty]
        private int _connectionTimeout = 10;

        public bool IsEditing { get; private set; }
        public ServerConfiguration Configuration { get; private set; } = new();

        public ConfigurationEditorViewModel(
            IValidator<ServerConfiguration> validator,
            ILogger<ConfigurationEditorViewModel> logger) : base(logger)
        {
            _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        }

        public void LoadConfiguration(ServerConfiguration config)
        {
            IsEditing = true;
            Configuration = config ?? new ServerConfiguration();
            Name = Configuration.Name;
            ServerUrl = Configuration.ServerUrl;
            Transport = Configuration.Transport;
            Token = Configuration.Authentication?.Token ?? string.Empty;
            ApiKey = Configuration.Authentication?.ApiKey ?? string.Empty;
            HeartbeatInterval = Configuration.HeartbeatInterval;
            ConnectionTimeout = Configuration.ConnectionTimeout;

            if (Configuration.Proxy != null)
            {
                ProxyEnabled = Configuration.Proxy.Enabled;
                ProxyUrl = Configuration.Proxy.ProxyUrl ?? string.Empty;
            }
        }

        public void LoadNewConfiguration()
        {
            IsEditing = false;
            Configuration = new ServerConfiguration
            {
                Id = Guid.NewGuid().ToString(),
                Name = "New Configuration",
                ServerUrl = "ws://localhost:8080",
                Transport = TransportType.WebSocket,
                HeartbeatInterval = 30,
                ConnectionTimeout = 10
            };
            Name = Configuration.Name;
            ServerUrl = Configuration.ServerUrl;
            Transport = Configuration.Transport;
            Token = string.Empty;
            ApiKey = string.Empty;
            ProxyEnabled = false;
            ProxyUrl = string.Empty;
        }

        [RelayCommand]
        public async Task SaveConfigurationAsync()
        {
            try
            {
                ClearError();

                Configuration.Name = Name;
                Configuration.ServerUrl = ServerUrl;
                Configuration.Transport = Transport;
                Configuration.HeartbeatInterval = HeartbeatInterval;
                Configuration.ConnectionTimeout = ConnectionTimeout;

                if (!string.IsNullOrEmpty(Token) || !string.IsNullOrEmpty(ApiKey))
                {
                    Configuration.Authentication = new AuthenticationConfig
                    {
                        Token = Token,
                        ApiKey = ApiKey
                    };
                }

                if (ProxyEnabled)
                {
                    Configuration.Proxy = new ProxyConfig
                    {
                        Enabled = true,
                        ProxyUrl = ProxyUrl
                    };
                }

                var validationResult = await _validator.ValidateAsync(Configuration);
                if (!validationResult.IsValid)
                {
                    var errors = string.Join("; ", validationResult.Errors);
                    SetError("验证失败：" + errors);
                    return;
                }

                OnSaveRequested?.Invoke(this, Configuration);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "保存配置失败");
                SetError("保存配置失败：" + ex.Message);
            }
        }

        public event EventHandler<ServerConfiguration>? OnSaveRequested;
        public event EventHandler? OnCancelRequested;

        [RelayCommand]
        public void Cancel() => OnCancelRequested?.Invoke(this, EventArgs.Empty);
    }
}
