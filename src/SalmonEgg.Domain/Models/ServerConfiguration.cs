using System;
using System.Collections.Generic;

namespace SalmonEgg.Domain.Models
{
    /// <summary>
    /// 服务器配置
    /// </summary>
    public class ServerConfiguration
    {
        /// <summary>
        /// 配置唯一标识符
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Opaque persistence revision returned by the authoritative configuration store.
        /// It is used to reject stale read-modify-write operations and is never serialized into
        /// a connection profile by the domain layer.
        /// </summary>
        public string? PersistenceRevision { get; set; }

        /// <summary>
        /// 配置名称
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 服务器 URL
        /// </summary>
        public string ServerUrl { get; set; } = string.Empty;

        /// <summary>
        /// Stdio 命令（仅当 Transport=Stdio 时使用）
        /// </summary>
        public string StdioCommand { get; set; } = string.Empty;

        /// <summary>
        /// Stdio 参数（仅当 Transport=Stdio 时使用）
        /// </summary>
        public List<string> StdioArguments { get; set; } = new();

        /// <summary>
        /// Stdio 进程环境变量（仅当 Transport=Stdio 时使用）。
        /// 部分 ACP agent 只从环境变量读取模型选择与凭据，无法用命令行参数表达。
        /// </summary>
        /// <remarks>
        /// 值随 YAML 明文持久化，因此只用于非敏感运行配置；凭据必须走
        /// <see cref="Authentication"/> 并落入安全存储。
        /// </remarks>
        public Dictionary<string, string> StdioEnvironment { get; set; } = new(StringComparer.Ordinal);

        /// <summary>
        /// 传输类型
        /// </summary>
        public TransportType Transport { get; set; }

        /// <summary>
        /// 认证配置
        /// </summary>
        public AuthenticationConfig? Authentication { get; set; }

        /// <summary>
        /// 代理配置
        /// </summary>
        public ProxyConfig? Proxy { get; set; }

        /// <summary>
        /// 连接超时（秒）
        /// </summary>
        public int ConnectionTimeout { get; set; } = AcpConnectionTimeoutPolicy.DefaultSeconds;

        /// <summary>
        /// 该配置是否通过过端到端连通性测试。
        /// </summary>
        /// <remarks>
        /// 默认 <see cref="ProfileVerification.Unknown"/>：只有明确记录过判定的写入方才会改动它，
        /// 因此 CLI、配置编辑器与本状态出现之前写下的 profile 都不会被回溯标记。
        ///
        /// 极性是有意如此——"已验证"是需要显式写入的正面事实。若某天旧版本客户端丢弃了这个字段，
        /// profile 退化为 <see cref="ProfileVerificationState.Unknown"/>，最坏结果是少一句提醒；
        /// 反向的布尔设计则会让未验证配置凭空自称已验证。
        /// </remarks>
        public ProfileVerification Verification { get; set; } = ProfileVerification.Unknown;

        public string EndpointDisplay
        {
            get
            {
                if (Transport == TransportType.Stdio)
                {
                    var command = (StdioCommand ?? string.Empty).Trim();
                    var args = StdioCommandLine.FormatArgumentsText(StdioArguments);
                    if (string.IsNullOrWhiteSpace(command))
                    {
                        return string.Empty;
                    }

                    return string.IsNullOrWhiteSpace(args) ? command : $"{command} {args}";
                }

                return ServerUrl ?? string.Empty;
            }
        }
    }
}
