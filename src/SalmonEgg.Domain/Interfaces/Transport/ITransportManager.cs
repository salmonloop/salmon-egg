using System.Collections.Generic;
using System.Threading.Tasks;

namespace SalmonEgg.Domain.Interfaces.Transport
{
    /// <summary>
    /// 传输管理器接口。
    /// 用于管理多个传输连接的注册、检索和生命周期。
    /// </summary>
    public interface ITransportManager
    {
        /// <summary>
        /// 注册新的传输连接。
        /// </summary>
        /// <param name="transport">要注册的传输对象</param>
        /// <param name="transportId">传输的唯一标识符（可选，如果为 null 则自动生成）</param>
        /// <returns>注册的传输 ID</returns>
        Task<string> RegisterTransportAsync(ITransport transport, string? transportId = null);

        /// <summary>
        /// 根据 ID 获取传输连接。
        /// </summary>
        /// <param name="transportId">传输 ID</param>
        /// <returns>传输对象，如果不存在则返回 null</returns>
        ITransport? GetTransport(string transportId);

        /// <summary>
        /// 断开指定的传输连接。
        /// </summary>
        /// <param name="transportId">传输 ID</param>
        /// <returns>是否成功断开</returns>
        Task<bool> DisconnectTransportAsync(string transportId);

        /// <summary>
        /// 获取所有活跃的传输 ID 列表。
        /// </summary>
        /// <returns>活跃传输 ID 列表</returns>
        IEnumerable<string> GetActiveTransportIds();

        /// <summary>
        /// 根据 ID 移除传输连接（不执行断开操作）。
        /// </summary>
        /// <param name="transportId">传输 ID</param>
        /// <returns>是否成功移除</returns>
        bool RemoveTransport(string transportId);

        /// <summary>
        /// 断开所有传输连接。
        /// </summary>
        /// <returns>成功断开的传输数量</returns>
        Task<int> DisconnectAllTransportsAsync();
    }
}
