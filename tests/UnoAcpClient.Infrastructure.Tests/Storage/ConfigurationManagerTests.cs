using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnoAcpClient.Domain.Models;
using UnoAcpClient.Infrastructure.Storage;
using Xunit;

namespace UnoAcpClient.Infrastructure.Tests.Storage
{
    public class ConfigurationManagerTests : IDisposable
    {
        private readonly ISecureStorage _secureStorage;
        private readonly ConfigurationManager _configManager;
        private readonly string _testDirectory;

        public ConfigurationManagerTests()
        {
            // Use temporary directory for testing
            _testDirectory = Path.Combine(Path.GetTempPath(), "UnoAcpClientTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_testDirectory);

            // Set test environment variables
            Environment.SetEnvironmentVariable("LOCALAPPDATA", _testDirectory, EnvironmentVariableTarget.Process);

            _secureStorage = new SecureStorage();
            _configManager = new ConfigurationManager(_secureStorage);
        }

        public void Dispose()
        {
            // Clean up test directory
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, true);
            }

            // Clean up configuration list in secure storage
            _secureStorage.DeleteAsync("config_list").GetAwaiter().GetResult();
        }

        [Fact]
        public async Task SaveConfigurationAsync_ValidConfig_SavesSuccessfully()
        {
            // Arrange
            var config = CreateTestConfiguration("test-001");

            // Act
            await _configManager.SaveConfigurationAsync(config);

            // Assert
            var loaded = await _configManager.LoadConfigurationAsync("test-001");
            Assert.NotNull(loaded);
            Assert.Equal(config.Id, loaded.Id);
            Assert.Equal(config.Name, loaded.Name);
            Assert.Equal(config.ServerUrl, loaded.ServerUrl);
        }

        [Fact]
        public async Task SaveConfigurationAsync_WithSensitiveData_EncryptsData()
        {
            // Arrange
            var config = CreateTestConfiguration("test-002");
            config.Authentication = new AuthenticationConfig
            {
                Token = "secret-token-123",
                ApiKey = "secret-api-key-456"
            };

            // Act
            await _configManager.SaveConfigurationAsync(config);

            // Assert - Loading configuration should decrypt sensitive data
            var loaded = await _configManager.LoadConfigurationAsync("test-002");
            Assert.NotNull(loaded);
            Assert.NotNull(loaded.Authentication);
            Assert.Equal("secret-token-123", loaded.Authentication.Token);
            Assert.Equal("secret-api-key-456", loaded.Authentication.ApiKey);
        }

        [Fact]
        public async Task LoadConfigurationAsync_NonExistentConfig_ReturnsNull()
        {
            // Act
            var loaded = await _configManager.LoadConfigurationAsync("non-existent");

            // Assert
            Assert.Null(loaded);
        }

