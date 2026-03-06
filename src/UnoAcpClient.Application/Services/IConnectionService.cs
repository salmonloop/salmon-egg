using System.Threading.Tasks;
using UnoAcpClient.Application.Common;
using UnoAcpClient.Domain.Models;

namespace UnoAcpClient.Application.Services
{
    /// <summary>
    /// 连接服务接口
    /// 提供应用层的连接管理功能
    /// </summary>
    public interface IConnectionService
    {
        /// <summary>
        /// 异步连接到服务器
        /// </summary>
        /// <param name="configId">服务器配置 ID</param>
        /// <returns>操作结果</returns>
        Task<Result> ConnectAsync(string configId);

        /// <summary>
        /// 异步断开与服务器的连接
        /// </summary>
        /// <returns>表示异步操作的任务</returns>
        Task DisconnectAsync();

        /// <summary>
        /// 获取当前连接状态
        /// </summary>
        /// <returns>当前连接状态</returns>
        ConnectionState GetCurrentState();
    }
}
