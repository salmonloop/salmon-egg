using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Infrastructure.Storage.YamlModels;
using YamlDotNet.Core;

namespace SalmonEgg.Infrastructure.Storage;

/// <summary>
/// 配置管理器实现（YAML + SecureStorage）
/// - 非敏感：YAML 文件（可读/可审计/可 diff）
/// - 敏感：ISecureStorage（平台安全存储的抽象）
/// </summary>
public sealed class ConfigurationManager : IConfigurationService
{
    private const int CurrentSchemaVersion = 1;

    private readonly ISecureStorage _secureStorage;
    private readonly IAppFileStore _fileStore;
    private readonly string _serversDirectory;

    public ConfigurationManager(ISecureStorage secureStorage, IAppFileStore fileStore, IAppDataService appData)
    {
        _secureStorage = secureStorage ?? throw new ArgumentNullException(nameof(secureStorage));
        _fileStore = fileStore ?? throw new ArgumentNullException(nameof(fileStore));
        if (appData is null) throw new ArgumentNullException(nameof(appData));

        _serversDirectory = System.IO.Path.Combine(appData.ConfigRootPath, "servers");
    }

    public async Task SaveConfigurationAsync(ServerConfiguration config)
    {
        if (config is null) throw new ArgumentNullException(nameof(config));
        if (string.IsNullOrWhiteSpace(config.Id)) throw new ArgumentException("Configuration ID cannot be empty", nameof(config));

        var serverPath = GetServerYamlPath(config.Id);
        await EnsureWritableSchemaAsync(serverPath).ConfigureAwait(false);

        var mode = GetAuthenticationMode(config.Authentication);
        await PersistSecretsAsync(config.Id, mode, config.Authentication).ConfigureAwait(false);

        var yamlModel = ToYaml(config, mode);
        var yaml = YamlSerialization.CreateSerializer().Serialize(yamlModel);
        await _fileStore.WriteAllTextAsync(serverPath, yaml).ConfigureAwait(false);
    }

    public async Task<ServerConfiguration?> LoadConfigurationAsync(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Configuration ID cannot be empty", nameof(id));

        var path = GetServerYamlPath(id);
        ServerConfigurationYamlV1 yamlModel;
        try
        {
            var yaml = await _fileStore.ReadAllTextAsync(path).ConfigureAwait(false);
            if (yaml is null)
            {
                return null;
            }

            yamlModel = YamlSerialization.CreateDeserializer().Deserialize<ServerConfigurationYamlV1>(yaml);
        }
        catch (YamlException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }

        if (yamlModel.SchemaVersion <= 0)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(yamlModel.Name))
        {
            return null;
        }

        if (TransportFromString(yamlModel.Transport) != TransportType.Stdio && string.IsNullOrWhiteSpace(yamlModel.ServerUrl))
        {
            return null;
        }

        if (TransportFromString(yamlModel.Transport) == TransportType.Stdio && string.IsNullOrWhiteSpace(yamlModel.StdioCommand))
        {
            return null;
        }

