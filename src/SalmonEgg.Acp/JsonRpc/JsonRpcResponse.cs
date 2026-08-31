using System.Text.Json;
using System.Text.Json.Serialization;

namespace SalmonEgg.Acp.JsonRpc
{
    /// <summary>
    /// JSON-RPC 2.0 response message.
    /// Sent by the server in reply to a request. A response carries exactly one of result or error.
    /// </summary>
    internal sealed class JsonRpcResponse : JsonRpcMessage
    {
        /// <summary>
        /// The unique identifier of the request being answered.
        /// Must match the id value carried by the request message.
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
        /// The result of the method invocation.
        /// Present on success, in which case error is null.
        /// May be any JSON value.
        /// </summary>
        [JsonPropertyName("result")]
        public JsonElement? Result { get; set; }

        /// <summary>
        /// The error information.
        /// Present on failure, in which case result is null.
        /// Null when the response is successful.
        /// </summary>
        [JsonPropertyName("error")]
        public JsonRpcError? Error { get; set; }

        /// <summary>
        /// Creates a new successful response instance.
        /// </summary>
        public JsonRpcResponse()
        {
        }

        /// <summary>
        /// Creates a new successful response instance.
        /// </summary>
        /// <param name="id">The ID of the corresponding request</param>
        /// <param name="result">The response result</param>
        public JsonRpcResponse(object? id, JsonElement? result)
        {
            Id = id;
            Result = result;
            Error = null;
        }

        /// <summary>
        /// Creates a new error response instance.
        /// </summary>
        /// <param name="id">The ID of the corresponding request</param>
        /// <param name="error">The error information</param>
        public JsonRpcResponse(object? id, JsonRpcError error)
        {
            Id = id;
            Result = null;
            Error = error;
        }

        /// <summary>
        /// Gets a value indicating whether the response is successful.
        /// </summary>
        /// <remarks>
        /// Local convenience only. A JSON-RPC 2.0 Response carries exactly jsonrpc/id/result/error,
        /// so this must never reach the wire.
        /// </remarks>
        [JsonIgnore]
        public bool IsSuccess => Error == null && Result.HasValue;

        /// <summary>
        /// Gets a value indicating whether the response is an error.
        /// </summary>
        /// <remarks>
        /// Local convenience only; see <see cref="IsSuccess"/>. Not part of the JSON-RPC envelope.
        /// </remarks>
        [JsonIgnore]
        public bool IsError => Error != null;
    }
}
