using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace SalmonEgg.Infrastructure.Storage
{
    /// <summary>
    /// 基础安全存储实现
    /// 注意：此实现使用简单的 Base64 编码，不提供真正的加密
    /// 平台特定的实现应该在各平台项目中提供，使用：
    /// - Windows: DPAPI (Data Protection API)
    /// - iOS: Keychain
    /// - Android: KeyStore
    /// - WebAssembly: 内存存储（不持久化敏感信息）
    /// </summary>
    public class SecureStorage : ISecureStorage
    {
        private readonly string _storageDirectory;
        private readonly Dictionary<string, string> _memoryCache = new Dictionary<string, string>();

        public SecureStorage()
        {
            // 使用应用数据目录
            var appDataRoot = SalmonEggPaths.GetAppDataRootPath();
            _storageDirectory = Path.Combine(appDataRoot, "SecureStorage");
            
            if (!Directory.Exists(_storageDirectory))
                Directory.CreateDirectory(_storageDirectory);
        }

        public virtual async Task SaveAsync(string key, string value)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentNullException(nameof(key));
            if (value == null)
                throw new ArgumentNullException(nameof(value));

            await Task.Run(() =>
            {
                try
                {
                    // 简单的 Base64 编码（注意：这不是真正的加密）
                    var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
                    var filePath = GetFilePath(key);
                    File.WriteAllText(filePath, encoded);
                    
                    // 同时缓存到内存
                    _memoryCache[key] = value;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to save secure data for key '{key}'", ex);
                }
            });
        }

        public virtual async Task<string?> LoadAsync(string key)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentNullException(nameof(key));

            return await Task.Run(() =>
            {
                try
                {
                    // 先检查内存缓存
                    if (_memoryCache.TryGetValue(key, out var cachedValue))
                        return cachedValue;

                    var filePath = GetFilePath(key);
                    if (!File.Exists(filePath))
                        return null;

                    var encoded = File.ReadAllText(filePath);
                    var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
                    
                    // 缓存到内存
                    _memoryCache[key] = decoded;
                    
                    return decoded;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to load secure data for key '{key}'", ex);
                }
            });
        }

        public virtual async Task DeleteAsync(string key)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentNullException(nameof(key));

            await Task.Run(() =>
            {
                try
                {
                    var filePath = GetFilePath(key);
                    if (File.Exists(filePath))
                        File.Delete(filePath);
                    
                    // 从内存缓存中移除
                    _memoryCache.Remove(key);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to delete secure data for key '{key}'", ex);
                }
            });
        }

        private string GetFilePath(string key)
        {
            // 使用 key 的哈希作为文件名，避免特殊字符问题
            var fileName = Convert.ToBase64String(
                Encoding.UTF8.GetBytes(key))
                .Replace("/", "_")
                .Replace("+", "-") + ".dat";
            return Path.Combine(_storageDirectory, fileName);
        }
    }
}
