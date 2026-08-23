using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Infrastructure.Storage;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.Storage;

public sealed class ConfigurationManagerTests : IDisposable
{
    private readonly ISecureStorage _secureStorage;
    private readonly ConfigurationManager _configManager;
    private readonly string _testDirectory;

    public ConfigurationManagerTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "SalmonEggTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDirectory);
        Environment.SetEnvironmentVariable("SALMONEGG_APPDATA_ROOT", Path.Combine(_testDirectory, "SalmonEgg"), EnvironmentVariableTarget.Process);

        _secureStorage = new RecordingSecureStorage();
        _configManager = new ConfigurationManager(_secureStorage, new FileSystemAppFileStore(), new AppDataService(), NullLogger<ConfigurationManager>.Instance);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("SALMONEGG_APPDATA_ROOT", null, EnvironmentVariableTarget.Process);
        try
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, recursive: true);
            }
        }
        catch
        {
            // ignore cleanup failures
        }
    }

    [Fact]
    public async Task SaveConfigurationAsync_ValidConfig_WritesYamlAndLoadsBack()
    {
        var config = CreateTestConfiguration("test-001");

        await _configManager.SaveConfigurationAsync(config);

        Assert.True(File.Exists(GetServerYamlPath(config.Id)));

        var yaml = await File.ReadAllTextAsync(GetServerYamlPath(config.Id), TestContext.Current.CancellationToken);
        Assert.Contains("schema_version:", yaml);
        Assert.Contains("id:", yaml);
        Assert.DoesNotContain("{", yaml);

        var loaded = await _configManager.LoadConfigurationAsync(config.Id);
        Assert.NotNull(loaded);
        Assert.Equal(config.Id, loaded!.Id);
        Assert.Equal(config.Name, loaded.Name);
        Assert.Equal(config.ServerUrl, loaded.ServerUrl);
        Assert.False(string.IsNullOrWhiteSpace(config.PersistenceRevision));
        Assert.Equal(config.PersistenceRevision, loaded.PersistenceRevision);
    }

    [Fact]
    public async Task SaveConfigurationAsync_WithStaleRevision_RejectsWithoutChangingYamlOrSecrets()
    {
        var config = CreateTestConfiguration("revision-conflict");
        config.Authentication = new AuthenticationConfig { Token = "initial-token" };
        await _configManager.SaveConfigurationAsync(config);

        var first = await _configManager.LoadConfigurationAsync(config.Id);
        var stale = await _configManager.LoadConfigurationAsync(config.Id);
        Assert.NotNull(first);
        Assert.NotNull(stale);

        first!.Name = "First writer";
        first.Authentication = new AuthenticationConfig { Token = "first-token" };
        await _configManager.SaveConfigurationAsync(first);

        stale!.Name = "Stale writer";
        stale.Authentication = new AuthenticationConfig { Token = "stale-token" };
        var exception = await Assert.ThrowsAsync<ConfigurationPersistenceException>(
            () => _configManager.SaveConfigurationAsync(stale));

        Assert.Equal(ConfigurationPersistenceFailureReason.ConfigurationConflict, exception.Reason);
        var reloaded = await _configManager.LoadConfigurationAsync(config.Id);
        Assert.NotNull(reloaded);
        Assert.Equal("First writer", reloaded!.Name);
        Assert.Equal("first-token", reloaded.Authentication?.Token);
    }

    [Fact]
    public async Task SaveConfigurationAsync_AcrossManagerInstances_SerializesAndRejectsStaleWriter()
    {
        var config = CreateTestConfiguration("cross-manager-conflict");
        await _configManager.SaveConfigurationAsync(config);
        var secondManager = new ConfigurationManager(
            _secureStorage,
            new FileSystemAppFileStore(),
            new AppDataService(),
            NullLogger<ConfigurationManager>.Instance);
        var first = await _configManager.LoadConfigurationAsync(config.Id);
        var second = await secondManager.LoadConfigurationAsync(config.Id);
        Assert.NotNull(first);
        Assert.NotNull(second);

        first!.Name = "First manager";
        second!.Name = "Second manager";
        var firstSave = _configManager.SaveConfigurationAsync(first);
        var secondSave = secondManager.SaveConfigurationAsync(second);
        var results = await Task.WhenAll(
            CapturePersistenceResultAsync(firstSave),
            CapturePersistenceResultAsync(secondSave));

        Assert.Single(results, result => result is null);
        var conflict = Assert.Single(results, result => result is not null);
        Assert.Equal(ConfigurationPersistenceFailureReason.ConfigurationConflict, conflict!.Reason);
        var reloaded = await _configManager.LoadConfigurationAsync(config.Id);
        Assert.NotNull(reloaded);
        Assert.Contains(reloaded!.Name, new[] { "First manager", "Second manager" });
    }

    [Fact]
    public async Task SaveConfigurationAsync_WithBearerToken_StoresSecretNotInYaml()
    {
        var config = CreateTestConfiguration("test-002");
        config.Authentication = new AuthenticationConfig { Token = "secret-token-123" };

        await _configManager.SaveConfigurationAsync(config);

        var yaml = await File.ReadAllTextAsync(GetServerYamlPath(config.Id), TestContext.Current.CancellationToken);
        Assert.DoesNotContain("secret-token-123", yaml, StringComparison.Ordinal);

        var loaded = await _configManager.LoadConfigurationAsync(config.Id);
        Assert.NotNull(loaded);
        Assert.NotNull(loaded!.Authentication);
        Assert.Equal("secret-token-123", loaded.Authentication!.Token);
        Assert.True(string.IsNullOrEmpty(loaded.Authentication.ApiKey));
    }

    [Fact]
    public async Task SaveConfigurationAsync_WithApiKey_StoresSecretNotInYaml()
    {
        var config = CreateTestConfiguration("test-003");
        config.Authentication = new AuthenticationConfig { ApiKey = "secret-api-key-456" };

        await _configManager.SaveConfigurationAsync(config);

        var yaml = await File.ReadAllTextAsync(GetServerYamlPath(config.Id), TestContext.Current.CancellationToken);
        Assert.DoesNotContain("secret-api-key-456", yaml, StringComparison.Ordinal);

        var loaded = await _configManager.LoadConfigurationAsync(config.Id);
        Assert.NotNull(loaded);
        Assert.NotNull(loaded!.Authentication);
        Assert.Equal("secret-api-key-456", loaded.Authentication!.ApiKey);
        Assert.True(string.IsNullOrEmpty(loaded.Authentication.Token));
    }

    [Fact]
    public async Task SaveConfigurationAsync_WithSshBridgeStdio_PersistsStdioTransportShape()
    {
        var config = new ServerConfiguration
        {
            Id = "stdio-ssh-001",
            Name = "SSH Bridge",
            Transport = TransportType.Stdio,
            StdioCommand = "ssh",
            StdioArguments = ["-T", "-o", "BatchMode=yes", "user@host", "/opt/acp/bin/agent", "stdio"],
            ConnectionTimeout = 10
        };

        await _configManager.SaveConfigurationAsync(config);

        var yaml = await File.ReadAllTextAsync(GetServerYamlPath(config.Id), TestContext.Current.CancellationToken);
        Assert.Contains("transport: stdio", yaml, StringComparison.Ordinal);
        Assert.Contains("stdio_command: ssh", yaml, StringComparison.Ordinal);
        Assert.Contains("stdio_arguments:", yaml, StringComparison.Ordinal);
        Assert.Contains("- -T", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("stdio_args:", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaveConfigurationAsync_WithStdioEnvironment_PersistsBlockStyleAndLoadsBack()
    {
        var config = new ServerConfiguration
        {
            Id = "stdio-env-001",
            Name = "Env Overlay",
            Transport = TransportType.Stdio,
            StdioCommand = "npx",
            StdioArguments = ["@scope/adapter", "--acp"],
            StdioEnvironment = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["AGENT_DISABLE_AUTO_UPDATE"] = "1"
            },
            ConnectionTimeout = 10
        };

        await _configManager.SaveConfigurationAsync(config);

        var yaml = await File.ReadAllTextAsync(GetServerYamlPath(config.Id), TestContext.Current.CancellationToken);
        // Block style, one entry per indented line: the persistence spec requires the file stay
        // readable and mergeable, which a flow-style map would break.
        Assert.Contains(
            "stdio_environment:" + Environment.NewLine + "  AGENT_DISABLE_AUTO_UPDATE: 1",
            yaml.ReplaceLineEndings(),
            StringComparison.Ordinal);
        Assert.DoesNotContain("{", yaml, StringComparison.Ordinal);

        var loaded = await _configManager.LoadConfigurationAsync(config.Id);
        Assert.NotNull(loaded);
        Assert.Equal("1", Assert.Contains("AGENT_DISABLE_AUTO_UPDATE", loaded!.StdioEnvironment));
    }

    [Fact]
    public async Task SaveConfigurationAsync_WithoutStdioEnvironment_OmitsTheKeyEntirely()
    {
        var config = new ServerConfiguration
        {
            Id = "stdio-env-absent-001",
            Name = "No Env",
            Transport = TransportType.Stdio,
            StdioCommand = "agent",
            ConnectionTimeout = 10
        };

        await _configManager.SaveConfigurationAsync(config);

        var yaml = await File.ReadAllTextAsync(GetServerYamlPath(config.Id), TestContext.Current.CancellationToken);
        Assert.DoesNotContain("stdio_environment", yaml, StringComparison.Ordinal);

        var loaded = await _configManager.LoadConfigurationAsync(config.Id);
        Assert.NotNull(loaded);
        Assert.Empty(loaded!.StdioEnvironment);
    }

    [Fact]
    public async Task LoadConfigurationAsync_WithSchemaVersion2File_HydratesEmptyEnvironmentWithoutMigration()
    {
        var configId = "schema-v2-no-env-001";
        var path = GetServerYamlPath(configId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(
            path,
            """
            schema_version: 2
            id: schema-v2-no-env-001
            name: Legacy Stdio
            transport: stdio
            stdio_command: agent
            stdio_arguments:
            - --acp
            connection_timeout_seconds: 10
            authentication:
              mode: none
            proxy:
              mode: system
            """, TestContext.Current.CancellationToken);

        var loaded = await _configManager.LoadConfigurationAsync(configId);

        Assert.NotNull(loaded);
        Assert.Equal("agent", loaded!.StdioCommand);
        Assert.Empty(loaded.StdioEnvironment);
    }

    [Fact]
    public async Task TransportPersistence_WritesAndReadsCanonicalStreamableHttp()
    {
        var config = CreateTestConfiguration("streamable-http-001");
        config.Transport = TransportType.StreamableHttp;
        config.ServerUrl = "https://agents.example.com/acp";

        await _configManager.SaveConfigurationAsync(config);

        var yaml = await File.ReadAllTextAsync(GetServerYamlPath(config.Id), TestContext.Current.CancellationToken);
        Assert.Contains("transport: streamable_http", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("http_sse", yaml, StringComparison.Ordinal);

        var loaded = await _configManager.LoadConfigurationAsync(config.Id);
        Assert.NotNull(loaded);
        Assert.Equal(TransportType.StreamableHttp, loaded!.Transport);
    }

    [Fact]
    public async Task LoadConfigurationAsync_WithNonCanonicalStdioArgsField_DoesNotHydrateArguments()
    {
        var configId = "noncanonical-stdio-args-001";
        var path = GetServerYamlPath(configId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(
            path,
            """
            schema_version: 2
            id: noncanonical-stdio-args-001
            name: Noncanonical Args
            transport: stdio
            stdio_command: agent
            stdio_args: --serve
            connection_timeout_seconds: 10
            authentication:
              mode: none
            proxy:
              mode: system
            """, TestContext.Current.CancellationToken);

        var loaded = await _configManager.LoadConfigurationAsync(configId);

        Assert.NotNull(loaded);
        Assert.Empty(loaded!.StdioArguments);
    }

    [Fact]
    public async Task SaveConfigurationAsync_WhenSecureStorageUnavailable_ThrowsConfigurationPersistenceException()
    {
        var config = CreateTestConfiguration("secret-service-unavailable-001");
        config.Authentication = new AuthenticationConfig { Token = "secret-token" };
        var manager = new ConfigurationManager(
            new FailingSecureStorage(),
            new FileSystemAppFileStore(),
            new AppDataService(),
            NullLogger<ConfigurationManager>.Instance);

        var ex = await Assert.ThrowsAsync<ConfigurationPersistenceException>(
            () => manager.SaveConfigurationAsync(config));

        Assert.Equal(ConfigurationPersistenceFailureReason.SecureStorageUnavailable, ex.Reason);
        Assert.Contains("Secure storage is unavailable", ex.UserMessage, StringComparison.Ordinal);
        Assert.False(File.Exists(GetServerYamlPath(config.Id)));
    }

    [Fact]
    public async Task SaveConfigurationAsync_WhenSecretSnapshotCannotBeRead_ThrowsConfigurationPersistenceException()
    {
        var config = CreateTestConfiguration("secret-snapshot-unavailable-001");
        config.Authentication = new AuthenticationConfig { Token = "secret-token" };
        var manager = new ConfigurationManager(
            new LoadFailingSecureStorage(),
            new FileSystemAppFileStore(),
            new AppDataService(),
            NullLogger<ConfigurationManager>.Instance);

        var ex = await Assert.ThrowsAsync<ConfigurationPersistenceException>(
            () => manager.SaveConfigurationAsync(config));

        Assert.Equal(ConfigurationPersistenceFailureReason.SecureStorageUnavailable, ex.Reason);
        Assert.Contains("Secure storage is unavailable", ex.UserMessage, StringComparison.Ordinal);
        Assert.False(File.Exists(GetServerYamlPath(config.Id)));
    }

    [Fact]
    public async Task SaveConfigurationAsync_ProfileConfiguration_DoesNotWriteMcpServers()
    {
        var config = CreateTestConfiguration("mcp-servers-001");

        await _configManager.SaveConfigurationAsync(config);

        var yaml = await File.ReadAllTextAsync(GetServerYamlPath(config.Id), TestContext.Current.CancellationToken);
        Assert.DoesNotContain("mcp_servers:", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("transport: stdio", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("transport: http", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("transport: sse", yaml, StringComparison.Ordinal);

        var loaded = await _configManager.LoadConfigurationAsync(config.Id);
        Assert.NotNull(loaded);
    }

    [Fact]
    public async Task SaveConfigurationAsync_ProfileConfiguration_DoesNotWriteMcpMeta()
    {
        var config = CreateTestConfiguration("mcp-meta-001");

        await _configManager.SaveConfigurationAsync(config);

        var yaml = await File.ReadAllTextAsync(GetServerYamlPath(config.Id), TestContext.Current.CancellationToken);
        Assert.DoesNotContain("mcp_servers:", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("meta:", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("source: profile", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("scope: workspace", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("secret_ref: header-auth", yaml, StringComparison.Ordinal);

        var loaded = await _configManager.LoadConfigurationAsync(config.Id);

        Assert.NotNull(loaded);
    }

    [Fact]
    public async Task LoadConfigurationAsync_NonExistentConfig_ReturnsNull()
    {
        var loaded = await _configManager.LoadConfigurationAsync("non-existent");
        Assert.Null(loaded);
    }

    [Fact]
    public async Task LoadConfigurationAsync_WhenYamlIdIsMissing_ReturnsNull()
    {
        var configId = "missing-id-001";
        var path = GetServerYamlPath(configId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(
            path,
            """
            schema_version: 2
            name: Missing Id
            transport: websocket
            server_url: ws://localhost:8080
            connection_timeout_seconds: 10
            authentication:
              mode: none
            proxy:
              mode: system
            """, TestContext.Current.CancellationToken);

        var loaded = await _configManager.LoadConfigurationAsync(configId);

        Assert.Null(loaded);
    }

    [Fact]
    public async Task LoadConfigurationAsync_WhenConfigurationFileCannotBeRead_ThrowsPersistenceException()
    {
        // I/O 故障(文件被占用、磁盘错误、无权限)不得降级为 null——那会让 CLI 把暂态故障
        // 误报为 "Server not found",且 remove 会在故障消除后真的删掉它刚声称不存在的服务器。
        // 必须抛 ConfigurationPersistenceException,让 CLI 映射为可重试的 Failure(1)。
        var manager = new ConfigurationManager(_secureStorage, new FailingAppFileStore(), new AppDataService(), NullLogger<ConfigurationManager>.Instance);

        var ex = await Assert.ThrowsAsync<ConfigurationPersistenceException>(
            () => manager.LoadConfigurationAsync("unreadable"));

        Assert.Equal(ConfigurationPersistenceFailureReason.ConfigurationReadFailed, ex.Reason);
    }

    [Fact]
    public async Task ListConfigurationsAsync_MultipleConfigs_EnumeratesServerYamlFiles()
    {
        var config1 = CreateTestConfiguration("test-list-001");
        var config2 = CreateTestConfiguration("test-list-002");
        var config3 = CreateTestConfiguration("test-list-003");

        await _configManager.SaveConfigurationAsync(config1);
        await _configManager.SaveConfigurationAsync(config2);
        await _configManager.SaveConfigurationAsync(config3);

        var configs = (await _configManager.ListConfigurationsAsync()).ToList();
        Assert.Equal(3, configs.Count);
        Assert.Contains(configs, c => c.Id == "test-list-001");
        Assert.Contains(configs, c => c.Id == "test-list-002");
        Assert.Contains(configs, c => c.Id == "test-list-003");
    }

    [Fact]
    public async Task ListConfigurationsAsync_WhenYamlIdIsMissing_SkipsConfiguration()
    {
        var validConfig = CreateTestConfiguration("valid-list-id-001");
        await _configManager.SaveConfigurationAsync(validConfig);
        var missingIdPath = GetServerYamlPath("missing-list-id-001");
        Directory.CreateDirectory(Path.GetDirectoryName(missingIdPath)!);
        await File.WriteAllTextAsync(
            missingIdPath,
            """
            schema_version: 2
            name: Missing List Id
            transport: websocket
            server_url: ws://localhost:8080
            connection_timeout_seconds: 10
            authentication:
              mode: none
            proxy:
              mode: system
            """, TestContext.Current.CancellationToken);

        var configs = (await _configManager.ListConfigurationsAsync()).ToList();

        Assert.Single(configs);
        Assert.Equal(validConfig.Id, configs[0].Id);
    }

    [Fact]
    public async Task ListConfigurationsAsync_WhenConfigurationDirectoryCannotBeEnumerated_ThrowsReadFailure()
    {
        var manager = new ConfigurationManager(_secureStorage, new FailingAppFileStore(), new AppDataService(), NullLogger<ConfigurationManager>.Instance);

        var ex = await Assert.ThrowsAsync<ConfigurationPersistenceException>(
            () => manager.ListConfigurationsAsync());

        Assert.Equal(ConfigurationPersistenceFailureReason.ConfigurationReadFailed, ex.Reason);
    }

    [Fact]
    public async Task LoadConfigurationAsync_UnknownFields_AreIgnored()
    {
        var config = CreateTestConfiguration("unknown-fields-001");
        await _configManager.SaveConfigurationAsync(config);

        var path = GetServerYamlPath(config.Id);
        var yaml = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        yaml += $"{Environment.NewLine}totally_unknown_field: 123{Environment.NewLine}";
        await File.WriteAllTextAsync(path, yaml, TestContext.Current.CancellationToken);

        var loaded = await _configManager.LoadConfigurationAsync(config.Id);
        Assert.NotNull(loaded);
        Assert.Equal(config.Id, loaded!.Id);
    }

    [Fact]
    public async Task LoadConfigurationAsync_UnknownEnumValue_FallsBackToDefault()
    {
        var config = CreateTestConfiguration("unknown-enum-001");
        await _configManager.SaveConfigurationAsync(config);

        var path = GetServerYamlPath(config.Id);
        var yaml = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        yaml = yaml.Replace("transport: websocket", "transport: totally_unknown_transport", StringComparison.OrdinalIgnoreCase);
        await File.WriteAllTextAsync(path, yaml, TestContext.Current.CancellationToken);

        var loaded = await _configManager.LoadConfigurationAsync(config.Id);
        Assert.NotNull(loaded);
        Assert.Equal(TransportType.WebSocket, loaded!.Transport);
    }

    [Fact]
    public async Task LoadConfigurationAsync_WhenConnectionTimeoutMissing_UsesSharedDefault()
    {
        var config = CreateTestConfiguration("missing-timeout-001");
        await _configManager.SaveConfigurationAsync(config);

        var path = GetServerYamlPath(config.Id);
        var yaml = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        yaml = yaml.Replace($"connection_timeout_seconds: {config.ConnectionTimeout}{Environment.NewLine}", string.Empty, StringComparison.Ordinal);
        await File.WriteAllTextAsync(path, yaml, TestContext.Current.CancellationToken);

        var loaded = await _configManager.LoadConfigurationAsync(config.Id);

        Assert.NotNull(loaded);
        Assert.Equal(AcpConnectionTimeoutPolicy.DefaultSeconds, loaded!.ConnectionTimeout);
    }

    [Fact]
    public async Task LoadConfigurationAsync_WhenProxyBlockMissing_UsesSharedDefaultProxyMode()
    {
        var config = CreateTestConfiguration("missing-proxy-001");
        await _configManager.SaveConfigurationAsync(config);

        var path = GetServerYamlPath(config.Id);
        var yaml = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        var proxyMode = ProxyConfig.DefaultMode switch
        {
            ProxyMode.System => "system",
            ProxyMode.Custom => "custom",
            _ => "none"
        };
        var proxyBlock = $"proxy:{Environment.NewLine}  mode: {proxyMode}{Environment.NewLine}  enabled: false{Environment.NewLine}  proxy_url: ''{Environment.NewLine}";
        yaml = yaml.Replace(proxyBlock, string.Empty, StringComparison.Ordinal);
        await File.WriteAllTextAsync(path, yaml, TestContext.Current.CancellationToken);

        var loaded = await _configManager.LoadConfigurationAsync(config.Id);

        Assert.NotNull(loaded);
        Assert.NotNull(loaded!.Proxy);
        Assert.Equal(ProxyConfig.DefaultMode, loaded.Proxy!.Mode);
    }

    [Fact]
    public async Task SaveConfigurationAsync_WhenExistingFileIsCorruptedYaml_OverwritesAndLoadsBack()
    {
        var configId = "corrupted-then-save-001";
        var path = GetServerYamlPath(configId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, ":\n  - definitely not yaml", TestContext.Current.CancellationToken);

        var config = CreateTestConfiguration(configId);
        await _configManager.SaveConfigurationAsync(config);

        var loaded = await _configManager.LoadConfigurationAsync(configId);
        Assert.NotNull(loaded);
        Assert.Equal(config.Id, loaded!.Id);
        Assert.Equal(config.Name, loaded.Name);
        Assert.Equal(config.ServerUrl, loaded.ServerUrl);
    }

    [Fact]
    public async Task SaveConfigurationAsync_WhenYamlWriteFails_RestoresPreviousSecrets()
    {
        var config = CreateTestConfiguration("write-failure-rollback");
        config.Authentication = new AuthenticationConfig { Token = "new-token" };
        var storage = new RecordingSecureStorage();
        await storage.SaveAsync("salmonegg/config/write-failure-rollback/token", "old-token");
        var manager = new ConfigurationManager(
            storage,
            new WriteFailingAppFileStore(),
            new AppDataService(),
            NullLogger<ConfigurationManager>.Instance);

        var ex = await Assert.ThrowsAsync<ConfigurationPersistenceException>(
            () => manager.SaveConfigurationAsync(config));

        Assert.Equal(ConfigurationPersistenceFailureReason.ConfigurationWriteFailed, ex.Reason);
        Assert.Equal("old-token", await storage.LoadAsync("salmonegg/config/write-failure-rollback/token"));
    }

    [Fact]
    public async Task SaveConfigurationAsync_WhenSchemaPreflightReadFails_WrapsAsPersistenceFailureAndLeavesSecretsUntouched()
    {
        // 预检读现有 YAML 时的 I/O 失败必须与其他持久化失败一样包装为 ConfigurationPersistenceException，
        // 否则会逃逸为裸 IOException，CLI 顶层无法给出可区分的失败原因。
        // 此时尚未捕获快照也未写入新凭据，旧凭据必须原样保留（既不被新值覆盖，也不被回滚误删）。
        var config = CreateTestConfiguration("preflight-read-failure");
        config.Authentication = new AuthenticationConfig { Token = "new-token" };
        var storage = new RecordingSecureStorage();
        await storage.SaveAsync("salmonegg/config/preflight-read-failure/token", "old-token");
        var manager = new ConfigurationManager(
            storage,
            new ReadFailingAppFileStore(),
            new AppDataService(),
            NullLogger<ConfigurationManager>.Instance);

        var ex = await Assert.ThrowsAsync<ConfigurationPersistenceException>(
            () => manager.SaveConfigurationAsync(config));

        Assert.Equal(ConfigurationPersistenceFailureReason.ConfigurationWriteFailed, ex.Reason);
        Assert.NotNull(ex.InnerException);
        // 旧凭据未被改动：预检失败发生在任何写入之前。
        Assert.Equal("old-token", await storage.LoadAsync("salmonegg/config/preflight-read-failure/token"));
    }

    [Fact]
    public async Task SaveConfigurationAsync_WhenYamlFlushFailsAfterReplacement_RestoresPreviousYamlAndSecrets()
    {
        var config = CreateTestConfiguration("flush-after-replace");
        config.Authentication = new AuthenticationConfig { ApiKey = "old-api-key" };
        await _configManager.SaveConfigurationAsync(config);

        config.Authentication = new AuthenticationConfig { Token = "new-token" };
        var persistence = new ThrowOnFirstFlushPersistence();
        var manager = new ConfigurationManager(
            _secureStorage,
            new FileSystemAppFileStore(persistence),
            new AppDataService(),
            NullLogger<ConfigurationManager>.Instance);

        var ex = await Assert.ThrowsAsync<ConfigurationPersistenceException>(
            () => manager.SaveConfigurationAsync(config));

        Assert.Equal(ConfigurationPersistenceFailureReason.ConfigurationWriteFailed, ex.Reason);
        Assert.Equal("old-api-key", await _secureStorage.LoadAsync("salmonegg/config/flush-after-replace/apiKey"));
        Assert.Null(await _secureStorage.LoadAsync("salmonegg/config/flush-after-replace/token"));
        var reloaded = await _configManager.LoadConfigurationAsync(config.Id);
        Assert.NotNull(reloaded);
        Assert.Equal("old-api-key", reloaded!.Authentication?.ApiKey);
        Assert.Null(reloaded.Authentication?.Token);
        Assert.Equal(2, persistence.FlushCount);
    }

    [Fact]
    public async Task SaveConfigurationAsync_WhenFirstYamlFlushFails_RemovesCandidateAndDoesNotSaveSecrets()
    {
        var config = CreateTestConfiguration("flush-first-save");
        config.Authentication = new AuthenticationConfig { Token = "new-token" };
        var persistence = new ThrowOnFirstFlushPersistence();
        var manager = new ConfigurationManager(
            _secureStorage,
            new FileSystemAppFileStore(persistence),
            new AppDataService(),
            NullLogger<ConfigurationManager>.Instance);

        var ex = await Assert.ThrowsAsync<ConfigurationPersistenceException>(
            () => manager.SaveConfigurationAsync(config));

        Assert.Equal(ConfigurationPersistenceFailureReason.ConfigurationWriteFailed, ex.Reason);
        Assert.False(File.Exists(GetServerYamlPath(config.Id)));
        Assert.Null(await _secureStorage.LoadAsync("salmonegg/config/flush-first-save/token"));
        Assert.Equal(2, persistence.FlushCount);
    }

    [Fact]
    public async Task SaveConfigurationAsync_WhenSecondSecretMutationFails_RestoresPreviousSecretsAndYaml()
    {
        var config = CreateTestConfiguration("secret-mutation-rollback");
        config.Authentication = new AuthenticationConfig { ApiKey = "old-api-key" };
        var storage = new RecordingSecureStorage();
        var initialManager = new ConfigurationManager(
            storage,
            new FileSystemAppFileStore(),
            new AppDataService(),
            NullLogger<ConfigurationManager>.Instance);
        await initialManager.SaveConfigurationAsync(config);
        var manager = new ConfigurationManager(
            new ThrowingDeleteSecureStorage(storage, failOnCall: 1),
            new FileSystemAppFileStore(),
            new AppDataService(),
            NullLogger<ConfigurationManager>.Instance);
        config.Authentication = new AuthenticationConfig { Token = "new-token" };

        var ex = await Assert.ThrowsAsync<ConfigurationPersistenceException>(
            () => manager.SaveConfigurationAsync(config));

        Assert.Equal(ConfigurationPersistenceFailureReason.SecretPersistenceFailed, ex.Reason);
        Assert.Null(await storage.LoadAsync("salmonegg/config/secret-mutation-rollback/token"));
        Assert.Equal("old-api-key", await storage.LoadAsync("salmonegg/config/secret-mutation-rollback/apiKey"));
        var reloaded = await initialManager.LoadConfigurationAsync(config.Id);
        Assert.NotNull(reloaded);
        Assert.Equal("old-api-key", reloaded!.Authentication?.ApiKey);
        Assert.Null(reloaded.Authentication?.Token);
    }

    [Fact]
    public async Task DeleteConfigurationAsync_WhenSecureCleanupFails_RetainsYamlForRetry()
    {
        var config = CreateTestConfiguration("delete-secure-failure");
        config.Authentication = new AuthenticationConfig { Token = "delete-token" };
        await _configManager.SaveConfigurationAsync(config);
        await _secureStorage.SaveAsync($"salmonegg/config/{config.Id}/apiKey", "legacy-api-key");
        var failingStorage = new ThrowingDeleteSecureStorage(_secureStorage, failOnCall: 2);
        var manager = new ConfigurationManager(
            failingStorage,
            new FileSystemAppFileStore(),
            new AppDataService(),
            NullLogger<ConfigurationManager>.Instance);

        var ex = await Assert.ThrowsAsync<ConfigurationPersistenceException>(
            () => manager.DeleteConfigurationAsync(config.Id));

        Assert.Equal(ConfigurationPersistenceFailureReason.SecureStorageCleanupFailed, ex.Reason);
        Assert.True(File.Exists(GetServerYamlPath(config.Id)));
        Assert.Equal("delete-token", await _secureStorage.LoadAsync($"salmonegg/config/{config.Id}/token"));
        Assert.Equal("legacy-api-key", await _secureStorage.LoadAsync($"salmonegg/config/{config.Id}/apiKey"));

        await manager.DeleteConfigurationAsync(config.Id);
        Assert.False(File.Exists(GetServerYamlPath(config.Id)));
        Assert.Null(await _secureStorage.LoadAsync($"salmonegg/config/{config.Id}/apiKey"));
    }

    [Fact]
    public async Task DeleteConfigurationAsync_WhenYamlDeleteFails_RestoresYamlAndCredentials()
    {
        var config = CreateTestConfiguration("delete-file-failure");
        config.Authentication = new AuthenticationConfig { Token = "delete-token" };
        await _configManager.SaveConfigurationAsync(config);
        var manager = new ConfigurationManager(
            _secureStorage,
            new DeleteFailingAppFileStore(),
            new AppDataService(),
            NullLogger<ConfigurationManager>.Instance);

        var ex = await Assert.ThrowsAsync<ConfigurationPersistenceException>(
            () => manager.DeleteConfigurationAsync(config.Id));

        Assert.Equal(ConfigurationPersistenceFailureReason.ConfigurationDeleteFailed, ex.Reason);
        Assert.True(File.Exists(GetServerYamlPath(config.Id)));
        Assert.Equal("delete-token", await _secureStorage.LoadAsync($"salmonegg/config/{config.Id}/token"));
        Assert.Null(await _secureStorage.LoadAsync($"salmonegg/config/{config.Id}/apiKey"));

        await _configManager.DeleteConfigurationAsync(config.Id);
        Assert.False(File.Exists(GetServerYamlPath(config.Id)));
    }

    [Fact]
    public async Task DeleteConfigurationAsync_RemovesYamlAndSecrets()
    {
        var config = CreateTestConfiguration("to-delete-001");
        config.Authentication = new AuthenticationConfig { Token = "delete-me-token" };

        await _configManager.SaveConfigurationAsync(config);
        Assert.True(File.Exists(GetServerYamlPath(config.Id)));

        await _configManager.DeleteConfigurationAsync(config.Id);

        Assert.False(File.Exists(GetServerYamlPath(config.Id)));
        Assert.Null(await _configManager.LoadConfigurationAsync(config.Id));
        Assert.Null(await _secureStorage.LoadAsync($"salmonegg/config/{config.Id}/token"));
    }

    [Fact]
    public async Task SaveConfigurationAsync_EmptyId_ThrowsArgumentException()
    {
        var config = CreateTestConfiguration("unused");
        config.Id = "";
        await Assert.ThrowsAsync<ArgumentException>(() => _configManager.SaveConfigurationAsync(config));
    }

    [Fact]
    public async Task LoadConfigurationAsync_EmptyId_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _configManager.LoadConfigurationAsync(""));
    }

    [Fact]
    public async Task DeleteConfigurationAsync_EmptyId_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _configManager.DeleteConfigurationAsync(""));
    }

    [Fact]
    public void Constructor_DoesNotCreateServersDirectory()
    {
        var serversDirectory = Path.Combine(_testDirectory, "SalmonEgg", "config", "servers");

        Assert.False(Directory.Exists(serversDirectory));
    }

    [Fact]
    public async Task SaveConfigurationAsync_WhenSchemaTooNew_ThrowsTypedRefusalAndLeavesFileUntouched()
    {
        // 高版本 server 文件必须拒绝写回且原样保留；宿主按 Reason 识别后给出升级指引。
        const string foreignYaml =
            "schema_version: 88\nid: future-001\nname: Future\ntransport: websocket\nserver_url: ws://localhost:1\n";
        var path = GetServerYamlPath("future-001");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, foreignYaml, TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<ConfigurationPersistenceException>(
            () => _configManager.SaveConfigurationAsync(CreateTestConfiguration("future-001")));

        Assert.Equal(ConfigurationPersistenceFailureReason.SchemaVersionTooNew, exception.Reason);
        Assert.Contains("schema_version 88", exception.UserMessage, StringComparison.Ordinal);
        Assert.Contains("Refusing to overwrite", exception.UserMessage, StringComparison.Ordinal);
        Assert.Equal(foreignYaml, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    private string GetServerYamlPath(string id) =>
        Path.Combine(_testDirectory, "SalmonEgg", "config", "servers", $"{id}.yaml");

    private static ServerConfiguration CreateTestConfiguration(string id) =>
        new()
        {
            Id = id,
            Name = $"Test Configuration {id}",
            ServerUrl = "ws://localhost:8080",
            Transport = TransportType.WebSocket,
            ConnectionTimeout = 10
        };

    private static async Task<ConfigurationPersistenceException?> CapturePersistenceResultAsync(Task saveTask)
    {
        try
        {
            await saveTask;
            return null;
        }
        catch (ConfigurationPersistenceException exception)
        {
            return exception;
        }
    }

    private sealed class ThrowOnFirstFlushPersistence : IFileSystemPersistence
    {
        public int FlushCount { get; private set; }

        public Task LoadAsync(System.Threading.CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task FlushAsync(System.Threading.CancellationToken cancellationToken = default)
        {
            FlushCount++;
            if (FlushCount == 1)
            {
                throw new IOException("flush failed after candidate mutation");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FailingSecureStorage : ISecureStorage
    {
        public Task SaveAsync(string key, string value)
            => throw new SecureStorageUnavailableException("Linux Secret Service is unavailable.");

        public Task<string?> LoadAsync(string key)
            => Task.FromResult<string?>(null);

        public Task DeleteAsync(string key)
            => Task.CompletedTask;
    }

    private sealed class LoadFailingSecureStorage : ISecureStorage
    {
        public Task SaveAsync(string key, string value)
            => throw new SecureStorageUnavailableException("Secure storage load is unavailable.");

        public Task<string?> LoadAsync(string key)
            => throw new SecureStorageUnavailableException("Secure storage load is unavailable.");

        public Task DeleteAsync(string key)
            => Task.CompletedTask;
    }

    private sealed class ThrowingDeleteSecureStorage : ISecureStorage
    {
        private readonly ISecureStorage _inner;
        private readonly int _failOnCall;
        private int _deleteCalls;

        public ThrowingDeleteSecureStorage(ISecureStorage inner, int failOnCall)
        {
            _inner = inner;
            _failOnCall = failOnCall;
        }

        public Task SaveAsync(string key, string value) => _inner.SaveAsync(key, value);

        public Task<string?> LoadAsync(string key) => _inner.LoadAsync(key);

        public Task DeleteAsync(string key)
        {
            _deleteCalls++;
            if (_deleteCalls == _failOnCall)
            {
                throw new IOException("delete failed");
            }

            return _inner.DeleteAsync(key);
        }
    }

    private sealed class RecordingSecureStorage : ISecureStorage
    {
        private readonly ConcurrentDictionary<string, string> _values = new(StringComparer.Ordinal);

        public Task SaveAsync(string key, string value)
        {
            ValidateKey(key);
            ArgumentNullException.ThrowIfNull(value);
            _values[key] = value;
            return Task.CompletedTask;
        }

        public Task<string?> LoadAsync(string key)
        {
            ValidateKey(key);
            _values.TryGetValue(key, out var value);
            return Task.FromResult<string?>(value);
        }

        public Task DeleteAsync(string key)
        {
            ValidateKey(key);
            _values.TryRemove(key, out _);
            return Task.CompletedTask;
        }

        private static void ValidateKey(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentNullException(nameof(key));
            }
        }
    }
}
