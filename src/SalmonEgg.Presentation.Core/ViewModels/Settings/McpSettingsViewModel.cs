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
    private readonly IStringLocalizer<CoreStrings> _localizer;
    private readonly ILogger<McpSettingsViewModel> _logger;
    private bool _suppressEnabledPersistence;
    private int _enabledSaveVersion;

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isImportPanelOpen;

    [ObservableProperty]
    private string _importJsonText = string.Empty;

    [ObservableProperty]
    private string _importStatusMessage = string.Empty;

    public McpSettingsViewModel(
        IMcpSettingsService settingsService,
        IStringLocalizer<CoreStrings> localizer,
        ILogger<McpSettingsViewModel> logger)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ObservableCollection<McpServerRowViewModel> Servers { get; } = new();

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
            _suppressEnabledPersistence = true;
            try
            {
                IsEnabled = settings.IsEnabled;
            }
            finally
            {
                _suppressEnabledPersistence = false;
            }

            Servers.Clear();
            foreach (var server in settings.Servers)
            {
                Servers.Add(McpServerRowViewModel.FromServer(server, RemoveServer));
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
            IsLoading = false;
        }
    }

    partial void OnIsEnabledChanged(bool value)
    {
        if (_suppressEnabledPersistence)
        {
            return;
        }

        _ = SaveEnabledStateAsync(value, Interlocked.Increment(ref _enabledSaveVersion));
    }

    [RelayCommand]
    private void AddServer()
    {
        Servers.Add(new McpServerRowViewModel(RemoveServer)
        {
            Name = "new-mcp-server",
            Transport = McpServerTransport.Stdio
        });
    }

    [RelayCommand]
    private void OpenImportPanel()
    {
        IsImportPanelOpen = true;
        ImportStatusMessage = string.Empty;
    }

    [RelayCommand]
    private void CollapseImportPanel()
    {
        IsImportPanelOpen = false;
    }

    [RelayCommand]
    private void ClearImportJson()
    {
        ImportJsonText = string.Empty;
        ImportStatusMessage = string.Empty;
    }

    [RelayCommand]
    private Task ImportJsonAsync()
    {
        try
        {
            IsImportPanelOpen = true;
            var imported = ParseImportJson(ImportJsonText, RemoveServer);
            if (imported.Count == 0)
            {
                SetImportStatus("McpSettings_ImportFailed");
                return Task.CompletedTask;
            }

            var replacements = 0;
            foreach (var row in imported)
            {
                var existing = Servers.FirstOrDefault(server =>
                    string.Equals(server.Name, row.Name, StringComparison.OrdinalIgnoreCase));
                if (existing is null)
                {
                    Servers.Add(row);
                    continue;
                }

                var index = Servers.IndexOf(existing);
                Servers[index] = row;
                replacements++;
            }

            SetImportStatus(replacements > 0
                ? "McpSettings_ImportSucceededWithReplacements"
                : "McpSettings_ImportSucceeded");
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Failed to import MCP settings from JSON");
            SetImportStatus("McpSettings_ImportFailed");
        }

        return Task.CompletedTask;
    }

    [RelayCommand]
    private void RemoveServer(McpServerRowViewModel? server)
    {
        if (server is not null)
        {
            Servers.Remove(server);
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

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var serversElement = root.TryGetProperty("mcpServers", out var mcpServers)
            ? mcpServers
            : root;

        return serversElement.ValueKind switch
        {
            JsonValueKind.Array => ParseServerArray(serversElement, remove),
            JsonValueKind.Object => ParseServerObject(serversElement, remove),
            _ => throw new JsonException("MCP import root must be an object or array.")
        };
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

            result.Add(ParseServer(server, ReadRequiredString(server, "name"), remove));
        }

        return result;
    }

    private static List<McpServerRowViewModel> ParseServerObject(
        JsonElement serversElement,
        Action<McpServerRowViewModel>? remove)
    {
        if (LooksLikeSingleServer(serversElement))
        {
            return [ParseServer(serversElement, ReadRequiredString(serversElement, "name"), remove)];
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
                Name = name,
                Transport = McpServerTransport.Stdio,
                Command = ReadRequiredString(server, "command"),
                ArgumentsText = string.Join(" ", ReadStringArray(server, "args")),
                EnvironmentText = JoinNameValuePairs(ReadNameValuePairs(server, "env"))
            },
            "http" or "streamable-http" or "streamable_http" => new McpServerRowViewModel(remove ?? NoopRemove)
            {
                Name = name,
                Transport = McpServerTransport.Http,
                Url = ReadRequiredString(server, "url"),
                HeadersText = JoinHeaderPairs(ReadNameValuePairs(server, "headers"))
            },
            "sse" => new McpServerRowViewModel(remove ?? NoopRemove)
            {
                Name = name,
                Transport = McpServerTransport.Sse,
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

    private void SetImportStatus(string resourceKey)
    {
        ImportStatusMessage = _localizer[resourceKey];
        StatusMessage = ImportStatusMessage;
    }

    private async Task SaveEnabledStateAsync(bool isEnabled, int version)
    {
        try
        {
            var settings = await _settingsService.LoadAsync().ConfigureAwait(false);
            if (version != Volatile.Read(ref _enabledSaveVersion))
            {
                return;
            }

            settings.IsEnabled = isEnabled;
            await _settingsService.SaveAsync(settings).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist MCP enabled state");
            StatusMessage = _localizer["McpSettings_SaveFailed"];
        }
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (HasIncompleteServerDraft())
            {
                StatusMessage = _localizer["McpSettings_SaveValidationFailed"];
                return;
            }

            var settings = new McpSettings
            {
                IsEnabled = IsEnabled,
                Servers = Servers.Select(server => server.ToServer()).ToList()
            };

            await _settingsService.SaveAsync(settings, cancellationToken).ConfigureAwait(true);
            StatusMessage = _localizer["McpSettings_Saved"];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save MCP settings");
            StatusMessage = _localizer["McpSettings_SaveFailed"];
        }
    }

    private bool HasIncompleteServerDraft()
        => Servers.Any(server =>
            string.IsNullOrWhiteSpace(server.Name)
            || (server.Transport == McpServerTransport.Stdio && string.IsNullOrWhiteSpace(server.Command))
            || (server.Transport is McpServerTransport.Http or McpServerTransport.Sse
                && string.IsNullOrWhiteSpace(server.Url)));
}
