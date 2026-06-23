using System;
using System.ComponentModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using SalmonEgg.Domain.Models;
using SalmonEgg.Presentation.ViewModels.Chat;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public interface ISettingsAcpConnectionState : INotifyPropertyChanged
{
    string? AgentName { get; }

    string? AgentVersion { get; }

    bool IsConnecting { get; }

    bool IsInitializing { get; }

    bool IsConnected { get; }

    string? ConnectionErrorMessage { get; }

    bool HasConnectionError { get; }
}

public interface ISettingsAcpConnectionCommands
{
    IAsyncRelayCommand DisconnectCommand { get; }

    Task ConnectToAcpProfileAsync(ServerConfiguration profile);

    Task ConnectProfileAsync(ServerConfiguration profile);

    Task DisconnectProfileAsync(ServerConfiguration profile);

    Task ReconnectProfileAsync(ServerConfiguration profile);
}

public interface ISettingsAcpTransportConfiguration : IAcpTransportConfiguration, INotifyPropertyChanged
{
}

public interface ISettingsForegroundChatConnection : ISettingsAcpConnectionState
{
    TransportConfigViewModel TransportConfig { get; }

    IAsyncRelayCommand DisconnectCommand { get; }

    IAsyncRelayCommand<ServerConfiguration> ConnectToAcpProfileCommand { get; }

    string? ForegroundTransportProfileId { get; }
}

public interface ISettingsChatConnection : ISettingsAcpConnectionState, ISettingsAcpConnectionCommands
{
    ISettingsAcpTransportConfiguration TransportConfig { get; }
}

internal sealed class SettingsTransportConfigurationAdapter : ISettingsAcpTransportConfiguration
{
    private readonly TransportConfigViewModel _transportConfig;

    public SettingsTransportConfigurationAdapter(TransportConfigViewModel transportConfig)
    {
        _transportConfig = transportConfig ?? throw new ArgumentNullException(nameof(transportConfig));
    }

    public event PropertyChangedEventHandler? PropertyChanged
    {
        add => _transportConfig.PropertyChanged += value;
        remove => _transportConfig.PropertyChanged -= value;
    }

    public TransportType SelectedTransportType
    {
        get => _transportConfig.SelectedTransportType;
        set => _transportConfig.SelectedTransportType = value;
    }

    public string StdioCommand
    {
        get => _transportConfig.StdioCommand;
        set => _transportConfig.StdioCommand = value;
    }

    public string StdioArgs
    {
        get => _transportConfig.StdioArgs;
        set => _transportConfig.StdioArgs = value;
    }

    public string RemoteUrl
    {
        get => _transportConfig.RemoteUrl;
        set => _transportConfig.RemoteUrl = value;
    }

    public (bool IsValid, string? ErrorMessage) Validate() => _transportConfig.Validate();
}

internal sealed class CompositeSettingsChatConnection : ISettingsChatConnection
{
    private readonly ISettingsAcpConnectionState _state;
    private readonly ISettingsAcpConnectionCommands _commands;

    public CompositeSettingsChatConnection(
        ISettingsAcpConnectionState state,
        ISettingsAcpConnectionCommands commands,
        ISettingsAcpTransportConfiguration transportConfig)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        TransportConfig = transportConfig ?? throw new ArgumentNullException(nameof(transportConfig));
    }

    public event PropertyChangedEventHandler? PropertyChanged
    {
        add => _state.PropertyChanged += value;
        remove => _state.PropertyChanged -= value;
    }

    public ISettingsAcpTransportConfiguration TransportConfig { get; }
    public string? AgentName => _state.AgentName;

    public string? AgentVersion => _state.AgentVersion;

    public bool IsConnecting => _state.IsConnecting;

    public bool IsInitializing => _state.IsInitializing;

    public bool IsConnected => _state.IsConnected;

    public string? ConnectionErrorMessage => _state.ConnectionErrorMessage;

    public bool HasConnectionError => _state.HasConnectionError;

    public IAsyncRelayCommand DisconnectCommand => _commands.DisconnectCommand;

    public Task ConnectToAcpProfileAsync(ServerConfiguration profile) => _commands.ConnectToAcpProfileAsync(profile);

    public Task ConnectProfileAsync(ServerConfiguration profile) => _commands.ConnectProfileAsync(profile);

    public Task DisconnectProfileAsync(ServerConfiguration profile) => _commands.DisconnectProfileAsync(profile);

    public Task ReconnectProfileAsync(ServerConfiguration profile) => _commands.ReconnectProfileAsync(profile);
}

