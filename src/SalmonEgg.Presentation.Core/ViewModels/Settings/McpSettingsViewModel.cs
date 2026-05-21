using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SalmonEgg.Domain.Models.Mcp;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Resources;

namespace SalmonEgg.Presentation.ViewModels.Settings;

public sealed partial class McpSettingsViewModel : ObservableObject
{
    private readonly IMcpSettingsService _settingsService;
    private readonly IPlatformShellService _platformShell;
    private readonly IStringLocalizer<CoreStrings> _localizer;
    private readonly ILogger<McpSettingsViewModel> _logger;
    private List<McpServer> _persistedServers = [];
    private bool _isLoadingRows;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _importStatusMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditorOpen))]
    private McpServerRowViewModel? _editingServer;

    public McpSettingsViewModel(
        IMcpSettingsService settingsService,
        IPlatformShellService platformShell,
        IStringLocalizer<CoreStrings> localizer,
        ILogger<McpSettingsViewModel> logger)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _platformShell = platformShell ?? throw new ArgumentNullException(nameof(platformShell));
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ObservableCollection<McpServerRowViewModel> Servers { get; } = new();

    public bool IsEditorOpen => EditingServer is not null;

    [RelayCommand]
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (IsLoading)
        {
            return;
        }

        try
        {
            IsLoading = true;
            var settings = await _settingsService.LoadAsync(cancellationToken).ConfigureAwait(true);
            _persistedServers = McpServerJsonConverter.CloneServers(settings.Servers);
            _isLoadingRows = true;
            Servers.Clear();
            foreach (var server in settings.Servers)
            {
                var row = McpServerRowViewModel.FromServer(
                    server,
                    RemoveServer,
                    SaveServerAsync,
                    OpenEditor,
                    MarkServerUnsaved,
                    SaveEnabledState);
                row.SetStatusMessage(_localizer["McpSettings_RowSaved"]);
                Servers.Add(row);
            }

            StatusMessage = string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load MCP settings");
            StatusMessage = _localizer["McpSettings_LoadFailed"];
        }
        finally
        {
            _isLoadingRows = false;
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void AddServer()
    {
        var row = new McpServerRowViewModel(RemoveServer, SaveServerAsync)
        {
            Name = "new-mcp-server",
            Transport = McpServerTransport.Stdio,
            IsDetailsExpanded = true
        };
        row.SetStatusMessage(_localizer["McpSettings_RowUnsaved"]);
        row.SetEditedCallback(MarkServerUnsaved);
        EditingServer = row;
    }

    [RelayCommand]
    private void CloseEditor()
    {
        EditingServer = null;
    }

    [RelayCommand]
    private async Task FillEditorFromClipboardAsync()
    {
        var clipboardText = await _platformShell.ReadClipboardTextAsync().ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(clipboardText))
        {
            SetImportStatus("McpSettings_ClipboardEmpty");
            return;
        }

        try
        {
            var imported = ParseImportJson(clipboardText, RemoveServer);
            var importedRow = imported.FirstOrDefault();
            if (importedRow is null)
            {
                SetImportStatus("McpSettings_ImportFailed");
                return;
            }

            if (EditingServer is null)
            {
                AddServer();
            }

            ApplyImportedRowToEditor(EditingServer!, importedRow);
            SetImportStatus("McpSettings_ClipboardFilled");
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Failed to fill MCP editor from clipboard JSON");
            SetImportStatus("McpSettings_ImportFailed");
        }
    }

    [RelayCommand]
    private void RemoveServer(McpServerRowViewModel? server)
    {
        if (server is not null)
        {
            Servers.Remove(server);
            if (server.PersistedName is not null)
            {
                _ = PersistRemovedServerAsync(server.PersistedName);
            }
        }
    }

    private static List<McpServerRowViewModel> ParseImportJson(
        string json,
        Action<McpServerRowViewModel>? remove = null)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException("MCP import JSON is empty.");
        }

        using var document = JsonDocument.Parse(ExtractJsonPayload(json));
        var root = document.RootElement;
        var nestedServers = ParseNestedMcpServers(root, remove);
        if (nestedServers.Count > 0)
        {
            return nestedServers;
        }

        return root.ValueKind switch
        {
            JsonValueKind.Array => ParseServerArray(root, remove),
            JsonValueKind.Object => ParseServerObject(root, remove),
            _ => throw new JsonException("MCP import root must be an object or array.")
        };
    }

    private static List<McpServerRowViewModel> ParseNestedMcpServers(
        JsonElement element,
        Action<McpServerRowViewModel>? remove)
    {
        var result = new List<McpServerRowViewModel>();
        CollectNestedMcpServers(element, remove, result);
        return result;
    }

    private static void CollectNestedMcpServers(
        JsonElement element,
        Action<McpServerRowViewModel>? remove,
        List<McpServerRowViewModel> result)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (element.TryGetProperty("mcpServers", out var mcpServers))
                {
                    TryAppendServerContainer(mcpServers, remove, result);
                }

                foreach (var property in element.EnumerateObject())
                {
                    if (!string.Equals(property.Name, "mcpServers", StringComparison.Ordinal))
                    {
                        CollectNestedMcpServers(property.Value, remove, result);
                    }
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectNestedMcpServers(item, remove, result);
                }

                break;
        }
    }

    private static void TryAppendServerContainer(
        JsonElement serversElement,
        Action<McpServerRowViewModel>? remove,
        List<McpServerRowViewModel> result)
    {
        try
        {
            var parsed = serversElement.ValueKind switch
            {
                JsonValueKind.Array => ParseServerArray(serversElement, remove),
                JsonValueKind.Object => ParseServerObject(serversElement, remove),
                _ => []
            };
            result.AddRange(parsed);
        }
        catch (JsonException)
        {
        }
    }

    private static List<McpServerRowViewModel> ParseServerArray(
        JsonElement serversElement,
        Action<McpServerRowViewModel>? remove)
    {
        var result = new List<McpServerRowViewModel>();
        foreach (var server in serversElement.EnumerateArray())
        {
            if (server.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("MCP server entry must be an object.");
            }

            var fallbackName = ReadOptionalString(server, "name") ?? $"mcp-server-{result.Count + 1}";
            result.Add(ParseServer(server, fallbackName, remove));
        }

        return result;
    }

    private static List<McpServerRowViewModel> ParseServerObject(
        JsonElement serversElement,
        Action<McpServerRowViewModel>? remove)
    {
        if (LooksLikeSingleServer(serversElement))
        {
            var fallbackName = ReadOptionalString(serversElement, "name") ?? "mcp-server";
            return [ParseServer(serversElement, fallbackName, remove)];
        }

        var result = new List<McpServerRowViewModel>();
        foreach (var property in serversElement.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("MCP server entry must be an object.");
            }

            result.Add(ParseServer(property.Value, property.Name, remove));
        }

        return result;
    }

    private static McpServerRowViewModel ParseServer(
        JsonElement server,
        string fallbackName,
        Action<McpServerRowViewModel>? remove)
    {
        var type = ReadOptionalString(server, "type")
            ?? ReadOptionalString(server, "transport")
            ?? (server.TryGetProperty("url", out _) ? "http" : "stdio");
        var name = ReadOptionalString(server, "name") ?? fallbackName;

        return type.ToLowerInvariant() switch
        {
            "stdio" => new McpServerRowViewModel(remove ?? NoopRemove)
            {
                Enabled = true,
                Name = name,
                Transport = McpServerTransport.Stdio,
                IsDetailsExpanded = true,
                Command = ReadRequiredString(server, "command"),
                ArgumentsText = string.Join(" ", ReadStringArray(server, "args")),
                EnvironmentText = JoinNameValuePairs(ReadNameValuePairs(server, "env"))
            },
            "http" or "streamable-http" or "streamable_http" => new McpServerRowViewModel(remove ?? NoopRemove)
            {
                Enabled = true,
                Name = name,
                Transport = McpServerTransport.Http,
                IsDetailsExpanded = true,
                Url = ReadRequiredString(server, "url"),
                HeadersText = JoinHeaderPairs(ReadNameValuePairs(server, "headers"))
            },
            "sse" => new McpServerRowViewModel(remove ?? NoopRemove)
            {
                Enabled = true,
                Name = name,
                Transport = McpServerTransport.Sse,
                IsDetailsExpanded = true,
                Url = ReadRequiredString(server, "url"),
                HeadersText = JoinHeaderPairs(ReadNameValuePairs(server, "headers"))
            },
            _ => throw new JsonException("Unsupported MCP server transport.")
        };
    }

    private static bool LooksLikeSingleServer(JsonElement element)
        => element.TryGetProperty("command", out _)
            || element.TryGetProperty("url", out _)
            || element.TryGetProperty("type", out _)
            || element.TryGetProperty("transport", out _);

    private static string ReadRequiredString(JsonElement element, string propertyName)
    {
        var value = ReadOptionalString(element, propertyName);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new JsonException($"MCP server is missing '{propertyName}'.");
        }

        return value;
    }

    private static string? ReadOptionalString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw new JsonException($"MCP server '{propertyName}' must be a string.");
        }

        return value.GetString();
    }

    private static List<string> ReadStringArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var values) || values.ValueKind == JsonValueKind.Null)
        {
            return [];
        }

        if (values.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException($"MCP server '{propertyName}' must be an array.");
        }

        var result = new List<string>();
        foreach (var value in values.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.String)
            {
                throw new JsonException($"MCP server '{propertyName}' entries must be strings.");
            }

            result.Add(value.GetString() ?? string.Empty);
        }

        return result;
    }

    private static List<KeyValuePair<string, string>> ReadNameValuePairs(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var values) || values.ValueKind == JsonValueKind.Null)
        {
            return [];
        }

        return values.ValueKind switch
        {
            JsonValueKind.Object => ReadNameValueObject(values),
            JsonValueKind.Array => ReadNameValueArray(values, propertyName),
            _ => throw new JsonException($"MCP server '{propertyName}' must be an object or array.")
        };
    }

    private static List<KeyValuePair<string, string>> ReadNameValueObject(JsonElement values)
    {
        var result = new List<KeyValuePair<string, string>>();
        foreach (var property in values.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
            {
                throw new JsonException("MCP name/value object values must be strings.");
            }

            result.Add(new KeyValuePair<string, string>(property.Name, property.Value.GetString() ?? string.Empty));
        }

        return result;
    }

    private static List<KeyValuePair<string, string>> ReadNameValueArray(JsonElement values, string propertyName)
    {
        var result = new List<KeyValuePair<string, string>>();
        foreach (var value in values.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException($"MCP server '{propertyName}' entries must be objects.");
            }

            result.Add(new KeyValuePair<string, string>(
                ReadRequiredString(value, "name"),
                ReadRequiredString(value, "value")));
        }

        return result;
    }

    private static string JoinNameValuePairs(IReadOnlyList<KeyValuePair<string, string>> values)
        => string.Join(Environment.NewLine, values.Select(value => $"{value.Key}={value.Value}"));

    private static string JoinHeaderPairs(IReadOnlyList<KeyValuePair<string, string>> values)
        => string.Join(Environment.NewLine, values.Select(value => $"{value.Key}: {value.Value}"));

    private static readonly Action<McpServerRowViewModel> NoopRemove = static _ => { };

    private static string ExtractJsonPayload(string text)
    {
        var payload = text.Trim();
        if (payload.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLineEnd = payload.IndexOf('\n');
            if (firstLineEnd >= 0)
            {
                payload = payload[(firstLineEnd + 1)..].Trim();
            }

            var fenceStart = payload.LastIndexOf("```", StringComparison.Ordinal);
            if (fenceStart >= 0)
            {
                payload = payload[..fenceStart].Trim();
            }
        }

        var objectStart = payload.IndexOf('{');
        var arrayStart = payload.IndexOf('[');
        var start = objectStart >= 0 && arrayStart >= 0
            ? Math.Min(objectStart, arrayStart)
            : Math.Max(objectStart, arrayStart);
        if (start < 0)
        {
            return payload;
        }

        var endChar = payload[start] == '{' ? '}' : ']';
        var end = payload.LastIndexOf(endChar);
        return end > start ? payload[start..(end + 1)] : payload;
    }

    private void SetImportStatus(string resourceKey)
    {
        ImportStatusMessage = _localizer[resourceKey];
    }

    private void ApplyImportedRowToEditor(McpServerRowViewModel editor, McpServerRowViewModel imported)
    {
        editor.Name = imported.Name;
        editor.Transport = imported.Transport;
        editor.Command = imported.Command;
        editor.ArgumentsText = imported.ArgumentsText;
        editor.EnvironmentText = imported.EnvironmentText;
        editor.Url = imported.Url;
        editor.HeadersText = imported.HeadersText;
        editor.SetStatusMessage(_localizer["McpSettings_RowUnsaved"]);
    }

    private void OpenEditor(McpServerRowViewModel server)
    {
        EditingServer = server.CreateEditorCopy(RemoveServer, SaveServerAsync, MarkServerUnsaved);
    }

    private void MarkServerUnsaved(McpServerRowViewModel server)
    {
        server.SetStatusMessage(_localizer["McpSettings_RowUnsaved"]);
    }

    private async Task SaveServerAsync(McpServerRowViewModel server)
    {
        try
        {
            var validationKey = GetValidationResourceKey(server);
            if (validationKey is not null)
            {
                var message = _localizer[validationKey];
                server.SetStatusMessage(message);
                StatusMessage = message;
                return;
            }

            var savedServer = server.ToServer();
            var nextServers = ReplacePersistedServer(server.PersistedName, savedServer);
            await PersistServersAsync(nextServers, CancellationToken.None).ConfigureAwait(true);
            var savedMessage = _localizer["McpSettings_RowSaved"];
            var savedRow = CreateListRow(savedServer, savedMessage);
            ReplaceListRow(server.PersistedName, savedRow);
            server.MarkClean(savedServer.Name, savedMessage);
            EditingServer = null;
            StatusMessage = _localizer["McpSettings_Saved"];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save MCP settings");
            var message = _localizer["McpSettings_SaveFailed"];
            server.SetStatusMessage(message);
            StatusMessage = message;
        }
    }

    private async void SaveEnabledState(McpServerRowViewModel server)
    {
        if (_isLoadingRows)
        {
            return;
        }

        if (server.Enabled)
        {
            var validationKey = GetValidationResourceKey(server);
            if (validationKey is not null)
            {
                server.SetEnabledFromStore(false);
                var message = _localizer[validationKey];
                server.SetStatusMessage(message);
                StatusMessage = message;
                return;
            }
        }

        await SaveServerAsync(server).ConfigureAwait(true);
    }

    private McpServerRowViewModel CreateListRow(McpServer server, string statusMessage)
    {
        var row = McpServerRowViewModel.FromServer(
            server,
            RemoveServer,
            SaveServerAsync,
            OpenEditor,
            MarkServerUnsaved,
            SaveEnabledState);
        row.SetStatusMessage(statusMessage);
        return row;
    }

    private void ReplaceListRow(string? persistedName, McpServerRowViewModel row)
    {
        var index = persistedName is null
            ? Servers.ToList().FindIndex(candidate => string.Equals(candidate.Name, row.Name, StringComparison.OrdinalIgnoreCase))
            : Servers.ToList().FindIndex(candidate => string.Equals(candidate.PersistedName ?? candidate.Name, persistedName, StringComparison.OrdinalIgnoreCase));

        if (index >= 0)
        {
            Servers[index] = row;
        }
        else
        {
            Servers.Add(row);
        }
    }

    private async Task PersistRemovedServerAsync(string persistedName)
    {
        try
        {
            var nextServers = _persistedServers
                .Where(server => !IsPersistedMatch(server, persistedName))
                .ToList();
            await PersistServersAsync(nextServers, CancellationToken.None).ConfigureAwait(true);
            StatusMessage = _localizer["McpSettings_Removed"];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove MCP server");
            StatusMessage = _localizer["McpSettings_SaveFailed"];
        }
    }

    private async Task PersistServersAsync(List<McpServer> servers, CancellationToken cancellationToken)
    {
        var settings = new McpSettings { Servers = servers };
        await _settingsService.SaveAsync(settings, cancellationToken).ConfigureAwait(true);
        _persistedServers = McpServerJsonConverter.CloneServers(servers);
    }

    private List<McpServer> ReplacePersistedServer(string? persistedName, McpServer server)
    {
        var nextServers = McpServerJsonConverter.CloneServers(_persistedServers);
        var index = persistedName is null
            ? nextServers.FindIndex(candidate => string.Equals(candidate.Name, server.Name, StringComparison.OrdinalIgnoreCase))
            : nextServers.FindIndex(candidate => IsPersistedMatch(candidate, persistedName));

        if (index >= 0)
        {
            nextServers[index] = server;
        }
        else
        {
            nextServers.Add(server);
        }

        return nextServers;
    }

    private static bool IsPersistedMatch(McpServer server, string persistedName)
        => string.Equals(server.Name, persistedName, StringComparison.OrdinalIgnoreCase);

    private static string? GetValidationResourceKey(McpServerRowViewModel server)
    {
        if (string.IsNullOrWhiteSpace(server.Name))
        {
            return "McpSettings_SaveValidationNameRequired";
        }

        if (!server.Enabled)
        {
            return null;
        }

        if (server.Transport == McpServerTransport.Stdio && string.IsNullOrWhiteSpace(server.Command))
        {
            return "McpSettings_SaveValidationCommandRequired";
        }

        return server.Transport is McpServerTransport.Http or McpServerTransport.Sse
            && string.IsNullOrWhiteSpace(server.Url)
            ? "McpSettings_SaveValidationUrlRequired"
            : null;
    }
}
