using System;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models;

namespace SalmonEgg.Domain.Services
{
    /// <summary>
    /// 定义连接管理服务的接口
    /// </summary>
    public interface IConnectionManager
    {
        /// <summary>
        /// 异步连接到 ACP 服务器
        /// </summary>
        /// <param name="config">服务器配置</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>连接操作的结果</returns>
        Task<ConnectionResult> ConnectAsync(ServerConfiguration config, CancellationToken ct);

        /// <summary>
        /// 异步断开与服务器的连接
        /// </summary>
        /// <returns>表示异步操作的任务</returns>
        Task DisconnectAsync();

        /// <summary>
        /// 异步发送 ACP 消息到服务器
        /// </summary>
        /// <param name="message">要发送的 ACP 消息</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>发送操作的结果</returns>
        Task<SendResult> SendMessageAsync(AcpMessage message, CancellationToken ct);

        /// <summary>
        /// 获取传入消息的可观察流
        /// </summary>
        IObservable<AcpMessage> IncomingMessages { get; }

        /// <summary>
        /// 获取连接状态变化的可观察流
        /// </summary>
        IObservable<ConnectionState> ConnectionStateChanges { get; }
    }
}
