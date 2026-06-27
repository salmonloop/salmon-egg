using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SalmonEgg.Domain.Models.Protocol
{
    /// <summary>
    /// Authenticate 方法的请求参数。
    /// 用于向 Agent 发起认证请求。
    /// </summary>
    public class AuthenticateParams
    {
        /// <summary>
        /// Agent-advertised authentication method id (from initializeResponse.authMethods[].id).
        /// </summary>
        [JsonPropertyName("methodId")]
        public string MethodId { get; set; } = string.Empty;

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
        public AuthenticateParams(string methodId)
        {
            MethodId = methodId;
        }
    }

    /// <summary>
    /// Authenticate 方法的响应。
    /// </summary>
    public class AuthenticateResponse
    {
        [JsonPropertyName("_meta")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string, object?>? Meta { get; set; }
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
