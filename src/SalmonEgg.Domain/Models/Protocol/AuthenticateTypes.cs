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
    /// Agent 对认证请求的响应。
    /// </summary>
    public class AuthenticateResponse
    {
        /// <summary>
        /// 是否认证成功。
        /// </summary>
        [JsonPropertyName("authenticated")]
        public bool Authenticated { get; set; }

        /// <summary>
        /// 可选的消息（例如错误信息或欢迎信息）。
        /// </summary>
        [JsonPropertyName("message")]
        public string? Message { get; set; }

        /// <summary>
        /// 可选的用户信息。
        /// </summary>
        [JsonPropertyName("user")]
        public object? User { get; set; }

        /// <summary>
        /// 创建新的 AuthenticateResponse 实例。
        /// </summary>
        public AuthenticateResponse()
        {
        }

        /// <summary>
        /// 创建新的 AuthenticateResponse 实例。
        /// </summary>
        /// <param name="authenticated">是否认证成功</param>
        /// <param name="message">可选消息</param>
        /// <param name="user">可选用户信息</param>
        public AuthenticateResponse(bool authenticated, string? message = null, object? user = null)
        {
            Authenticated = authenticated;
            Message = message;
            User = user;
        }

        /// <summary>
        /// 创建成功的认证响应。
        /// </summary>
        /// <param name="message">欢迎消息</param>
        /// <param name="user">用户信息</param>
        /// <returns>成功的认证响应</returns>
        public static AuthenticateResponse Success(string? message = null, object? user = null)
        {
            return new AuthenticateResponse(true, message, user);
        }

        /// <summary>
        /// 创建失败的认证响应。
        /// </summary>
        /// <param name="message">错误消息</param>
        /// <returns>失败的认证响应</returns>
        public static AuthenticateResponse Failure(string message)
        {
            return new AuthenticateResponse(false, message);
        }
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
