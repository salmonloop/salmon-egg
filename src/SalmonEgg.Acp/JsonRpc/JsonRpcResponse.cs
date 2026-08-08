using System.Text.Json;
using System.Text.Json.Serialization;

namespace SalmonEgg.Acp.JsonRpc
{
    /// <summary>
    /// JSON-RPC 2.0 响应消息。
    /// 用于服务器对请求的响应。响应消息恰好包含 result 或 error 之一。
    /// </summary>
    internal sealed class JsonRpcResponse : JsonRpcMessage
    {
        /// <summary>
        /// 对应请求的唯一标识符。
        /// 必须与请求消息中的 id 值相同。
        /// </summary>
        /// <remarks>
        /// Always serialized, overriding the envelope-wide WhenWritingNull policy. JSON-RPC 2.0
        /// requires every Response to carry an id, and mandates an explicit null when the id could
        /// not be determined (parse error / invalid request). Omitting the member instead would
        /// make the payload a Notification rather than a Response.
        /// </remarks>
        [JsonPropertyName("id")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public object? Id { get; set; }

        /// <summary>
        /// 方法调用的结果。
        /// 在成功时存在，error 为 null。
        /// 可以是任何 JSON 值。
        /// </summary>
        [JsonPropertyName("result")]
        public JsonElement? Result { get; set; }

        /// <summary>
        /// 错误信息。
        /// 在失败时存在，result 为 null。
        /// 如果响应成功，则为 null。
        /// </summary>
        [JsonPropertyName("error")]
        public JsonRpcError? Error { get; set; }

        /// <summary>
        /// 创建一个新的成功响应实例。
        /// </summary>
        public JsonRpcResponse()
        {
        }

        /// <summary>
        /// 创建一个新的成功响应实例。
        /// </summary>
        /// <param name="id">对应的请求 ID</param>
        /// <param name="result">响应结果</param>
        public JsonRpcResponse(object? id, JsonElement? result)
        {
            Id = id;
            Result = result;
            Error = null;
        }

        /// <summary>
        /// 创建一个新的错误响应实例。
        /// </summary>
        /// <param name="id">对应的请求 ID</param>
        /// <param name="error">错误信息</param>
        public JsonRpcResponse(object? id, JsonRpcError error)
        {
            Id = id;
            Result = null;
            Error = error;
        }

        /// <summary>
        /// 判断响应是否成功。
        /// </summary>
        /// <remarks>
        /// Local convenience only. A JSON-RPC 2.0 Response carries exactly jsonrpc/id/result/error,
        /// so this must never reach the wire.
        /// </remarks>
        [JsonIgnore]
        public bool IsSuccess => Error == null && Result.HasValue;

        /// <summary>
        /// 判断响应是否失败。
        /// </summary>
        /// <remarks>
        /// Local convenience only; see <see cref="IsSuccess"/>. Not part of the JSON-RPC envelope.
        /// </remarks>
        [JsonIgnore]
        public bool IsError => Error != null;
    }
}
