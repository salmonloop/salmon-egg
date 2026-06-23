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
    ITransportSupportPolicy transportSupportPolicy,
    ILogger<ConfigurationEditorViewModel> logger) : ViewModelBase(logger)
{
    private readonly IValidator<ServerConfiguration> _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    private readonly IConfigurationService _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
    private readonly ITransportSupportPolicy _transportSupportPolicy = transportSupportPolicy ?? throw new ArgumentNullException(nameof(transportSupportPolicy));

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

    public ObservableCollection<TransportOption> TransportOptions { get; } =
        CreateTransportOptions(transportSupportPolicy);

    [ObservableProperty]
    private TransportOption? _selectedTransportOption;

    public bool IsStdio => Transport == TransportType.Stdio;

    public bool IsRemote => Transport == TransportType.WebSocket || Transport == TransportType.HttpSse;

    public bool IsCustomProxy => ProxyMode == ProxyMode.Custom;

    public ObservableCollection<ProxyModeOption> ProxyModeOptions { get; } = CreateProxyModeOptions();

    [ObservableProperty]
    private string _token = string.Empty;

    [ObservableProperty]
    private string _apiKey = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomProxy))]
    private ProxyMode _proxyMode = ProxyConfig.DefaultMode;

    [ObservableProperty]
    private string _proxyUrl = string.Empty;

    [ObservableProperty]
    private ProxyModeOption? _selectedProxyModeOption;

    [ObservableProperty]
    private int _connectionTimeout = AcpConnectionTimeoutPolicy.DefaultSeconds;

    public int ConnectionTimeoutMinimum => AcpConnectionTimeoutPolicy.MinimumSeconds;

    public int ConnectionTimeoutMaximum => AcpConnectionTimeoutPolicy.MaximumSeconds;

    public bool IsEditing { get; private set; }
    public ServerConfiguration Configuration { get; private set; } = new();

    public void LoadBlankConfiguration()
    {
        var defaultTransport = _transportSupportPolicy.DefaultTransport;
        IsEditing = false;
        Configuration = new ServerConfiguration
        {
            Id = Guid.NewGuid().ToString(),
            Name = string.Empty,
            Transport = defaultTransport,
            ServerUrl = string.Empty,
            StdioCommand = string.Empty,
            StdioArgs = string.Empty,
            ConnectionTimeout = AcpConnectionTimeoutPolicy.DefaultSeconds
        };

        Name = Configuration.Name;
        ServerUrl = Configuration.ServerUrl;
        StdioCommand = Configuration.StdioCommand;
        StdioArgs = Configuration.StdioArgs;
        Transport = Configuration.Transport;
        SelectedTransportOption = TransportOptions.FirstOrDefault(o => o.Type == Transport) ?? TransportOptions.FirstOrDefault();
        Token = string.Empty;
        ApiKey = string.Empty;
        ProxyMode = ProxyConfig.DefaultMode;
        ProxyUrl = string.Empty;
        SelectedProxyModeOption = ProxyModeOptions.FirstOrDefault(o => o.Mode == ProxyConfig.DefaultMode) ?? ProxyModeOptions.FirstOrDefault();
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

    partial void OnProxyModeChanged(ProxyMode value)
    {
        SelectedProxyModeOption = ProxyModeOptions.FirstOrDefault(o => o.Mode == value) ?? ProxyModeOptions.FirstOrDefault();
        if (value != ProxyMode.Custom)
        {
            ProxyUrl = string.Empty;
        }
    }

    partial void OnSelectedProxyModeOptionChanged(ProxyModeOption? value)
    {
        if (value == null)
        {
            return;
        }

        ProxyMode = value.Mode;
    }

    public void LoadConfiguration(ServerConfiguration config)
    {
        IsEditing = true;
        Configuration = config ?? new ServerConfiguration();
        var transport = ResolveSupportedTransportType(Configuration.Transport);
        Name = Configuration.Name;
        ServerUrl = Configuration.ServerUrl;
        StdioCommand = Configuration.StdioCommand;
        StdioArgs = Configuration.StdioArgs;
        Transport = transport;
        Token = Configuration.Authentication?.Token ?? string.Empty;
        ApiKey = Configuration.Authentication?.ApiKey ?? string.Empty;
        ConnectionTimeout = Configuration.ConnectionTimeout;

        if (Configuration.Proxy != null)
        {
            ProxyMode = Configuration.Proxy.Mode;
            ProxyUrl = ProxyMode == ProxyMode.Custom
                ? Configuration.Proxy.ProxyUrl ?? string.Empty
                : string.Empty;
        }
        else
        {
            ProxyMode = ProxyConfig.DefaultMode;
            ProxyUrl = string.Empty;
        }

        SelectedTransportOption = TransportOptions.FirstOrDefault(o => o.Type == Transport) ?? TransportOptions.FirstOrDefault();
        SelectedProxyModeOption = ProxyModeOptions.FirstOrDefault(o => o.Mode == ProxyMode) ?? ProxyModeOptions.FirstOrDefault();
    }

    public void LoadNewConfiguration()
    {
        IsEditing = false;
        Configuration = new ServerConfiguration
        {
            Id = Guid.NewGuid().ToString(),
            Name = "New Configuration",
            ServerUrl = "ws://localhost:8080",
            Transport = _transportSupportPolicy.DefaultTransport,
            ConnectionTimeout = AcpConnectionTimeoutPolicy.DefaultSeconds
        };
        Name = Configuration.Name;
        ServerUrl = Configuration.ServerUrl;
        StdioCommand = Configuration.StdioCommand;
        StdioArgs = Configuration.StdioArgs;
        Transport = Configuration.Transport;
        SelectedTransportOption = TransportOptions.FirstOrDefault(o => o.Type == Transport) ?? TransportOptions.FirstOrDefault();
        Token = string.Empty;
        ApiKey = string.Empty;
        ProxyMode = ProxyConfig.DefaultMode;
        ProxyUrl = string.Empty;
        SelectedProxyModeOption = ProxyModeOptions.FirstOrDefault(o => o.Mode == ProxyConfig.DefaultMode) ?? ProxyModeOptions.FirstOrDefault();
    }

    public void LoadNewFromTransportConfig(TransportConfigViewModel transportConfig, string? name = null)
    {
        if (transportConfig == null)
        {
            LoadNewConfiguration();
            return;
        }

        var transport = ResolveSupportedTransportType(transportConfig.SelectedTransportType);
        IsEditing = false;
        Configuration = new ServerConfiguration
        {
            Id = Guid.NewGuid().ToString(),
            Name = string.IsNullOrWhiteSpace(name) ? "New Configuration" : name.Trim(),
            Transport = transport,
            ServerUrl = transport == TransportType.Stdio ? string.Empty : (transportConfig.RemoteUrl ?? string.Empty),
            StdioCommand = transport == TransportType.Stdio ? (transportConfig.StdioCommand ?? string.Empty) : string.Empty,
            StdioArgs = transport == TransportType.Stdio ? (transportConfig.StdioArgs ?? string.Empty) : string.Empty,
            ConnectionTimeout = AcpConnectionTimeoutPolicy.DefaultSeconds
        };

        Name = Configuration.Name;
        ServerUrl = Configuration.ServerUrl;
        StdioCommand = Configuration.StdioCommand;
        StdioArgs = Configuration.StdioArgs;
        Transport = Configuration.Transport;
        SelectedTransportOption = TransportOptions.FirstOrDefault(o => o.Type == Transport) ?? TransportOptions.FirstOrDefault();
        Token = string.Empty;
        ApiKey = string.Empty;
        ProxyMode = ProxyConfig.DefaultMode;
        ProxyUrl = string.Empty;
        SelectedProxyModeOption = ProxyModeOptions.FirstOrDefault(o => o.Mode == ProxyConfig.DefaultMode) ?? ProxyModeOptions.FirstOrDefault();
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

            Configuration.ConnectionTimeout = ConnectionTimeout;

            if (!string.IsNullOrEmpty(Token) || !string.IsNullOrEmpty(ApiKey))
            {
                Configuration.Authentication = new AuthenticationConfig
                {
                    Token = Token,
                    ApiKey = ApiKey
                };
            }

            Configuration.Proxy = new ProxyConfig
            {
                Mode = ProxyMode,
                ProxyUrl = ProxyMode == ProxyMode.Custom ? ProxyUrl : string.Empty
            };

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

    private static ObservableCollection<TransportOption> CreateTransportOptions(ITransportSupportPolicy transportSupportPolicy)
    {
        ArgumentNullException.ThrowIfNull(transportSupportPolicy);

        var options = new ObservableCollection<TransportOption>();
        if (transportSupportPolicy.IsSupported(TransportType.Stdio))
        {
            options.Add(new TransportOption(TransportType.Stdio, "Stdio（子进程）"));
        }

        options.Add(new TransportOption(TransportType.WebSocket, "WebSocket"));
        options.Add(new TransportOption(TransportType.HttpSse, "HTTP SSE"));
        return options;
    }

    private static ObservableCollection<ProxyModeOption> CreateProxyModeOptions()
        => new()
        {
            new ProxyModeOption(ProxyMode.System, "使用系统代理"),
            new ProxyModeOption(ProxyMode.None, "不使用代理"),
            new ProxyModeOption(ProxyMode.Custom, "自定义代理")
        };

    private TransportType ResolveDefaultTransportType()
        => _transportSupportPolicy.DefaultTransport;

    private TransportType ResolveSupportedTransportType(TransportType transport)
        => _transportSupportPolicy.Coerce(transport);
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

public sealed class ProxyModeOption
{
    public ProxyModeOption(ProxyMode mode, string name)
    {
        Mode = mode;
        Name = name;
    }

    public ProxyMode Mode { get; }

    public string Name { get; }
}
