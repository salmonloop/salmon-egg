using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using SalmonEgg.Domain.Models;
using SalmonEgg.Presentation.ViewModels.Chat;

namespace SalmonEgg.Presentation.ViewModels.Settings;

public sealed partial class AcpConnectionSettingsViewModel : ObservableObject, IDisposable
{
    private readonly ILogger<AcpConnectionSettingsViewModel> _logger;
    private readonly AppPreferencesViewModel _preferences;
    private bool _disposed;

    public ChatViewModel Chat { get; }
    public AcpProfilesViewModel Profiles { get; }

    public ObservableCollection<TransportOptionViewModel> TransportOptions { get; } = new()
    {
        new TransportOptionViewModel(TransportType.Stdio, "Stdio（本地）"),
        new TransportOptionViewModel(TransportType.WebSocket, "WebSocket"),
        new TransportOptionViewModel(TransportType.HttpSse, "HTTP SSE"),
    };

    [ObservableProperty]
    private TransportOptionViewModel? _selectedTransport;

    public string SelectedTransportName => SelectedTransport?.Name ?? string.Empty;

    public string AgentDisplayName =>
        ResolveConnectedProfileName()
        ?? (string.IsNullOrWhiteSpace(Chat.AgentName) ? "Agent" : Chat.AgentName!);

    public string ConnectionStatusText
    {
        get
        {
            if (Chat.IsConnecting || Chat.IsInitializing)
            {
                return "正在连接…";
            }

            if (Chat.IsConnected)
            {
                return "已连接";
            }

            if (Chat.HasConnectionError)
            {
                return "连接失败";
            }

            return "未连接";
        }
    }

    public string CurrentEndpointDisplay
    {
        get
        {
            if (Chat.TransportConfig.SelectedTransportType == TransportType.Stdio)
            {
                var cmd = (Chat.TransportConfig.StdioCommand ?? string.Empty).Trim();
                var args = (Chat.TransportConfig.StdioArgs ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(cmd))
                {
                    return "—";
                }

                return string.IsNullOrWhiteSpace(args) ? cmd : $"{cmd} {args}";
            }

            var url = (Chat.TransportConfig.RemoteUrl ?? string.Empty).Trim();
            return string.IsNullOrWhiteSpace(url) ? "—" : url;
        }
    }

    public AcpConnectionSettingsViewModel(
        ChatViewModel chatViewModel,
        AcpProfilesViewModel profiles,
        AppPreferencesViewModel preferences,
        ILogger<AcpConnectionSettingsViewModel> logger)
    {
        Chat = chatViewModel ?? throw new ArgumentNullException(nameof(chatViewModel));
        Profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        SelectedTransport = TransportOptions.FirstOrDefault(o => o.Type == Chat.TransportConfig.SelectedTransportType)
                            ?? TransportOptions.First();

        Chat.TransportConfig.PropertyChanged += OnTransportConfigPropertyChanged;
        Chat.PropertyChanged += OnChatPropertyChanged;
        Profiles.Profiles.CollectionChanged += OnProfilesCollectionChanged;
        _preferences.PropertyChanged += OnPreferencesPropertyChanged;
    }

    private void OnProfilesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(AgentDisplayName));
    }

    private void OnPreferencesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppPreferencesViewModel.LastSelectedServerId))
        {
            OnPropertyChanged(nameof(AgentDisplayName));
        }
    }

    private void OnChatPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Chat.AgentName))
        {
            OnPropertyChanged(nameof(AgentDisplayName));
        }

        if (e.PropertyName == nameof(Chat.IsConnected) ||
            e.PropertyName == nameof(Chat.IsConnecting) ||
            e.PropertyName == nameof(Chat.IsInitializing) ||
            e.PropertyName == nameof(Chat.ConnectionErrorMessage))
        {
            OnPropertyChanged(nameof(ConnectionStatusText));
        }
    }

    private void OnTransportConfigPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Chat.TransportConfig.SelectedTransportType))
        {
            var current = TransportOptions.FirstOrDefault(o => o.Type == Chat.TransportConfig.SelectedTransportType);
            if (current != null && SelectedTransport?.Type != current.Type)
            {
                SelectedTransport = current;
            }
        }

        if (e.PropertyName == nameof(Chat.TransportConfig.SelectedTransportType) ||
            e.PropertyName == nameof(Chat.TransportConfig.RemoteUrl) ||
            e.PropertyName == nameof(Chat.TransportConfig.StdioCommand) ||
            e.PropertyName == nameof(Chat.TransportConfig.StdioArgs))
        {
            OnPropertyChanged(nameof(CurrentEndpointDisplay));
        }
    }

    partial void OnSelectedTransportChanged(TransportOptionViewModel? value)
    {
        try
        {
            if (value == null)
            {
                return;
            }

            Chat.TransportConfig.SelectedTransportType = value.Type;
            OnPropertyChanged(nameof(SelectedTransportName));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to change transport type");
        }
    }

    public async Task ConnectToProfileAsync(ServerConfiguration? profile)
    {
        if (profile == null)
        {
            return;
        }

        try
        {
            // Reuse the same connection path as the chat header selector so behavior stays consistent.
            await Chat.ConnectToAcpProfileCommand.ExecuteAsync(profile);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to profile {ProfileId}", profile.Id);
        }
        finally
        {
            OnPropertyChanged(nameof(AgentDisplayName));
        }
    }

    private string? ResolveConnectedProfileName()
    {
        var id = _preferences.LastSelectedServerId;
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        return Profiles.Profiles.FirstOrDefault(p => p.Id == id)?.Name;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Chat.TransportConfig.PropertyChanged -= OnTransportConfigPropertyChanged;
        Chat.PropertyChanged -= OnChatPropertyChanged;
        Profiles.Profiles.CollectionChanged -= OnProfilesCollectionChanged;
        _preferences.PropertyChanged -= OnPreferencesPropertyChanged;
    }
}

public sealed class TransportOptionViewModel
{
    public TransportOptionViewModel(TransportType type, string name)
    {
        Type = type;
        Name = name;
    }

    public TransportType Type { get; }

    public string Name { get; }
}