        var config = FromYaml(yamlModel, fallbackId: id);
        await HydrateSecretsAsync(config, yamlModel.Authentication?.Mode).ConfigureAwait(false);
        return config;
    }

    public async Task<IEnumerable<ServerConfiguration>> ListConfigurationsAsync()
    {
        var paths = new List<string>();
        try
        {
            await foreach (var path in _fileStore.EnumerateFilesAsync(_serversDirectory, "*.yaml").ConfigureAwait(false))
            {
                paths.Add(path);
            }
        }
        catch (IOException)
        {
            return Array.Empty<ServerConfiguration>();
        }

        var tasks = paths.Select(async path =>
        {
            try
            {
                var yaml = await _fileStore.ReadAllTextAsync(path).ConfigureAwait(false);
                if (yaml is null)
                {
                    return null;
                }

                var localDeserializer = YamlSerialization.CreateDeserializer();
                var yamlModel = localDeserializer.Deserialize<ServerConfigurationYamlV1>(yaml);
                if (yamlModel.SchemaVersion <= 0)
                {
                    return null;
                }

                if (string.IsNullOrWhiteSpace(yamlModel.Name))
                {
                    return null;
                }

                var transport = TransportFromString(yamlModel.Transport);
                if (transport != TransportType.Stdio && string.IsNullOrWhiteSpace(yamlModel.ServerUrl))
                {
                    return null;
                }

                if (transport == TransportType.Stdio && string.IsNullOrWhiteSpace(yamlModel.StdioCommand))
                {
                    return null;
                }

                return FromYaml(yamlModel, fallbackId: System.IO.Path.GetFileNameWithoutExtension(path));
            }
            catch (Exception)
            {
                // Ignore malformed or unreadable individual files.
                return null;
            }
        });

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);

        return results
            .Where(x => x != null)
            .Select(x => x!)
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task DeleteConfigurationAsync(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Configuration ID cannot be empty", nameof(id));

        var path = GetServerYamlPath(id);
        try
        {
            await _fileStore.DeleteAsync(path).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to delete configuration '{id}'", ex);
        }

        await _secureStorage.DeleteAsync(GetTokenKey(id)).ConfigureAwait(false);
        await _secureStorage.DeleteAsync(GetApiKeyKey(id)).ConfigureAwait(false);
    }

    private static ServerConfigurationYamlV1 ToYaml(ServerConfiguration config, string mode)
    {
        return new ServerConfigurationYamlV1
        {
            SchemaVersion = CurrentSchemaVersion,
            UpdatedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
            Id = config.Id,
            Name = config.Name,
            Transport = TransportToString(config.Transport),
            ServerUrl = config.ServerUrl,
            StdioCommand = config.StdioCommand,
            StdioArgs = config.StdioArgs,
            ConnectionTimeoutSeconds = config.ConnectionTimeout,
            Authentication = new AuthenticationYamlV1 { Mode = mode },
            Proxy = config.Proxy is { Enabled: true }
                ? new ProxyYamlV1 { Enabled = true, ProxyUrl = config.Proxy.ProxyUrl ?? string.Empty }
                : new ProxyYamlV1 { Enabled = false, ProxyUrl = string.Empty }
        };
    }

    private static ServerConfiguration FromYaml(ServerConfigurationYamlV1 yamlModel, string fallbackId)
    {
        var config = new ServerConfiguration
        {
            Id = string.IsNullOrWhiteSpace(yamlModel.Id) ? fallbackId : yamlModel.Id,
            Name = yamlModel.Name ?? string.Empty,
            ServerUrl = yamlModel.ServerUrl ?? string.Empty,
            StdioCommand = yamlModel.StdioCommand ?? string.Empty,
            StdioArgs = yamlModel.StdioArgs ?? string.Empty,
            Transport = TransportFromString(yamlModel.Transport),
            ConnectionTimeout = yamlModel.ConnectionTimeoutSeconds > 0 ? yamlModel.ConnectionTimeoutSeconds : 10
        };

        if (yamlModel.Proxy is { Enabled: true })
        {
            config.Proxy = new ProxyConfig
            {
                Enabled = true,
                ProxyUrl = string.IsNullOrWhiteSpace(yamlModel.Proxy.ProxyUrl) ? null : yamlModel.Proxy.ProxyUrl
            };
        }

        return config;
    }

    private async Task HydrateSecretsAsync(ServerConfiguration config, string? mode)
    {
        if (config is null) return;

        mode = (mode ?? "none").Trim().ToLowerInvariant();
        if (mode == "bearer_token")
        {
            var token = await _secureStorage.LoadAsync(GetTokenKey(config.Id)).ConfigureAwait(false);
            config.Authentication = new AuthenticationConfig { Token = token };
            return;
        }

        if (mode == "api_key")
        {
            var apiKey = await _secureStorage.LoadAsync(GetApiKeyKey(config.Id)).ConfigureAwait(false);
            config.Authentication = new AuthenticationConfig { ApiKey = apiKey };
            return;
        }
    }

    private async Task PersistSecretsAsync(string id, string mode, AuthenticationConfig? authentication)
    {
        if (mode == "bearer_token")
        {
            var token = authentication?.Token;
            if (!string.IsNullOrEmpty(token))
            {
                await _secureStorage.SaveAsync(GetTokenKey(id), token).ConfigureAwait(false);
            }
            else
            {
                await _secureStorage.DeleteAsync(GetTokenKey(id)).ConfigureAwait(false);
            }

            await _secureStorage.DeleteAsync(GetApiKeyKey(id)).ConfigureAwait(false);
            return;
        }

        if (mode == "api_key")
        {
            var apiKey = authentication?.ApiKey;
            if (!string.IsNullOrEmpty(apiKey))
            {
                await _secureStorage.SaveAsync(GetApiKeyKey(id), apiKey).ConfigureAwait(false);
            }
            else
            {
                await _secureStorage.DeleteAsync(GetApiKeyKey(id)).ConfigureAwait(false);
            }

            await _secureStorage.DeleteAsync(GetTokenKey(id)).ConfigureAwait(false);
            return;
        }

        await _secureStorage.DeleteAsync(GetTokenKey(id)).ConfigureAwait(false);
        await _secureStorage.DeleteAsync(GetApiKeyKey(id)).ConfigureAwait(false);
    }

    private static string GetAuthenticationMode(AuthenticationConfig? authentication)
    {
        var token = authentication?.Token;
        var apiKey = authentication?.ApiKey;

        var hasToken = !string.IsNullOrWhiteSpace(token);
        var hasApiKey = !string.IsNullOrWhiteSpace(apiKey);

        if (hasToken && hasApiKey)
        {
            throw new InvalidOperationException("Authentication cannot specify both Token and ApiKey.");
        }

        if (hasToken) return "bearer_token";
        if (hasApiKey) return "api_key";
        return "none";
    }

    private static string TransportToString(TransportType transport) =>
        transport switch
        {
            TransportType.Stdio => "stdio",
            TransportType.HttpSse => "http_sse",
            _ => "websocket"
        };

    private static TransportType TransportFromString(string? value)
    {
        value = (value ?? string.Empty).Trim().ToLowerInvariant();
        return value switch
        {
            "stdio" => TransportType.Stdio,
            "http_sse" => TransportType.HttpSse,
            "websocket" => TransportType.WebSocket,
            _ => TransportType.WebSocket
        };
    }

    private string GetServerYamlPath(string id)
    {
        var fileName = GetServerFileName(id);
        return System.IO.Path.Combine(_serversDirectory, fileName + ".yaml");
    }

    private static string GetServerFileName(string id)
    {
        if (IsSafeFileName(id))
        {
            return id;
        }

        var bytes = Encoding.UTF8.GetBytes(id);
        var encoded = Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return "id_" + encoded;
    }

    private static bool IsSafeFileName(string value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch) || ch == '-' || ch == '_') continue;
            return false;
        }
        return true;
    }

    private static string GetTokenKey(string serverId) => $"salmonegg/config/{serverId}/token";

    private static string GetApiKeyKey(string serverId) => $"salmonegg/config/{serverId}/apiKey";

    private async Task EnsureWritableSchemaAsync(string serverPath)
    {
        try
        {
            var yaml = await _fileStore.ReadAllTextAsync(serverPath).ConfigureAwait(false);
            if (yaml is null)
            {
                return;
            }

            var existing = YamlSerialization.CreateDeserializer().Deserialize<ServerConfigurationYamlV1>(yaml);
            if (existing.SchemaVersion > CurrentSchemaVersion)
            {
                throw new InvalidOperationException(
                    $"Configuration schema_version {existing.SchemaVersion} is newer than supported version {CurrentSchemaVersion}. Refusing to overwrite.");
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch
        {
            // If the file exists but can't be parsed, we still refuse to overwrite to avoid data loss.
            throw new InvalidOperationException("Existing configuration file is unreadable; refusing to overwrite.");
        }
    }
}
