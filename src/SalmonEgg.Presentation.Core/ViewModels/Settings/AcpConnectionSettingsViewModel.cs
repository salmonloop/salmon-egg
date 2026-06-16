using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Resources;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.ViewModels.Chat;

namespace SalmonEgg.Presentation.ViewModels.Settings;

public sealed partial class AcpConnectionSettingsViewModel : ObservableObject, IDisposable
{
    private readonly ILogger<AcpConnectionSettingsViewModel> _logger;
    private readonly AppPreferencesViewModel _preferences;
    private readonly ISettingsAcpConnectionCommands _connectionCommands;
    private readonly ISettingsAcpTransportConfiguration _transportConfig;
    private readonly ITransportSupportPolicy _transportSupportPolicy;
    private readonly IStringLocalizer<CoreStrings> _localizer;
    private readonly IUiDispatcher _uiDispatcher;
    private bool _suppressRemoteDirectoryProjection;
    private bool _disposed;

    public ISettingsChatConnection Chat { get; }
    public AcpProfilesViewModel Profiles { get; }

    public ObservableCollection<TransportOptionViewModel> TransportOptions { get; }

    public ObservableCollection<AcpRemoteDirectoryRowViewModel> RemoteDirectoryRows { get; } = new();
    public ObservableCollection<HydrationCompletionModeOptionViewModel> HydrationCompletionModeOptions { get; }

    public bool CanEditRemoteDirectories => !string.IsNullOrWhiteSpace(ResolveSelectedProfileId());

    [ObservableProperty]
    private TransportOptionViewModel? _selectedTransport;

    [ObservableProperty]
    private HydrationCompletionModeOptionViewModel? _selectedHydrationCompletionMode;

    public bool IsAcpEnabled
    {
        get => _preferences.AcpEnabled;
        set
        {
            if (_preferences.AcpEnabled == value)
            {
                return;
            }

            _preferences.AcpEnabled = value;
            OnPropertyChanged();
        }
    }

    public string SelectedTransportName => SelectedTransport?.Name ?? string.Empty;
    public string SelectedHydrationCompletionModeDescription => SelectedHydrationCompletionMode?.Description ?? string.Empty;

    public AcpConnectionSettingsViewModel(
        ChatViewModel chatViewModel,
        IAcpConnectionCommands connectionCommands,
        AcpProfilesViewModel profiles,
        AppPreferencesViewModel preferences,
        ITransportSupportPolicy transportSupportPolicy,
        ILogger<AcpConnectionSettingsViewModel> logger,
        IStringLocalizer<CoreStrings> localizer,
        IUiDispatcher? uiDispatcher = null)
        : this(new SettingsChatConnectionAdapter(chatViewModel, connectionCommands), profiles, preferences, transportSupportPolicy, logger, localizer, uiDispatcher)
    {
    }

    public AcpConnectionSettingsViewModel(
        ISettingsChatConnection chat,
        AcpProfilesViewModel profiles,
        AppPreferencesViewModel preferences,
        ITransportSupportPolicy transportSupportPolicy,
        ILogger<AcpConnectionSettingsViewModel> logger,
        IStringLocalizer<CoreStrings> localizer,
        IUiDispatcher? uiDispatcher = null)
        : this(
            chat,
            chat,
            chat.TransportConfig,
            profiles,
            preferences,
            transportSupportPolicy,
            logger,
            localizer,
            chat,
            uiDispatcher)
    {
    }

    public AcpConnectionSettingsViewModel(
        ISettingsAcpConnectionState connectionState,
        ISettingsAcpConnectionCommands connectionCommands,
        ISettingsAcpTransportConfiguration transportConfig,
        AcpProfilesViewModel profiles,
        AppPreferencesViewModel preferences,
        ITransportSupportPolicy transportSupportPolicy,
        ILogger<AcpConnectionSettingsViewModel> logger,
        IStringLocalizer<CoreStrings> localizer,
        IUiDispatcher? uiDispatcher = null)
        : this(
            connectionState,
            connectionCommands,
            transportConfig,
            profiles,
            preferences,
            transportSupportPolicy,
            logger,
            localizer,
            chatFacade: null,
            uiDispatcher)
    {
    }

