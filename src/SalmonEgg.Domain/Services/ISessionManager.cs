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
        /// 原子地获取或创建会话的<b>运行时追踪容器</b>（tracking slot），已存在则复用、否则新建空容器。
        /// 这里的 <see cref="Session"/> 是当前连接上会话状态的内存投影落地点，<b>不是</b>历史会话的事实源：
        /// 历史正文始终来自 Agent 通过 <c>session/load</c>（V1）或 <c>resume + replayFrom</c>（V2）重放的
        /// <c>session/update</c>。本方法只保证有一个可供重放写入的容器，绝不伪造查不到的历史内容——
        /// 调用方在权威重放前应先 <see cref="UpdateSession"/> 清空该容器。
        /// 相比先 <see cref="GetSession"/> 再 <see cref="CreateSessionAsync"/> 的 check-then-act，
        /// 此方法在并发下不会因竞态而抛"已存在"异常或丢失更新。
        /// </summary>
        /// <param name="sessionId">会话 ID</param>
        /// <param name="cwd">工作目录（仅本调用实际新建容器时生效）</param>
        /// <returns>现有或新创建的运行时追踪容器</returns>
        Session GetOrCreateTrackingSlot(string sessionId, string? cwd = null);

        /// <summary>
        /// 在会话锁下拷贝历史记录，返回快照。
        /// 供 UI 枚举，避免把 live 列表暴露给与写入线程并发的读取方。
        /// </summary>
        /// <param name="sessionId">会话 ID</param>
        /// <returns>历史记录快照；会话不存在时返回空列表</returns>
        IReadOnlyList<SessionUpdateEntry> SnapshotHistory(string sessionId);

        /// <summary>
        /// 更新会话。
        /// </summary>
        /// <param name="sessionId">会话 ID</param>
        /// <param name="updateAction">更新操作</param>
        /// <param name="updateActivity">是否同步更新最后活动时间</param>
        /// <returns>是否成功更新</returns>
        bool UpdateSession(string sessionId, Action<Session> updateAction, bool updateActivity = true);

        /// <summary>
        /// 取消会话。
        /// </summary>
        /// <param name="sessionId">会话 ID</param>
        /// <returns>是否成功取消</returns>
        Task<bool> CancelSessionAsync(string sessionId);

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
