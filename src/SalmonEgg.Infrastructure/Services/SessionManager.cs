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
    /// <remarks>
    /// 本类只负责会话的<b>身份与生命周期</b>（按 ID 建、查、删、枚举），不负责保护单个会话的内部状态：
    /// <see cref="GetSession"/> 交出的是 live 引用，会话自身的并发安全由 <see cref="Session"/> 自己持有。
    /// 因此这里没有、也不应该有一把保护会话字段的锁——那把锁只能保护经由本类的写入，
    /// 拿到引用后的直接写入会绕过它，形成"单边加锁"的假象。
    /// 会话集合的原子性由 <see cref="ConcurrentDictionary{TKey, TValue}"/> 提供。
    /// </remarks>
    public class SessionManager : ISessionManager
    {
        private readonly ConcurrentDictionary<string, Session> _sessions = new();

        /// <summary>
        /// 创建新的会话。
        /// </summary>
        /// <param name="sessionId">会话 ID</param>
        /// <param name="cwd">工作目录</param>
        /// <returns>创建后的会话对象</returns>
        public Task<Session> CreateSessionAsync(string sessionId, string cwd)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                throw new ArgumentException("Session ID cannot be empty.", nameof(sessionId));
            }

            ArgumentException.ThrowIfNullOrWhiteSpace(cwd);

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
        /// 原子地获取或创建本地运行时追踪槽（tracking slot）。这里返回的 <see cref="Session"/>
        /// 是当前连接上会话的**运行时投影容器**，不是历史会话的事实源：历史正文由 Agent 在
        /// session/load(V1) 或 resume+replayFrom(V2) 时通过 session/update 重放写入，本槽只是
        /// 承接重放的落地对象（调用方随即 <see cref="Session.ClearHistory"/> 洗净再接收权威重放）。因此"创建"绝不等于
        /// 伪造一个查不到的历史会话——仅是为即将到来的权威加载准备一个空容器。
        /// 并发调用同一 ID 只会创建一个实例、都拿到同一引用、绝不抛错，消除调用方 check-then-act
        /// (先 Get 后 Create)与 <see cref="CreateSessionAsync"/> 的 TryAdd-即抛在并发下"一方必抛"的竞态。
        /// </summary>
        /// <param name="sessionId">会话 ID</param>
        /// <param name="cwd">工作目录（仅在本调用实际创建追踪槽时生效）</param>
        /// <returns>已存在或新建的运行时追踪槽</returns>
        public Session GetOrCreateTrackingSlot(string sessionId, string cwd)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                throw new ArgumentException("Session ID cannot be empty.", nameof(sessionId));
            }

            ArgumentNullException.ThrowIfNull(cwd);

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
                // 「未终止则取消」的判定与写入必须原子完成，这个不可分性属于会话自身，
                // 因此由 Session.TryCancel 在其内部临界区内保证。
                return Task.FromResult(session.TryCancel());
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
