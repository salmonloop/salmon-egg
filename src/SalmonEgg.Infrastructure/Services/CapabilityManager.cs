using System;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Domain.Services;
using SalmonEgg.Acp.Client;

namespace SalmonEgg.Infrastructure.Services
{
    /// <summary>
    /// 能力管理器实现。
    /// 用于管理客户端和 Agent 的能力声明和查询。
    /// </summary>
    public class CapabilityManager : ICapabilityManager
    {
        private readonly ClientCapabilities _clientCapabilities;
        private AgentCapabilities? _agentCapabilities;

        /// <summary>
        /// 创建新的 CapabilityManager 实例。
        /// </summary>
        public CapabilityManager()
        {
            // 统一复用初始化路径的默认客户端能力声明，避免内部查询与外部协商漂移。
            _clientCapabilities = ClientCapabilityDefaults.Create();
        }

        /// <summary>
        /// 创建新的 CapabilityManager 实例，使用自定义的客户端能力。
        /// </summary>
        /// <param name="clientCapabilities">客户端能力</param>
        public CapabilityManager(ClientCapabilities clientCapabilities)
        {
            _clientCapabilities = clientCapabilities ?? throw new ArgumentNullException(nameof(clientCapabilities));
        }

        /// <summary>
        /// 获取客户端的能力声明。
        /// </summary>
        public ClientCapabilities GetClientCapabilities()
        {
            return _clientCapabilities;
        }

        /// <summary>
        /// 设置 Agent 的能力声明。
        /// </summary>
        /// <param name="capabilities">Agent 能力对象</param>
        public void SetAgentCapabilities(AgentCapabilities capabilities)
        {
            _agentCapabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        }

        /// <summary>
        /// 获取 Agent 的能力声明。
        /// </summary>
        public AgentCapabilities? GetAgentCapabilities()
        {
            return _agentCapabilities;
        }

        /// <summary>
        /// 判断是否支持指定的能力。
        /// 优先检查客户端能力，然后检查 Agent 能力。
        /// </summary>
        public bool IsCapabilitySupported(string capabilityName)
        {
            return IsClientCapabilitySupported(capabilityName) || IsAgentCapabilitySupported(capabilityName);
        }

        /// <summary>
        /// 判断客户端是否支持指定的能力。
        /// </summary>
        public bool IsClientCapabilitySupported(string capabilityName)
        {
            if (string.IsNullOrWhiteSpace(capabilityName))
            {
                return false;
            }

            return capabilityName switch
            {
                var name when string.Equals(name, "fs", StringComparison.OrdinalIgnoreCase) =>
                    _clientCapabilities.Fs != null,
                var name when string.Equals(name, "terminal", StringComparison.OrdinalIgnoreCase) =>
                    _clientCapabilities.Terminal ?? false,
                _ => _clientCapabilities.SupportsExtension(capabilityName)
            };
        }

        /// <summary>
        /// 判断 Agent 是否支持指定的能力。
        /// </summary>
        public bool IsAgentCapabilitySupported(string capabilityName)
        {
            if (_agentCapabilities == null || string.IsNullOrWhiteSpace(capabilityName))
            {
                return false;
            }

            return capabilityName switch
            {
                var name when string.Equals(name, "image", StringComparison.OrdinalIgnoreCase) =>
                    _agentCapabilities.SupportsImage,
                var name when string.Equals(name, "audio", StringComparison.OrdinalIgnoreCase) =>
                    _agentCapabilities.SupportsAudio,
                var name when string.Equals(name, "embeddedcontext", StringComparison.OrdinalIgnoreCase) =>
                    _agentCapabilities.SupportsEmbeddedContext,
                var name when string.Equals(name, "loadsession", StringComparison.OrdinalIgnoreCase) =>
                    _agentCapabilities.SupportsSessionLoading,
                var name when string.Equals(name, "http", StringComparison.OrdinalIgnoreCase) =>
                    _agentCapabilities.SupportsHttp,
                var name when string.Equals(name, "sse", StringComparison.OrdinalIgnoreCase) =>
                    _agentCapabilities.SupportsSse,
                _ => false
            };
        }
    }
}
