namespace SalmonEgg.Domain.Exceptions
{
    /// <summary>
    /// ACP 协议错误代码枚举
    /// </summary>
    public enum AcpErrorCode
    {
        /// <summary>
        /// 无效的消息格式
        /// </summary>
        InvalidMessage = 1000,
        
        /// <summary>
        /// 不支持的协议版本
        /// </summary>
        UnsupportedVersion = 1001,
        
        /// <summary>
        /// 缺少必需字段
        /// </summary>
        MissingRequiredField = 1002,
        
        /// <summary>
        /// 无效的消息类型
        /// </summary>
        InvalidMessageType = 1003,
        
        /// <summary>
        /// 序列化错误
        /// </summary>
        SerializationError = 1004
    }
}
