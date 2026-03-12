using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models.Session;
using SalmonEgg.Domain.Services;


namespace SalmonEgg.Infrastructure.Services
{
    /// <summary>
    /// 会话管理器实现。
    /// 用于管理会话的创建、检索、更新和取消。
    /// </summary>
    public class SessionManager : ISessionManager
    {
        private readonly ConcurrentDictionary<string, Session> _sessions = new();
        private readonly object _lock = new();

        /// <summary>
        /// 创建新的会话。
        /// </summary>
        /// <param name="sessionId">会话 ID</param>
        /// <param name="cwd">工作目录</param>
        /// <returns>创建后的会话对象</returns>
        public Task<Session> CreateSessionAsync(string sessionId, string? cwd = null)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                throw new ArgumentException("会话 ID 不能为空", nameof(sessionId));
            }

            var session = new Session(sessionId, cwd);
            session.DisplayName = SessionNamePolicy.CreateDefault(sessionId);

            // 如果会话已存在，抛出异常
            if (_sessions.TryAdd(sessionId, session))
            {
                return Task.FromResult(session);
            }
            else
            {
                throw new InvalidOperationException($"会话 '{sessionId}' 已存在");
            }
        }

        /// <summary>
        /// 根据会话 ID 获取会话。
        /// </summary>
        /// <param name="sessionId">会话 ID</param>
        /// <returns>会话对象，如果不存在则返回 null</returns>
        public Session? GetSession(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return null;
            }

            return _sessions.TryGetValue(sessionId, out var session) ? session : null;
        }

        /// <summary>
        /// 更新会话。
        /// </summary>
        /// <param name="sessionId">会话 ID</param>
        /// <param name="updateAction">更新操作</param>
        /// <param name="updateActivity">是否同步更新最后活动时间</param>
        /// <returns>是否成功更新</returns>
        public bool UpdateSession(string sessionId, Action<Session> updateAction, bool updateActivity = true)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return false;
            }

            if (updateAction == null)
            {
                throw new ArgumentNullException(nameof(updateAction));
            }

            if (_sessions.TryGetValue(sessionId, out var session))
            {
                lock (_lock)
                {
                    updateAction(session);
                    if (updateActivity)
                    {
                        session.UpdateActivity();
                    }
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 取消会话。
        /// </summary>
        /// <param name="sessionId">会话 ID</param>
        /// <param name="reason">取消原因</param>
        /// <returns>是否成功取消</returns>
        public Task<bool> CancelSessionAsync(string sessionId, string? reason = null)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return Task.FromResult(false);
            }

            if (_sessions.TryGetValue(sessionId, out var session))
            {
                lock (_lock)
                {
                    if (session.IsTerminated)
                    {
                        // 会话已经终止
                        return Task.FromResult(false);
                    }

                    session.State = SessionState.Cancelled;
                    session.UpdateActivity();

                    // 可以在这里记录取消原因
                    if (!string.IsNullOrWhiteSpace(reason))
                    {
                        // 将取消原因添加到历史中（如果需要）
                    }
                }
                return Task.FromResult(true);
            }

            return Task.FromResult(false);
        }

        /// <summary>
        /// 获取所有会话。
        /// </summary>
        /// <returns>所有会话的列表</returns>
        public IEnumerable<Session> GetAllSessions()
        {
            return _sessions.Values;
        }

        /// <summary>
        /// 删除会话。
        /// </summary>
        /// <param name="sessionId">会话 ID</param>
        /// <returns>是否成功删除</returns>
        public bool RemoveSession(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return false;
            }

            return _sessions.TryRemove(sessionId, out _);
        }

        /// <summary>
        /// 获取活跃的会话数量。
        /// </summary>
        public int GetActiveSessionCount()
        {
            int count = 0;
            foreach (var session in _sessions.Values)
            {
                if (session.IsActive)
                {
                    count++;
                }
            }
            return count;
        }

        /// <summary>
        /// 清理已终止的会话。
        /// </summary>
        /// <returns>清理的会话数量</returns>
        public int CleanupTerminatedSessions()
        {
            var terminatedSessions = new List<string>();

            foreach (var kvp in _sessions)
            {
                if (kvp.Value.IsTerminated)
                {
                    terminatedSessions.Add(kvp.Key);
                }
            }

            int count = 0;
            foreach (var sessionId in terminatedSessions)
            {
                if (_sessions.TryRemove(sessionId, out _))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// 生成唯一的会话 ID。
        /// </summary>
        /// <returns>唯一的会话 ID</returns>
        public static string GenerateSessionId()
        {
            return Guid.NewGuid().ToString("N");
        }
    }
}
