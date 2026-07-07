using System;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.Diagnostics;
using SalmonEgg.Domain.Models.Protocol;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Resources;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.ViewModels.Chat;

namespace SalmonEgg.Presentation.ViewModels.Settings;

public partial class DataStorageSettingsViewModel : ObservableObject
{
    private readonly IAppDataService _paths;
    private readonly IAppMaintenanceService _maintenance;
    private readonly IDiagnosticsBundleService _diagnostics;
    private readonly IPlatformShellService _shell;
    private readonly IPlatformCapabilityService _capabilities;
    private readonly IStorageLocationService _storageLocations;
    private readonly ISessionExportService _sessionExport;
    private readonly ICloudConfigSyncService _cloudConfigSync;
    private readonly IUiInteractionService _ui;
    private readonly IStringLocalizer<CoreStrings> _localizer;
    private readonly ILogger<DataStorageSettingsViewModel> _logger;

    public AppPreferencesViewModel Preferences { get; }
    public ChatViewModel Chat { get; }

    public string AppDataRootPath => _paths.AppDataRootPath;
    public string LogsDirectoryPath => _paths.LogsDirectoryPath;
    public string CacheRootPath => _paths.CacheRootPath;
    public string ExportsDirectoryPath => _paths.ExportsDirectoryPath;

    public bool CanOpenExternalFiles => _capabilities.SupportsExternalFileOpen;

    public bool CanExportLocalFiles => _capabilities.SupportsLocalFileExport;

    public bool IsCloudConfigSyncConfigured =>
        _cloudConfigSync.Providers.Any(provider => provider.ProviderId == "onedrive" && provider.IsConfigured);

    [ObservableProperty]
    private bool _isCloudConfigSyncBusy;

    [ObservableProperty]
    private bool _isCloudConfigSyncEnabled;

    [ObservableProperty]
    private string _cloudConfigSyncStatusText = string.Empty;

    [ObservableProperty]
    private string _cloudConfigSyncLastSyncText = string.Empty;

    [ObservableProperty]
    private string _cloudConfigSyncErrorText = string.Empty;

    public DataStorageSettingsViewModel(
        AppPreferencesViewModel preferences,
        ChatViewModel chatViewModel,
        IAppDataService paths,
        IAppMaintenanceService maintenance,
        IDiagnosticsBundleService diagnostics,
        IPlatformShellService shell,
        IPlatformCapabilityService capabilities,
        IStorageLocationService storageLocations,
        ISessionExportService sessionExport,
        ICloudConfigSyncService cloudConfigSync,
        IUiInteractionService ui,
        IStringLocalizer<CoreStrings> localizer,
        ILogger<DataStorageSettingsViewModel> logger)
    {
        Preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        Chat = chatViewModel ?? throw new ArgumentNullException(nameof(chatViewModel));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _maintenance = maintenance ?? throw new ArgumentNullException(nameof(maintenance));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        _storageLocations = storageLocations ?? throw new ArgumentNullException(nameof(storageLocations));
        _sessionExport = sessionExport ?? throw new ArgumentNullException(nameof(sessionExport));
        _cloudConfigSync = cloudConfigSync ?? throw new ArgumentNullException(nameof(cloudConfigSync));
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ApplyCloudConfigSyncSettings(Preferences.CloudConfigSync);
    }

    [RelayCommand]
    private Task OpenAppDataFolderAsync() => OpenStorageLocationAsync(AppStorageLocation.AppData);

    [RelayCommand]
    private Task OpenCacheFolderAsync() => OpenStorageLocationAsync(AppStorageLocation.Cache);

    [RelayCommand]
    private Task OpenLogsFolderAsync() => OpenStorageLocationAsync(AppStorageLocation.Logs);

    [RelayCommand]
    private Task OpenExportsFolderAsync() => OpenStorageLocationAsync(AppStorageLocation.Exports);

    [RelayCommand]
    private async Task ExportCurrentSessionMarkdownAsync()
    {
        await ExportCurrentSessionAsync("md");
    }

