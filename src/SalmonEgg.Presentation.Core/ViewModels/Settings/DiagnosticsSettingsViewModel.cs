using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SalmonEgg.Domain.Models.Diagnostics;
using SalmonEgg.Domain.Models.Protocol;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Resources;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.ViewModels.Chat;

namespace SalmonEgg.Presentation.ViewModels.Settings;

public sealed partial class DiagnosticsSettingsViewModel : ObservableObject
{
    private readonly IAppDataService _paths;
    private readonly IDiagnosticsBundleService _bundle;
    private readonly IPlatformShellService _shell;
    private readonly IPlatformCapabilityService _capabilities;
    private readonly IStorageLocationService _storageLocations;
    private readonly ILogFileCatalog _logFileCatalog;
    private readonly IUiInteractionService _ui;
    private readonly IStringLocalizer<CoreStrings> _localizer;
    private readonly ILogger<DiagnosticsSettingsViewModel> _logger;

    public ChatViewModel Chat { get; }

    public LiveLogViewerViewModel LiveLogViewer { get; }

    public VoiceInputDiagnosticsViewModel VoiceInputDiagnostics { get; }

    public GamepadDiagnosticsViewModel GamepadDiagnostics { get; }

    public string AppVersion => System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString()
        ?? System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString()
        ?? "unknown";

    public string ProtocolVersion => new InitializeParams().ProtocolVersion.ToString();

    public string OsDescription => System.Runtime.InteropServices.RuntimeInformation.OSDescription;

    public string FrameworkDescription => System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;

    public string AppDataRootPath => _paths.AppDataRootPath;

    public string LogsDirectoryPath => _paths.LogsDirectoryPath;

    public bool CanOpenExternalFiles => _capabilities.SupportsExternalFileOpen;

    public bool CanExportLocalFiles => _capabilities.SupportsLocalFileExport;

    [ObservableProperty]
    private string? _latestLogFilePath;

    public DiagnosticsSettingsViewModel(
        ChatViewModel chatViewModel,
        IAppDataService paths,
        IDiagnosticsBundleService bundle,
        IPlatformShellService shell,
        IPlatformCapabilityService capabilities,
        IStorageLocationService storageLocations,
        ILogFileCatalog logFileCatalog,
        IUiInteractionService ui,
        LiveLogViewerViewModel liveLogViewer,
        VoiceInputDiagnosticsViewModel voiceInputDiagnostics,
        GamepadDiagnosticsViewModel gamepadDiagnostics,
        IStringLocalizer<CoreStrings> localizer,
        ILogger<DiagnosticsSettingsViewModel> logger)
    {
        Chat = chatViewModel ?? throw new ArgumentNullException(nameof(chatViewModel));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _bundle = bundle ?? throw new ArgumentNullException(nameof(bundle));
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        _storageLocations = storageLocations ?? throw new ArgumentNullException(nameof(storageLocations));
        _logFileCatalog = logFileCatalog ?? throw new ArgumentNullException(nameof(logFileCatalog));
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        LiveLogViewer = liveLogViewer ?? throw new ArgumentNullException(nameof(liveLogViewer));
        VoiceInputDiagnostics = voiceInputDiagnostics ?? throw new ArgumentNullException(nameof(voiceInputDiagnostics));
        GamepadDiagnostics = gamepadDiagnostics ?? throw new ArgumentNullException(nameof(gamepadDiagnostics));
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [RelayCommand]
    private async Task RefreshLatestLogFileAsync()
    {
        try
        {
            LatestLogFilePath = (await _logFileCatalog.GetLatestAsync(_paths.LogsDirectoryPath))?.Path;
        }
        catch
        {
            LatestLogFilePath = null;
        }
    }

    [RelayCommand]
    private Task OpenLogsFolderAsync() => OpenStorageLocationAsync(AppStorageLocation.Logs);

    [RelayCommand]
    private Task OpenAppDataFolderAsync() => OpenStorageLocationAsync(AppStorageLocation.AppData);

    [RelayCommand]
    private async Task CopyRecentLogSnippetAsync()
    {
        try
        {
            await RefreshLatestLogFileAsync();
            if (string.IsNullOrWhiteSpace(LatestLogFilePath))
            {
                _ = await _shell.CopyToClipboardAsync(_localizer["Diagnostics_NoLogFileFound"]);
                return;
            }

            var text = await _logFileCatalog.ReadTailAsync(LatestLogFilePath, 8000);
            if (text is null)
            {
                _ = await _shell.CopyToClipboardAsync(_localizer["Diagnostics_NoLogFileFound"]);
                return;
            }

            var copied = await _shell.CopyToClipboardAsync(text);
            if (!copied)
            {
                _logger.LogWarning("Clipboard copy is not supported on the current platform.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CopyRecentLogSnippet failed");
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

            var snapshot = new DiagnosticsSnapshot
            {
                AppVersion = AppVersion,
                ProtocolVersion = ProtocolVersion,
                OsDescription = OsDescription,
                FrameworkDescription = FrameworkDescription,
                Properties = new Dictionary<string, string>
                {
                    ["AgentName"] = Chat.AgentName ?? string.Empty,
                    ["AgentVersion"] = Chat.AgentVersion ?? string.Empty,
                    ["IsConnected"] = Chat.IsConnected.ToString(),
                    ["CurrentSessionId"] = Chat.CurrentSessionId ?? string.Empty
                }
            };

            var result = await _bundle.CreateBundleAsync(snapshot);
            if (result.Status is DiagnosticsBundleStatus.Unsupported || string.IsNullOrWhiteSpace(result.Path))
            {
                await NotifyLocalFileExportUnsupportedAsync();
                return;
            }

            if (!await _shell.OpenFileAsync(result.Path))
            {
                await NotifyExternalOpenUnsupportedAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CreateDiagnosticsBundle failed");
        }
    }

    private async Task OpenStorageLocationAsync(AppStorageLocation location)
    {
        if (!await _storageLocations.OpenAsync(location))
        {
            await NotifyExternalOpenUnsupportedAsync();
        }
    }

    private Task NotifyExternalOpenUnsupportedAsync()
        => _ui.ShowInfoAsync(_localizer["Platform_ExternalOpenUnsupported"]);

    private Task NotifyLocalFileExportUnsupportedAsync()
        => _ui.ShowInfoAsync(_localizer["Platform_LocalFileExportUnsupported"]);
}
