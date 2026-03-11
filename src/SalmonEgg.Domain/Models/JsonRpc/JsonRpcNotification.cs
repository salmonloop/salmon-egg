using System.Text.Json;
using System.Text.Json.Serialization;

namespace SalmonEgg.Domain.Models.JsonRpc
{
    /// <summary>
    /// JSON-RPC 2.0 通知消息。
    /// 通知是一种特殊类型的请求，它没有响应，并且接收方不返回任何东西。
    /// 通知消息不包含 id 字段。
    /// </summary>
    public class JsonRpcNotification : JsonRpcMessage
    {
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
       [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
       public JsonElement? Params { get; set; }

        /// <summary>
       /// 创建一个新的 JsonRpcNotification 实例。
       /// </summary>
       public JsonRpcNotification()
       {
       }

       /// <summary>
       /// 创建一个新的 JsonRpcNotification 实例。
       /// </summary>
       /// <param name="method">方法名</param>
       /// <param name="params">方法参数</param>
       public JsonRpcNotification(string method, JsonElement? @params = null)
       {
           Method = method;
           Params = @params;
       }
    }
}
