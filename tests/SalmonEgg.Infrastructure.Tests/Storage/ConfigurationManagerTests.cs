using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models;
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

        _secureStorage = new SecureStorage();
        _configManager = new ConfigurationManager(_secureStorage);
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

        var yaml = await File.ReadAllTextAsync(GetServerYamlPath(config.Id));
        Assert.Contains("schema_version:", yaml);
        Assert.Contains("id:", yaml);
        Assert.DoesNotContain("{", yaml);

        var loaded = await _configManager.LoadConfigurationAsync(config.Id);
        Assert.NotNull(loaded);
        Assert.Equal(config.Id, loaded!.Id);
        Assert.Equal(config.Name, loaded.Name);
        Assert.Equal(config.ServerUrl, loaded.ServerUrl);
    }

    [Fact]
    public async Task SaveConfigurationAsync_WithBearerToken_StoresSecretNotInYaml()
    {
        var config = CreateTestConfiguration("test-002");
        config.Authentication = new AuthenticationConfig { Token = "secret-token-123" };

        await _configManager.SaveConfigurationAsync(config);

        var yaml = await File.ReadAllTextAsync(GetServerYamlPath(config.Id));
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

        var yaml = await File.ReadAllTextAsync(GetServerYamlPath(config.Id));
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
            StdioArgs = "-T -o BatchMode=yes user@host /opt/acp/bin/agent stdio",
            HeartbeatInterval = 30,
            ConnectionTimeout = 10
        };

        await _configManager.SaveConfigurationAsync(config);

        var yaml = await File.ReadAllTextAsync(GetServerYamlPath(config.Id));
        Assert.Contains("transport: stdio", yaml, StringComparison.Ordinal);
        Assert.Contains("stdio_command: ssh", yaml, StringComparison.Ordinal);
        Assert.Contains("stdio_args: -T -o BatchMode=yes user@host /opt/acp/bin/agent stdio", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadConfigurationAsync_NonExistentConfig_ReturnsNull()
    {
        var loaded = await _configManager.LoadConfigurationAsync("non-existent");
        Assert.Null(loaded);
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
    public async Task LoadConfigurationAsync_UnknownFields_AreIgnored()
    {
        var config = CreateTestConfiguration("unknown-fields-001");
        await _configManager.SaveConfigurationAsync(config);

        var path = GetServerYamlPath(config.Id);
        var yaml = await File.ReadAllTextAsync(path);
        yaml += $"{Environment.NewLine}totally_unknown_field: 123{Environment.NewLine}";
        await File.WriteAllTextAsync(path, yaml);

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
        var yaml = await File.ReadAllTextAsync(path);
        yaml = yaml.Replace("transport: websocket", "transport: totally_unknown_transport", StringComparison.OrdinalIgnoreCase);
        await File.WriteAllTextAsync(path, yaml);

        var loaded = await _configManager.LoadConfigurationAsync(config.Id);
        Assert.NotNull(loaded);
        Assert.Equal(TransportType.WebSocket, loaded!.Transport);
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

    private string GetServerYamlPath(string id) =>
        Path.Combine(_testDirectory, "SalmonEgg", "config", "servers", $"{id}.yaml");

    private static ServerConfiguration CreateTestConfiguration(string id) =>
        new()
        {
            Id = id,
            Name = $"Test Configuration {id}",
            ServerUrl = "ws://localhost:8080",
            Transport = TransportType.WebSocket,
            HeartbeatInterval = 30,
            ConnectionTimeout = 10
        };
}
