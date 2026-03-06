namespace UnoAcpClient.Domain.Models
{
    /// <summary>
    /// 传输类型枚举
    /// </summary>
    public enum TransportType
    {
        /// <summary>
        /// WebSocket 传输
        /// </summary>
        WebSocket,

        /// <summary>
        /// HTTP Server-Sent Events 传输
        /// </summary>
        HttpSse
    }
}