    private AcpConnectionSettingsViewModel(
        ISettingsAcpConnectionState connectionState,
        ISettingsAcpConnectionCommands connectionCommands,
        ISettingsAcpTransportConfiguration transportConfig,
        AcpProfilesViewModel profiles,
        AppPreferencesViewModel preferences,
        ITransportSupportPolicy transportSupportPolicy,
        ILogger<AcpConnectionSettingsViewModel> logger,
        IStringLocalizer<CoreStrings> localizer,
        ISettingsChatConnection? chatFacade,
        IUiDispatcher? uiDispatcher)
    {
        ArgumentNullException.ThrowIfNull(connectionState);
        _connectionCommands = connectionCommands ?? throw new ArgumentNullException(nameof(connectionCommands));
        _transportConfig = transportConfig ?? throw new ArgumentNullException(nameof(transportConfig));
        Profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _transportSupportPolicy = transportSupportPolicy ?? throw new ArgumentNullException(nameof(transportSupportPolicy));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _uiDispatcher = uiDispatcher ?? InlineUiDispatcher.Instance;
        Chat = chatFacade ?? new CompositeSettingsChatConnection(connectionState, _connectionCommands, _transportConfig);
        TransportOptions = CreateTransportOptions(_transportSupportPolicy, _localizer);
        HydrationCompletionModeOptions = CreateHydrationCompletionModeOptions(_localizer);

        SelectedTransport = TransportOptions.FirstOrDefault(o => o.Type == _transportConfig.SelectedTransportType)
                            ?? TransportOptions.FirstOrDefault();
        if (SelectedTransport != null && _transportConfig.SelectedTransportType != SelectedTransport.Type)
        {
            _transportConfig.SelectedTransportType = SelectedTransport.Type;
        }

        _transportConfig.PropertyChanged += OnTransportConfigPropertyChanged;
        Profiles.PropertyChanged += OnProfilesPropertyChanged;
        Profiles.Profiles.CollectionChanged += OnProfilesCollectionChanged;
        _preferences.PropertyChanged += OnPreferencesPropertyChanged;
        _preferences.AgentRemoteDirectories.CollectionChanged += OnAgentRemoteDirectoriesCollectionChanged;

        SelectedHydrationCompletionMode = ResolveHydrationCompletionModeOption(_preferences.AcpHydrationCompletionMode);
        RefreshRemoteDirectoryRows();
    }

