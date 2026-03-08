using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SalmonEgg.Domain.Services.Security;

namespace SalmonEgg.Infrastructure.Services.Security
{
    /// <summary>
    /// 权限管理器实现。
    /// 用于管理权限请求、授予和检查。
    /// </summary>
    public class PermissionManager : IPermissionManager
    {
        private readonly ConcurrentDictionary<string, PermissionEntry> _permissions = new();
        private readonly object _lock = new();

        /// <summary>
        /// 请求权限。
        /// </summary>
        public Task<PermissionResponse> RequestPermissionAsync(string sessionId, string operation, string? path = null, List<PermissionOption>? options = null)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                throw new ArgumentException("会话 ID 不能为空", nameof(sessionId));
            }

            if (string.IsNullOrWhiteSpace(operation))
            {
                throw new ArgumentException("操作名称不能为空", nameof(operation));
            }

            // 检查是否已有权限配置
            var key = BuildPermissionKey(operation, path);

            lock (_lock)
            {
                if (_permissions.TryGetValue(key, out var existingPermission))
                {
                    // 如果已过期，移除
                    if (existingPermission.IsExpired)
                    {
                        _permissions.TryRemove(key, out _);
                    }
                    else if (existingPermission.IsAllowed)
                    {
                        return Task.FromResult(PermissionResponse.Selected());
                    }
                }
            }

            // 如果没有预配置的权限，返回需要用户交互的响应
            // 在实际应用中，这里会触发事件通知 UI 显示权限对话框
            if (options != null && options.Any())
            {
                // 返回默认选项（通常是"询问"）
                var defaultOption = options.FirstOrDefault(o => o.Kind == "ask")
                                 ?? options.FirstOrDefault(o => o.Kind == "allow")
                                 ?? options.FirstOrDefault();

                if (defaultOption != null)
                {
                    return Task.FromResult(PermissionResponse.Selected(defaultOption.OptionId));
                }
            }

            // 默认返回需要用户确认
            return Task.FromResult(new PermissionResponse("pending"));
        }

        /// <summary>
        /// 判断是否允许执行指定操作。
        /// </summary>
        public bool IsOperationAllowed(string operation, string? path = null)
        {
            if (string.IsNullOrWhiteSpace(operation))
            {
                return false;
            }

            var key = BuildPermissionKey(operation, path);

            lock (_lock)
            {
                if (_permissions.TryGetValue(key, out var permission))
                {
                    // 检查是否过期
                    if (permission.IsExpired)
                    {
                        _permissions.TryRemove(key, out _);
                        return false;
                    }

                    return permission.IsAllowed;
                }

                // 尝试检查更通用的权限（不带路径）
                if (!string.IsNullOrWhiteSpace(path))
                {
                    var generalKey = BuildPermissionKey(operation, null);
                    if (_permissions.TryGetValue(generalKey, out var generalPermission))
                    {
                        if (generalPermission.IsExpired)
                        {
                            _permissions.TryRemove(generalKey, out _);
                            return false;
                        }

                        return generalPermission.IsAllowed;
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// 授予权限。
        /// </summary>
        public void GrantPermission(string operation, string? path = null, TimeSpan? duration = null)
        {
            if (string.IsNullOrWhiteSpace(operation))
            {
                throw new ArgumentException("操作名称不能为空", nameof(operation));
            }

            var key = BuildPermissionKey(operation, path);
            var expiresAt = duration.HasValue ? DateTime.UtcNow + duration.Value : (DateTime?)null;

            lock (_lock)
            {
                _permissions[key] = new PermissionEntry
                {
                    Operation = operation,
                    Path = path,
                    IsAllowed = true,
                    ExpiresAt = expiresAt
                };
            }
        }

        /// <summary>
        /// 撤销权限。
        /// </summary>
        public void RevokePermission(string operation, string? path = null)
        {
            if (string.IsNullOrWhiteSpace(operation))
            {
                throw new ArgumentException("操作名称不能为空", nameof(operation));
            }

            var key = BuildPermissionKey(operation, path);

            lock (_lock)
            {
                _permissions.TryRemove(key, out _);
            }
        }

        /// <summary>
        /// 清除所有权限。
        /// </summary>
        public void ClearAllPermissions()
        {
            lock (_lock)
            {
                _permissions.Clear();
            }
        }

        /// <summary>
        /// 获取当前所有的权限配置。
        /// </summary>
        public IEnumerable<PermissionEntry> GetAllPermissions()
        {
            lock (_lock)
            {
                // 过滤掉已过期的权限
                return _permissions.Values
                    .Where(p => !p.IsExpired)
                    .ToList();
            }
        }

        /// <summary>
        /// 构建权限键。
        /// </summary>
        private string BuildPermissionKey(string operation, string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return operation.ToLowerInvariant();
            }

            return $"{operation.ToLowerInvariant()}:{path.ToLowerInvariant()}";
        }

        /// <summary>
        /// 清理已过期的权限。
        /// </summary>
        public int CleanupExpiredPermissions()
        {
            int count = 0;
            var expiredKeys = new List<string>();

            lock (_lock)
            {
                foreach (var kvp in _permissions)
                {
                    if (kvp.Value.IsExpired)
                    {
                        expiredKeys.Add(kvp.Key);
                    }
                }

                foreach (var key in expiredKeys)
                {
                    if (_permissions.TryRemove(key, out _))
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        /// <summary>
        /// 临时授予权限（用于一次性操作）。
        /// </summary>
        public void GrantTemporaryPermission(string operation, string? path = null, int minutes = 10)
        {
            GrantPermission(operation, path, TimeSpan.FromMinutes(minutes));
        }

        /// <summary>
        /// 永久授予权限（无过期时间）。
        /// </summary>
        public void GrantPermanentPermission(string operation, string? path = null)
        {
            GrantPermission(operation, path, null);
        }
    }
}