/// <summary>
/// Breaks the DI circular dependency between <see cref="AcpProfilesViewModel"/> and <see cref="ChatViewModel"/>
/// by deferring resolution of <see cref="ISettingsChatConnection"/> until the first connect/disconnect call.
/// </summary>
public sealed class LazySettingsAcpConnectionCommandsAdapter : ISettingsAcpConnectionCommands
{
    private readonly Lazy<ISettingsChatConnection> _inner;

    public LazySettingsAcpConnectionCommandsAdapter(Lazy<ISettingsChatConnection> inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public IAsyncRelayCommand DisconnectCommand => _inner.Value.DisconnectCommand;

    public Task ConnectToAcpProfileAsync(ServerConfiguration profile)
        => _inner.Value.ConnectToAcpProfileAsync(profile);

    public Task ConnectProfileAsync(ServerConfiguration profile)
        => _inner.Value.ConnectProfileAsync(profile);

    public Task DisconnectProfileAsync(ServerConfiguration profile)
        => _inner.Value.DisconnectProfileAsync(profile);

    public Task ReconnectProfileAsync(ServerConfiguration profile)
        => _inner.Value.ReconnectProfileAsync(profile);
}

public sealed class SettingsChatConnectionAdapter : ISettingsChatConnection
{
    private readonly ISettingsForegroundChatConnection _foregroundConnection;
    private readonly IAcpConnectionCommands _connectionCommands;
    private readonly ISettingsAcpTransportConfiguration _transportConfig;

    public SettingsChatConnectionAdapter(ISettingsForegroundChatConnection foregroundConnection, IAcpConnectionCommands connectionCommands)
    {
        _foregroundConnection = foregroundConnection ?? throw new ArgumentNullException(nameof(foregroundConnection));
        _connectionCommands = connectionCommands ?? throw new ArgumentNullException(nameof(connectionCommands));
        _transportConfig = new SettingsTransportConfigurationAdapter(_foregroundConnection.TransportConfig);
    }

    public event PropertyChangedEventHandler? PropertyChanged
    {
        add => _foregroundConnection.PropertyChanged += value;
        remove => _foregroundConnection.PropertyChanged -= value;
    }

    public ISettingsAcpTransportConfiguration TransportConfig => _transportConfig;

    public string? AgentName => _foregroundConnection.AgentName;

    public string? AgentVersion => _foregroundConnection.AgentVersion;

    public bool IsConnecting => _foregroundConnection.IsConnecting;

    public bool IsInitializing => _foregroundConnection.IsInitializing;

    public bool IsConnected => _foregroundConnection.IsConnected;

    public string? ConnectionErrorMessage => _foregroundConnection.ConnectionErrorMessage;

    public bool HasConnectionError => _foregroundConnection.HasConnectionError;

    public IAsyncRelayCommand DisconnectCommand => _foregroundConnection.DisconnectCommand;

    public Task ConnectToAcpProfileAsync(ServerConfiguration profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return _foregroundConnection.ConnectToAcpProfileCommand.ExecuteAsync(profile);
    }

    public Task ConnectProfileAsync(ServerConfiguration profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (ShouldUseForegroundConnection(profile.Id))
        {
            return ConnectToAcpProfileAsync(profile);
        }

        return _connectionCommands.ConnectProfileInPoolAsync(profile, _transportConfig);
    }

    public Task DisconnectProfileAsync(ServerConfiguration profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (ShouldUseForegroundConnection(profile.Id))
        {
            return _foregroundConnection.DisconnectCommand.ExecuteAsync(null);
        }

        return _connectionCommands.DisconnectProfileInPoolAsync(profile.Id);
    }

    public async Task ReconnectProfileAsync(ServerConfiguration profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (ShouldUseForegroundConnection(profile.Id))
        {
            await _foregroundConnection.DisconnectCommand.ExecuteAsync(null).ConfigureAwait(false);
            await ConnectToAcpProfileAsync(profile).ConfigureAwait(false);
            return;
        }

        await _connectionCommands.DisconnectProfileInPoolAsync(profile.Id).ConfigureAwait(false);
        await _connectionCommands.ConnectProfileInPoolAsync(profile, _transportConfig).ConfigureAwait(false);
    }

    // Settings profile actions must route through the same authoritative foreground identity
    // that drives chat/start-page state; only non-foreground profiles may be managed as warm pool entries.
    private bool ShouldUseForegroundConnection(string? profileId)
        => !string.IsNullOrWhiteSpace(profileId)
           && string.Equals(_foregroundConnection.ForegroundTransportProfileId, profileId, StringComparison.Ordinal);
}