    private void OnProfilesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        PostToUi(() =>
        {
            RefreshRemoteDirectoryRows();
        });
    }

    private void OnPreferencesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppPreferencesViewModel.AcpEnabled))
        {
            PostToUi(() => OnPropertyChanged(nameof(IsAcpEnabled)));
            return;
        }

        if (e.PropertyName == nameof(AppPreferencesViewModel.AcpHydrationCompletionMode))
        {
            PostToUi(() =>
            {
                var option = ResolveHydrationCompletionModeOption(_preferences.AcpHydrationCompletionMode);
                if (!ReferenceEquals(SelectedHydrationCompletionMode, option))
                {
                    SelectedHydrationCompletionMode = option;
                }
            });
        }
    }

    private void OnProfilesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AcpProfilesViewModel.SelectedProfile) &&
            e.PropertyName != nameof(AcpProfilesViewModel.SelectedProfileItem))
        {
            return;
        }

        PostToUi(() =>
        {
            OnPropertyChanged(nameof(CanEditRemoteDirectories));
            AddRemoteDirectoryCommand.NotifyCanExecuteChanged();
            RefreshRemoteDirectoryRows();
        });
    }

    private void OnAgentRemoteDirectoriesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_suppressRemoteDirectoryProjection)
        {
            return;
        }

        PostToUi(RefreshRemoteDirectoryRows);
    }

    private void OnTransportConfigPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        PostToUi(() =>
        {
            if (e.PropertyName == nameof(ISettingsAcpTransportConfiguration.SelectedTransportType))
            {
                var supportedType = ResolveSupportedTransportType(_transportConfig.SelectedTransportType);
                if (_transportConfig.SelectedTransportType != supportedType)
                {
                    _transportConfig.SelectedTransportType = supportedType;
                    return;
                }

                var current = TransportOptions.FirstOrDefault(o => o.Type == supportedType);
                if (current != null && SelectedTransport?.Type != current.Type)
                {
                    SelectedTransport = current;
                }
            }
        });
    }

    partial void OnSelectedTransportChanged(TransportOptionViewModel? value)
    {
        try
        {
            if (value == null)
            {
                return;
            }

            _transportConfig.SelectedTransportType = value.Type;
            OnPropertyChanged(nameof(SelectedTransportName));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to change transport type");
        }
    }

    private TransportType ResolveSupportedTransportType(TransportType transportType)
        => _transportSupportPolicy.Coerce(transportType);

    private static ObservableCollection<TransportOptionViewModel> CreateTransportOptions(
        ITransportSupportPolicy transportSupportPolicy,
        IStringLocalizer<CoreStrings> localizer)
    {
        var options = new ObservableCollection<TransportOptionViewModel>();
        if (transportSupportPolicy.IsSupported(TransportType.Stdio))
        {
            options.Add(new TransportOptionViewModel(TransportType.Stdio, localizer["AcpConnection_TransportStdio"]));
        }

        options.Add(new TransportOptionViewModel(TransportType.WebSocket, localizer["AcpConnection_TransportWebSocket"]));
        options.Add(new TransportOptionViewModel(TransportType.HttpSse, localizer["AcpConnection_TransportHttpSse"]));
        return options;
    }

    private static ObservableCollection<HydrationCompletionModeOptionViewModel> CreateHydrationCompletionModeOptions(
        IStringLocalizer<CoreStrings> localizer)
    {
        var options = new ObservableCollection<HydrationCompletionModeOptionViewModel>
        {
            new("StrictReplay", localizer["AcpConnection_HydrationStrictReplayName"], localizer["AcpConnection_HydrationStrictReplayDescription"]),
            new("LoadResponse", localizer["AcpConnection_HydrationLoadResponseName"], localizer["AcpConnection_HydrationLoadResponseDescription"])
        };

        return options;
    }

    partial void OnSelectedHydrationCompletionModeChanged(HydrationCompletionModeOptionViewModel? value)
    {
        OnPropertyChanged(nameof(SelectedHydrationCompletionModeDescription));
        if (value == null)
        {
            return;
        }

        if (!string.Equals(_preferences.AcpHydrationCompletionMode, value.Value, StringComparison.Ordinal))
        {
            _preferences.AcpHydrationCompletionMode = value.Value;
        }
    }

    [RelayCommand(CanExecute = nameof(CanEditRemoteDirectories))]
    private void AddRemoteDirectory()
    {
        var profileId = ResolveSelectedProfileId();
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return;
        }

        _preferences.AgentRemoteDirectories.Add(new AgentRemoteDirectory
        {
            ProfileId = profileId,
            DirectoryId = Guid.NewGuid().ToString("N"),
            DisplayName = string.Empty,
            RemotePath = string.Empty
        });
    }

    public async Task ConnectToProfileAsync(ServerConfiguration? profile)
    {
        if (profile == null)
        {
            return;
        }

        try
        {
            await _connectionCommands.ConnectToAcpProfileAsync(profile);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to profile {ProfileId}", profile.Id);
        }
    }

    public async Task HandleConnectionToggleAsync(bool shouldConnect)
    {
        var profile = Profiles.SelectedProfile;
        if (profile == null)
        {
            return;
        }

        try
        {
            if (shouldConnect)
            {
                await _connectionCommands.ConnectProfileInPoolAsync(profile);
                return;
            }

            await _connectionCommands.DisconnectProfileInPoolAsync(profile.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to handle ACP connection toggle (ShouldConnect={ShouldConnect})", shouldConnect);
        }
    }

    private string? ResolveSelectedProfileId()
    {
        var id = Profiles.SelectedProfile?.Id?.Trim();
        return string.IsNullOrWhiteSpace(id) ? null : id;
    }

    private HydrationCompletionModeOptionViewModel ResolveHydrationCompletionModeOption(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            var match = HydrationCompletionModeOptions.FirstOrDefault(
                option => string.Equals(option.Value, value, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                return match;
            }
        }

        return HydrationCompletionModeOptions[0];
    }

    private void RefreshRemoteDirectoryRows()
    {
        var profileId = ResolveSelectedProfileId();
        if (string.IsNullOrWhiteSpace(profileId))
        {
            RemoteDirectoryRows.Clear();
            return;
        }

        var directories = _preferences.AgentRemoteDirectories
            .Where(d => string.Equals(d.ProfileId, profileId, StringComparison.Ordinal))
            .ToList();
        var existingRows = RemoteDirectoryRows.ToDictionary(row => row.Directory);
        var nextRows = new List<AcpRemoteDirectoryRowViewModel>(directories.Count);

        foreach (var directory in directories)
        {
            if (existingRows.TryGetValue(directory, out var existing))
            {
                existing.ReplaceDirectory(directory);
                nextRows.Add(existing);
                continue;
            }

            nextRows.Add(new AcpRemoteDirectoryRowViewModel(directory, this));
        }

        for (var i = RemoteDirectoryRows.Count - 1; i >= 0; i--)
        {
            if (!nextRows.Contains(RemoteDirectoryRows[i]))
            {
                RemoteDirectoryRows.RemoveAt(i);
            }
        }

        for (var i = 0; i < nextRows.Count; i++)
        {
            if (i >= RemoteDirectoryRows.Count)
            {
                RemoteDirectoryRows.Add(nextRows[i]);
                continue;
            }

            if (!ReferenceEquals(RemoteDirectoryRows[i], nextRows[i]))
            {
                RemoteDirectoryRows.Insert(i, nextRows[i]);
                RemoteDirectoryRows.RemoveAt(i + 1);
            }
        }
    }

    internal void UpdateRemoteDirectory(AcpRemoteDirectoryRowViewModel row)
    {
        var index = _preferences.AgentRemoteDirectories.IndexOf(row.Directory);
        if (index < 0)
        {
            return;
        }

        var updated = new AgentRemoteDirectory
        {
            ProfileId = row.Directory.ProfileId,
            DirectoryId = row.Directory.DirectoryId,
            DisplayName = (row.DisplayName ?? string.Empty).Trim(),
            RemotePath = (row.RemotePath ?? string.Empty).Trim()
        };

        if (string.Equals(row.Directory.DisplayName, updated.DisplayName, StringComparison.Ordinal)
            && string.Equals(row.Directory.RemotePath, updated.RemotePath, StringComparison.Ordinal))
        {
            return;
        }

        _suppressRemoteDirectoryProjection = true;
        try
        {
            _preferences.AgentRemoteDirectories[index] = updated;
        }
        finally
        {
            _suppressRemoteDirectoryProjection = false;
        }

        row.ReplaceDirectory(updated);
    }

    internal void RemoveRemoteDirectory(AcpRemoteDirectoryRowViewModel row)
    {
        if (row == null)
        {
            return;
        }

        _preferences.AgentRemoteDirectories.Remove(row.Directory);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _transportConfig.PropertyChanged -= OnTransportConfigPropertyChanged;
        Profiles.PropertyChanged -= OnProfilesPropertyChanged;
        Profiles.Profiles.CollectionChanged -= OnProfilesCollectionChanged;
        _preferences.PropertyChanged -= OnPreferencesPropertyChanged;
        _preferences.AgentRemoteDirectories.CollectionChanged -= OnAgentRemoteDirectoriesCollectionChanged;
    }

    private void PostToUi(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (_uiDispatcher.HasThreadAccess)
        {
            action();
            return;
        }

        _uiDispatcher.Enqueue(action);
    }

    private sealed class InlineUiDispatcher : IUiDispatcher
    {
        public static InlineUiDispatcher Instance { get; } = new();

        public bool HasThreadAccess => true;

        public void Enqueue(Action action) => action();

        public Task EnqueueAsync(Action action)
        {
            action();
            return Task.CompletedTask;
        }

        public Task EnqueueAsync(Func<Task> function) => function();
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

public sealed class HydrationCompletionModeOptionViewModel
{
    public HydrationCompletionModeOptionViewModel(string value, string name, string description)
    {
        Value = value;
        Name = name;
        Description = description;
    }

    public string Value { get; }

    public string Name { get; }

    public string Description { get; }
}

public sealed partial class AcpRemoteDirectoryRowViewModel : ObservableObject
{
    private readonly AcpConnectionSettingsViewModel _owner;
    private bool _isApplyingModel;

    internal AcpRemoteDirectoryRowViewModel(AgentRemoteDirectory directory, AcpConnectionSettingsViewModel owner)
    {
        Directory = directory ?? throw new ArgumentNullException(nameof(directory));
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _displayName = directory.DisplayName;
        _remotePath = directory.RemotePath;
        RemoveCommand = new RelayCommand(Remove);
    }

    internal AgentRemoteDirectory Directory { get; private set; }

    [ObservableProperty]
    private string _displayName;

    [ObservableProperty]
    private string _remotePath;

    public IRelayCommand RemoveCommand { get; }

    partial void OnDisplayNameChanged(string value) => UpdateOwner();

    partial void OnRemotePathChanged(string value) => UpdateOwner();

    internal void ReplaceDirectory(AgentRemoteDirectory directory)
    {
        Directory = directory ?? throw new ArgumentNullException(nameof(directory));
        _isApplyingModel = true;
        try
        {
            DisplayName = Directory.DisplayName;
            RemotePath = Directory.RemotePath;
        }
        finally
        {
            _isApplyingModel = false;
        }
    }

    private void UpdateOwner()
    {
        if (_isApplyingModel)
        {
            return;
        }

        _owner.UpdateRemoteDirectory(this);
    }

    private void Remove() => _owner.RemoveRemoteDirectory(this);
}
