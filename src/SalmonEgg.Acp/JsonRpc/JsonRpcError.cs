using System.Text.Json;
using System.Text.Json.Serialization;

namespace SalmonEgg.Acp.JsonRpc
{
    /// <summary>
    /// JSON-RPC 2.0 error object.
    /// The error object is included in a response when an error or exception occurs.
    /// </summary>
    internal sealed class JsonRpcError
    {
        /// <summary>
        /// Identifies the error type. Per the JSON-RPC 2.0 specification, this is a number.
        /// The range reserved for predefined error codes is -32768 to -32000.
        /// </summary>
        [JsonPropertyName("code")]
        public int Code { get; set; }

        /// <summary>
        /// A short, descriptive message suitable for display to the user.
        /// </summary>
        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// An object carrying detailed information about the error.
        /// May be of any type, and is used to provide additional information about the error.
        /// </summary>
        [JsonPropertyName("data")]
        public object? Data { get; set; }

        /// <summary>
        /// Creates a new <see cref="JsonRpcError"/> instance.
        /// </summary>
        public JsonRpcError()
        {
        }

        /// <summary>
        /// Creates a new <see cref="JsonRpcError"/> instance.
        /// </summary>
        /// <param name="code">The error code.</param>
        /// <param name="message">The error message.</param>
        /// <param name="data">Optional additional data.</param>
        public JsonRpcError(int code, string message, object? data = null)
        {
            Code = code;
            Message = message;
            Data = data;
        }

        /// <summary>
        /// Creates an error object representing a parse error.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <returns>A <see cref="JsonRpcError"/> instance.</returns>
        public static JsonRpcError CreateParseError(string message = "Invalid JSON")
        {
            return new JsonRpcError(JsonRpcErrorCode.ParseError, message);
        }

        /// <summary>
        /// Creates an error object representing an invalid request.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <returns>A <see cref="JsonRpcError"/> instance.</returns>
        public static JsonRpcError CreateInvalidRequest(string message = "Invalid Request")
        {
            return new JsonRpcError(JsonRpcErrorCode.InvalidRequest, message);
        }

        /// <summary>
        /// Creates an error object representing a method that was not found.
        /// </summary>
        /// <param name="methodName">The method name.</param>
        /// <returns>A <see cref="JsonRpcError"/> instance.</returns>
        public static JsonRpcError CreateMethodNotFound(string methodName)
        {
            return new JsonRpcError(JsonRpcErrorCode.MethodNotFound, $"Method '{methodName}' not found");
        }

        /// <summary>
        /// Creates an error object representing invalid parameters.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <returns>A <see cref="JsonRpcError"/> instance.</returns>
        public static JsonRpcError CreateInvalidParams(string message = "Invalid params")
        {
            return new JsonRpcError(JsonRpcErrorCode.InvalidParams, message);
        }

        /// <summary>
        /// Creates an error object representing an internal error.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <returns>A <see cref="JsonRpcError"/> instance.</returns>
        public static JsonRpcError CreateInternalError(string message = "Internal error")
        {
            return new JsonRpcError(JsonRpcErrorCode.InternalError, message);
        }

        /// <summary>
        /// Determines whether the error code is a standard error code.
        /// </summary>
        /// <returns><c>true</c> if the error code is a standard error code.</returns>
        public bool IsStandardError()
        {
            return JsonRpcErrorCode.IsStandardErrorCode(Code);
        }

        /// <summary>
        /// Determines whether the error code is an ACP extension error code.
        /// </summary>
        /// <returns><c>true</c> if the error code is an ACP extension error code.</returns>
        public bool IsAcpError()
        {
            return JsonRpcErrorCode.IsAcpErrorCode(Code);
        }
    }
}
