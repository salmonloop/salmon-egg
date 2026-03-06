using UnoAcpClient.Domain.Models;

namespace UnoAcpClient.Domain.Services
{
    /// <summary>
    /// 定义 ACP 协议处理服务的接口
    /// </summary>
    public interface IAcpProtocolService
    {
        /// <summary>
        /// 解析 JSON 字符串为 ACP 消息对象
        /// </summary>
        /// <param name="json">JSON 格式的消息字符串</param>
        /// <returns>解析后的 ACP 消息对象</returns>
        /// <exception cref="Exceptions.AcpProtocolException">当消息格式无效时抛出</exception>
        AcpMessage ParseMessage(string json);

        /// <summary>
        /// 将 ACP 消息对象序列化为 JSON 字符串
        /// </summary>
        /// <param name="message">要序列化的 ACP 消息对象</param>
        /// <returns>JSON 格式的消息字符串</returns>
        string SerializeMessage(AcpMessage message);

        /// <summary>
        /// 验证 ACP 消息的有效性
        /// </summary>
        /// <param name="message">要验证的 ACP 消息对象</param>
        /// <returns>如果消息有效返回 true，否则返回 false</returns>
        bool ValidateMessage(AcpMessage message);

        /// <summary>
        /// 协商客户端和服务器之间的协议版本
        /// </summary>
        /// <param name="clientVersion">客户端支持的协议版本</param>
        /// <param name="serverVersion">服务器支持的协议版本</param>
        /// <returns>协商后的协议版本，如果不兼容则返回错误信息</returns>
        string NegotiateVersion(string clientVersion, string serverVersion);
    }
}
