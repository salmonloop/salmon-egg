using System.Text.Json.Serialization;

namespace SalmonEgg.Domain.Models.JsonRpc
{
    /// <summary>
    /// JSON-RPC 2.0 消息的抽象基类。
    /// 所有 JSON-RPC 消息都必须包含 jsonrpc 字段，其值固定为 "2.0"。
    /// </summary>
    public abstract class JsonRpcMessage
    {
        /// <summary>
        /// JSON-RPC 协议版本，固定为 "2.0"。
        /// 所有 JSON-RPC 2.0 消息都必须包含此字段。
        /// </summary>
        [JsonPropertyName("jsonrpc")]
        public string JsonRpc { get; set; } = "2.0";
    }
}
