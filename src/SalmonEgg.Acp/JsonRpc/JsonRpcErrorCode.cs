namespace SalmonEgg.Acp.JsonRpc
{
    /// <summary>
    /// Standard JSON-RPC 2.0 error code constants.
    /// The error codes defined by the JSON-RPC 2.0 specification.
    /// </summary>
    public static class JsonRpcErrorCode
    {
        #region Standard JSON-RPC 2.0 error codes

        /// <summary>
        /// -32700: Parse error
        /// Invalid JSON, or a JSON parsing failure.
        /// </summary>
        public const int ParseError = -32700;

        /// <summary>
        /// -32600: Invalid Request
        /// The request message is malformed (a required field is missing, a field has the wrong type, and so on).
        /// </summary>
        public const int InvalidRequest = -32600;

        /// <summary>
        /// -32601: Method not found
        /// The requested method does not exist.
        /// </summary>
        public const int MethodNotFound = -32601;

        /// <summary>
        /// -32602: Invalid params
        /// The method parameters are invalid (a required parameter is missing, a parameter has the wrong type,
        /// and so on).
        /// </summary>
        public const int InvalidParams = -32602;

        /// <summary>
        /// -32603: Internal error
        /// An internal server error.
        /// </summary>
        public const int InternalError = -32603;

        #endregion

        #region ACP extension error codes

        /// <summary>
        /// -32000: Authentication required
        /// ACP: authentication is required first (for example, calling authenticate or completing an external
        /// sign-in flow).
        /// </summary>
        public const int AuthenticationRequired = -32000;

        /// <summary>
        /// -32001: Permission denied
        /// ACP: permission was denied.
        /// </summary>
        public const int PermissionDenied = -32001;

        /// <summary>
        /// -32002: Resource not found
        /// ACP: the resource was not found (a session, a file, and so on).
        /// </summary>
        public const int ResourceNotFound = -32002;

        /// <summary>
        /// Compatibility alias: session not found (classified as Resource not found in the ACP schema).
        /// </summary>
        public const int SessionNotFound = ResourceNotFound;

        /// <summary>
        /// -32003: Method not allowed
        /// The method is not allowed (for example, calling it on a session that has not been initialized).
        /// </summary>
        public const int MethodNotAllowed = -32003;

        /// <summary>
        /// -32004: Protocol version mismatch
        /// The protocol version does not match.
        /// </summary>
        public const int ProtocolVersionMismatch = -32004;

        /// <summary>
        /// -32005: Capability not supported
        /// The capability is not supported.
        /// </summary>
        public const int CapabilityNotSupported = -32005;

        #endregion

        /// <summary>
        /// Determines whether an error code is a standard JSON-RPC 2.0 error code.
        /// </summary>
        /// <param name="code">The error code.</param>
        /// <returns>true if it is a standard error code.</returns>
        public static bool IsStandardErrorCode(int code)
        {
            return code >= -32700 && code <= -32603;
        }

        /// <summary>
        /// Determines whether an error code is an ACP extension error code.
        /// </summary>
        /// <param name="code">The error code.</param>
        /// <returns>true if it is an ACP extension error code.</returns>
        public static bool IsAcpErrorCode(int code)
        {
            return code >= -32099 && code <= -32000;
        }

        /// <summary>
        /// Gets the standard error message for an error code.
        /// </summary>
        /// <param name="code">The error code.</param>
        /// <returns>The error message.</returns>
        public static string GetErrorMessage(int code)
        {
            return code switch
            {
                ParseError => "Parse error",
                InvalidRequest => "Invalid Request",
                MethodNotFound => "Method not found",
                InvalidParams => "Invalid params",
                InternalError => "Internal error",
                PermissionDenied => "Permission denied",
                ResourceNotFound => "Resource not found",
                MethodNotAllowed => "Method not allowed",
                ProtocolVersionMismatch => "Protocol version mismatch",
                CapabilityNotSupported => "Capability not supported",
                _ => $"Unknown error (code: {code})"
            };
        }
    }
}
