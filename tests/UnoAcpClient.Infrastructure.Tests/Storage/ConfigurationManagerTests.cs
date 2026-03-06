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
            // 使用临时目录进行测试
            _testDirectory = Path.Combine(Path.GetTempPath(), "UnoAcpClientTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_testDirectory);

            // 设置测试环境变量
            Environment.SetEnvironmentVariable("LOCALAPPDATA", _testDirectory, EnvironmentVariableTarget.Process);

            _secureStorage = new SecureStorage();
            _configManager = new ConfigurationManager(_secureStorage);
        }

        public void Dispose()
        {
            // 清理测试目录
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, true);
            }

            // 清理安全存储中的配置列表
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

            // Assert - 加载配置应该能解密敏感数据
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
            
            // 使用反射调用私有方法
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

            // Assert - 配置应该被完全删除
            var loaded = await _configManager.LoadConfigurationAsync("test-delete-002");
            Assert.Null(loaded);

            // 验证加密数据也被删除（通过尝试加载不应该抛出异常）
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
