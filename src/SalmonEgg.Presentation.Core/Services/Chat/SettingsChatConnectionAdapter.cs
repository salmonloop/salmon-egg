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
}

public interface ISettingsAcpTransportConfiguration : IAcpTransportConfiguration, INotifyPropertyChanged
{
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
}

public sealed class SettingsChatConnectionAdapter : ISettingsChatConnection
{
    private readonly ChatViewModel _chatViewModel;
    private readonly ISettingsAcpTransportConfiguration _transportConfig;

    public SettingsChatConnectionAdapter(ChatViewModel chatViewModel)
    {
        _chatViewModel = chatViewModel ?? throw new ArgumentNullException(nameof(chatViewModel));
        _transportConfig = new SettingsTransportConfigurationAdapter(_chatViewModel.TransportConfig);
    }

    public event PropertyChangedEventHandler? PropertyChanged
    {
        add => _chatViewModel.PropertyChanged += value;
        remove => _chatViewModel.PropertyChanged -= value;
    }

    public ISettingsAcpTransportConfiguration TransportConfig => _transportConfig;

    public string? AgentName => _chatViewModel.AgentName;

    public string? AgentVersion => _chatViewModel.AgentVersion;

    public bool IsConnecting => _chatViewModel.IsConnecting;

    public bool IsInitializing => _chatViewModel.IsInitializing;

    public bool IsConnected => _chatViewModel.IsConnected;

    public string? ConnectionErrorMessage => _chatViewModel.ConnectionErrorMessage;

    public bool HasConnectionError => _chatViewModel.HasConnectionError;

    public IAsyncRelayCommand DisconnectCommand => _chatViewModel.DisconnectCommand;

    public Task ConnectToAcpProfileAsync(ServerConfiguration profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return _chatViewModel.ConnectToAcpProfileCommand.ExecuteAsync(profile);
    }
}
