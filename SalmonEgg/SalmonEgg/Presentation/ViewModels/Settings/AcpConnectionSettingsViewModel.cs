using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SalmonEgg.Domain.Models;
using SalmonEgg.Presentation.ViewModels.Chat;

namespace SalmonEgg.Presentation.ViewModels.Settings;

public sealed partial class AcpConnectionSettingsViewModel : ObservableObject, IDisposable
{
    private readonly ILogger<AcpConnectionSettingsViewModel> _logger;
    private bool _disposed;
    private bool _isApplyingProfile;

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

    public bool HasSelectedProfile => Profiles.SelectedProfile != null;

    public string SelectedProfileName => Profiles.SelectedProfile?.Name ?? string.Empty;

    public string AgentDisplayName =>
        string.IsNullOrWhiteSpace(Chat.AgentName) ? "Agent" : Chat.AgentName!;

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUpdateSelectedProfile))]
    private bool _isProfileDirty;

    public bool CanUpdateSelectedProfile => HasSelectedProfile && IsProfileDirty;

    [RelayCommand]
    private async Task SaveCurrentAsNewProfileAsync()
    {
        try
        {
            var profile = CreateProfileFromCurrentConfig(GenerateDefaultProfileName());
            await Profiles.SaveNewAsync(profile);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save current config as new profile");
        }
    }

    [RelayCommand]
    private async Task SaveCurrentAsCopyAsync()
    {
        try
        {
            var baseName = Profiles.SelectedProfile?.Name;
            if (string.IsNullOrWhiteSpace(baseName))
            {
                await SaveCurrentAsNewProfileAsync();
                return;
            }

            var profile = CreateProfileFromCurrentConfig(GenerateUniqueName($"复制 - {baseName.Trim()}"));
            await Profiles.SaveNewAsync(profile);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save current config as profile copy");
        }
    }

    public AcpConnectionSettingsViewModel(
        ChatViewModel chatViewModel,
        AcpProfilesViewModel profiles,
        ILogger<AcpConnectionSettingsViewModel> logger)
    {
        Chat = chatViewModel ?? throw new ArgumentNullException(nameof(chatViewModel));
        Profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        SelectedTransport = TransportOptions.FirstOrDefault(o => o.Type == Chat.TransportConfig.SelectedTransportType)
                            ?? TransportOptions.First();

        Chat.TransportConfig.PropertyChanged += OnTransportConfigPropertyChanged;
        Chat.PropertyChanged += OnChatPropertyChanged;
        Profiles.PropertyChanged += OnProfilesPropertyChanged;
        UpdateDirtyState();
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

    private void OnProfilesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Profiles.SelectedProfile))
        {
            ApplySelectedProfileToConfig();
        }
    }

    private void ApplySelectedProfileToConfig()
    {
        var profile = Profiles.SelectedProfile;
        if (profile == null)
        {
            UpdateDirtyState();
            return;
        }

        _isApplyingProfile = true;
        try
        {
            SelectedTransport = TransportOptions.FirstOrDefault(o => o.Type == profile.Transport) ?? TransportOptions.First();
            Chat.TransportConfig.SelectedTransportType = profile.Transport;

            if (profile.Transport == TransportType.Stdio)
            {
                Chat.TransportConfig.StdioCommand = profile.StdioCommand ?? string.Empty;
                Chat.TransportConfig.StdioArgs = profile.StdioArgs ?? string.Empty;
            }
            else
            {
                Chat.TransportConfig.RemoteUrl = profile.ServerUrl ?? string.Empty;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to apply selected profile to transport config");
        }
        finally
        {
            _isApplyingProfile = false;
            UpdateDirtyState();
            OnPropertyChanged(nameof(HasSelectedProfile));
            OnPropertyChanged(nameof(SelectedProfileName));
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

        if (!_isApplyingProfile)
        {
            UpdateDirtyState();
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
            if (!_isApplyingProfile)
            {
                UpdateDirtyState();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to change transport type");
        }
    }

    private void UpdateDirtyState()
    {
        var profile = Profiles.SelectedProfile;
        if (profile == null)
        {
            IsProfileDirty = false;
            return;
        }

        var cfg = Chat.TransportConfig;
        var sameTransport = profile.Transport == cfg.SelectedTransportType;
        if (!sameTransport)
        {
            IsProfileDirty = true;
            return;
        }

        if (profile.Transport == TransportType.Stdio)
        {
            IsProfileDirty =
                !string.Equals((profile.StdioCommand ?? string.Empty).Trim(), (cfg.StdioCommand ?? string.Empty).Trim(), StringComparison.Ordinal) ||
                !string.Equals((profile.StdioArgs ?? string.Empty).Trim(), (cfg.StdioArgs ?? string.Empty).Trim(), StringComparison.Ordinal);
            return;
        }

        IsProfileDirty = !string.Equals((profile.ServerUrl ?? string.Empty).Trim(), (cfg.RemoteUrl ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);
    }

    [RelayCommand]
    private async Task UpdateSelectedProfileFromCurrentAsync()
    {
        var profile = Profiles.SelectedProfile;
        if (profile == null)
        {
            return;
        }

        try
        {
            var updated = new ServerConfiguration
            {
                Id = profile.Id,
                Name = profile.Name,
                Transport = Chat.TransportConfig.SelectedTransportType,
                ServerUrl = Chat.TransportConfig.SelectedTransportType == TransportType.Stdio ? string.Empty : (Chat.TransportConfig.RemoteUrl ?? string.Empty),
                StdioCommand = Chat.TransportConfig.SelectedTransportType == TransportType.Stdio ? (Chat.TransportConfig.StdioCommand ?? string.Empty) : string.Empty,
                StdioArgs = Chat.TransportConfig.SelectedTransportType == TransportType.Stdio ? (Chat.TransportConfig.StdioArgs ?? string.Empty) : string.Empty,
                Authentication = profile.Authentication,
                Proxy = profile.Proxy,
                HeartbeatInterval = profile.HeartbeatInterval,
                ConnectionTimeout = profile.ConnectionTimeout
            };

            await Profiles.SaveAsync(updated);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update selected profile from current config");
        }
        finally
        {
            UpdateDirtyState();
        }
    }

    private ServerConfiguration CreateProfileFromCurrentConfig(string name)
    {
        var cfg = Chat.TransportConfig;
        var transport = cfg.SelectedTransportType;

        return new ServerConfiguration
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            Transport = transport,
            ServerUrl = transport == TransportType.Stdio ? string.Empty : (cfg.RemoteUrl ?? string.Empty),
            StdioCommand = transport == TransportType.Stdio ? (cfg.StdioCommand ?? string.Empty) : string.Empty,
            StdioArgs = transport == TransportType.Stdio ? (cfg.StdioArgs ?? string.Empty) : string.Empty,
            HeartbeatInterval = 30,
            ConnectionTimeout = 10
        };
    }

    private string GenerateDefaultProfileName()
    {
        var cfg = Chat.TransportConfig;
        var transport = cfg.SelectedTransportType;

        string baseName;
        if (transport == TransportType.Stdio)
        {
            var cmd = (cfg.StdioCommand ?? string.Empty).Trim();
            baseName = string.IsNullOrWhiteSpace(cmd) ? "本地 Agent" : $"本地 - {cmd}";
        }
        else
        {
            var url = (cfg.RemoteUrl ?? string.Empty).Trim();
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
            {
                baseName = $"远程 - {uri.Host}";
            }
            else
            {
                baseName = "远程 Agent";
            }
        }

        return GenerateUniqueName(baseName);
    }

    private string GenerateUniqueName(string baseName)
    {
        baseName = string.IsNullOrWhiteSpace(baseName) ? "新预设" : baseName.Trim();
        if (Profiles.Profiles.Count == 0)
        {
            return baseName;
        }

        var candidate = baseName;
        var index = 2;
        while (Profiles.Profiles.Any(p => string.Equals(p.Name, candidate, StringComparison.OrdinalIgnoreCase)))
        {
            candidate = $"{baseName} ({index})";
            index++;
        }

        return candidate;
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
        Profiles.PropertyChanged -= OnProfilesPropertyChanged;
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
