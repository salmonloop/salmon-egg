using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using UnoAcpClient.Domain.Models;
using UnoAcpClient.Domain.Services;

namespace UnoAcpClient.Infrastructure.Storage
{
    /// <summary>
    /// 配置管理器实现
    /// 负责服务器配置的持久化、加载、加密和验证
    /// </summary>
    public class ConfigurationManager : IConfigurationService
    {
        private readonly ISecureStorage _secureStorage;
        private readonly string _configDirectory;
        private readonly JsonSerializerOptions _jsonOptions;

        // 用于存储配置列表的键
        private const string ConfigListKey = "config_list";

        public ConfigurationManager(ISecureStorage secureStorage)
        {
            _secureStorage = secureStorage ?? throw new ArgumentNullException(nameof(secureStorage));

            // 设置配置文件目录
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _configDirectory = Path.Combine(appData, "UnoAcpClient", "Configurations");

            if (!Directory.Exists(_configDirectory))
                Directory.CreateDirectory(_configDirectory);

            // 配置 JSON 序列化选项
            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNameCaseInsensitive = true
            };
        }

        /// <summary>
        /// 保存服务器配置
        /// </summary>
        public async Task SaveConfigurationAsync(ServerConfiguration config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            if (string.IsNullOrWhiteSpace(config.Id))
                throw new ArgumentException("Configuration ID cannot be empty", nameof(config));

            try
            {
                // 1. 加密敏感信息
                var secureConfig = await EncryptSensitiveDataAsync(config);

                // 2. 序列化配置
                var json = JsonSerializer.Serialize(secureConfig, _jsonOptions);

                // 3. 保存到文件
                var filePath = GetConfigFilePath(config.Id);
                await File.WriteAllTextAsync(filePath, json);

                // 4. 更新配置列表
                await UpdateConfigListAsync(config.Id, config.Name);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to save configuration '{config.Id}'", ex);
            }
        }

        /// <summary>
        /// 加载指定的服务器配置
        /// </summary>
        public async Task<ServerConfiguration?> LoadConfigurationAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Configuration ID cannot be empty", nameof(id));

