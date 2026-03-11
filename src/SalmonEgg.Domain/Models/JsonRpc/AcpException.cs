using System;

namespace SalmonEgg.Domain.Models.JsonRpc
{
    /// <summary>
    /// ACP 协议异常类。
    /// 用于表示 JSON-RPC 2.0 协议级别的错误。
    /// </summary>
    public class AcpException : Exception
    {
        /// <summary>
        /// JSON-RPC 2.0 错误码。
        /// </summary>
        public int ErrorCode { get; }

        /// <summary>
        /// 可选的附加错误数据。
        /// </summary>
        public object? ErrorData { get; }

        /// <summary>
        /// 创建新的 AcpException 实例。
        /// </summary>
        /// <param name="errorCode">JSON-RPC 2.0 错误码</param>
        /// <param name="message">异常消息</param>
        /// <param name="errorData">可选的附加数据</param>
        public AcpException(int errorCode, string message, object? errorData = null)
            : base(message)
        {
            ErrorCode = errorCode;
            ErrorData = errorData;
        }

        /// <summary>
        /// 创建新的 AcpException 实例。
        /// </summary>
        /// <param name="errorCode">JSON-RPC 2.0 错误码</param>
        /// <param name="message">异常消息</param>
        /// <param name="innerException">内部异常</param>
        /// <param name="errorData">可选的附加数据</param>
        public AcpException(int errorCode, string message, Exception innerException, object? errorData = null)
            : base(message, innerException)
        {
            ErrorCode = errorCode;
            ErrorData = errorData;
        }

        /// <summary>
        /// 创建一个解析错误的异常。
        /// </summary>
        /// <param name="innerException">解析异常</param>
        /// <returns>AcpException 实例</returns>
        public static AcpException CreateParseError(Exception innerException)
        {
            return new AcpException(
                JsonRpcErrorCode.ParseError,
                "Invalid JSON: " + innerException.Message,
                innerException);
        }

        /// <summary>
        /// 创建一个无效请求的异常。
        /// </summary>
        /// <param name="message">错误消息</param>
        /// <param name="data">可选的附加数据</param>
        /// <returns>AcpException 实例</returns>
        public static AcpException CreateInvalidRequest(string message, object? errorData = null)
        {
            return new AcpException(
                JsonRpcErrorCode.InvalidRequest,
                message,
                errorData);
        }

        /// <summary>
        /// 创建一个方法未找到的异常。
        /// </summary>
        /// <param name="methodName">方法名</param>
        /// <returns>AcpException 实例</returns>
        public static AcpException CreateMethodNotFound(string methodName)
        {
            return new AcpException(
                JsonRpcErrorCode.MethodNotFound,
                $"Method '{methodName}' not found");
        }

        /// <summary>
        /// 创建一个参数无效的异常。
        /// </summary>
        /// <param name="message">错误消息</param>
        /// <param name="data">可选的附加数据</param>
        /// <returns>AcpException 实例</returns>
        public static AcpException CreateInvalidParams(string message, object? errorData = null)
        {
            return new AcpException(
                JsonRpcErrorCode.InvalidParams,
                message,
                errorData);
        }

        /// <summary>
        /// 创建一个内部错误的异常。
        /// </summary>
        /// <param name="message">错误消息</param>
        /// <param name="innerException">内部异常</param>
        /// <param name="data">可选的附加数据</param>
        /// <returns>AcpException 实例</returns>
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
        /// 创建一个未初始化的异常（客户端本地状态）。
        /// </summary>
        /// <returns>AcpException 实例</returns>
        public static AcpException CreateNotInitialized()
        {
            return new AcpException(
                JsonRpcErrorCode.InvalidRequest,
                "ACP client is not initialized. Call InitializeAsync first.");
        }

        /// <summary>
        /// 创建一个会话未找到的异常。
        /// </summary>
        /// <param name="sessionId">会话 ID</param>
        /// <returns>AcpException 实例</returns>
        public static AcpException CreateSessionNotFound(string sessionId)
        {
            return new AcpException(
                JsonRpcErrorCode.SessionNotFound,
                $"Session '{sessionId}' not found");
        }

        /// <summary>
        /// 创建一个权限被拒绝的异常。
        /// </summary>
        /// <param name="operation">操作名称</param>
        /// <param name="path">可选的路径</param>
        /// <returns>AcpException 实例</returns>
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
        /// 创建一个协议版本不匹配的异常。
        /// </summary>
        /// <param name="expected">期望的版本</param>
        /// <param name="actual">实际的版本</param>
        /// <returns>AcpException 实例</returns>
        public static AcpException CreateProtocolVersionMismatch(string expected, string actual)
        {
            return new AcpException(
                JsonRpcErrorCode.ProtocolVersionMismatch,
                $"Protocol version mismatch. Expected: {expected}, Actual: {actual}");
        }
    }
}
