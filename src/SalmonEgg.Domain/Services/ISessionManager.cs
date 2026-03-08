using System.Collections.Generic;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models.Session;
using System;

namespace SalmonEgg.Domain.Services
{
    /// <summary>
    /// 会话管理器接口。
    /// 用于管理会话的创建、检索、更新和取消。
    /// </summary>
    public interface ISessionManager
    {
        /// <summary>
        /// 创建新的会话。
        /// </summary>
        /// <param name="sessionId">会话 ID</param>
        /// <param name="cwd">工作目录</param>
        /// <returns>创建后的会话对象</returns>
        Task<Session> CreateSessionAsync(string sessionId, string? cwd = null);

        /// <summary>
        /// 根据会话 ID 获取会话。
        /// </summary>
        /// <param name="sessionId">会话 ID</param>
        /// <returns>会话对象，如果不存在则返回 null</returns>
        Session? GetSession(string sessionId);

        /// <summary>
        /// 更新会话。
        /// </summary>
        /// <param name="sessionId">会话 ID</param>
        /// <param name="updateAction">更新操作</param>
        /// <returns>是否成功更新</returns>
        bool UpdateSession(string sessionId, Action<Session> updateAction);

        /// <summary>
        /// 取消会话。
        /// </summary>
        /// <param name="sessionId">会话 ID</param>
        /// <param name="reason">取消原因</param>
        /// <returns>是否成功取消</returns>
        Task<bool> CancelSessionAsync(string sessionId, string? reason = null);

        /// <summary>
        /// 获取所有会话。
        /// </summary>
        /// <returns>所有会话的列表</returns>
        IEnumerable<Session> GetAllSessions();

        /// <summary>
        /// 删除会话。
        /// </summary>
        /// <param name="sessionId">会话 ID</param>
        /// <returns>是否成功删除</returns>
        bool RemoveSession(string sessionId);
    }
}
