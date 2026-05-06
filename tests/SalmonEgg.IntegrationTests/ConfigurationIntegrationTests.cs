using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models;
using SalmonEgg.Infrastructure.Storage;
using Xunit;

namespace SalmonEgg.IntegrationTests;

/// <summary>
/// 配置持久化集成测试：YAML（非敏感）+ SecureStorage（敏感）
/// </summary>
public sealed class ConfigurationIntegrationTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly SecureStorage _secureStorage;
    private readonly ConfigurationManager _configManager;

    public ConfigurationIntegrationTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "SalmonEggIntegrationTests", Guid.NewGuid().ToString("N"));
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
    public async Task SaveThenLoadConfiguration_ShouldPreserveNonSensitiveAndSecrets()
    {
        var config = new ServerConfiguration
        {
            Id = "integration-test-001",
            Name = "Integration Test Server",
            ServerUrl = "wss://integration-test.example.com",
            Transport = TransportType.WebSocket,
            ConnectionTimeout = 20,
            Authentication = new AuthenticationConfig
            {
                Token = "integration-token-123"
            },
            Proxy = new ProxyConfig
            {
                Enabled = true,
                ProxyUrl = "http://proxy.integration.com:8080"
            }
        };

        await _configManager.SaveConfigurationAsync(config);

        var yaml = await File.ReadAllTextAsync(GetServerYamlPath(config.Id));
        Assert.DoesNotContain("integration-token-123", yaml, StringComparison.Ordinal);

        var loaded = await _configManager.LoadConfigurationAsync(config.Id);
        Assert.NotNull(loaded);
        Assert.Equal(config.Id, loaded!.Id);
        Assert.Equal(config.Name, loaded.Name);
        Assert.Equal(config.ServerUrl, loaded.ServerUrl);
        Assert.Equal(config.Transport, loaded.Transport);
        Assert.Equal(config.ConnectionTimeout, loaded.ConnectionTimeout);
        Assert.NotNull(loaded.Authentication);
        Assert.Equal("integration-token-123", loaded.Authentication!.Token);
        Assert.NotNull(loaded.Proxy);
        Assert.Equal(config.Proxy!.Enabled, loaded.Proxy!.Enabled);
        Assert.Equal(config.Proxy.ProxyUrl, loaded.Proxy.ProxyUrl);
    }

    [Fact]
    public async Task ManageMultipleConfigurations_ShouldListAllServers()
    {
        var uniqueId = Guid.NewGuid().ToString("N");
        var config1 = new ServerConfiguration { Id = $"multi-{uniqueId}-1", Name = "Server 1", ServerUrl = "wss://server1.example.com", Transport = TransportType.WebSocket };
        var config2 = new ServerConfiguration { Id = $"multi-{uniqueId}-2", Name = "Server 2", ServerUrl = "wss://server2.example.com", Transport = TransportType.HttpSse };
        var config3 = new ServerConfiguration { Id = $"multi-{uniqueId}-3", Name = "Server 3", ServerUrl = "wss://server3.example.com", Transport = TransportType.WebSocket };

        await _configManager.SaveConfigurationAsync(config1);
        await _configManager.SaveConfigurationAsync(config2);
        await _configManager.SaveConfigurationAsync(config3);

        var list = await _configManager.ListConfigurationsAsync();
        var ids = list.Select(x => x.Id).ToList();
        Assert.Contains(config1.Id, ids);
        Assert.Contains(config2.Id, ids);
        Assert.Contains(config3.Id, ids);
    }

    [Fact]
    public async Task DeleteConfiguration_ShouldRemoveYamlAndSecrets()
    {
        var config = new ServerConfiguration
        {
            Id = "to-delete-001",
            Name = "To Delete",
            ServerUrl = "wss://delete.example.com",
            Transport = TransportType.WebSocket,
            Authentication = new AuthenticationConfig { ApiKey = "delete-key" }
        };

        await _configManager.SaveConfigurationAsync(config);
        Assert.True(File.Exists(GetServerYamlPath(config.Id)));
        Assert.NotNull(await _secureStorage.LoadAsync($"salmonegg/config/{config.Id}/apiKey"));

        await _configManager.DeleteConfigurationAsync(config.Id);
        Assert.False(File.Exists(GetServerYamlPath(config.Id)));
        Assert.Null(await _secureStorage.LoadAsync($"salmonegg/config/{config.Id}/apiKey"));
        Assert.Null(await _configManager.LoadConfigurationAsync(config.Id));
    }

    [Fact]
    public async Task CorruptedYaml_ShouldHandleGracefully()
    {
        var configId = "corrupted-test";
        Directory.CreateDirectory(Path.GetDirectoryName(GetServerYamlPath(configId))!);
        await File.WriteAllTextAsync(GetServerYamlPath(configId), ":\n  - definitely not yaml");

        var loaded = await _configManager.LoadConfigurationAsync(configId);
        Assert.Null(loaded);
    }

    private string GetServerYamlPath(string id) =>
        Path.Combine(_testDirectory, "SalmonEgg", "config", "servers", $"{id}.yaml");
}
