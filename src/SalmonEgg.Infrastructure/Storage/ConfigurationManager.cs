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
    private readonly string _serversDirectory;

    public ConfigurationManager(ISecureStorage secureStorage)
    {
        _secureStorage = secureStorage ?? throw new ArgumentNullException(nameof(secureStorage));
        _serversDirectory = SalmonEggPaths.GetServersDirectoryPath();
        Directory.CreateDirectory(_serversDirectory);
    }

    public async Task SaveConfigurationAsync(ServerConfiguration config)
    {
        if (config is null) throw new ArgumentNullException(nameof(config));
        if (string.IsNullOrWhiteSpace(config.Id)) throw new ArgumentException("Configuration ID cannot be empty", nameof(config));

        var serverPath = GetServerYamlPath(config.Id);
        EnsureWritableSchema(serverPath);

        var mode = GetAuthenticationMode(config.Authentication);
        await PersistSecretsAsync(config.Id, mode, config.Authentication).ConfigureAwait(false);

        var yamlModel = ToYaml(config, mode);
        var yaml = YamlSerialization.CreateSerializer().Serialize(yamlModel);
        await AtomicFile.WriteUtf8AtomicAsync(serverPath, yaml).ConfigureAwait(false);
    }

    public async Task<ServerConfiguration?> LoadConfigurationAsync(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Configuration ID cannot be empty", nameof(id));

        var path = GetServerYamlPath(id);
        if (!File.Exists(path))
        {
            return null;
        }

        ServerConfigurationYamlV1 yamlModel;
        try
        {
            var yaml = await File.ReadAllTextAsync(path).ConfigureAwait(false);
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
        if (!Directory.Exists(_serversDirectory))
        {
            return Array.Empty<ServerConfiguration>();
        }

        var result = new List<ServerConfiguration>();
        var deserializer = YamlSerialization.CreateDeserializer();

        foreach (var path in Directory.EnumerateFiles(_serversDirectory, "*.yaml", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var yaml = await File.ReadAllTextAsync(path).ConfigureAwait(false);
                var yamlModel = deserializer.Deserialize<ServerConfigurationYamlV1>(yaml);
                if (yamlModel.SchemaVersion <= 0)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(yamlModel.Name))
                {
                    continue;
                }

                var transport = TransportFromString(yamlModel.Transport);
                if (transport != TransportType.Stdio && string.IsNullOrWhiteSpace(yamlModel.ServerUrl))
                {
                    continue;
                }

                if (transport == TransportType.Stdio && string.IsNullOrWhiteSpace(yamlModel.StdioCommand))
                {
                    continue;
                }

                var config = FromYaml(yamlModel, fallbackId: Path.GetFileNameWithoutExtension(path));
                result.Add(config);
            }
            catch (Exception)
            {
                // ignore malformed files
            }
        }

        return result
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
            if (File.Exists(path))
            {
                File.Delete(path);
            }
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
        return Path.Combine(_serversDirectory, fileName + ".yaml");
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

    private static void EnsureWritableSchema(string serverPath)
    {
        if (!File.Exists(serverPath))
        {
            return;
        }

        try
        {
            var yaml = File.ReadAllText(serverPath);
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
