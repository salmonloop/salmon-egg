using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SalmonEgg.Domain.Models.Diagnostics;
using SalmonEgg.Domain.Models.Protocol;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.ViewModels.Chat;

namespace SalmonEgg.Presentation.ViewModels.Settings;

public partial class DataStorageSettingsViewModel : ObservableObject
{
    private readonly IAppDataService _paths;
    private readonly IAppMaintenanceService _maintenance;
    private readonly IDiagnosticsBundleService _diagnostics;
    private readonly IPlatformShellService _shell;
    private readonly ILogger<DataStorageSettingsViewModel> _logger;

    public AppPreferencesViewModel Preferences { get; }
    public ChatViewModel Chat { get; }

    public string AppDataRootPath => _paths.AppDataRootPath;
    public string LogsDirectoryPath => _paths.LogsDirectoryPath;
    public string CacheRootPath => _paths.CacheRootPath;
    public string ExportsDirectoryPath => _paths.ExportsDirectoryPath;

    public DataStorageSettingsViewModel(
        AppPreferencesViewModel preferences,
        ChatViewModel chatViewModel,
        IAppDataService paths,
        IAppMaintenanceService maintenance,
        IDiagnosticsBundleService diagnostics,
        IPlatformShellService shell,
        ILogger<DataStorageSettingsViewModel> logger)
    {
        Preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        Chat = chatViewModel ?? throw new ArgumentNullException(nameof(chatViewModel));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _maintenance = maintenance ?? throw new ArgumentNullException(nameof(maintenance));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [RelayCommand]
    private Task OpenAppDataFolderAsync() => _shell.OpenFolderAsync(_paths.AppDataRootPath);

    [RelayCommand]
    private Task OpenCacheFolderAsync()
    {
        Directory.CreateDirectory(_paths.CacheRootPath);
        return _shell.OpenFolderAsync(_paths.CacheRootPath);
    }

    [RelayCommand]
    private Task OpenLogsFolderAsync() => _shell.OpenFolderAsync(_paths.LogsDirectoryPath);

    [RelayCommand]
    private Task OpenExportsFolderAsync()
    {
        Directory.CreateDirectory(_paths.ExportsDirectoryPath);
        return _shell.OpenFolderAsync(_paths.ExportsDirectoryPath);
    }

    [RelayCommand]
    private async Task ExportCurrentSessionMarkdownAsync()
    {
        await ExportCurrentSessionAsync("md").ConfigureAwait(false);
    }

    [RelayCommand]
    private async Task ExportCurrentSessionJsonAsync()
    {
        await ExportCurrentSessionAsync("json").ConfigureAwait(false);
    }

    private async Task ExportCurrentSessionAsync(string format)
    {
        try
        {
            Directory.CreateDirectory(_paths.ExportsDirectoryPath);
            var sessionId = Chat.CurrentSessionId ?? "no-session";
            var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
            var fileName = $"session-{sessionId}-{timestamp}.{format}";
            fileName = SanitizeFileName(fileName);
            var path = Path.Combine(_paths.ExportsDirectoryPath, fileName);

            if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
            {
                var payload = new ExportPayload(
                    Chat.CurrentSessionId,
                    Chat.AgentName,
                    Chat.AgentVersion,
                    DateTimeOffset.UtcNow,
                    Chat.MessageHistory.Select(m => new ExportMessage(
                        m.Id,
                        m.Timestamp,
                        m.IsOutgoing,
                        m.ContentType,
                        m.Title,
                        m.TextContent)).ToList());

                var json = JsonSerializer.Serialize(payload, ExportPayloadJsonContext.Default.ExportPayload);
                await File.WriteAllTextAsync(path, json, Encoding.UTF8).ConfigureAwait(false);
            }
            else
            {
                var sb = new StringBuilder();
                sb.AppendLine($"# Session Export");
                sb.AppendLine();
                sb.AppendLine($"- SessionId: `{Chat.CurrentSessionId}`");
                sb.AppendLine($"- Agent: `{Chat.AgentName}` `{Chat.AgentVersion}`");
                sb.AppendLine($"- ExportedAt(UTC): `{DateTimeOffset.UtcNow:O}`");
                sb.AppendLine();

                foreach (var m in Chat.MessageHistory)
                {
                    var who = m.IsOutgoing ? "User" : "Agent";
                    sb.AppendLine($"## {who} · {m.Timestamp:O}");
                    if (!string.IsNullOrWhiteSpace(m.Title))
                    {
                        sb.AppendLine();
                        sb.AppendLine($"**{m.Title}**");
                    }

                    if (!string.IsNullOrWhiteSpace(m.TextContent))
                    {
                        sb.AppendLine();
                        sb.AppendLine(m.TextContent);
                    }

                    sb.AppendLine();
                }

                await File.WriteAllTextAsync(path, sb.ToString(), Encoding.UTF8).ConfigureAwait(false);
            }

            await _shell.OpenFileAsync(path).ConfigureAwait(false);
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
            var snapshot = new DiagnosticsSnapshot
            {
                AppVersion = typeof(App).Assembly.GetName().Version?.ToString() ?? "unknown",
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

            var zipPath = await _diagnostics.CreateBundleAsync(snapshot).ConfigureAwait(false);
            await _shell.OpenFileAsync(zipPath).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CreateDiagnosticsBundle failed");
        }
    }

    [RelayCommand]
    private async Task ClearCacheAsync()
    {
        await _maintenance.ClearCacheAsync().ConfigureAwait(false);
    }

    [RelayCommand]
    private async Task ClearAllLocalDataAsync()
    {
        await _maintenance.ClearAllLocalDataAsync().ConfigureAwait(false);
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }
        return name;
    }
}

internal sealed record ExportMessage(
    string Id,
    DateTimeOffset Timestamp,
    bool IsOutgoing,
    string? ContentType,
    string? Title,
    string? Text);

internal sealed record ExportPayload(
    string? SessionId,
    string? AgentName,
    string? AgentVersion,
    DateTimeOffset ExportedAtUtc,
    List<ExportMessage> Messages);

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(ExportPayload))]
internal partial class ExportPayloadJsonContext : JsonSerializerContext
{
}
