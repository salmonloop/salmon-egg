using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
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
public sealed class ConfigurationManager : IConfigurationService, IConfigurationRecoveryService
{
    private const int CurrentSchemaVersion = 2;

    private readonly ISecureStorage _secureStorage;
    private readonly IConfigurationFileStore _fileStore;
    private readonly ILogger<ConfigurationManager> _logger;
    private readonly string _serversDirectory;
    private readonly ConfigurationProfileLockProvider _profileLockProvider;
    private readonly ConfigurationRecoveryCoordinator _recovery;
    private readonly SemaphoreSlim _operationGate = new(1, 1);

    public ConfigurationManager(
        ISecureStorage secureStorage,
        IConfigurationFileStore fileStore,
        IAppDataService appData,
        ILogger<ConfigurationManager> logger)
    {
        _secureStorage = secureStorage ?? throw new ArgumentNullException(nameof(secureStorage));
        _fileStore = fileStore ?? throw new ArgumentNullException(nameof(fileStore));
        if (appData is null) throw new ArgumentNullException(nameof(appData));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _serversDirectory = System.IO.Path.Combine(appData.ConfigRootPath, "servers");
        _profileLockProvider = new ConfigurationProfileLockProvider(appData);
        _recovery = new ConfigurationRecoveryCoordinator(fileStore, secureStorage, appData, _profileLockProvider);
    }