            try
            {
                var filePath = GetConfigFilePath(id);

                // 检查文件是否存在
                if (!File.Exists(filePath))
                    return null;

                // 1. 从文件加载
                var json = await File.ReadAllTextAsync(filePath);

                // 2. 反序列化
                var config = JsonSerializer.Deserialize<ServerConfiguration>(json, _jsonOptions);

                if (config == null)
                {
                    // 配置文件损坏，返回默认配置
                    return CreateDefaultConfiguration(id);
                }

                // 3. 解密敏感信息
                var decryptedConfig = await DecryptSensitiveDataAsync(config);

                // 4. 验证配置
                if (!ValidateConfiguration(decryptedConfig))
                {
                    // 配置无效，返回默认配置
                    return CreateDefaultConfiguration(id);
                }

                return decryptedConfig;
            }
            catch (JsonException)
            {
                // JSON 解析失败，配置文件损坏，返回默认配置
                return CreateDefaultConfiguration(id);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to load configuration '{id}'", ex);
            }
        }

        /// <summary>
        /// 列出所有服务器配置
        /// </summary>
        public async Task<IEnumerable<ServerConfiguration>> ListConfigurationsAsync()
        {
            try
            {
                var configList = await GetConfigListAsync();
                var configurations = new List<ServerConfiguration>();

                foreach (var configId in configList.Keys)
                {
                    var config = await LoadConfigurationAsync(configId);
                    if (config != null)
                    {
                        configurations.Add(config);
                    }
                }

                return configurations;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to list configurations", ex);
            }
        }

        /// <summary>
        /// 删除指定的服务器配置
        /// </summary>
        public async Task DeleteConfigurationAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Configuration ID cannot be empty", nameof(id));

            try
            {
                // 1. 删除配置文件
                var filePath = GetConfigFilePath(id);
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }

                // 2. 删除加密的敏感信息
                await DeleteEncryptedDataAsync(id);

                // 3. 从配置列表中移除
                await RemoveFromConfigListAsync(id);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to delete configuration '{id}'", ex);
            }
        }

        #region Private Helper Methods

        /// <summary>
        /// 加密配置中的敏感信息
        /// </summary>
        private async Task<ServerConfiguration> EncryptSensitiveDataAsync(ServerConfiguration config)
        {
            var secureConfig = CloneConfiguration(config);

            if (secureConfig.Authentication != null)
            {
                // 加密 Token
                if (!string.IsNullOrEmpty(secureConfig.Authentication.Token))
                {
                    var tokenKey = GetSecureKey(config.Id, "token");
                    await _secureStorage.SaveAsync(tokenKey, secureConfig.Authentication.Token);
                    secureConfig.Authentication.Token = "[ENCRYPTED]";
                }

                // 加密 ApiKey
                if (!string.IsNullOrEmpty(secureConfig.Authentication.ApiKey))
                {
                    var apiKeyKey = GetSecureKey(config.Id, "apikey");
                    await _secureStorage.SaveAsync(apiKeyKey, secureConfig.Authentication.ApiKey);
                    secureConfig.Authentication.ApiKey = "[ENCRYPTED]";
                }
            }

            return secureConfig;
        }

        /// <summary>
        /// 解密配置中的敏感信息
        /// </summary>
        private async Task<ServerConfiguration> DecryptSensitiveDataAsync(ServerConfiguration config)
        {
            var decryptedConfig = CloneConfiguration(config);

            if (decryptedConfig.Authentication != null)
            {
                // 解密 Token
                if (decryptedConfig.Authentication.Token == "[ENCRYPTED]")
                {
                    var tokenKey = GetSecureKey(config.Id, "token");
                    var token = await _secureStorage.LoadAsync(tokenKey);
                    decryptedConfig.Authentication.Token = token;
                }

                // 解密 ApiKey
                if (decryptedConfig.Authentication.ApiKey == "[ENCRYPTED]")
                {
                    var apiKeyKey = GetSecureKey(config.Id, "apikey");
                    var apiKey = await _secureStorage.LoadAsync(apiKeyKey);
                    decryptedConfig.Authentication.ApiKey = apiKey;
                }
            }

            return decryptedConfig;
        }

        /// <summary>
        /// 删除加密的敏感数据
        /// </summary>
        private async Task DeleteEncryptedDataAsync(string configId)
        {
            var tokenKey = GetSecureKey(configId, "token");
            var apiKeyKey = GetSecureKey(configId, "apikey");

            await _secureStorage.DeleteAsync(tokenKey);
            await _secureStorage.DeleteAsync(apiKeyKey);
        }

        /// <summary>
        /// 验证配置的完整性
        /// </summary>
        private bool ValidateConfiguration(ServerConfiguration config)
        {
            if (config == null)
                return false;

            if (string.IsNullOrWhiteSpace(config.Id))
                return false;

            if (string.IsNullOrWhiteSpace(config.ServerUrl))
                return false;

            // 验证 URL 格式
            if (!Uri.TryCreate(config.ServerUrl, UriKind.Absolute, out _))
                return false;

            return true;
        }

        /// <summary>
        /// 创建默认配置
        /// </summary>
        private ServerConfiguration CreateDefaultConfiguration(string id)
        {
            return new ServerConfiguration
            {
                Id = id,
                Name = "Default Configuration",
                ServerUrl = "ws://localhost:8080",
                Transport = TransportType.WebSocket,
                HeartbeatInterval = 30,
                ConnectionTimeout = 10
            };
        }

        /// <summary>
        /// 克隆配置对象
        /// </summary>
        private ServerConfiguration CloneConfiguration(ServerConfiguration config)
        {
            var json = JsonSerializer.Serialize(config, _jsonOptions);
            return JsonSerializer.Deserialize<ServerConfiguration>(json, _jsonOptions)!;
        }

        /// <summary>
        /// 获取配置文件路径
        /// </summary>
        private string GetConfigFilePath(string configId)
        {
            var fileName = $"{configId}.json";
            return Path.Combine(_configDirectory, fileName);
        }

        /// <summary>
        /// 获取安全存储的键
        /// </summary>
        private string GetSecureKey(string configId, string field)
        {
            return $"config_{configId}_{field}";
        }

        /// <summary>
        /// 获取配置列表
        /// </summary>
        private async Task<Dictionary<string, string>> GetConfigListAsync()
        {
            try
            {
                var json = await _secureStorage.LoadAsync(ConfigListKey);
                if (string.IsNullOrEmpty(json))
                {
                    return new Dictionary<string, string>();
                }

                return JsonSerializer.Deserialize<Dictionary<string, string>>(json, _jsonOptions)
                    ?? new Dictionary<string, string>();
            }
            catch
            {
                return new Dictionary<string, string>();
            }
        }

        /// <summary>
        /// 更新配置列表
        /// </summary>
        private async Task UpdateConfigListAsync(string configId, string configName)
        {
            var configList = await GetConfigListAsync();
            configList[configId] = configName;

            var json = JsonSerializer.Serialize(configList, _jsonOptions);
            await _secureStorage.SaveAsync(ConfigListKey, json);
        }

        /// <summary>
        /// 从配置列表中移除
        /// </summary>
        private async Task RemoveFromConfigListAsync(string configId)
        {
            var configList = await GetConfigListAsync();
            configList.Remove(configId);

            var json = JsonSerializer.Serialize(configList, _jsonOptions);
            await _secureStorage.SaveAsync(ConfigListKey, json);
        }

        #endregion
    }
}
