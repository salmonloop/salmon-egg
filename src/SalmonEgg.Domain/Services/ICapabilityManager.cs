using SalmonEgg.Domain.Models.Protocol;

namespace SalmonEgg.Domain.Services
{
    /// <summary>
    /// 能力管理器接口。
    /// 用于管理客户端和 Agent 的能力声明和查询。
    /// </summary>
    public interface ICapabilityManager
    {
        /// <summary>
        /// 获取客户端的能力声明。
        /// </summary>
        /// <returns>客户端能力对象</returns>
        ClientCapabilities GetClientCapabilities();

        /// <summary>
        /// 设置 Agent 的能力声明。
        /// </summary>
        /// <param name="capabilities">Agent 能力对象</param>
        void SetAgentCapabilities(AgentCapabilities capabilities);

        /// <summary>
        /// 获取 Agent 的能力声明。
        /// </summary>
        /// <returns>Agent 能力对象，如果未初始化则返回 null</returns>
        AgentCapabilities? GetAgentCapabilities();

        /// <summary>
        /// 判断是否支持指定的能力。
        /// </summary>
        /// <param name="capabilityName">能力名称（例如 "fs", "terminal", "image", "audio"）</param>
        /// <returns>如果支持返回 true，否则返回 false</returns>
        bool IsCapabilitySupported(string capabilityName);

        /// <summary>
        /// 判断客户端是否支持指定的能力。
        /// </summary>
        /// <param name="capabilityName">能力名称</param>
        /// <returns>如果支持返回 true，否则返回 false</returns>
        bool IsClientCapabilitySupported(string capabilityName);

        /// <summary>
        /// 判断 Agent 是否支持指定的能力。
        /// </summary>
        /// <param name="capabilityName">能力名称</param>
        /// <returns>如果支持返回 true，否则返回 false</returns>
        bool IsAgentCapabilitySupported(string capabilityName);
    }
}
