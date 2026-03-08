using System;
using SalmonEgg.Domain.Models.Protocol;
using SalmonEgg.Domain.Services;

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
            // 默认声明客户端支持文件系统能力
            _clientCapabilities = new ClientCapabilities
            {
                Fs = new FsCapability(readTextFile: true, writeTextFile: true),
                Terminal = true
            };
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
            return capabilityName.ToLower() switch
            {
                "fs" => _clientCapabilities.Fs != null,
                "terminal" => _clientCapabilities.Terminal ?? false,
                _ => false
            };
        }

        /// <summary>
        /// 判断 Agent 是否支持指定的能力。
        /// </summary>
        public bool IsAgentCapabilitySupported(string capabilityName)
        {
            if (_agentCapabilities == null)
                return false;

            return capabilityName.ToLower() switch
            {
                "image" => _agentCapabilities.SupportsImage,
                "audio" => _agentCapabilities.SupportsAudio,
                "embeddedcontext" => _agentCapabilities.SupportsEmbeddedContext,
                "loadsession" => _agentCapabilities.SupportsSessionLoading,
                "http" => _agentCapabilities.SupportsHttp,
                "sse" => _agentCapabilities.SupportsSse,
                _ => false
            };
        }
    }
}
