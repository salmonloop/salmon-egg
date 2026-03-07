using System.Text.Json;
using UnoAcpClient.Domain.Models.JsonRpc;

namespace UnoAcpClient.Domain.Interfaces
{
    /// <summary>
    /// JSON-RPC 2.0 消息解析器接口。
    /// 提供消息的解析和序列化功能。
    /// </summary>
    public interface IMessageParser
    {
        /// <summary>
        /// 解析 JSON 字符串为 JSON-RPC 消息。
        /// 自动检测消息类型（请求、通知、响应）。
        /// </summary>
        /// <param name="json">JSON 字符串</param>
        /// <returns>解析后的 JsonRpcMessage 实例</returns>
        /// <exception cref="AcpException">当 JSON 无效或解析失败时抛出</exception>
        JsonRpcMessage ParseMessage(string json);

        /// <summary>
        /// 解析 JSON 字符串为请求消息。
        /// </summary>
        /// <param name="json">JSON 字符串</param>
        /// <returns>JsonRpcRequest 实例</returns>
        /// <exception cref="AcpException">当 JSON 无效、不是请求消息或解析失败时抛出</exception>
        JsonRpcRequest ParseRequest(string json);

        /// <summary>
        /// 解析 JSON 字符串为通知消息。
        /// </summary>
        /// <param name="json">JSON 字符串</param>
        /// <returns>JsonRpcNotification 实例</returns>
        /// <exception cref="AcpException">当 JSON 无效、不是通知消息或解析失败时抛出</exception>
        JsonRpcNotification ParseNotification(string json);

        /// <summary>
        /// 解析 JSON 字符串为响应消息。
        /// </summary>
        /// <param name="json">JSON 字符串</param>
        /// <returns>JsonRpcResponse 实例</returns>
        /// <exception cref="AcpException">当 JSON 无效、不是响应消息或解析失败时抛出</exception>
        JsonRpcResponse ParseResponse(string json);

        /// <summary>
        /// 将 JSON-RPC 消息序列化为 JSON 字符串。
        /// </summary>
        /// <param name="message">要序列化的消息</param>
        /// <returns>JSON 字符串</returns>
        string SerializeMessage(JsonRpcMessage message);

       /// <summary>
       /// 获取 JsonSerializerOptions 实例供外部使用。
       /// </summary>
       JsonSerializerOptions Options { get; }
       }
   }
