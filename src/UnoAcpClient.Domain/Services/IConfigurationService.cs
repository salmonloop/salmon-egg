using System.Collections.Generic;
using System.Threading.Tasks;
using UnoAcpClient.Domain.Models;

namespace UnoAcpClient.Domain.Services
{
    /// <summary>
    /// 配置管理服务接口
    /// </summary>
    public interface IConfigurationService
    {
        /// <summary>
        /// 保存服务器配置
        /// </summary>
        /// <param name="config">要保存的配置</param>
        Task SaveConfigurationAsync(ServerConfiguration config);

        /// <summary>
        /// 加载指定的服务器配置
        /// </summary>
        /// <param name="id">配置 ID</param>
        /// <returns>服务器配置，如果不存在则返回 null</returns>
        Task<ServerConfiguration?> LoadConfigurationAsync(string id);

        /// <summary>
        /// 列出所有服务器配置
        /// </summary>
        /// <returns>所有配置的列表</returns>
        Task<IEnumerable<ServerConfiguration>> ListConfigurationsAsync();

        /// <summary>
        /// 删除指定的服务器配置
        /// </summary>
        /// <param name="id">配置 ID</param>
        Task DeleteConfigurationAsync(string id);
    }
}
