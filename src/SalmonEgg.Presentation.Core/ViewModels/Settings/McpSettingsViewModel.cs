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
    private readonly SemaphoreSlim _persistenceLock = new(1, 1);
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
            EditingServer = null;
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
                    SaveEnabledStateAsync);
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
                CloseEditorForPersistedName(server.PersistedName);
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
        if (TryFindNestedMcpServers(root, out var nestedServers))
        {
            return ParseServerContainer(nestedServers, remove);
        }

        return ParseServerContainer(root, remove);
    }

    private static List<McpServerRowViewModel> ParseServerContainer(
        JsonElement serversElement,
        Action<McpServerRowViewModel>? remove)
        => serversElement.ValueKind switch
        {
            JsonValueKind.Array => ParseServerArray(serversElement, remove),
            JsonValueKind.Object => ParseServerObject(serversElement, remove),
            _ => throw new JsonException("MCP import root must be an object or array.")
        };

    private static bool TryFindNestedMcpServers(
        JsonElement element,
        out JsonElement serversElement)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (element.TryGetProperty("mcpServers", out var mcpServers))
                {
                    serversElement = mcpServers;
                    return true;
                }

                foreach (var property in element.EnumerateObject())
                {
                    if (TryFindNestedMcpServers(property.Value, out serversElement))
                    {
                        return true;
                    }
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    if (TryFindNestedMcpServers(item, out serversElement))
                    {
                        return true;
                    }
                }

                break;
        }

        serversElement = default;
        return false;
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
        var enabled = ReadEnabled(server);

        var row = type.ToLowerInvariant() switch
        {
            "stdio" => new McpServerRowViewModel(remove ?? NoopRemove)
            {
                Enabled = enabled.Value,
                Name = name,
                Transport = McpServerTransport.Stdio,
                IsDetailsExpanded = true,
                Command = ReadRequiredString(server, "command"),
                ArgumentsText = string.Join(" ", ReadStringArray(server, "args")),
                EnvironmentText = JoinNameValuePairs(ReadNameValuePairs(server, "env"))
            },
            "http" or "streamable-http" or "streamable_http" => new McpServerRowViewModel(remove ?? NoopRemove)
            {
                Enabled = enabled.Value,
                Name = name,
                Transport = McpServerTransport.Http,
                IsDetailsExpanded = true,
                Url = ReadRequiredString(server, "url"),
                HeadersText = JoinHeaderPairs(ReadNameValuePairs(server, "headers"))
            },
            "sse" => new McpServerRowViewModel(remove ?? NoopRemove)
            {
                Enabled = enabled.Value,
                Name = name,
                Transport = McpServerTransport.Sse,
                IsDetailsExpanded = true,
                Url = ReadRequiredString(server, "url"),
                HeadersText = JoinHeaderPairs(ReadNameValuePairs(server, "headers"))
            },
            _ => throw new JsonException("Unsupported MCP server transport.")
        };

        if (enabled.IsExplicit)
        {
            row.MarkExplicitEnabledSetting();
        }

        return row;
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

    private static (bool Value, bool IsExplicit) ReadEnabled(JsonElement element)
    {
        if (ReadOptionalBoolean(element, "enabled") is { } enabled)
        {
            return (enabled, true);
        }

        return ReadOptionalBoolean(element, "disabled") is { } disabled
            ? (!disabled, true)
            : (true, false);
    }

    private static bool? ReadOptionalBoolean(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            _ => throw new JsonException($"MCP server '{propertyName}' must be a boolean.")
        };
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
        if (imported.HasExplicitEnabledSetting)
        {
            editor.Enabled = imported.Enabled;
        }
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

            if (server.PersistedName is not null && !Servers.Any(candidate => IsRowPersistedMatch(candidate, server.PersistedName)))
            {
                EditingServer = null;
                StatusMessage = _localizer["McpSettings_Removed"];
                return;
            }

            var savedServer = server.ToServer();
            var persisted = await PersistServerAsync(server.PersistedName, savedServer, CancellationToken.None).ConfigureAwait(true);
            if (!persisted)
            {
                EditingServer = null;
                StatusMessage = _localizer["McpSettings_Removed"];
                return;
            }

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

    private async Task SaveEnabledStateAsync(McpServerRowViewModel server)
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

        if (server.PersistedName is not null)
        {
            CloseEditorForPersistedName(server.PersistedName);
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
            SaveEnabledStateAsync);
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
            await PersistRemoveAsync(persistedName, CancellationToken.None).ConfigureAwait(true);
            StatusMessage = _localizer["McpSettings_Removed"];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove MCP server");
            StatusMessage = _localizer["McpSettings_SaveFailed"];
        }
    }

    private async Task<bool> PersistServerAsync(string? persistedName, McpServer server, CancellationToken cancellationToken)
    {
        var skippedStalePersistedRow = false;
        await PersistMutationAsync(
            servers =>
            {
                var index = persistedName is null
                    ? servers.FindIndex(candidate => string.Equals(candidate.Name, server.Name, StringComparison.OrdinalIgnoreCase))
                    : servers.FindIndex(candidate => IsPersistedMatch(candidate, persistedName));

                if (persistedName is not null
                    && index < 0
                    && !Servers.Any(candidate => IsRowPersistedMatch(candidate, persistedName)))
                {
                    skippedStalePersistedRow = true;
                    return servers;
                }

                if (index >= 0)
                {
                    servers[index] = server;
                }
                else
                {
                    servers.Add(server);
                }

                return servers;
            },
            cancellationToken).ConfigureAwait(true);
        return !skippedStalePersistedRow;
    }

    private async Task PersistRemoveAsync(string persistedName, CancellationToken cancellationToken)
    {
        await PersistMutationAsync(
            servers => servers
                .Where(server => !IsPersistedMatch(server, persistedName))
                .ToList(),
            cancellationToken).ConfigureAwait(true);
    }

    private async Task PersistMutationAsync(
        Func<List<McpServer>, List<McpServer>> mutate,
        CancellationToken cancellationToken)
    {
        await _persistenceLock.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            var nextServers = mutate(McpServerJsonConverter.CloneServers(_persistedServers));
            var settings = new McpSettings { Servers = nextServers };
            await _settingsService.SaveAsync(settings, cancellationToken).ConfigureAwait(true);
            _persistedServers = McpServerJsonConverter.CloneServers(nextServers);
        }
        finally
        {
            _persistenceLock.Release();
        }
    }

    private static bool IsPersistedMatch(McpServer server, string persistedName)
        => string.Equals(server.Name, persistedName, StringComparison.OrdinalIgnoreCase);

    private static bool IsRowPersistedMatch(McpServerRowViewModel row, string persistedName)
        => string.Equals(row.PersistedName ?? row.Name, persistedName, StringComparison.OrdinalIgnoreCase);

    private void CloseEditorForPersistedName(string persistedName)
    {
        if (EditingServer is not null && IsRowPersistedMatch(EditingServer, persistedName))
        {
            EditingServer = null;
        }
    }

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
