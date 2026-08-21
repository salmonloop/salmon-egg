using System.Threading.Tasks;

namespace SalmonEgg.Domain.Services
{
    /// <summary>
    /// 查询服务器敏感凭据存在性的边界。
    /// </summary>
    /// <remarks>
    /// 凭据写入与清除必须经过 <see cref="IConfigurationService"/>，使安全存储中的值与
    /// YAML 中的 authentication mode 保持同一条权威状态链路。此接口只暴露不含明文的
    /// 状态查询，避免 CLI 绕过配置持久化流程。
    /// </remarks>
    public interface IServerCredentialService
    {
        /// <summary>
        /// 查询该服务器的凭据存在性。
        /// </summary>
        /// <param name="serverId">服务器配置 ID</param>
        /// <returns>只含存在性的状态，不含凭据值</returns>
        Task<ServerCredentialStatus> GetStatusAsync(string serverId);
    }
}
