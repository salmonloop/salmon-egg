using System.Text.Json;
using System.Text.Json.Serialization;

namespace SalmonEgg.Domain.Models.JsonRpc
{
    /// <summary>
    /// JSON-RPC 2.0 响应消息。
    /// 用于服务器对请求的响应。响应消息恰好包含 result 或 error 之一。
    /// </summary>
    public class JsonRpcResponse : JsonRpcMessage
    {
        /// <summary>
        /// 对应请求的唯一标识符。
        /// 必须与请求消息中的 id 值相同。
        /// </summary>
        [JsonPropertyName("id")]
        public object Id { get; set; } = string.Empty;

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
        public JsonRpcResponse(object id, JsonElement? result)
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
        public JsonRpcResponse(object id, JsonRpcError error)
        {
            Id = id;
            Result = null;
            Error = error;
        }

        /// <summary>
        /// 判断响应是否成功。
        /// </summary>
        public bool IsSuccess => Error == null && Result.HasValue;

        /// <summary>
        /// 判断响应是否失败。
        /// </summary>
        public bool IsError => Error != null;
    }
}
