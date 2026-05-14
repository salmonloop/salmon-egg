using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SalmonEgg.Domain.Models.Diagnostics;
using SalmonEgg.Domain.Models.Protocol;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.ViewModels.Chat;

namespace SalmonEgg.Presentation.ViewModels.Settings;

public sealed partial class DiagnosticsSettingsViewModel : ObservableObject
{
    private readonly IAppDataService _paths;
    private readonly IDiagnosticsBundleService _bundle;
    private readonly IPlatformShellService _shell;
    private readonly ILogFileCatalog _logFileCatalog;
    private readonly ILogger<DiagnosticsSettingsViewModel> _logger;

    public ChatViewModel Chat { get; }

    public LiveLogViewerViewModel LiveLogViewer { get; }

    public string AppVersion => System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString()
        ?? System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString()
        ?? "unknown";

    public string ProtocolVersion => new InitializeParams().ProtocolVersion.ToString();

    public string OsDescription => System.Runtime.InteropServices.RuntimeInformation.OSDescription;

    public string FrameworkDescription => System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;

    public string AppDataRootPath => _paths.AppDataRootPath;

    public string LogsDirectoryPath => _paths.LogsDirectoryPath;

    [ObservableProperty]
    private string? _latestLogFilePath;

    public DiagnosticsSettingsViewModel(
        ChatViewModel chatViewModel,
        IAppDataService paths,
        IDiagnosticsBundleService bundle,
        IPlatformShellService shell,
        ILogFileCatalog logFileCatalog,
        LiveLogViewerViewModel liveLogViewer,
        ILogger<DiagnosticsSettingsViewModel> logger)
    {
        Chat = chatViewModel ?? throw new ArgumentNullException(nameof(chatViewModel));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _bundle = bundle ?? throw new ArgumentNullException(nameof(bundle));
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        _logFileCatalog = logFileCatalog ?? throw new ArgumentNullException(nameof(logFileCatalog));
        LiveLogViewer = liveLogViewer ?? throw new ArgumentNullException(nameof(liveLogViewer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [RelayCommand]
    private async Task RefreshLatestLogFileAsync()
    {
        try
        {
            LatestLogFilePath = (await _logFileCatalog.GetLatestAsync(_paths.LogsDirectoryPath).ConfigureAwait(false))?.Path;
        }
        catch
        {
            LatestLogFilePath = null;
        }
    }

    [RelayCommand]
    private Task OpenLogsFolderAsync() => _shell.OpenFolderAsync(_paths.LogsDirectoryPath);

    [RelayCommand]
    private Task OpenAppDataFolderAsync() => _shell.OpenFolderAsync(_paths.AppDataRootPath);

    [RelayCommand]
    private async Task CopyRecentLogSnippetAsync()
    {
        try
        {
            await RefreshLatestLogFileAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(LatestLogFilePath))
            {
                _ = await _shell.CopyToClipboardAsync("No log file found.").ConfigureAwait(false);
                return;
            }

            var text = await _logFileCatalog.ReadTailAsync(LatestLogFilePath, 8000).ConfigureAwait(false);
            if (text is null)
            {
                _ = await _shell.CopyToClipboardAsync("No log file found.").ConfigureAwait(false);
                return;
            }

            var copied = await _shell.CopyToClipboardAsync(text).ConfigureAwait(false);
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

            var path = await _bundle.CreateBundleAsync(snapshot).ConfigureAwait(false);
            await _shell.OpenFileAsync(path).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CreateDiagnosticsBundle failed");
        }
    }
}
