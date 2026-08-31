using System.Text.Json;
using System.Text.Json.Serialization;

namespace SalmonEgg.Acp.JsonRpc
{
    /// <summary>
    /// A JSON-RPC 2.0 notification message.
    /// A notification is a special kind of request that has no response, and the receiver returns nothing.
    /// Notification messages do not carry an id field.
    /// </summary>
    internal sealed class JsonRpcNotification : JsonRpcMessage
    {
        /// <summary>
        /// The name of the method to invoke.
        /// </summary>
        [JsonPropertyName("method")]
        public string Method { get; set; } = string.Empty;

        /// <summary>
        /// The parameters for the method.
        /// May be an object, an array, a primitive value, or omitted.
        /// </summary>
        [JsonPropertyName("params")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public JsonElement? Params { get; set; }

        /// <summary>
        /// Creates a new JsonRpcNotification instance.
        /// </summary>
        public JsonRpcNotification()
        {
        }

        /// <summary>
        /// Creates a new JsonRpcNotification instance.
        /// </summary>
        /// <param name="method">The method name.</param>
        /// <param name="params">The method parameters.</param>
        public JsonRpcNotification(string method, JsonElement? @params = null)
        {
            Method = method;
            Params = @params;
        }
    }
}
