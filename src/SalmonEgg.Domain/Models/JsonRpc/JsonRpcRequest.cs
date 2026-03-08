using System.Text.Json;
using System.Text.Json.Serialization;

namespace SalmonEgg.Domain.Models.JsonRpc
{
    /// <summary>
    /// JSON-RPC 2.0 请求消息。
    /// 用于从客户端向服务器发送请求。
    /// </summary>
    public class JsonRpcRequest : JsonRpcMessage
    {
        /// <summary>
        /// 请求的唯一标识符。
        /// 可以是字符串、数字、null，但不能是布尔值。
        /// 服务器必须在响应中包含相同的 id。
        /// </summary>
        [JsonPropertyName("id")]
        public object Id { get; set; } = string.Empty;

        /// <summary>
        /// 要调用的方法名。
        /// </summary>
        [JsonPropertyName("method")]
        public string Method { get; set; } = string.Empty;

        /// <summary>
        /// 方法的参数。
        /// 可以是对象、数组、原始值或省略。
        /// </summary>
        [JsonPropertyName("params")]
        public JsonElement? Params { get; set; }

        /// <summary>
        /// 创建一个新的 JsonRpcRequest 实例。
        /// </summary>
        public JsonRpcRequest()
        {
        }

        /// <summary>
        /// 创建一个新的 JsonRpcRequest 实例。
        /// </summary>
        /// <param name="id">请求 ID</param>
        /// <param name="method">方法名</param>
        /// <param name="params">方法参数</param>
        public JsonRpcRequest(object id, string method, JsonElement? @params = null)
        {
            Id = id;
            Method = method;
            Params = @params;
        }
    }
}
