using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentValidation;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SalmonEgg.Application.Validators;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Localization;
using SalmonEgg.Presentation.Core.Resources;
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
    IStringLocalizer<CoreStrings> localizer,
    ILogger<ConfigurationEditorViewModel> logger) : ViewModelBase(logger)
{
    private readonly IValidator<ServerConfiguration> _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    private readonly IConfigurationService _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
    private readonly ITransportSupportPolicy _transportSupportPolicy = transportSupportPolicy ?? throw new ArgumentNullException(nameof(transportSupportPolicy));
    private readonly IStringLocalizer<CoreStrings> _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _serverUrl = string.Empty;

    [ObservableProperty]
    private string _stdioCommand = string.Empty;

    [ObservableProperty]
    private string _stdioArgumentsText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStdio))]
    [NotifyPropertyChangedFor(nameof(IsRemote))]
    private TransportType _transport;

    public ObservableCollection<TransportOption> TransportOptions { get; } =
        CreateTransportOptions(transportSupportPolicy, localizer);

    [ObservableProperty]
    private TransportOption? _selectedTransportOption;

    public bool IsStdio => Transport == TransportType.Stdio;

    public bool IsRemote => Transport == TransportType.WebSocket || Transport == TransportType.StreamableHttp;

    public bool IsCustomProxy => ProxyMode == ProxyMode.Custom;

    public ObservableCollection<ProxyModeOption> ProxyModeOptions { get; } = CreateProxyModeOptions(localizer);

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSaveConfiguration))]
    [NotifyPropertyChangedFor(nameof(CanRetryProfileLoad))]
    [NotifyPropertyChangedFor(nameof(CanDismissError))]
    private bool _hasProfileLoadError;

    private string? _profileId;

    public bool CanSaveConfiguration => !IsBusy && !HasProfileLoadError;

    public bool CanRetryProfileLoad => HasProfileLoadError && !IsBusy && !string.IsNullOrWhiteSpace(_profileId);

    public bool CanDismissError => !HasProfileLoadError;

    public string RetryProfileLoadLabel => _localizer["AgentProfileEditor_RetryLoad"];

    public void LoadBlankConfiguration()
    {
        var defaultTransport = _transportSupportPolicy.DefaultTransport;
        _profileId = null;
        HasProfileLoadError = false;
        IsEditing = false;
        Configuration = new ServerConfiguration
        {
            Id = Guid.NewGuid().ToString(),
            Name = string.Empty,
            Transport = defaultTransport,
            ServerUrl = string.Empty,
            StdioCommand = string.Empty,
            StdioArguments = new(),
            ConnectionTimeout = AcpConnectionTimeoutPolicy.DefaultSeconds
        };

        Name = Configuration.Name;
        ServerUrl = Configuration.ServerUrl;
        StdioCommand = Configuration.StdioCommand;
        StdioArgumentsText = StdioCommandLine.FormatArgumentsText(Configuration.StdioArguments);
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

    protected override void OnIsBusyChangedCore(bool value)
    {
        OnPropertyChanged(nameof(CanSaveConfiguration));
        OnPropertyChanged(nameof(CanRetryProfileLoad));
    }

    public async Task LoadConfigurationAsync(string profileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        _profileId = profileId;
        IsBusy = true;

        try
        {
            var configuration = await _configurationService.LoadConfigurationAsync(profileId);
            if (configuration == null)
            {
                LoadBlankConfiguration();
                return;
            }

            LoadConfiguration(configuration);
            _profileId = profileId;
        }
        catch (ConfigurationPersistenceException ex)
        {
            Logger.LogError(ex, "Failed to load configuration: {Reason}", ex.Reason);
            LoadBlankConfiguration();
            _profileId = profileId;
            HasProfileLoadError = true;
            SetError(_localizer["AgentProfileEditor_LoadFailedFormat", ex.UserMessage]);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load configuration");
            LoadBlankConfiguration();
            _profileId = profileId;
            HasProfileLoadError = true;
            SetError(_localizer["AgentProfileEditor_LoadFailedFormat", ex.Message]);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RetryProfileLoadAsync()
    {
        if (!CanRetryProfileLoad)
        {
            return;
        }

        await LoadConfigurationAsync(_profileId!);
    }

    public void LoadConfiguration(ServerConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);

        _profileId = config.Id;
        HasProfileLoadError = false;
        ClearError();
        IsEditing = true;
        Configuration = config;
        var transport = ResolveSupportedTransportType(Configuration.Transport);
        Name = Configuration.Name;
        ServerUrl = Configuration.ServerUrl;
        StdioCommand = Configuration.StdioCommand;
        StdioArgumentsText = StdioCommandLine.FormatArgumentsText(Configuration.StdioArguments);
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
        _profileId = null;
        HasProfileLoadError = false;
        ClearError();
        IsEditing = false;
        Configuration = new ServerConfiguration
        {
            Id = Guid.NewGuid().ToString(),
            Name = ResolveNewConfigurationName(),
            ServerUrl = "ws://localhost:8080",
            Transport = _transportSupportPolicy.DefaultTransport,
            ConnectionTimeout = AcpConnectionTimeoutPolicy.DefaultSeconds
        };
        Name = Configuration.Name;
        ServerUrl = Configuration.ServerUrl;
        StdioCommand = Configuration.StdioCommand;
        StdioArgumentsText = StdioCommandLine.FormatArgumentsText(Configuration.StdioArguments);
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
        _profileId = null;
        HasProfileLoadError = false;
        ClearError();
        IsEditing = false;
        Configuration = new ServerConfiguration
        {
            Id = Guid.NewGuid().ToString(),
            Name = string.IsNullOrWhiteSpace(name) ? ResolveNewConfigurationName() : name.Trim(),
            Transport = transport,
            ServerUrl = transport == TransportType.Stdio ? string.Empty : (transportConfig.RemoteUrl ?? string.Empty),
            StdioCommand = transport == TransportType.Stdio ? (transportConfig.StdioCommand ?? string.Empty) : string.Empty,
            StdioArguments = transport == TransportType.Stdio ? transportConfig.StdioArguments.ToList() : new(),
            ConnectionTimeout = AcpConnectionTimeoutPolicy.DefaultSeconds
        };

        Name = Configuration.Name;
        ServerUrl = Configuration.ServerUrl;
        StdioCommand = Configuration.StdioCommand;
        StdioArgumentsText = StdioCommandLine.FormatArgumentsText(Configuration.StdioArguments);
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
        if (!CanSaveConfiguration)
        {
            return;
        }

        try
        {
            ClearError();

            Configuration.Name = Name;
            Configuration.Transport = Transport;

            if (Transport == TransportType.Stdio)
            {
                Configuration.ServerUrl = string.Empty;
                Configuration.StdioCommand = StdioCommand;
                Configuration.StdioArguments = StdioCommandLine.ParseArgumentsText(StdioArgumentsText).ToList();
            }
            else
            {
                Configuration.ServerUrl = ServerUrl;
                Configuration.StdioCommand = string.Empty;
                Configuration.StdioArguments = new();
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
                SetError(_localizer["AgentProfileEditor_ValidationFailedFormat", errors]);
                return;
            }

            await _configurationService.SaveConfigurationAsync(Configuration);
        }
        catch (ConfigurationPersistenceException ex)
        {
            Logger.LogError(ex, "Failed to save configuration: {Reason}", ex.Reason);
            SetError(_localizer["AgentProfileEditor_SaveFailedFormat", ex.UserMessage]);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to save configuration");
            SetError(_localizer["AgentProfileEditor_SaveFailedFormat", ex.Message]);
        }
    }

    private string ResolveNewConfigurationName()
    {
        const string fallback = "New Configuration";
        var localized = _localizer["AgentProfileEditor_NewConfigurationName"];
        return localized.ResourceNotFound || string.IsNullOrWhiteSpace(localized.Value)
            ? fallback
            : localized.Value;
    }

    [RelayCommand]
    public void Cancel()
    {
    }

    private static ObservableCollection<TransportOption> CreateTransportOptions(
        ITransportSupportPolicy transportSupportPolicy,
        IStringLocalizer<CoreStrings> localizer)
    {
        ArgumentNullException.ThrowIfNull(transportSupportPolicy);
        ArgumentNullException.ThrowIfNull(localizer);

        var options = new ObservableCollection<TransportOption>();
        if (transportSupportPolicy.IsSupported(TransportType.Stdio))
        {
            options.Add(new TransportOption(TransportType.Stdio, localizer[AcpTransportLocalization.StdioResourceKey]));
        }

        options.Add(new TransportOption(TransportType.WebSocket, localizer[AcpTransportLocalization.WebSocketResourceKey]));
        options.Add(new TransportOption(TransportType.StreamableHttp, localizer[AcpTransportLocalization.StreamableHttpResourceKey]));
        return options;
    }

    private static ObservableCollection<ProxyModeOption> CreateProxyModeOptions(
        IStringLocalizer<CoreStrings> localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        return new ObservableCollection<ProxyModeOption>
        {
            new(ProxyMode.System, localizer["AgentProfileEditor_ProxyModeSystem"]),
            new(ProxyMode.None, localizer["AgentProfileEditor_ProxyModeNone"]),
            new(ProxyMode.Custom, localizer["AgentProfileEditor_ProxyModeCustom"])
        };
    }

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
