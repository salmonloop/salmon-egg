using System;
using System.Threading.Tasks;
using UnoAcpClient.Application.Common;
using UnoAcpClient.Domain.Models;

namespace UnoAcpClient.Application.Services
{
    /// <summary>
    /// 消息服务接口
    /// 提供应用层的消息发送和接收功能
    /// </summary>
    public interface IMessageService
    {
        /// <summary>
        /// 异步发送请求消息
        /// </summary>
        /// <param name="method">方法名</param>
        /// <param name="parameters">请求参数</param>
        /// <returns>包含响应消息的操作结果</returns>
        Task<Result<AcpMessage>> SendRequestAsync(string method, object parameters);

        /// <summary>
        /// 获取通知消息的可观察流
        /// </summary>
        IObservable<AcpMessage> Notifications { get; }
    }
}
