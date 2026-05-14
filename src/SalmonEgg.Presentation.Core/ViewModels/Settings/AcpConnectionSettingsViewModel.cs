using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.ProjectAffinity;
using SalmonEgg.Domain.Services;
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
    private readonly IUiDispatcher _uiDispatcher;
    private bool _suppressPathMappingProjection;
    private bool _disposed;

    public ISettingsChatConnection Chat { get; }
    public AcpProfilesViewModel Profiles { get; }

    public ObservableCollection<TransportOptionViewModel> TransportOptions { get; }

    public ObservableCollection<AcpPathMappingRowViewModel> PathMappingRows { get; } = new();
    public ObservableCollection<HydrationCompletionModeOptionViewModel> HydrationCompletionModeOptions { get; } = new()
    {
        new("StrictReplay", "StrictReplay", "Complete hydration after replay projection reaches a stable state."),
        new("LoadResponse", "LoadResponse", "Complete hydration right after session/load; replay projects asynchronously.")
    };

    public bool CanEditPathMappings => !string.IsNullOrWhiteSpace(ResolveSelectedProfileId());

    [ObservableProperty]
    private TransportOptionViewModel? _selectedTransport;

    [ObservableProperty]
    private HydrationCompletionModeOptionViewModel? _selectedHydrationCompletionMode;

    public string SelectedTransportName => SelectedTransport?.Name ?? string.Empty;
    public string SelectedHydrationCompletionModeDescription => SelectedHydrationCompletionMode?.Description ?? string.Empty;

    public AcpConnectionSettingsViewModel(
        ChatViewModel chatViewModel,
        IAcpConnectionCommands connectionCommands,
        AcpProfilesViewModel profiles,
        AppPreferencesViewModel preferences,
        ITransportSupportPolicy transportSupportPolicy,
        ILogger<AcpConnectionSettingsViewModel> logger,
        IUiDispatcher? uiDispatcher = null)
        : this(new SettingsChatConnectionAdapter(chatViewModel, connectionCommands), profiles, preferences, transportSupportPolicy, logger, uiDispatcher)
    {
    }

    public AcpConnectionSettingsViewModel(
        ISettingsChatConnection chat,
        AcpProfilesViewModel profiles,
        AppPreferencesViewModel preferences,
        ITransportSupportPolicy transportSupportPolicy,
        ILogger<AcpConnectionSettingsViewModel> logger,
        IUiDispatcher? uiDispatcher = null)
        : this(
            chat,
            chat,
            chat.TransportConfig,
            profiles,
            preferences,
            transportSupportPolicy,
            logger,
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
        IUiDispatcher? uiDispatcher = null)
        : this(
            connectionState,
            connectionCommands,
            transportConfig,
            profiles,
            preferences,
            transportSupportPolicy,
            logger,
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
        _uiDispatcher = uiDispatcher ?? InlineUiDispatcher.Instance;
        Chat = chatFacade ?? new CompositeSettingsChatConnection(connectionState, _connectionCommands, _transportConfig);
        TransportOptions = CreateTransportOptions(_transportSupportPolicy);

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
        _preferences.ProjectPathMappings.CollectionChanged += OnProjectPathMappingsCollectionChanged;

        SelectedHydrationCompletionMode = ResolveHydrationCompletionModeOption(_preferences.AcpHydrationCompletionMode);
        RefreshPathMappingRows();
    }

    private void OnProfilesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        PostToUi(() =>
        {
            RefreshPathMappingRows();
        });
    }

    private void OnPreferencesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
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
            OnPropertyChanged(nameof(CanEditPathMappings));
            AddPathMappingCommand.NotifyCanExecuteChanged();
            RefreshPathMappingRows();
        });
    }

    private void OnProjectPathMappingsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_suppressPathMappingProjection)
        {
            return;
        }

        PostToUi(RefreshPathMappingRows);
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
        ITransportSupportPolicy transportSupportPolicy)
    {
        var options = new ObservableCollection<TransportOptionViewModel>();
        if (transportSupportPolicy.IsSupported(TransportType.Stdio))
        {
            options.Add(new TransportOptionViewModel(TransportType.Stdio, "Stdio（子进程）"));
        }

        options.Add(new TransportOptionViewModel(TransportType.WebSocket, "WebSocket"));
        options.Add(new TransportOptionViewModel(TransportType.HttpSse, "HTTP SSE"));
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

    [RelayCommand(CanExecute = nameof(CanEditPathMappings))]
    private void AddPathMapping()
    {
        var profileId = ResolveSelectedProfileId();
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return;
        }

        _preferences.ProjectPathMappings.Add(new ProjectPathMapping
        {
            ProfileId = profileId,
            RemoteRootPath = string.Empty,
            LocalRootPath = string.Empty
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

    private void RefreshPathMappingRows()
    {
        var profileId = ResolveSelectedProfileId();
        if (string.IsNullOrWhiteSpace(profileId))
        {
            PathMappingRows.Clear();
            return;
        }

        var mappings = _preferences.ProjectPathMappings
            .Where(m => string.Equals(m.ProfileId, profileId, StringComparison.Ordinal))
            .ToList();
        var existingRows = PathMappingRows.ToDictionary(row => row.Mapping);
        var nextRows = new List<AcpPathMappingRowViewModel>(mappings.Count);

        foreach (var mapping in mappings)
        {
            if (existingRows.TryGetValue(mapping, out var existing))
            {
                existing.SyncFromMapping();
                nextRows.Add(existing);
                continue;
            }

            nextRows.Add(new AcpPathMappingRowViewModel(mapping, this));
        }

        for (var i = PathMappingRows.Count - 1; i >= 0; i--)
        {
            if (!nextRows.Contains(PathMappingRows[i]))
            {
                PathMappingRows.RemoveAt(i);
            }
        }

        for (var i = 0; i < nextRows.Count; i++)
        {
            if (i >= PathMappingRows.Count)
            {
                PathMappingRows.Add(nextRows[i]);
                continue;
            }

            if (!ReferenceEquals(PathMappingRows[i], nextRows[i]))
            {
                PathMappingRows.Insert(i, nextRows[i]);
                PathMappingRows.RemoveAt(i + 1);
            }
        }
    }

    internal void UpdatePathMapping(AcpPathMappingRowViewModel row)
    {
        var index = _preferences.ProjectPathMappings.IndexOf(row.Mapping);
        if (index < 0)
        {
            return;
        }

        var updated = new ProjectPathMapping
        {
            ProfileId = row.Mapping.ProfileId,
            RemoteRootPath = (row.RemoteRootPath ?? string.Empty).Trim(),
            LocalRootPath = (row.LocalRootPath ?? string.Empty).Trim()
        };

        if (string.Equals(row.Mapping.RemoteRootPath, updated.RemoteRootPath, StringComparison.Ordinal) &&
            string.Equals(row.Mapping.LocalRootPath, updated.LocalRootPath, StringComparison.Ordinal))
        {
            return;
        }

        _suppressPathMappingProjection = true;
        try
        {
            _preferences.ProjectPathMappings[index] = updated;
        }
        finally
        {
            _suppressPathMappingProjection = false;
        }

        row.ReplaceMapping(updated);
    }

    internal void RemovePathMapping(AcpPathMappingRowViewModel row)
    {
        if (row == null)
        {
            return;
        }

        _preferences.ProjectPathMappings.Remove(row.Mapping);
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
        _preferences.ProjectPathMappings.CollectionChanged -= OnProjectPathMappingsCollectionChanged;
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

public sealed partial class AcpPathMappingRowViewModel : ObservableObject
{
    private readonly AcpConnectionSettingsViewModel _owner;
    private bool _isApplyingModel;

    internal AcpPathMappingRowViewModel(ProjectPathMapping mapping, AcpConnectionSettingsViewModel owner)
    {
        Mapping = mapping ?? throw new ArgumentNullException(nameof(mapping));
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _remoteRootPath = mapping.RemoteRootPath;
        _localRootPath = mapping.LocalRootPath;
        RemoveCommand = new RelayCommand(Remove);
    }

    internal ProjectPathMapping Mapping { get; private set; }

    public string ProfileId => Mapping.ProfileId;

    [ObservableProperty]
    private string _remoteRootPath;

    [ObservableProperty]
    private string _localRootPath;

    public IRelayCommand RemoveCommand { get; }

    partial void OnRemoteRootPathChanged(string value)
    {
        Commit();
    }

    partial void OnLocalRootPathChanged(string value)
    {
        Commit();
    }

    internal void ReplaceMapping(ProjectPathMapping mapping)
    {
        Mapping = mapping ?? throw new ArgumentNullException(nameof(mapping));
        SyncFromMapping();
        OnPropertyChanged(nameof(ProfileId));
    }

    internal void SyncFromMapping()
    {
        _isApplyingModel = true;
        try
        {
            RemoteRootPath = Mapping.RemoteRootPath;
            LocalRootPath = Mapping.LocalRootPath;
        }
        finally
        {
            _isApplyingModel = false;
        }
    }

    private void Commit()
    {
        if (_isApplyingModel)
        {
            return;
        }

        _owner.UpdatePathMapping(this);
    }

    private void Remove()
    {
        _owner.RemovePathMapping(this);
    }
}
