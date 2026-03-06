namespace UnoAcpClient.Domain.Models
{
    /// <summary>
    /// 认证配置
    /// </summary>
    public class AuthenticationConfig
    {
        /// <summary>
        /// 认证令牌
        /// </summary>
        public string? Token { get; set; }

        /// <summary>
        /// API 密钥
        /// </summary>
        public string? ApiKey { get; set; }
    }
}
