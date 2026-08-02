using System;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Domain.Models.Diagnostics;
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
    private readonly IUiInteractionService _ui;
    private readonly IStringLocalizer<CoreStrings> _localizer;
    private readonly ILogger<DataStorageSettingsViewModel> _logger;

    public DataStorageSettingsViewModel(
        AppPreferencesViewModel preferences,
        ChatViewModel chatViewModel,
        CloudConfigSettingsViewModel cloudConfig,
        IAppDataService paths,
        IAppMaintenanceService maintenance,
        IDiagnosticsBundleService diagnostics,
        IPlatformShellService shell,
        IPlatformCapabilityService capabilities,
        IStorageLocationService storageLocations,
        ISessionExportService sessionExport,
        IUiInteractionService ui,
        IStringLocalizer<CoreStrings> localizer,
        ILogger<DataStorageSettingsViewModel> logger)
    {
        Preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        Chat = chatViewModel ?? throw new ArgumentNullException(nameof(chatViewModel));
        CloudConfig = cloudConfig ?? throw new ArgumentNullException(nameof(cloudConfig));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _maintenance = maintenance ?? throw new ArgumentNullException(nameof(maintenance));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        _storageLocations = storageLocations ?? throw new ArgumentNullException(nameof(storageLocations));
        _sessionExport = sessionExport ?? throw new ArgumentNullException(nameof(sessionExport));
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public AppPreferencesViewModel Preferences { get; }

    public ChatViewModel Chat { get; }

    public CloudConfigSettingsViewModel CloudConfig { get; }

    public string AppDataRootPath => _paths.AppDataRootPath;

    public string LogsDirectoryPath => _paths.LogsDirectoryPath;

    public string CacheRootPath => _paths.CacheRootPath;

    public string ExportsDirectoryPath => _paths.ExportsDirectoryPath;

    public bool CanOpenExternalFiles => _capabilities.SupportsExternalFileOpen;

    public bool CanExportLocalFiles => _capabilities.SupportsLocalFileExport;

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
                ProtocolVersion = AcpProtocolVersion.Latest.ToString(),
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
            await _ui.ShowInfoAsync(_localizer["DataStorage_CreateDiagnosticsBundleFailed"]).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task ClearCacheAsync()
    {
        try
        {
            await _maintenance.ClearCacheAsync().ConfigureAwait(false);
            await _ui.ShowInfoAsync(_localizer["General_ClearCacheSuccess"]).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ClearCache failed");
            await _ui.ShowInfoAsync(_localizer["General_ClearCacheFailed"]).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task ClearAllLocalDataAsync()
    {
        try
        {
            await _maintenance.ClearAllLocalDataAsync().ConfigureAwait(false);
            await _ui.ShowInfoAsync(_localizer["DataStorage_ClearAllLocalDataSuccess"]).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ClearAllLocalData failed");
            await _ui.ShowInfoAsync(_localizer["DataStorage_ClearAllLocalDataFailed"]).ConfigureAwait(true);
        }
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
                transcript.Select(message => new SessionExportMessage(
                    message.Id,
                    ToExportTimestamp(message.Timestamp),
                    message.IsOutgoing,
                    message.ContentType,
                    message.Title,
                    message.TextContent)).ToList());

            var result = await _sessionExport.ExportAsync(request);
            await OpenExportResultOrNotifyAsync(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ExportCurrentSession failed");
            await _ui.ShowInfoAsync(_localizer["DataStorage_ExportSessionFailed"]).ConfigureAwait(true);
        }
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

    private Task NotifyExternalOpenUnsupportedAsync() =>
        _ui.ShowInfoAsync(_localizer["Platform_ExternalOpenUnsupported"]);

    private Task NotifyLocalFileExportUnsupportedAsync() =>
        _ui.ShowInfoAsync(_localizer["Platform_LocalFileExportUnsupported"]);

    private static DateTimeOffset? ToExportTimestamp(DateTime? timestamp)
    {
        if (!timestamp.HasValue)
        {
            // The source message carried no authoritative time; the export must not invent one.
            return null;
        }

        var value = timestamp.Value;
        var utc = value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();
        return new DateTimeOffset(utc);
    }
}
