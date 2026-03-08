using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SalmonEgg.Infrastructure.Storage
{
    /// <summary>
    /// WebAssembly 平台安全存储实现
    /// 注意：WebAssembly 环境中不持久化敏感信息，仅在会话期间保存在内存中
    /// </summary>
    public class WebAssemblySecureStorage : ISecureStorage
    {
        private readonly Dictionary<string, string> _memoryStorage = new Dictionary<string, string>();

        public Task SaveAsync(string key, string value)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentNullException(nameof(key));
            if (value == null)
                throw new ArgumentNullException(nameof(value));

            _memoryStorage[key] = value;
            return Task.CompletedTask;
        }

        public Task<string?> LoadAsync(string key)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentNullException(nameof(key));

            _memoryStorage.TryGetValue(key, out var value);
            return Task.FromResult<string?>(value);
        }

        public Task DeleteAsync(string key)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentNullException(nameof(key));

            _memoryStorage.Remove(key);
            return Task.CompletedTask;
        }
    }
}