    public async Task RecoverPendingTransactionsAsync(CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _recovery.RecoverPendingTransactionsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ConfigurationRecoveryRequiredException exception)
        {
            throw CreateRecoveryRequiredException(exception);
        }
        catch (ConfigurationLockUnavailableException exception)
        {
            throw new ConfigurationPersistenceException(
                ConfigurationPersistenceFailureReason.ConfigurationLockUnavailable,
                "Another process is using a configuration profile. Retry after it finishes.",
                exception);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task SaveConfigurationAsync(ServerConfiguration config)
    {
        if (config is null) throw new ArgumentNullException(nameof(config));
        if (string.IsNullOrWhiteSpace(config.Id)) throw new ArgumentException("Configuration ID cannot be empty", nameof(config));

        await _operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await ExecuteForProfileAsync(config.Id, () => SaveUnderLockAsync(config)).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<ServerConfiguration?> LoadConfigurationAsync(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Configuration ID cannot be empty", nameof(id));

        await _operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await ExecuteForProfileAsync(id, () => LoadUnderLockAsync(id)).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<IEnumerable<ServerConfiguration>> ListConfigurationsAsync()
    {
        await _operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            try
            {
                await _recovery.RecoverPendingTransactionsAsync().ConfigureAwait(false);
            }
            catch (ConfigurationRecoveryRequiredException exception)
            {
                throw CreateRecoveryRequiredException(exception);
            }
            catch (ConfigurationLockUnavailableException exception)
            {
                throw CreateLockUnavailableException(exception);
            }

            return await ListUnderLockAsync().ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task DeleteConfigurationAsync(string id, string? expectedRevision = null)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Configuration ID cannot be empty", nameof(id));

        await _operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await ExecuteForProfileAsync(
                id,
                () => DeleteUnderLockAsync(id, expectedRevision)).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task ExecuteForProfileAsync(string profileId, Func<Task> action)
    {
        try
        {
            await using var profileLock = await _profileLockProvider.AcquireAsync(profileId).ConfigureAwait(false);
            await _recovery.RecoverProfileUnderLockAsync(profileId).ConfigureAwait(false);

            await action().ConfigureAwait(false);
        }
        catch (ConfigurationRecoveryRequiredException exception)
        {
            throw CreateRecoveryRequiredException(exception);
        }
        catch (ConfigurationLockUnavailableException exception)
        {
            throw CreateLockUnavailableException(exception);
        }
    }

    private async Task<T> ExecuteForProfileAsync<T>(string profileId, Func<Task<T>> action)
    {
        try
        {
            await using var profileLock = await _profileLockProvider.AcquireAsync(profileId).ConfigureAwait(false);
            await _recovery.RecoverProfileUnderLockAsync(profileId).ConfigureAwait(false);

            return await action().ConfigureAwait(false);
        }
        catch (ConfigurationRecoveryRequiredException exception)
        {
            throw CreateRecoveryRequiredException(exception);
        }
        catch (ConfigurationLockUnavailableException exception)
        {
            throw CreateLockUnavailableException(exception);
        }
    }

    private async Task SaveUnderLockAsync(ServerConfiguration config)
    {
        var serverPath = GetServerYamlPath(config.Id);
        var current = await ReadExistingYamlAsync(serverPath, forWrite: true).ConfigureAwait(false);
        EnsureWritableRevision(config, current);

        var mode = GetAuthenticationMode(config.Authentication);
        var nextRevision = Guid.NewGuid().ToString("N");
        var yaml = YamlSerialization.CreateSerializer().Serialize(ToYaml(config, mode, nextRevision));

        ConfigurationRecoveryJournal? journal = null;
        var stage = DurableMutationStage.Preparing;
        try
        {
            var snapshot = await CaptureSecretsAsync(config.Id).ConfigureAwait(false);
            journal = await _recovery.PrepareAsync(
                config.Id,
                serverPath,
                current.RawYaml,
                current.RawYaml is not null,
                snapshot.Token,
                snapshot.ApiKey).ConfigureAwait(false);

            stage = DurableMutationStage.ApplyingFile;
            await using var fileTransaction = await _fileStore.BeginWriteAsync(serverPath, yaml).ConfigureAwait(false);
            await fileTransaction.ApplyAndFlushAsync().ConfigureAwait(false);
            await _recovery.MarkYamlAppliedAsync(journal).ConfigureAwait(false);

            stage = DurableMutationStage.ApplyingSecrets;
            await using var secretTransaction = await BeginSecretTransactionAsync(config.Id, mode, config.Authentication).ConfigureAwait(false);
            stage = DurableMutationStage.Committing;
            var committedJournal = await _recovery.MarkCommittedAsync(journal).ConfigureAwait(false);
            secretTransaction.Complete();
            fileTransaction.Complete();
            await _recovery.CleanupCommittedBestEffortAsync(committedJournal).ConfigureAwait(false);
            config.PersistenceRevision = nextRevision;
        }
        catch (Exception exception)
        {
            await RecoverAfterFailureAsync(config.Id, exception).ConfigureAwait(false);
            throw MapMutationException(exception, stage, isDelete: false);
        }
    }

    private async Task<ServerConfiguration?> LoadUnderLockAsync(string id)
    {
        var path = GetServerYamlPath(id);
        var current = await ReadExistingYamlAsync(path, forWrite: false).ConfigureAwait(false);
        if (current.Model is null)
        {
            return null;
        }

        var yamlModel = current.Model;
        if (yamlModel.SchemaVersion <= 0 ||
            string.IsNullOrWhiteSpace(yamlModel.Name) ||
            string.IsNullOrWhiteSpace(yamlModel.Id))
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

        var config = FromYaml(yamlModel);
        await HydrateSecretsAsync(config, yamlModel.Authentication?.Mode).ConfigureAwait(false);
        return config;
    }

    private async Task<IEnumerable<ServerConfiguration>> ListUnderLockAsync()
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
                    if (yamlModel.SchemaVersion <= 0 ||
                        string.IsNullOrWhiteSpace(yamlModel.Name) ||
                        string.IsNullOrWhiteSpace(yamlModel.Id))
                    {
                        continue;
                    }

                    var transport = TransportFromString(yamlModel.Transport);
                    if ((transport != TransportType.Stdio && string.IsNullOrWhiteSpace(yamlModel.ServerUrl)) ||
                        (transport == TransportType.Stdio && string.IsNullOrWhiteSpace(yamlModel.StdioCommand)))
                    {
                        continue;
                    }

                    result.Add(FromYaml(yamlModel));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Skipping unreadable configuration file {Path} during list", path);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ConfigurationPersistenceException(
                ConfigurationPersistenceFailureReason.ConfigurationReadFailed,
                "Configuration directory could not be read; retry once it is available.",
                ex);
        }

        return result
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task DeleteUnderLockAsync(string id, string? expectedRevision)
    {
        var path = GetServerYamlPath(id);
        var current = await ReadExistingYamlAsync(path, forWrite: false).ConfigureAwait(false);
        if (current.RawYaml is null)
        {
            if (expectedRevision is not null)
            {
                throw CreateConflictException(id, expectedRevision, null);
            }

            return;
        }

        if (current.Model is not null && expectedRevision is not null &&
            !string.Equals(expectedRevision, current.Model.Revision ?? string.Empty, StringComparison.Ordinal))
        {
            throw CreateConflictException(id, expectedRevision, current.Model.Revision);
        }

        ConfigurationRecoveryJournal? journal = null;
        var stage = DurableMutationStage.Preparing;
        try
        {
            var snapshot = await CaptureSecretsAsync(id).ConfigureAwait(false);
            journal = await _recovery.PrepareAsync(
                id,
                path,
                current.RawYaml,
                oldFileExisted: true,
                snapshot.Token,
                snapshot.ApiKey).ConfigureAwait(false);

            stage = DurableMutationStage.ApplyingFile;
            await using var fileTransaction = await _fileStore.BeginDeleteAsync(path).ConfigureAwait(false);
            await fileTransaction.ApplyAndFlushAsync().ConfigureAwait(false);
            await _recovery.MarkYamlAppliedAsync(journal).ConfigureAwait(false);

            stage = DurableMutationStage.ApplyingSecrets;
            await using var secretTransaction = await BeginSecretTransactionAsync(id, "none", authentication: null).ConfigureAwait(false);
            stage = DurableMutationStage.Committing;
            var committedJournal = await _recovery.MarkCommittedAsync(journal).ConfigureAwait(false);
            secretTransaction.Complete();
            fileTransaction.Complete();
            await _recovery.CleanupCommittedBestEffortAsync(committedJournal).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await RecoverAfterFailureAsync(id, exception).ConfigureAwait(false);
            throw MapMutationException(exception, stage, isDelete: true);
        }
    }

    private async Task<ICloudSecretUpdateTransaction> BeginSecretTransactionAsync(
        string id,
        string mode,
        AuthenticationConfig? authentication)
    {
        var updates = new Dictionary<string, CloudSecretUpdate>(StringComparer.Ordinal)
        {
            [ConfigurationSecretKeys.GetTokenKey(id)] = mode == "bearer_token"
                ? CloudSecretUpdate.Replace(authentication?.Token ?? string.Empty)
                : CloudSecretUpdate.Clear(),
            [ConfigurationSecretKeys.GetApiKeyKey(id)] = mode == "api_key"
                ? CloudSecretUpdate.Replace(authentication?.ApiKey ?? string.Empty)
                : CloudSecretUpdate.Clear()
        };

        return await CloudSecretUpdateTransaction.BeginAsync(_secureStorage, updates).ConfigureAwait(false);
    }

    private async Task<ExistingYaml> ReadExistingYamlAsync(string path, bool forWrite)
    {
        string? yaml;
        try
        {
            yaml = await _fileStore.ReadAllTextAsync(path).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ConfigurationPersistenceException(
                forWrite
                    ? ConfigurationPersistenceFailureReason.ConfigurationWriteFailed
                    : ConfigurationPersistenceFailureReason.ConfigurationReadFailed,
                forWrite
                    ? "Configuration file could not be read for schema validation; configuration was not saved."
                    : "Configuration file could not be read; retry once the file is available.",
                exception);
        }

        if (yaml is null)
        {
            return new ExistingYaml(null, null);
        }

        try
        {
            return new ExistingYaml(yaml, YamlSerialization.CreateDeserializer().Deserialize<ServerConfigurationYaml>(yaml));
        }
        catch (YamlException exception)
        {
            if (forWrite)
            {
                _logger.LogWarning(exception, "Existing configuration file {Path} is corrupted; will overwrite with new data", path);
            }

            return new ExistingYaml(yaml, null);
        }
    }

    private static void EnsureWritableRevision(ServerConfiguration config, ExistingYaml current)
    {
        if (current.Model is not null && current.Model.SchemaVersion > CurrentSchemaVersion)
        {
            throw new InvalidOperationException(
                $"Configuration schema_version {current.Model.SchemaVersion} is newer than supported version {CurrentSchemaVersion}. Refusing to overwrite.");
        }

        var currentRevision = current.Model?.Revision ?? string.Empty;
        if (current.RawYaml is null)
        {
            if (config.PersistenceRevision is not null)
            {
                throw CreateConflictException(config.Id, config.PersistenceRevision, null);
            }

            return;
        }

        if (current.Model is null ||
            current.Model.SchemaVersion <= 0 ||
            string.IsNullOrWhiteSpace(current.Model.Id))
        {
            if (config.PersistenceRevision is not null)
            {
                throw CreateConflictException(config.Id, config.PersistenceRevision, null);
            }

            return;
        }

        if (config.PersistenceRevision is null ||
            !string.Equals(config.PersistenceRevision, currentRevision, StringComparison.Ordinal))
        {
            throw CreateConflictException(config.Id, config.PersistenceRevision, currentRevision);
        }
    }

    private static ConfigurationPersistenceException CreateConflictException(
        string profileId,
        string? expectedRevision,
        string? actualRevision) =>
        new(
            ConfigurationPersistenceFailureReason.ConfigurationConflict,
            $"Configuration '{profileId}' changed since it was loaded. Reload it and retry (expected revision '{expectedRevision ?? "<new>"}', actual '{actualRevision ?? "<missing>"}').");

    private static ConfigurationPersistenceException CreateLockUnavailableException(
        ConfigurationLockUnavailableException exception) =>
        new(
            ConfigurationPersistenceFailureReason.ConfigurationLockUnavailable,
            "Another process is using a configuration profile. Retry after it finishes.",
            exception);

    private async Task RecoverAfterFailureAsync(string profileId, Exception operationException)
    {
        try
        {
            await _recovery.RecoverProfileUnderLockAsync(profileId).ConfigureAwait(false);
        }
        catch (Exception recoveryException)
        {
            throw new ConfigurationPersistenceException(
                ConfigurationPersistenceFailureReason.ConfigurationRecoveryRequired,
                "The configuration operation failed and automatic recovery could not be completed. Retry after resolving the recovery data.",
                new AggregateException(operationException, recoveryException));
        }
    }

    private static ConfigurationPersistenceException MapMutationException(
        Exception exception,
        DurableMutationStage stage,
        bool isDelete)
    {
        if (exception is ConfigurationPersistenceException persistenceException)
        {
            return persistenceException;
        }

        if (exception is ConfigurationFileRollbackException)
        {
            return new ConfigurationPersistenceException(
                ConfigurationPersistenceFailureReason.ConfigurationRollbackFailed,
                isDelete
                    ? "Configuration could not be deleted and the previous file state could not be restored. Verify configuration before retrying."
                    : "Configuration file could not be saved and the previous file state could not be restored. Verify configuration before retrying.",
                exception);
        }

        if (exception is SecureStorageUnavailableException)
        {
            return new ConfigurationPersistenceException(
                ConfigurationPersistenceFailureReason.SecureStorageUnavailable,
                "Secure storage is unavailable; the configuration was not changed.",
                exception);
        }

        if (exception is ConfigurationRecoverySecretException)
        {
            return new ConfigurationPersistenceException(
                isDelete
                    ? ConfigurationPersistenceFailureReason.SecureStorageCleanupFailed
                    : ConfigurationPersistenceFailureReason.SecretPersistenceFailed,
                isDelete
                    ? "Credentials could not be cleared; the previous configuration was restored."
                    : "Credentials could not be updated; the previous configuration was restored.",
                exception);
        }

        var reason = stage == DurableMutationStage.ApplyingSecrets
            ? isDelete
                ? ConfigurationPersistenceFailureReason.SecureStorageCleanupFailed
                : ConfigurationPersistenceFailureReason.SecretPersistenceFailed
            : isDelete
                ? ConfigurationPersistenceFailureReason.ConfigurationDeleteFailed
                : ConfigurationPersistenceFailureReason.ConfigurationWriteFailed;
        var message = reason switch
        {
            ConfigurationPersistenceFailureReason.SecureStorageCleanupFailed => "Credentials could not be cleared; the previous configuration was restored.",
            ConfigurationPersistenceFailureReason.SecretPersistenceFailed => "Credentials could not be updated; the previous configuration was restored.",
            ConfigurationPersistenceFailureReason.ConfigurationDeleteFailed => "Configuration file could not be deleted; the previous configuration was restored.",
            _ => "Configuration file could not be saved; the previous configuration was restored."
        };
        return new ConfigurationPersistenceException(reason, message, exception);
    }

    private static ConfigurationPersistenceException CreateRecoveryRequiredException(
        ConfigurationRecoveryRequiredException exception) =>
        new(
            ConfigurationPersistenceFailureReason.ConfigurationRecoveryRequired,
            "An interrupted configuration transaction requires recovery before configuration can be used. Retry the operation or inspect the recovery data.",
            exception);

    private static ServerConfigurationYaml ToYaml(ServerConfiguration config, string mode, string revision)
    {
        return new ServerConfigurationYaml
        {
            SchemaVersion = CurrentSchemaVersion,
            UpdatedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
            Revision = revision,
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
            PersistenceRevision = yamlModel.Revision,
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
        try
        {
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
            }
        }
        catch (SecureStorageUnavailableException ex)
        {
            throw new ConfigurationPersistenceException(
                ConfigurationPersistenceFailureReason.SecureStorageUnavailable,
                "Secure storage is unavailable; configuration credentials could not be read.",
                ex);
        }
        catch (Exception ex)
        {
            throw new ConfigurationPersistenceException(
                ConfigurationPersistenceFailureReason.SecretPersistenceFailed,
                "Configuration credentials could not be read.",
                ex);
        }
    }

    private sealed record SecretSnapshot(string? Token, string? ApiKey);

    private sealed record ExistingYaml(string? RawYaml, ServerConfigurationYaml? Model);

    private enum DurableMutationStage
    {
        Preparing,
        ApplyingFile,
        ApplyingSecrets,
        Committing
    }

    private async Task<SecretSnapshot> CaptureSecretsAsync(string id)
        => new(
            await _secureStorage.LoadAsync(ConfigurationSecretKeys.GetTokenKey(id)).ConfigureAwait(false),
            await _secureStorage.LoadAsync(ConfigurationSecretKeys.GetApiKeyKey(id)).ConfigureAwait(false));

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
        => ConfigurationProfilePaths.GetServerYamlPath(_serversDirectory, id);

}
