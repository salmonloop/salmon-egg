namespace SalmonEgg.Domain.Models.JsonRpc
{
    /// <summary>
    /// JSON-RPC 2.0 标准错误码常量。
    /// 根据 JSON-RPC 2.0 规范定义的标准错误码。
    /// </summary>
    public static class JsonRpcErrorCode
    {
        #region JSON-RPC 2.0 标准错误码

        /// <summary>
        /// -32700: Parse error
        /// 无效的 JSON 或 JSON 解析错误。
        /// </summary>
        public const int ParseError = -32700;

        /// <summary>
        /// -32600: Invalid Request
        /// 请求消息格式无效（缺少必需字段、字段类型错误等）。
        /// </summary>
        public const int InvalidRequest = -32600;

        /// <summary>
        /// -32601: Method not found
        /// 请求的方法不存在。
        /// </summary>
        public const int MethodNotFound = -32601;

        /// <summary>
        /// -32602: Invalid params
        /// 方法参数无效（缺少必需参数、参数类型错误等）。
        /// </summary>
        public const int InvalidParams = -32602;

        /// <summary>
        /// -32603: Internal error
        /// 服务器内部错误。
        /// </summary>
        public const int InternalError = -32603;

        #endregion

        #region ACP 扩展错误码

        /// <summary>
        /// -32000: Server error (reserved for implementation-defined server-errors)
        /// ACP 协议级错误（未初始化）。
        /// </summary>
        public const int NotInitialized = -32000;

        /// <summary>
        /// -32001: Session not found
        /// 会话未找到。
        /// </summary>
        public const int SessionNotFound = -32001;

        /// <summary>
        /// -32002: Permission denied
        /// 权限被拒绝。
        /// </summary>
        public const int PermissionDenied = -32002;

        /// <summary>
        /// -32003: Method not allowed
        /// 方法不被允许（例如在未初始化的会话中调用）。
        /// </summary>
        public const int MethodNotAllowed = -32003;

        /// <summary>
        /// -32004: Protocol version mismatch
        /// 协议版本不匹配。
        /// </summary>
        public const int ProtocolVersionMismatch = -32004;

        /// <summary>
        /// -32005: Capability not supported
        /// 不支持的功能。
        /// </summary>
        public const int CapabilityNotSupported = -32005;

        #endregion

        /// <summary>
        /// 判断错误码是否为 JSON-RPC 2.0 标准错误码。
        /// </summary>
        /// <param name="code">错误码</param>
        /// <returns>如果是标准错误码返回 true</returns>
        public static bool IsStandardErrorCode(int code)
        {
            return code >= -32700 && code <= -32603;
        }

        /// <summary>
        /// 判断错误码是否为 ACP 扩展错误码。
        /// </summary>
        /// <param name="code">错误码</param>
        /// <returns>如果是 ACP 扩展错误码返回 true</returns>
        public static bool IsAcpErrorCode(int code)
        {
            return code >= -32099 && code <= -32000;
        }

        /// <summary>
        /// 获取错误码对应的标准错误消息。
        /// </summary>
        /// <param name="code">错误码</param>
        /// <returns>错误消息</returns>
        public static string GetErrorMessage(int code)
        {
            return code switch
            {
                ParseError => "Parse error",
                InvalidRequest => "Invalid Request",
                MethodNotFound => "Method not found",
                InvalidParams => "Invalid params",
                InternalError => "Internal error",
                NotInitialized => "Not initialized",
                SessionNotFound => "Session not found",
                PermissionDenied => "Permission denied",
                MethodNotAllowed => "Method not allowed",
                ProtocolVersionMismatch => "Protocol version mismatch",
                CapabilityNotSupported => "Capability not supported",
                _ => $"Unknown error (code: {code})"
            };
        }
    }
}
