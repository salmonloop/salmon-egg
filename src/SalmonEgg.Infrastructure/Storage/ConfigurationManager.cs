using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
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
    private const int CurrentSchemaVersion = 2;

    private readonly ISecureStorage _secureStorage;
    private readonly IAppFileStore _fileStore;
    private readonly ILogger<ConfigurationManager> _logger;
    private readonly string _serversDirectory;

    public ConfigurationManager(ISecureStorage secureStorage, IAppFileStore fileStore, IAppDataService appData, ILogger<ConfigurationManager> logger)
    {
        _secureStorage = secureStorage ?? throw new ArgumentNullException(nameof(secureStorage));
        _fileStore = fileStore ?? throw new ArgumentNullException(nameof(fileStore));
        if (appData is null) throw new ArgumentNullException(nameof(appData));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _serversDirectory = System.IO.Path.Combine(appData.ConfigRootPath, "servers");
    }

    public async Task SaveConfigurationAsync(ServerConfiguration config)
    {
        if (config is null) throw new ArgumentNullException(nameof(config));
        if (string.IsNullOrWhiteSpace(config.Id)) throw new ArgumentException("Configuration ID cannot be empty", nameof(config));

        var serverPath = GetServerYamlPath(config.Id);
        try
        {
            await EnsureWritableSchemaAsync(serverPath).ConfigureAwait(false);
        }
        catch (ConfigurationPersistenceException)
        {
            throw;
        }
        catch (InvalidOperationException)
        {
            // schema_version 过新等显式拒绝由 EnsureWritableSchemaAsync 原样抛出，保持既有语义。
            throw;
        }
        catch (Exception ex)
        {
            // 预检读现有 YAML 时的 I/O 失败（文件被占用、磁盘错误、无权限）必须与其他持久化失败
            // 同样包装为 ConfigurationPersistenceException，否则会逃逸为裸 BCL 异常，CLI 顶层无法
            // 给出可区分的失败原因。此时尚未捕获快照，无需回滚。
            throw new ConfigurationPersistenceException(
                ConfigurationPersistenceFailureReason.ConfigurationWriteFailed,
                "Configuration file could not be read for schema validation; configuration was not saved.",
                ex);
        }

        var mode = GetAuthenticationMode(config.Authentication);
        SecretSnapshot snapshot;
        try
        {
            snapshot = await CaptureSecretsAsync(config.Id).ConfigureAwait(false);
        }
        catch (SecureStorageUnavailableException ex)
        {
            throw new ConfigurationPersistenceException(
                ConfigurationPersistenceFailureReason.SecureStorageUnavailable,
                "Secure storage is unavailable; configuration was not saved.",
                ex);
        }
        catch (Exception ex)
        {
            throw new ConfigurationPersistenceException(
                ConfigurationPersistenceFailureReason.SecretPersistenceFailed,
                "Credentials could not be saved; configuration was not changed.",
                ex);
        }

        try
        {
            await PersistSecretsAsync(config.Id, mode, config.Authentication).ConfigureAwait(false);
        }
        catch (SecureStorageUnavailableException ex)
        {
            await TryRestoreSecretsAsync(config.Id, snapshot).ConfigureAwait(false);
            throw new ConfigurationPersistenceException(
                ConfigurationPersistenceFailureReason.SecureStorageUnavailable,
                "Secure storage is unavailable; configuration was not saved and credential changes were rolled back when possible.",
                ex);
        }
        catch (Exception ex)
        {
            await TryRestoreSecretsAsync(config.Id, snapshot).ConfigureAwait(false);
            throw new ConfigurationPersistenceException(
                ConfigurationPersistenceFailureReason.SecretPersistenceFailed,
                "Credentials could not be saved; credential changes were rolled back when possible.",
                ex);
        }

        try
        {
            var yamlModel = ToYaml(config, mode);
            var yaml = YamlSerialization.CreateSerializer().Serialize(yamlModel);
            await _fileStore.WriteAllTextAsync(serverPath, yaml).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await TryRestoreSecretsAsync(config.Id, snapshot).ConfigureAwait(false);

            throw new ConfigurationPersistenceException(
                ConfigurationPersistenceFailureReason.ConfigurationWriteFailed,
                "Configuration file could not be saved; credential changes were rolled back when possible.",
                ex);
        }
    }

    public async Task<ServerConfiguration?> LoadConfigurationAsync(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Configuration ID cannot be empty", nameof(id));

        var path = GetServerYamlPath(id);
        ServerConfigurationYaml yamlModel;
        try
        {
            var yaml = await _fileStore.ReadAllTextAsync(path).ConfigureAwait(false);
            if (yaml is null)
            {
                return null;
            }

            yamlModel = YamlSerialization.CreateDeserializer().Deserialize<ServerConfigurationYaml>(yaml);
        }
        catch (YamlException)
        {
            // 文件存在但 YAML 已损坏——视作未配置,允许后续用合法数据覆写。
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 文件被占用、磁盘错误或无权限是暂态/权限故障,不是"配置不存在"。
            // 降级为 null 会让调用方(尤其是 CLI show/update/remove 与 set-credential)误报
            // "Server not found",且 remove 会在故障消除后真的删掉它刚才声称不存在的服务器。
            // 这里抛 ConfigurationPersistenceException,让 CLI 映射为可重试的 Failure(1)。
            throw new ConfigurationPersistenceException(
                ConfigurationPersistenceFailureReason.ConfigurationReadFailed,
                "Configuration file could not be read; retry once the file is available.",
                ex);
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

        if (string.IsNullOrWhiteSpace(yamlModel.Id))
        {
            return null;
        }

        var config = FromYaml(yamlModel);
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

                    var yamlModel = deserializer.Deserialize<ServerConfigurationYaml>(yaml);
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

                    if (string.IsNullOrWhiteSpace(yamlModel.Id))
                    {
                        continue;
                    }

                    var config = FromYaml(yamlModel);
                    result.Add(config);
                }
                catch (Exception ex)
                {
                    // 列表枚举保持宽容:单个损坏或暂态不可读的文件不应让整列表失败。
                    // 但需记日志,避免静默跳过被占用文件让用户误以为服务器不存在。
                    _logger.LogWarning(ex, "Skipping unreadable configuration file {Path} during list", path);
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
            // Secure storage and the file system do not share a transaction. Clear credentials first
            // so a secure-storage failure leaves the YAML available for a retry rather than orphaning
            // secrets behind an already-deleted configuration.
            await _secureStorage.DeleteAsync(ConfigurationSecretKeys.GetTokenKey(id)).ConfigureAwait(false);
            await _secureStorage.DeleteAsync(ConfigurationSecretKeys.GetApiKeyKey(id)).ConfigureAwait(false);
        }
        catch (SecureStorageUnavailableException ex)
        {
            throw new ConfigurationPersistenceException(
                ConfigurationPersistenceFailureReason.SecureStorageCleanupFailed,
                "Credentials could not be cleared; the server configuration was retained.",
                ex);
        }
        catch (Exception ex)
        {
            throw new ConfigurationPersistenceException(
                ConfigurationPersistenceFailureReason.SecureStorageCleanupFailed,
                "Credentials could not be cleared; the server configuration was retained.",
                ex);
        }

        try
        {
            await _fileStore.DeleteAsync(path).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // This is intentionally security-first rather than fully atomic: secure storage and the
            // file system cannot share a transaction. The YAML remains as retryable, non-sensitive
            // metadata after credentials have been cleared; a later delete is idempotent.
            throw new ConfigurationPersistenceException(
                ConfigurationPersistenceFailureReason.ConfigurationDeleteFailed,
                "Credentials were cleared, but the server configuration file could not be deleted.",
                ex);
        }
    }

    private static ServerConfigurationYaml ToYaml(ServerConfiguration config, string mode)
    {
        return new ServerConfigurationYaml
        {
            SchemaVersion = CurrentSchemaVersion,
            UpdatedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
            Id = config.Id,
            Name = config.Name,
            Transport = TransportToString(config.Transport),
            ServerUrl = config.ServerUrl,
            StdioCommand = config.StdioCommand,
            StdioArguments = config.StdioArguments?.ToList() ?? new List<string>(),
            ConnectionTimeoutSeconds = config.ConnectionTimeout,
            Authentication = new AuthenticationYamlV1 { Mode = mode },
            Proxy = new ProxyYamlV1
            {
                Mode = ProxyModeToString(config.Proxy?.Mode ?? ProxyConfig.DefaultMode),
                ProxyUrl = config.Proxy?.Mode == ProxyMode.Custom ? config.Proxy.ProxyUrl ?? string.Empty : string.Empty
            }
        };
    }

    private static ServerConfiguration FromYaml(ServerConfigurationYaml yamlModel)
    {
        var config = new ServerConfiguration
        {
            Id = yamlModel.Id!,
            Name = yamlModel.Name ?? string.Empty,
            ServerUrl = yamlModel.ServerUrl ?? string.Empty,
            StdioCommand = yamlModel.StdioCommand ?? string.Empty,
            StdioArguments = yamlModel.StdioArguments ?? new List<string>(),
            Transport = TransportFromString(yamlModel.Transport),
            ConnectionTimeout = AcpConnectionTimeoutPolicy.ResolveSeconds(yamlModel.ConnectionTimeoutSeconds)
        };

        var proxyMode = ProxyModeFromYaml(yamlModel.Proxy);
        config.Proxy = new ProxyConfig
        {
            Mode = proxyMode,
            ProxyUrl = proxyMode == ProxyMode.Custom && !string.IsNullOrWhiteSpace(yamlModel.Proxy.ProxyUrl)
                ? yamlModel.Proxy.ProxyUrl
                : null
        };

        return config;
    }

    private async Task HydrateSecretsAsync(ServerConfiguration config, string? mode)
    {
        if (config is null) return;

        mode = (mode ?? "none").Trim().ToLowerInvariant();
        if (mode == "bearer_token")
        {
            var token = await _secureStorage.LoadAsync(ConfigurationSecretKeys.GetTokenKey(config.Id)).ConfigureAwait(false);
            config.Authentication = new AuthenticationConfig { Token = token };
            return;
        }

        if (mode == "api_key")
        {
            var apiKey = await _secureStorage.LoadAsync(ConfigurationSecretKeys.GetApiKeyKey(config.Id)).ConfigureAwait(false);
            config.Authentication = new AuthenticationConfig { ApiKey = apiKey };
            return;
        }
    }

    private sealed record SecretSnapshot(string? Token, string? ApiKey);

    private async Task<SecretSnapshot> CaptureSecretsAsync(string id)
        => new(
            await _secureStorage.LoadAsync(ConfigurationSecretKeys.GetTokenKey(id)).ConfigureAwait(false),
            await _secureStorage.LoadAsync(ConfigurationSecretKeys.GetApiKeyKey(id)).ConfigureAwait(false));

    private async Task RestoreSecretsAsync(string id, SecretSnapshot snapshot)
    {
        if (string.IsNullOrEmpty(snapshot.Token))
        {
            await _secureStorage.DeleteAsync(ConfigurationSecretKeys.GetTokenKey(id)).ConfigureAwait(false);
        }
        else
        {
            await _secureStorage.SaveAsync(ConfigurationSecretKeys.GetTokenKey(id), snapshot.Token).ConfigureAwait(false);
        }

        if (string.IsNullOrEmpty(snapshot.ApiKey))
        {
            await _secureStorage.DeleteAsync(ConfigurationSecretKeys.GetApiKeyKey(id)).ConfigureAwait(false);
        }
        else
        {
            await _secureStorage.SaveAsync(ConfigurationSecretKeys.GetApiKeyKey(id), snapshot.ApiKey).ConfigureAwait(false);
        }
    }

    private async Task TryRestoreSecretsAsync(string id, SecretSnapshot snapshot)
    {
        try
        {
            await RestoreSecretsAsync(id, snapshot).ConfigureAwait(false);
        }
        catch (Exception rollbackException)
        {
            _logger.LogError(
                rollbackException,
                "Failed to roll back credentials after configuration persistence failure for server {ServerId}",
                id);
        }
    }

    private async Task PersistSecretsAsync(string id, string mode, AuthenticationConfig? authentication)
    {
        if (mode == "bearer_token")
        {
            var token = authentication?.Token;
            if (!string.IsNullOrEmpty(token))
            {
                await _secureStorage.SaveAsync(ConfigurationSecretKeys.GetTokenKey(id), token).ConfigureAwait(false);
            }
            else
            {
                await _secureStorage.DeleteAsync(ConfigurationSecretKeys.GetTokenKey(id)).ConfigureAwait(false);
            }

            await _secureStorage.DeleteAsync(ConfigurationSecretKeys.GetApiKeyKey(id)).ConfigureAwait(false);
            return;
        }

        if (mode == "api_key")
        {
            var apiKey = authentication?.ApiKey;
            if (!string.IsNullOrEmpty(apiKey))
            {
                await _secureStorage.SaveAsync(ConfigurationSecretKeys.GetApiKeyKey(id), apiKey).ConfigureAwait(false);
            }
            else
            {
                await _secureStorage.DeleteAsync(ConfigurationSecretKeys.GetApiKeyKey(id)).ConfigureAwait(false);
            }

            await _secureStorage.DeleteAsync(ConfigurationSecretKeys.GetTokenKey(id)).ConfigureAwait(false);
            return;
        }

        await _secureStorage.DeleteAsync(ConfigurationSecretKeys.GetTokenKey(id)).ConfigureAwait(false);
        await _secureStorage.DeleteAsync(ConfigurationSecretKeys.GetApiKeyKey(id)).ConfigureAwait(false);
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
            TransportType.StreamableHttp => "streamable_http",
            _ => "websocket"
        };

    private static TransportType TransportFromString(string? value)
    {
        value = (value ?? string.Empty).Trim().ToLowerInvariant();
        return value switch
        {
            "stdio" => TransportType.Stdio,
            "streamable_http" => TransportType.StreamableHttp,
            "websocket" => TransportType.WebSocket,
            _ => TransportType.WebSocket
        };
    }

    private static string ProxyModeToString(ProxyMode mode) =>
        mode switch
        {
            ProxyMode.System => "system",
            ProxyMode.Custom => "custom",
            _ => "none"
        };

    private static ProxyMode ProxyModeFromYaml(ProxyYamlV1? proxy)
    {
        if (proxy is null)
        {
            return ProxyConfig.DefaultMode;
        }

        var mode = (proxy.Mode ?? string.Empty).Trim().ToLowerInvariant();
        return mode switch
        {
            "system" => ProxyMode.System,
            "custom" => ProxyMode.Custom,
            "none" => ProxyMode.None,
            _ when proxy.Enabled => ProxyMode.Custom,
            _ => ProxyConfig.DefaultMode
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

    private async Task EnsureWritableSchemaAsync(string serverPath)
    {
        try
        {
            var yaml = await _fileStore.ReadAllTextAsync(serverPath).ConfigureAwait(false);
            if (yaml is null)
            {
                return;
            }

            var existing = YamlSerialization.CreateDeserializer().Deserialize<ServerConfigurationYaml>(yaml);
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
        catch (YamlException ex)
        {
            // 文件存在但 YAML 已损坏（例如 WASM IDBFS 在浏览器崩溃后被截断）。
            // 允许用合法数据覆写——拒绝写入会把用户锁在无法保存的死路上。
            _logger.LogWarning(ex, "Existing configuration file {Path} is corrupted; will overwrite with new data", serverPath);
        }
    }
}