    [RelayCommand]
    private async Task ExportCurrentSessionJsonAsync()
    {
        await ExportCurrentSessionAsync("json");
    }

    private async Task ExportCurrentSessionAsync(string format)
    {
        try
        {
            if (!CanExportLocalFiles)
            {
                await NotifyLocalFileExportUnsupportedAsync();
                return;
            }

            var transcript = await Chat.GetCurrentSessionTranscriptSnapshotAsync();
            var request = new SessionExportRequest(
                format,
                Chat.CurrentSessionId,
                Chat.AgentName,
                Chat.AgentVersion,
                transcript.Select(m => new SessionExportMessage(
                    m.Id,
                    ToExportTimestamp(m.Timestamp),
                    m.IsOutgoing,
                    m.ContentType,
                    m.Title,
                    m.TextContent)).ToList());

            var result = await _sessionExport.ExportAsync(request);
            await OpenExportResultOrNotifyAsync(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ExportCurrentSession failed");
        }
    }

    [RelayCommand]
    private async Task CreateDiagnosticsBundleAsync()
    {
        try
        {
            if (!CanExportLocalFiles)
            {
                await NotifyLocalFileExportUnsupportedAsync();
                return;
            }

            var appVersion = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString()
                ?? System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString()
                ?? "unknown";
            var snapshot = new DiagnosticsSnapshot
            {
                AppVersion = appVersion,
                ProtocolVersion = new InitializeParams().ProtocolVersion.ToString(),
                OsDescription = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                FrameworkDescription = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                Properties =
                {
                    ["AgentName"] = Chat.AgentName ?? string.Empty,
                    ["AgentVersion"] = Chat.AgentVersion ?? string.Empty,
                    ["IsConnected"] = Chat.IsConnected.ToString(),
                    ["CurrentSessionId"] = Chat.CurrentSessionId ?? string.Empty,
                }
            };

            var result = await _diagnostics.CreateBundleAsync(snapshot);
            await OpenDiagnosticsBundleResultOrNotifyAsync(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CreateDiagnosticsBundle failed");
        }
    }

    [RelayCommand]
    private async Task ClearCacheAsync()
    {
        await _maintenance.ClearCacheAsync();
    }

    [RelayCommand]
    private async Task ClearAllLocalDataAsync()
    {
        await _maintenance.ClearAllLocalDataAsync();
    }

    [RelayCommand]
    private async Task AuthorizeOneDriveCloudSyncAsync()
    {
        await RunCloudSyncOperationAsync(() => _cloudConfigSync.AuthorizeAndSyncAsync("onedrive"));
    }

    [RelayCommand]
    private async Task SyncCloudConfigAsync()
    {
        await RunCloudSyncOperationAsync(() => _cloudConfigSync.SyncNowAsync());
    }

    [RelayCommand]
    private async Task DisconnectCloudConfigAsync()
    {
        await RunCloudSyncOperationAsync(() => _cloudConfigSync.DisconnectAsync());
    }

    private async Task OpenStorageLocationAsync(AppStorageLocation location)
    {
        if (!await _storageLocations.OpenAsync(location))
        {
            await NotifyExternalOpenUnsupportedAsync();
        }
    }

    private async Task OpenFileOrNotifyAsync(string path)
    {
        if (!await _shell.OpenFileAsync(path))
        {
            await NotifyExternalOpenUnsupportedAsync();
        }
    }

    private Task NotifyExternalOpenUnsupportedAsync()
        => _ui.ShowInfoAsync(_localizer["Platform_ExternalOpenUnsupported"]);

    private Task NotifyLocalFileExportUnsupportedAsync()
        => _ui.ShowInfoAsync(_localizer["Platform_LocalFileExportUnsupported"]);

    private async Task RunCloudSyncOperationAsync(Func<Task<CloudConfigSyncResult>> operation)
    {
        try
        {
            IsCloudConfigSyncBusy = true;
            CloudConfigSyncErrorText = string.Empty;
            var result = await operation();
            ApplyCloudConfigSyncResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cloud config sync command failed");
            CloudConfigSyncErrorText = _localizer["DataStorage_CloudSyncStatusFailed"];
            CloudConfigSyncStatusText = CloudConfigSyncErrorText;
        }
        finally
        {
            IsCloudConfigSyncBusy = false;
        }
    }

    private void ApplyCloudConfigSyncSettings(CloudConfigSyncSettings? settings)
    {
        IsCloudConfigSyncEnabled = settings?.Enabled == true;
        CloudConfigSyncStatusText = IsCloudConfigSyncEnabled
            ? _localizer["DataStorage_CloudSyncStatusEnabled"]
            : _localizer["DataStorage_CloudSyncStatusDisabled"];
    }

    private void ApplyCloudConfigSyncResult(CloudConfigSyncResult result)
    {
        IsCloudConfigSyncEnabled = result.Status is CloudConfigSyncStatus.Uploaded
            or CloudConfigSyncStatus.Restored
            or CloudConfigSyncStatus.ConflictRemoteApplied;
        CloudConfigSyncStatusText = result.Status switch
        {
            CloudConfigSyncStatus.Uploaded => _localizer["DataStorage_CloudSyncStatusUploaded"],
            CloudConfigSyncStatus.Restored => _localizer["DataStorage_CloudSyncStatusRestored"],
            CloudConfigSyncStatus.ConflictRemoteApplied => _localizer["DataStorage_CloudSyncStatusConflict"],
            CloudConfigSyncStatus.NotConfigured => _localizer["DataStorage_CloudSyncStatusNotConfigured"],
            CloudConfigSyncStatus.NotAuthorized => _localizer["DataStorage_CloudSyncStatusNotAuthorized"],
            CloudConfigSyncStatus.SignedOut => _localizer["DataStorage_CloudSyncStatusSignedOut"],
            CloudConfigSyncStatus.Disabled => _localizer["DataStorage_CloudSyncStatusDisabled"],
            CloudConfigSyncStatus.Failed => result.UserMessage ?? _localizer["DataStorage_CloudSyncStatusFailed"],
            _ => _localizer["DataStorage_CloudSyncStatusEnabled"]
        };

        CloudConfigSyncErrorText = result.Status is CloudConfigSyncStatus.Failed or CloudConfigSyncStatus.NotConfigured or CloudConfigSyncStatus.NotAuthorized
            ? CloudConfigSyncStatusText
            : string.Empty;

        if (result.LastSyncUtc.HasValue)
        {
            CloudConfigSyncLastSyncText = string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                _localizer["DataStorage_CloudSyncLastSyncFormat"],
                result.LastSyncUtc.Value.ToLocalTime());
        }

        Preferences.SetCloudConfigSyncSettings(new CloudConfigSyncSettings
        {
            Enabled = IsCloudConfigSyncEnabled,
            ProviderId = IsCloudConfigSyncEnabled ? result.ProviderId ?? "onedrive" : string.Empty,
            IncludeSecrets = true
        });
    }

    private async Task OpenExportResultOrNotifyAsync(SessionExportResult result)
    {
        if (result.Status is SessionExportStatus.Unsupported || string.IsNullOrWhiteSpace(result.Path))
        {
            await NotifyLocalFileExportUnsupportedAsync();
            return;
        }

        await OpenFileOrNotifyAsync(result.Path);
    }

    private async Task OpenDiagnosticsBundleResultOrNotifyAsync(DiagnosticsBundleResult result)
    {
        if (result.Status is DiagnosticsBundleStatus.Unsupported || string.IsNullOrWhiteSpace(result.Path))
        {
            await NotifyLocalFileExportUnsupportedAsync();
            return;
        }

        await OpenFileOrNotifyAsync(result.Path);
    }

    private static DateTimeOffset ToExportTimestamp(DateTime timestamp)
    {
        var utc = timestamp.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(timestamp, DateTimeKind.Utc)
            : timestamp.ToUniversalTime();
        return new DateTimeOffset(utc);
    }
}
