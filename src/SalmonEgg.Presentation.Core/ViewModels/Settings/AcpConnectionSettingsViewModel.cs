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
using SalmonEgg.Domain.Models.Protocol;
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
    private AcpRemoteDirectoryRowViewModel? _editingRemoteDirectory;
    private bool _disposed;

    public ISettingsChatConnection Chat { get; }
    public AcpProfilesViewModel Profiles { get; }

    public ObservableCollection<TransportOptionViewModel> TransportOptions { get; }

    public ObservableCollection<AcpRemoteDirectoryRowViewModel> RemoteDirectoryRows { get; } = new();
    public ObservableCollection<HydrationCompletionModeOptionViewModel> HydrationCompletionModeOptions { get; }

    public bool CanRefreshProfiles => !Profiles.IsLoading;
    public bool CanAddRemoteDirectory => _editingRemoteDirectory is null;

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
        _preferences.PropertyChanged += OnPreferencesPropertyChanged;
        _preferences.AgentRemoteDirectories.CollectionChanged += OnAgentRemoteDirectoriesCollectionChanged;

        SelectedHydrationCompletionMode = ResolveHydrationCompletionModeOption(_preferences.AcpHydrationCompletionMode);
        RefreshRemoteDirectoryRows();
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
        if (e.PropertyName == nameof(AcpProfilesViewModel.IsLoading))
        {
            PostToUi(() => OnPropertyChanged(nameof(CanRefreshProfiles)));
        }
    }

    private void OnAgentRemoteDirectoriesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
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

    [RelayCommand(CanExecute = nameof(CanAddRemoteDirectory))]
    private void AddRemoteDirectory()
    {
        if (_editingRemoteDirectory is not null)
        {
            return;
        }

        var directory = new AgentRemoteDirectory
        {
            DirectoryId = Guid.NewGuid().ToString("N"),
            DisplayName = string.Empty,
            RemotePath = string.Empty
        };

        var row = new AcpRemoteDirectoryRowViewModel(directory, this)
        {
            IsEditing = true,
            IsNew = true
        };
        row.BeginEditing();
        SetEditingRemoteDirectory(row);
        AddOrReplaceRemoteDirectoryRow(row);
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
        var directories = _preferences.AgentRemoteDirectories.ToList();
        var existingRows = RemoteDirectoryRows.ToDictionary(row => row.DirectoryId);
        var nextRows = new List<AcpRemoteDirectoryRowViewModel>(directories.Count);

        foreach (var directory in directories)
        {
            if (existingRows.TryGetValue(directory.DirectoryId, out var existing))
            {
                existing.ReplaceDirectory(directory);
                nextRows.Add(existing);
                continue;
            }

            nextRows.Add(new AcpRemoteDirectoryRowViewModel(directory, this));
        }

        if (_editingRemoteDirectory is { IsNew: true } draftRow
            && !nextRows.Contains(draftRow))
        {
            nextRows.Add(draftRow);
        }

        for (var i = RemoteDirectoryRows.Count - 1; i >= 0; i--)
        {
            if (!nextRows.Contains(RemoteDirectoryRows[i]))
            {
                if (ReferenceEquals(_editingRemoteDirectory, RemoteDirectoryRows[i]))
                {
                    SetEditingRemoteDirectory(null);
                }

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

        RefreshRemoteDirectoryCommandStates();
    }

    internal void BeginEditRemoteDirectory(AcpRemoteDirectoryRowViewModel row)
    {
        if (row == null || (_editingRemoteDirectory is not null && !ReferenceEquals(_editingRemoteDirectory, row)))
        {
            return;
        }

        SetEditingRemoteDirectory(row);
        row.BeginEditing();
    }

    internal bool CanBeginEditRemoteDirectory(AcpRemoteDirectoryRowViewModel row)
        => row is not null && (_editingRemoteDirectory is null || ReferenceEquals(_editingRemoteDirectory, row));

    internal Task SaveRemoteDirectoryAsync(AcpRemoteDirectoryRowViewModel row)
    {
        if (row == null)
        {
            return Task.CompletedTask;
        }

        var displayName = row.DisplayNameDraft?.Trim() ?? string.Empty;
        var remotePath = row.RemotePathDraft?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(remotePath) || !ProtocolPathRules.IsAbsolutePath(remotePath))
        {
            row.SetValidationMessage(_localizer["AcpRemoteDirectories_SaveValidationRemotePathRequired"]);
            SetEditingRemoteDirectory(row);
            return Task.CompletedTask;
        }

        var updated = new AgentRemoteDirectory
        {
            DirectoryId = row.DirectoryId ?? Guid.NewGuid().ToString("N"),
            DisplayName = displayName,
            RemotePath = remotePath
        };

        var index = FindRemoteDirectoryIndex(updated.DirectoryId);
        if (index < 0)
        {
            _preferences.AgentRemoteDirectories.Add(updated);
        }
        else
        {
            _preferences.AgentRemoteDirectories[index] = updated;
        }

        RemoveDuplicateRemoteDirectories(updated);

        row.Commit(updated);
        SetEditingRemoteDirectory(null);
        return Task.CompletedTask;
    }

    internal void CancelRemoteDirectoryEdit(AcpRemoteDirectoryRowViewModel row)
    {
        if (row == null)
        {
            return;
        }

        if (row.IsNew)
        {
            RemoveRemoteDirectory(row);
            return;
        }

        row.ResetDraft();
        row.CancelEditing();
        if (ReferenceEquals(_editingRemoteDirectory, row))
        {
            SetEditingRemoteDirectory(null);
        }
    }

    internal void RemoveRemoteDirectory(AcpRemoteDirectoryRowViewModel row)
    {
        if (row == null)
        {
            return;
        }

        var index = FindRemoteDirectoryIndex(row.DirectoryId);
        if (index >= 0)
        {
            _preferences.AgentRemoteDirectories.RemoveAt(index);
        }

        if (ReferenceEquals(_editingRemoteDirectory, row))
        {
            SetEditingRemoteDirectory(null);
        }

        RemoteDirectoryRows.Remove(row);
        RefreshRemoteDirectoryCommandStates();
    }

    private void AddOrReplaceRemoteDirectoryRow(AcpRemoteDirectoryRowViewModel row)
    {
        var existingIndex = RemoteDirectoryRows.ToList().FindIndex(candidate => string.Equals(candidate.DirectoryId, row.DirectoryId, StringComparison.Ordinal));
        if (existingIndex >= 0)
        {
            RemoteDirectoryRows[existingIndex] = row;
            return;
        }

        RemoteDirectoryRows.Add(row);
    }

    private void SetEditingRemoteDirectory(AcpRemoteDirectoryRowViewModel? row)
    {
        if (ReferenceEquals(_editingRemoteDirectory, row))
        {
            return;
        }

        if (_editingRemoteDirectory is not null)
        {
            _editingRemoteDirectory.IsEditing = false;
        }

        _editingRemoteDirectory = row;

        if (_editingRemoteDirectory is not null)
        {
            _editingRemoteDirectory.IsEditing = true;
        }

        AddRemoteDirectoryCommand.NotifyCanExecuteChanged();
        RefreshRemoteDirectoryCommandStates();
    }

    private int FindRemoteDirectoryIndex(string directoryId)
    {
        for (var i = 0; i < _preferences.AgentRemoteDirectories.Count; i++)
        {
            if (string.Equals(_preferences.AgentRemoteDirectories[i].DirectoryId, directoryId, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private void RemoveDuplicateRemoteDirectories(AgentRemoteDirectory current)
    {
        for (var i = _preferences.AgentRemoteDirectories.Count - 1; i >= 0; i--)
        {
            var candidate = _preferences.AgentRemoteDirectories[i];
            if (candidate is null
                || string.Equals(candidate.DirectoryId, current.DirectoryId, StringComparison.Ordinal)
                || !PathsEqual(candidate.RemotePath, current.RemotePath))
            {
                continue;
            }

            _preferences.AgentRemoteDirectories.RemoveAt(i);
        }
    }

    private static bool PathsEqual(string? left, string? right)
    {
        var normalizedLeft = NormalizePath(left);
        var normalizedRight = NormalizePath(right);
        var comparison = UsesCaseInsensitivePathSemantics(normalizedLeft) || UsesCaseInsensitivePathSemantics(normalizedRight)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(normalizedLeft, normalizedRight, comparison);
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return path.Trim().Replace('\\', '/').TrimEnd('/');
    }

    private static bool UsesCaseInsensitivePathSemantics(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return ProtocolPathRules.IsAbsolutePath(path)
            && (path.StartsWith(@"\\", StringComparison.Ordinal)
                || (path.Length >= 3
                    && char.IsLetter(path[0])
                    && path[1] == ':'
                    && path[2] == '/'));
    }

    private void RefreshRemoteDirectoryCommandStates()
    {
        foreach (var row in RemoteDirectoryRows)
        {
            row.NotifyCommandStatesChanged();
        }
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
    private string _validationMessage = string.Empty;
    private string _displayNameDraft = string.Empty;
    private string _remotePathDraft = string.Empty;

    internal AcpRemoteDirectoryRowViewModel(AgentRemoteDirectory directory, AcpConnectionSettingsViewModel owner)
    {
        Directory = directory ?? throw new ArgumentNullException(nameof(directory));
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        DirectoryId = directory.DirectoryId;
        _displayName = directory.DisplayName;
        _remotePath = directory.RemotePath;
        ResetDraft();
        BeginEditCommand = new RelayCommand(BeginEdit, CanBeginEdit);
        SaveCommand = new AsyncRelayCommand(SaveAsync, CanSave);
        CancelCommand = new RelayCommand(Cancel, CanCancel);
        RemoveCommand = new RelayCommand(Remove);
    }

    internal AgentRemoteDirectory Directory { get; private set; }

    internal string DirectoryId { get; private set; }
    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private bool _isNew;

    [ObservableProperty]
    private string _displayName;

    [ObservableProperty]
    private string _remotePath;

    public string DisplayNameDraft
    {
        get => _displayNameDraft;
        set => SetProperty(ref _displayNameDraft, value);
    }

    public string RemotePathDraft
    {
        get => _remotePathDraft;
        set => SetProperty(ref _remotePathDraft, value);
    }

    public string ValidationMessage
    {
        get => _validationMessage;
        private set => SetProperty(ref _validationMessage, value);
    }

    public IRelayCommand BeginEditCommand { get; }

    public IAsyncRelayCommand SaveCommand { get; }

    public IRelayCommand CancelCommand { get; }

    public IRelayCommand RemoveCommand { get; }

    internal void ReplaceDirectory(AgentRemoteDirectory directory)
    {
        Directory = directory ?? throw new ArgumentNullException(nameof(directory));
        DirectoryId = directory.DirectoryId;
        DisplayName = directory.DisplayName;
        RemotePath = directory.RemotePath;
        if (!IsEditing)
        {
            ResetDraft();
        }
    }

    internal void BeginEditing()
    {
        ValidationMessage = string.Empty;
        if (!IsEditing)
        {
            DisplayNameDraft = DisplayName;
            RemotePathDraft = RemotePath;
        }

        IsEditing = true;
        NotifyCommandStatesChanged();
    }

    internal void ResetDraft()
    {
        DisplayNameDraft = DisplayName;
        RemotePathDraft = RemotePath;
        ValidationMessage = string.Empty;
    }

    internal void Commit(AgentRemoteDirectory directory)
    {
        Directory = directory ?? throw new ArgumentNullException(nameof(directory));
        DirectoryId = directory.DirectoryId;
        DisplayName = directory.DisplayName;
        RemotePath = directory.RemotePath;
        DisplayNameDraft = directory.DisplayName;
        RemotePathDraft = directory.RemotePath;
        ValidationMessage = string.Empty;
        IsNew = false;
        IsEditing = false;

        NotifyCommandStatesChanged();
    }

    internal void CancelEditing()
    {
        IsEditing = false;
        ValidationMessage = string.Empty;
        NotifyCommandStatesChanged();
    }

    internal void SetValidationMessage(string message)
    {
        ValidationMessage = message ?? string.Empty;
    }

    internal void NotifyCommandStatesChanged()
    {
        BeginEditCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    private void BeginEdit() => _owner.BeginEditRemoteDirectory(this);

    private Task SaveAsync() => _owner.SaveRemoteDirectoryAsync(this);

    private void Cancel() => _owner.CancelRemoteDirectoryEdit(this);

    private void Remove() => _owner.RemoveRemoteDirectory(this);

    public string SummaryTitle => string.IsNullOrWhiteSpace(DisplayName) ? RemotePath : DisplayName;

    private bool CanBeginEdit() => !IsEditing && _owner.CanBeginEditRemoteDirectory(this);

    private bool CanSave() => IsEditing;

    private bool CanCancel() => IsEditing;

    partial void OnDisplayNameChanged(string value) => OnPropertyChanged(nameof(SummaryTitle));

    partial void OnRemotePathChanged(string value) => OnPropertyChanged(nameof(SummaryTitle));
}
