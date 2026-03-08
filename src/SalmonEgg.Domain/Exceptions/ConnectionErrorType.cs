namespace SalmonEgg.Domain.Exceptions
{
    /// <summary>
    /// 连接错误类型枚举
    /// </summary>
    public enum ConnectionErrorType
    {
        /// <summary>
        /// 无效的 URL
        /// </summary>
        InvalidUrl,
        
        /// <summary>
        /// 网络不可达
        /// </summary>
        NetworkUnreachable,
        
        /// <summary>
        /// 连接超时
        /// </summary>
        Timeout,
        
        /// <summary>
        /// 认证失败
        /// </summary>
        AuthenticationFailed,
        
        /// <summary>
        /// 不支持的传输协议
        /// </summary>
        UnsupportedTransport,
        
        /// <summary>
        /// 服务器错误
        /// </summary>
        ServerError
    }
}
