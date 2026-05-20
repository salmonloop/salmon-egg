using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.Mcp;
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
    private const string StdioMcpTransport = "stdio";
    private const string HttpMcpTransport = "http";
    private const string SseMcpTransport = "sse";

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

        var mcpValidation = McpServerSupportPolicy.Validate(config.McpServers, McpServerSupportPolicy.SupportAllTransports);
        if (!mcpValidation.IsSupported)
        {
            throw new InvalidOperationException($"MCP server configuration is invalid: {mcpValidation.ErrorMessage}");
        }

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

        var transport = TransportFromString(yamlModel.Transport);
        if (transport != TransportType.Stdio && string.IsNullOrWhiteSpace(yamlModel.ServerUrl))
        {
            return null;
        }

        if (transport == TransportType.Stdio && string.IsNullOrWhiteSpace(yamlModel.StdioCommand))
        {
            return null;
        }

        var config = FromYaml(yamlModel, fallbackId: id);
        await HydrateSecretsAsync(config, yamlModel.Authentication?.Mode).ConfigureAwait(false);
        return config;
    }

    public async Task<IEnumerable<ServerConfiguration>> ListConfigurationsAsync()
    {
        var result = new List<ServerConfiguration>();
        var deserializer = YamlSerialization.CreateDeserializer();

        try
        {
            await foreach (var path in _fileStore.EnumerateFilesAsync(_serversDirectory, "*.yaml").ConfigureAwait(false))
            {
                try
                {
                    var yaml = await _fileStore.ReadAllTextAsync(path).ConfigureAwait(false);
                    if (yaml is null)
                    {
                        continue;
                    }

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

                    var config = FromYaml(yamlModel, fallbackId: System.IO.Path.GetFileNameWithoutExtension(path));
                    result.Add(config);
                }
                catch (Exception)
                {
                    // Ignore malformed or unreadable individual files.
                }
            }
        }
        catch (IOException)
        {
            return Array.Empty<ServerConfiguration>();
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
                : new ProxyYamlV1 { Enabled = false, ProxyUrl = string.Empty },
            McpServers = ToYamlMcpServers(config.McpServers)
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
            ConnectionTimeout = yamlModel.ConnectionTimeoutSeconds > 0 ? yamlModel.ConnectionTimeoutSeconds : 10,
            McpServers = FromYamlMcpServers(yamlModel.McpServers)
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

    private static List<McpServerYamlV1> ToYamlMcpServers(IEnumerable<McpServer>? servers)
    {
        if (servers == null)
        {
            return new List<McpServerYamlV1>();
        }

        var yamlServers = new List<McpServerYamlV1>();
        foreach (var server in servers)
        {
            switch (server)
            {
                case StdioMcpServer stdio:
                    yamlServers.Add(new McpServerYamlV1
                    {
                        Transport = StdioMcpTransport,
                        Name = stdio.Name ?? string.Empty,
                        Command = stdio.Command ?? string.Empty,
                        Args = stdio.Args ?? new List<string>(),
                        Env = ToYamlNameValues(stdio.Env)
                    });
                    break;
                case HttpMcpServer http:
                    yamlServers.Add(new McpServerYamlV1
                    {
                        Transport = HttpMcpTransport,
                        Name = http.Name ?? string.Empty,
                        Url = http.Url ?? string.Empty,
                        Headers = ToYamlNameValues(http.Headers)
                    });
                    break;
                case SseMcpServer sse:
                    yamlServers.Add(new McpServerYamlV1
                    {
                        Transport = SseMcpTransport,
                        Name = sse.Name ?? string.Empty,
                        Url = sse.Url ?? string.Empty,
                        Headers = ToYamlNameValues(sse.Headers)
                    });
                    break;
            }
        }

        return yamlServers;
    }

    private static List<McpServer> FromYamlMcpServers(IEnumerable<McpServerYamlV1>? yamlServers)
    {
        if (yamlServers == null)
        {
            return new List<McpServer>();
        }

        var servers = new List<McpServer>();
        foreach (var yamlServer in yamlServers)
        {
            var transport = (yamlServer.Transport ?? "stdio").Trim().ToLowerInvariant();
            switch (transport)
            {
                case HttpMcpTransport:
                    servers.Add(new HttpMcpServer(
                        yamlServer.Name ?? string.Empty,
                        yamlServer.Url ?? string.Empty,
                        FromYamlHeaders(yamlServer.Headers)));
                    break;
                case SseMcpTransport:
                    servers.Add(new SseMcpServer(
                        yamlServer.Name ?? string.Empty,
                        yamlServer.Url ?? string.Empty,
                        FromYamlHeaders(yamlServer.Headers)));
                    break;
                default:
                    servers.Add(new StdioMcpServer(
                        yamlServer.Name ?? string.Empty,
                        yamlServer.Command ?? string.Empty,
                        yamlServer.Args ?? new List<string>(),
                        FromYamlEnv(yamlServer.Env)));
                    break;
            }
        }

        return servers;
    }

    private static List<McpNameValueYamlV1> ToYamlNameValues(IEnumerable<McpEnvVariable>? values)
    {
        if (values == null)
        {
            return new List<McpNameValueYamlV1>();
        }

        return values
            .Select(value => new McpNameValueYamlV1
            {
                Name = value.Name ?? string.Empty,
                Value = value.Value ?? string.Empty
            })
            .ToList();
    }

    private static List<McpNameValueYamlV1> ToYamlNameValues(IEnumerable<McpHttpHeader>? values)
    {
        if (values == null)
        {
            return new List<McpNameValueYamlV1>();
        }

        return values
            .Select(value => new McpNameValueYamlV1
            {
                Name = value.Name ?? string.Empty,
                Value = value.Value ?? string.Empty
            })
            .ToList();
    }

    private static List<McpEnvVariable> FromYamlEnv(IEnumerable<McpNameValueYamlV1>? values)
    {
        if (values == null)
        {
            return new List<McpEnvVariable>();
        }

        return values
            .Select(value => new McpEnvVariable(value.Name ?? string.Empty, value.Value ?? string.Empty))
            .ToList();
    }

    private static List<McpHttpHeader> FromYamlHeaders(IEnumerable<McpNameValueYamlV1>? values)
    {
        if (values == null)
        {
            return new List<McpHttpHeader>();
        }

        return values
            .Select(value => new McpHttpHeader(value.Name ?? string.Empty, value.Value ?? string.Empty))
            .ToList();
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
