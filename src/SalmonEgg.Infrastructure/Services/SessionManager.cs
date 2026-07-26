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
                throw new ArgumentException("Session ID cannot be empty.", nameof(sessionId));
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
                throw new InvalidOperationException($"Session '{sessionId}' already exists.");
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
        /// 原子地获取或创建会话。并发调用同一 ID 只会创建一个实例、都拿到同一引用、绝不抛错，
        /// 消除调用方 check-then-act(先 Get 后 Create)与 <see cref="CreateSessionAsync"/> 的
        /// TryAdd-即抛在并发下"一方必抛"的竞态。
        /// </summary>
        /// <param name="sessionId">会话 ID</param>
        /// <param name="cwd">工作目录（仅在本调用实际创建会话时生效）</param>
        /// <returns>已存在或新建的会话对象</returns>
        public Session GetOrCreateSession(string sessionId, string? cwd = null)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                throw new ArgumentException("Session ID cannot be empty.", nameof(sessionId));
            }

            return _sessions.GetOrAdd(sessionId, static (id, arg) =>
            {
                var session = new Session(id, arg)
                {
                    DisplayName = SessionNamePolicy.CreateDefault(id)
                };
                return session;
            }, cwd);
        }

        /// <summary>
        /// 在写锁下拷贝会话历史，返回快照。用于把 live <see cref="Session.History"/> 与并发的
        /// <see cref="Session.AddHistoryEntry"/> 隔离，避免调用方枚举 live 列表时被并发修改破坏。
        /// </summary>
        /// <param name="sessionId">会话 ID</param>
        /// <returns>历史快照；会话不存在时返回空列表</returns>
        public IReadOnlyList<SessionUpdateEntry> SnapshotHistory(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId)
                || !_sessions.TryGetValue(sessionId, out var session))
            {
                return Array.Empty<SessionUpdateEntry>();
            }

            lock (_lock)
            {
                return session.History.ToArray();
            }
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
        /// <returns>是否成功取消</returns>
        public Task<bool> CancelSessionAsync(string sessionId)
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
