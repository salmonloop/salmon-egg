using System.Text.Json.Serialization;

namespace SalmonEgg.Acp.JsonRpc
{
    /// <summary>
    /// Abstract base class for JSON-RPC 2.0 messages.
    /// Every JSON-RPC message must carry a jsonrpc field whose value is always "2.0".
    /// </summary>
    internal abstract class JsonRpcMessage
    {
        /// <summary>
        /// The JSON-RPC protocol version, always "2.0".
        /// Every JSON-RPC 2.0 message must include this field.
        /// </summary>
        [JsonPropertyName("jsonrpc")]
        public string JsonRpc { get; set; } = "2.0";
    }
}