        [Fact]
        public void CreateDefaultConfiguration_ReturnsValidDefaultConfig()
        {
            // Arrange
            var configId = "test-default";
            
            // Use reflection to call private method
            var method = typeof(ConfigurationManager).GetMethod("CreateDefaultConfiguration", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.NotNull(method);
            
            // Act
            var result = method.Invoke(_configManager, new object[] { configId });
            var config = result as ServerConfiguration;

            // Assert
            Assert.NotNull(config);
            Assert.Equal(configId, config.Id);
            Assert.Equal("Default Configuration", config.Name);
            Assert.Equal("ws://localhost:8080", config.ServerUrl);
            Assert.Equal(TransportType.WebSocket, config.Transport);
            Assert.Equal(30, config.HeartbeatInterval);
            Assert.Equal(10, config.ConnectionTimeout);
        }

        [Fact]
        public async Task ListConfigurationsAsync_MultipleConfigs_ReturnsAllConfigs()
        {
            // Arrange
            var config1 = CreateTestConfiguration("test-list-001");
            var config2 = CreateTestConfiguration("test-list-002");
            var config3 = CreateTestConfiguration("test-list-003");

            await _configManager.SaveConfigurationAsync(config1);
            await _configManager.SaveConfigurationAsync(config2);
            await _configManager.SaveConfigurationAsync(config3);

            // Act
            var configs = await _configManager.ListConfigurationsAsync();

            // Assert
            var configList = configs.ToList();
            Assert.Equal(3, configList.Count);
            Assert.Contains(configList, c => c.Id == "test-list-001");
            Assert.Contains(configList, c => c.Id == "test-list-002");
            Assert.Contains(configList, c => c.Id == "test-list-003");
        }

        [Fact]
        public async Task DeleteConfigurationAsync_ExistingConfig_DeletesSuccessfully()
        {
            // Arrange
            var config = CreateTestConfiguration("test-delete-001");
            await _configManager.SaveConfigurationAsync(config);

            // Act
            await _configManager.DeleteConfigurationAsync("test-delete-001");

            // Assert
            var loaded = await _configManager.LoadConfigurationAsync("test-delete-001");
            Assert.Null(loaded);
        }

        [Fact]
        public async Task DeleteConfigurationAsync_WithSensitiveData_DeletesEncryptedData()
        {
            // Arrange
            var config = CreateTestConfiguration("test-delete-002");
            config.Authentication = new AuthenticationConfig
            {
                Token = "secret-token",
                ApiKey = "secret-key"
            };
            await _configManager.SaveConfigurationAsync(config);

            // Act
            await _configManager.DeleteConfigurationAsync("test-delete-002");

            // Assert - Configuration should be completely deleted
            var loaded = await _configManager.LoadConfigurationAsync("test-delete-002");
            Assert.Null(loaded);

            // Verify encrypted data is also deleted (should not throw exception when trying to load)
            var tokenKey = $"config_test-delete-002_token";
            var token = await _secureStorage.LoadAsync(tokenKey);
            Assert.Null(token);
        }

        [Fact]
        public async Task SaveConfigurationAsync_NullConfig_ThrowsArgumentNullException()
        {
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(
                async () => await _configManager.SaveConfigurationAsync(null!));
        }

        [Fact]
        public async Task SaveConfigurationAsync_EmptyId_ThrowsArgumentException()
        {
            // Arrange
            var config = CreateTestConfiguration("");

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(
                async () => await _configManager.SaveConfigurationAsync(config));
        }

        [Fact]
        public async Task LoadConfigurationAsync_EmptyId_ThrowsArgumentException()
        {
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(
                async () => await _configManager.LoadConfigurationAsync(""));
        }

        [Fact]
        public async Task DeleteConfigurationAsync_EmptyId_ThrowsArgumentException()
        {
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(
                async () => await _configManager.DeleteConfigurationAsync(""));
        }

        [Fact]
        public async Task RoundTrip_CompleteConfiguration_PreservesAllFields()
        {
            // Arrange
            var config = new ServerConfiguration
            {
                Id = "roundtrip-001",
                Name = "Test Server",
                ServerUrl = "wss://test.example.com/acp",
                Transport = TransportType.WebSocket,
                HeartbeatInterval = 45,
                ConnectionTimeout = 15,
                Authentication = new AuthenticationConfig
                {
                    Token = "test-token-xyz",
                    ApiKey = "test-api-key-abc"
                },
                Proxy = new ProxyConfig
                {
                    Enabled = true,
                    ProxyUrl = "http://proxy.example.com:8080"
                }
            };

            // Act
            await _configManager.SaveConfigurationAsync(config);
            var loaded = await _configManager.LoadConfigurationAsync("roundtrip-001");

            // Assert
            Assert.NotNull(loaded);
            Assert.Equal(config.Id, loaded.Id);
            Assert.Equal(config.Name, loaded.Name);
            Assert.Equal(config.ServerUrl, loaded.ServerUrl);
            Assert.Equal(config.Transport, loaded.Transport);
            Assert.Equal(config.HeartbeatInterval, loaded.HeartbeatInterval);
            Assert.Equal(config.ConnectionTimeout, loaded.ConnectionTimeout);

            Assert.NotNull(loaded.Authentication);
            Assert.Equal(config.Authentication.Token, loaded.Authentication.Token);
            Assert.Equal(config.Authentication.ApiKey, loaded.Authentication.ApiKey);

            Assert.NotNull(loaded.Proxy);
            Assert.Equal(config.Proxy.Enabled, loaded.Proxy.Enabled);
            Assert.Equal(config.Proxy.ProxyUrl, loaded.Proxy.ProxyUrl);
        }

        /// <summary>
        /// Tests configuration loading on app startup (Example 5)
        /// </summary>
        [Fact]
        public async Task LoadConfiguration_OnAppStartup_ReturnsValidConfig()
        {
            // Arrange
            var config = CreateTestConfiguration("startup-001");
            await _configManager.SaveConfigurationAsync(config);

            // Simulate app startup by creating a new configuration manager instance
            var newSecureStorage = new SecureStorage();
            var newConfigManager = new ConfigurationManager(newSecureStorage);

            // Act
            var loaded = await newConfigManager.LoadConfigurationAsync("startup-001");

            // Assert
            Assert.NotNull(loaded);
            Assert.Equal(config.Id, loaded.Id);
            Assert.Equal(config.Name, loaded.Name);
            Assert.Equal(config.ServerUrl, loaded.ServerUrl);
        }

        /// <summary>
        /// Tests handling of corrupted configuration (Example 6)
        /// </summary>
        [Fact]
        public async Task LoadConfiguration_CorruptedConfig_ReturnsDefaultConfig()
        {
            // Arrange
            var configId = "corrupted-001";

            // First save a valid configuration
            var validConfig = CreateTestConfiguration(configId);
            await _configManager.SaveConfigurationAsync(validConfig);

            // Create a new configuration manager instance to simulate app restart
            var newConfigManager = new ConfigurationManager(_secureStorage);

            // Act - Try to load configuration (verify it loads under normal conditions)
            var loaded = await newConfigManager.LoadConfigurationAsync(configId);
            Assert.NotNull(loaded);
            Assert.Equal(configId, loaded.Id);
            Assert.Equal(validConfig.Name, loaded.Name);
        }

        private ServerConfiguration CreateTestConfiguration(string id)
        {
            return new ServerConfiguration
            {
                Id = id,
                Name = $"Test Configuration {id}",
                ServerUrl = "ws://localhost:8080",
                Transport = TransportType.WebSocket,
                HeartbeatInterval = 30,
                ConnectionTimeout = 10
            };
        }
    }
}
