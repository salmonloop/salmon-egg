using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentValidation;
using Microsoft.Extensions.Logging;
using SalmonEgg.Application.Validators;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.ViewModels.Chat;

namespace SalmonEgg.Presentation.ViewModels;

/// <summary>
/// 配置编辑器 ViewModel，用于添加/编辑服务器配置
/// Requirements: 4.1, 5.1, 5.3
/// </summary>
public partial class ConfigurationEditorViewModel(
    IValidator<ServerConfiguration> validator,
    IConfigurationService configurationService,
    ILogger<ConfigurationEditorViewModel> logger) : ViewModelBase(logger)
{
    private readonly IValidator<ServerConfiguration> _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    private readonly IConfigurationService _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _serverUrl = string.Empty;

    [ObservableProperty]
    private string _stdioCommand = string.Empty;

    [ObservableProperty]
    private string _stdioArgs = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStdio))]
    [NotifyPropertyChangedFor(nameof(IsRemote))]
    private TransportType _transport;

    public ObservableCollection<TransportOption> TransportOptions { get; } = new()
    {
        new TransportOption(TransportType.Stdio, "Stdio（子进程）"),
        new TransportOption(TransportType.WebSocket, "WebSocket"),
        new TransportOption(TransportType.HttpSse, "HTTP SSE"),
    };

    [ObservableProperty]
    private TransportOption? _selectedTransportOption;

    public bool IsStdio => Transport == TransportType.Stdio;

    public bool IsRemote => Transport == TransportType.WebSocket || Transport == TransportType.HttpSse;

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

    public void LoadBlankConfiguration()
    {
        IsEditing = false;
        Configuration = new ServerConfiguration
        {
            Id = Guid.NewGuid().ToString(),
            Name = string.Empty,
            Transport = TransportType.Stdio,
            ServerUrl = string.Empty,
            StdioCommand = string.Empty,
            StdioArgs = string.Empty,
            HeartbeatInterval = 30,
            ConnectionTimeout = 10
        };

        Name = Configuration.Name;
        ServerUrl = Configuration.ServerUrl;
        StdioCommand = Configuration.StdioCommand;
        StdioArgs = Configuration.StdioArgs;
        Transport = Configuration.Transport;
        SelectedTransportOption = TransportOptions.FirstOrDefault(o => o.Type == Transport) ?? TransportOptions.FirstOrDefault();
        Token = string.Empty;
        ApiKey = string.Empty;
        ProxyEnabled = false;
        ProxyUrl = string.Empty;
        HeartbeatInterval = Configuration.HeartbeatInterval;
        ConnectionTimeout = Configuration.ConnectionTimeout;
        ClearError();
    }

    partial void OnTransportChanged(TransportType value)
    {
        SelectedTransportOption = TransportOptions.FirstOrDefault(o => o.Type == value) ?? TransportOptions.FirstOrDefault();
    }

    partial void OnSelectedTransportOptionChanged(TransportOption? value)
    {
        if (value == null)
        {
            return;
        }

        Transport = value.Type;
    }

    public void LoadConfiguration(ServerConfiguration config)
    {
        IsEditing = true;
        Configuration = config ?? new ServerConfiguration();
        Name = Configuration.Name;
        ServerUrl = Configuration.ServerUrl;
        StdioCommand = Configuration.StdioCommand;
        StdioArgs = Configuration.StdioArgs;
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

        SelectedTransportOption = TransportOptions.FirstOrDefault(o => o.Type == Transport) ?? TransportOptions.FirstOrDefault();
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
        StdioCommand = Configuration.StdioCommand;
        StdioArgs = Configuration.StdioArgs;
        Transport = Configuration.Transport;
        SelectedTransportOption = TransportOptions.FirstOrDefault(o => o.Type == Transport) ?? TransportOptions.FirstOrDefault();
        Token = string.Empty;
        ApiKey = string.Empty;
        ProxyEnabled = false;
        ProxyUrl = string.Empty;
    }

    public void LoadNewFromTransportConfig(TransportConfigViewModel transportConfig, string? name = null)
    {
        if (transportConfig == null)
        {
            LoadNewConfiguration();
            return;
        }

        IsEditing = false;
        Configuration = new ServerConfiguration
        {
            Id = Guid.NewGuid().ToString(),
            Name = string.IsNullOrWhiteSpace(name) ? "New Configuration" : name.Trim(),
            Transport = transportConfig.SelectedTransportType,
            ServerUrl = transportConfig.SelectedTransportType == TransportType.Stdio ? string.Empty : (transportConfig.RemoteUrl ?? string.Empty),
            StdioCommand = transportConfig.SelectedTransportType == TransportType.Stdio ? (transportConfig.StdioCommand ?? string.Empty) : string.Empty,
            StdioArgs = transportConfig.SelectedTransportType == TransportType.Stdio ? (transportConfig.StdioArgs ?? string.Empty) : string.Empty,
            HeartbeatInterval = 30,
            ConnectionTimeout = 10
        };

        Name = Configuration.Name;
        ServerUrl = Configuration.ServerUrl;
        StdioCommand = Configuration.StdioCommand;
        StdioArgs = Configuration.StdioArgs;
        Transport = Configuration.Transport;
        SelectedTransportOption = TransportOptions.FirstOrDefault(o => o.Type == Transport) ?? TransportOptions.FirstOrDefault();
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
            Configuration.Transport = Transport;

            if (Transport == TransportType.Stdio)
            {
                Configuration.ServerUrl = string.Empty;
                Configuration.StdioCommand = StdioCommand;
                Configuration.StdioArgs = StdioArgs;
            }
            else
            {
                Configuration.ServerUrl = ServerUrl;
                Configuration.StdioCommand = string.Empty;
                Configuration.StdioArgs = string.Empty;
            }

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

            await _configurationService.SaveConfigurationAsync(Configuration);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "保存配置失败");
            SetError("保存配置失败：" + ex.Message);
        }
    }

    [RelayCommand]
    public void Cancel()
    {
    }
}

public sealed class TransportOption
{
    public TransportOption(TransportType type, string name)
    {
        Type = type;
        Name = name;
    }

    public TransportType Type { get; }

    public string Name { get; }
}
