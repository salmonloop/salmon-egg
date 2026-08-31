using System.Text.Json;
using System.Text.Json.Serialization;

namespace SalmonEgg.Acp.JsonRpc
{
    /// <summary>
    /// A JSON-RPC 2.0 request message.
    /// Used to send a request from the client to the server.
    /// </summary>
    internal sealed class JsonRpcRequest : JsonRpcMessage
    {
        /// <summary>
        /// The unique identifier of the request.
        /// May be a string, a number, or null, but never a boolean.
        /// The server must echo the same id back in its response.
        /// </summary>
        [JsonPropertyName("id")]
        public object Id { get; set; } = string.Empty;

        /// <summary>
        /// The name of the method to invoke.
        /// </summary>
        [JsonPropertyName("method")]
        public string Method { get; set; } = string.Empty;

        /// <summary>
        /// The parameters of the method.
        /// May be an object, an array, a primitive value, or omitted entirely.
        /// </summary>
        [JsonPropertyName("params")]
        public JsonElement? Params { get; set; }

        /// <summary>
        /// Creates a new JsonRpcRequest instance.
        /// </summary>
        public JsonRpcRequest()
        {
        }

        /// <summary>
        /// Creates a new JsonRpcRequest instance.
        /// </summary>
        /// <param name="id">The request id.</param>
        /// <param name="method">The method name.</param>
        /// <param name="params">The method parameters.</param>
        public JsonRpcRequest(object id, string method, JsonElement? @params = null)
        {
            Id = id;
            Method = method;
            Params = @params;
        }
    }
}
