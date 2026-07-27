using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace SalmonEgg.Acp.Protocol
{
    /// <summary>
    /// Authenticate 方法的请求参数。
    /// 用于向 Agent 发起认证请求。
    /// </summary>
    public sealed record AuthenticateParams : AcpProtocolObject
    {
        /// <summary>
        /// Agent-advertised authentication method id (from initializeResponse.authMethods[].id in v1 or
        /// initializeResponse.authMethods[].methodId in v2).
        /// </summary>
        [JsonPropertyName("methodId")]
        public required string MethodId { get; init; }

        /// <summary>
        /// 创建新的 AuthenticateParams 实例。
        /// </summary>
        public AuthenticateParams()
        {
        }

        /// <summary>
        /// Create params for a specific method id.
        /// </summary>
        /// <param name="methodId">Authentication method id</param>
        [SetsRequiredMembers]
        public AuthenticateParams(string methodId)
        {
            MethodId = methodId;
        }
    }

    /// <summary>
    /// Authenticate 方法的响应。
    /// </summary>
    public sealed record AuthenticateResponse : AcpProtocolObject
    {
    }

    /// <summary>
    /// Logout 方法的请求参数。
    /// </summary>
    public sealed record LogoutParams : AcpProtocolObject
    {
    }

    /// <summary>
    /// Logout 方法的响应。
    /// </summary>
    public sealed record LogoutResponse : AcpProtocolObject
    {
        public static readonly LogoutResponse Completed = new();
    }

    /// <summary>
    /// 认证方法枚举。
    /// </summary>
    public enum AuthMethod
    {
        /// <summary>
        /// Bearer Token 认证。
        /// </summary>
        Bearer,

        /// <summary>
        /// API Key 认证。
        /// </summary>
        ApiKey,

        /// <summary>
        /// 其他认证方法。
        /// </summary>
        Other
    }
}
