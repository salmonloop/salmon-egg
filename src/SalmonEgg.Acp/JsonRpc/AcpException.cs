using System;
using System.Text.Json;

namespace SalmonEgg.Acp.JsonRpc
{
    /// <summary>
    /// ACP protocol exception.
    /// Represents an error at the JSON-RPC 2.0 protocol level.
    /// </summary>
    public sealed class AcpException : Exception
    {
        /// <summary>
        /// The JSON-RPC 2.0 error code.
        /// </summary>
        public int ErrorCode { get; }

        /// <summary>
        /// Optional additional error data.
        /// </summary>
        public object? ErrorData { get; }

        /// <summary>
        /// Creates a new <see cref="AcpException"/> instance.
        /// </summary>
        /// <param name="errorCode">The JSON-RPC 2.0 error code.</param>
        /// <param name="message">The exception message.</param>
        /// <param name="errorData">Optional additional data.</param>
        public AcpException(int errorCode, string message, object? errorData = null)
            : base(FormatMessage(message, errorData))
        {
            ErrorCode = errorCode;
            ErrorData = errorData;
        }

        /// <summary>
        /// Creates a new <see cref="AcpException"/> instance.
        /// </summary>
        /// <param name="errorCode">The JSON-RPC 2.0 error code.</param>
        /// <param name="message">The exception message.</param>
        /// <param name="innerException">The inner exception.</param>
        /// <param name="errorData">Optional additional data.</param>
        public AcpException(int errorCode, string message, Exception innerException, object? errorData = null)
            : base(FormatMessage(message, errorData), innerException)
        {
            ErrorCode = errorCode;
            ErrorData = errorData;
        }

        /// <summary>
        /// Creates an exception representing a parse error.
        /// </summary>
        /// <param name="innerException">The parse exception.</param>
        /// <returns>An <see cref="AcpException"/> instance.</returns>
        public static AcpException CreateParseError(Exception innerException)
        {
            return new AcpException(
                JsonRpcErrorCode.ParseError,
                "Invalid JSON: " + innerException.Message,
                innerException);
        }

        /// <summary>
        /// Creates an exception representing an invalid request.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="data">Optional additional data.</param>
        /// <returns>An <see cref="AcpException"/> instance.</returns>
        public static AcpException CreateInvalidRequest(string message, object? errorData = null)
        {
            return new AcpException(
                JsonRpcErrorCode.InvalidRequest,
                message,
                errorData);
        }

        /// <summary>
        /// Creates an exception representing a method that was not found.
        /// </summary>
        /// <param name="methodName">The method name.</param>
        /// <returns>An <see cref="AcpException"/> instance.</returns>
        public static AcpException CreateMethodNotFound(string methodName)
        {
            return new AcpException(
                JsonRpcErrorCode.MethodNotFound,
                $"Method '{methodName}' not found");
        }

        /// <summary>
        /// Creates an exception representing invalid parameters.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="data">Optional additional data.</param>
        /// <returns>An <see cref="AcpException"/> instance.</returns>
        public static AcpException CreateInvalidParams(string message, object? errorData = null)
        {
            return new AcpException(
                JsonRpcErrorCode.InvalidParams,
                message,
                errorData);
        }

        /// <summary>
        /// Creates an exception representing an internal error.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="innerException">The inner exception.</param>
        /// <param name="data">Optional additional data.</param>
        /// <returns>An <see cref="AcpException"/> instance.</returns>
        public static AcpException CreateInternalError(string message, Exception? innerException = null, object? errorData = null)
        {
            if (innerException != null)
            {
                return new AcpException(
                    JsonRpcErrorCode.InternalError,
                    message,
                    innerException,
                    errorData);
            }

            return new AcpException(
                JsonRpcErrorCode.InternalError,
                message,
                errorData);
        }

        /// <summary>
        /// Creates an exception representing an uninitialized state (client-side local state).
        /// </summary>
        /// <returns>An <see cref="AcpException"/> instance.</returns>
        public static AcpException CreateNotInitialized()
        {
            return new AcpException(
                JsonRpcErrorCode.InvalidRequest,
                "ACP client is not initialized. Call InitializeAsync first.");
        }

        /// <summary>
        /// Creates an exception representing a session that was not found.
        /// </summary>
        /// <param name="sessionId">The session ID.</param>
        /// <returns>An <see cref="AcpException"/> instance.</returns>
        public static AcpException CreateSessionNotFound(string sessionId)
        {
            return new AcpException(
                JsonRpcErrorCode.SessionNotFound,
                $"Session '{sessionId}' not found");
        }

        /// <summary>
        /// Creates an exception representing a denied permission.
        /// </summary>
        /// <param name="operation">The operation name.</param>
        /// <param name="path">An optional path.</param>
        /// <returns>An <see cref="AcpException"/> instance.</returns>
        public static AcpException CreatePermissionDenied(string operation, string? path = null)
        {
            var message = path != null
                ? $"Permission denied for operation '{operation}' on path '{path}'"
                : $"Permission denied for operation '{operation}'";

            return new AcpException(
                JsonRpcErrorCode.PermissionDenied,
                message);
        }

        /// <summary>
        /// Creates an exception representing a protocol version mismatch.
        /// </summary>
        /// <param name="expected">The expected version.</param>
        /// <param name="actual">The actual version.</param>
        /// <returns>An <see cref="AcpException"/> instance.</returns>
        public static AcpException CreateProtocolVersionMismatch(string expected, string actual)
        {
            return new AcpException(
                JsonRpcErrorCode.ProtocolVersionMismatch,
                $"Protocol version mismatch. Expected: {expected}, Actual: {actual}");
        }

        private static string FormatMessage(string message, object? errorData)
        {
            var detail = ExtractErrorDetail(errorData);
            if (string.IsNullOrWhiteSpace(detail) ||
                string.Equals(message, detail, StringComparison.Ordinal))
            {
                return message;
            }

            return message + ": " + detail;
        }

        private static string? ExtractErrorDetail(object? errorData)
        {
            if (errorData is null)
            {
                return null;
            }

            if (errorData is string text)
            {
                return string.IsNullOrWhiteSpace(text) ? null : text;
            }

            if (errorData is JsonElement element)
            {
                return ExtractJsonElementDetail(element);
            }

            return errorData.ToString();
        }

        private static string? ExtractJsonElementDetail(JsonElement element)
        {
            if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                return null;
            }

            if (element.ValueKind == JsonValueKind.String)
            {
                return element.GetString();
            }

            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var propertyName in new[] { "details", "detail", "message", "error" })
                {
                    if (element.TryGetProperty(propertyName, out var property) &&
                        property.ValueKind == JsonValueKind.String)
                    {
                        return property.GetString();
                    }
                }
            }

            return element.GetRawText();
        }
    }
}
