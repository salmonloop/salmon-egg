using System.Text.Json;
using System.Text.Json.Serialization;

namespace SalmonEgg.Domain.Models.JsonRpc
{
    /// <summary>
    /// JSON-RPC 2.0 错误对象。
    /// 当发生错误或异常时，error 对象被包含在响应中。
    /// </summary>
    public class JsonRpcError
    {
        /// <summary>
        /// 错误类型标识。根据 JSON-RPC 2.0 规范，这是一个数字。
        /// 预定义的错误码范围是 -32768 到 -32000。
        /// </summary>
        [JsonPropertyName("code")]
        public int Code { get; set; }

        /// <summary>
        /// 简短的描述性消息，适合于显示给用户。
        /// </summary>
        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// 包含错误详细信息的对象。
        /// 可以是任何类型，用于提供关于错误的额外信息。
        /// </summary>
        [JsonPropertyName("data")]
        public object? Data { get; set; }

        /// <summary>
        /// 创建一个新的 JsonRpcError 实例。
        /// </summary>
        public JsonRpcError()
        {
        }

        /// <summary>
        /// 创建一个新的 JsonRpcError 实例。
        /// </summary>
        /// <param name="code">错误码</param>
        /// <param name="message">错误消息</param>
        /// <param name="data">可选的附加数据</param>
        public JsonRpcError(int code, string message, object? data = null)
        {
            Code = code;
            Message = message;
            Data = data;
        }

        /// <summary>
        /// 创建一个解析错误的错误对象。
        /// </summary>
        /// <param name="message">错误消息</param>
        /// <returns>JsonRpcError 实例</returns>
        public static JsonRpcError CreateParseError(string message = "Invalid JSON")
        {
            return new JsonRpcError(JsonRpcErrorCode.ParseError, message);
        }

        /// <summary>
        /// 创建一个无效请求的错误对象。
        /// </summary>
        /// <param name="message">错误消息</param>
        /// <returns>JsonRpcError 实例</returns>
        public static JsonRpcError CreateInvalidRequest(string message = "Invalid Request")
        {
            return new JsonRpcError(JsonRpcErrorCode.InvalidRequest, message);
        }

        /// <summary>
        /// 创建一个方法未找到的错误对象。
        /// </summary>
        /// <param name="methodName">方法名</param>
        /// <returns>JsonRpcError 实例</returns>
        public static JsonRpcError CreateMethodNotFound(string methodName)
        {
            return new JsonRpcError(JsonRpcErrorCode.MethodNotFound, $"Method '{methodName}' not found");
        }

        /// <summary>
        /// 创建一个参数无效的错误对象。
        /// </summary>
        /// <param name="message">错误消息</param>
        /// <returns>JsonRpcError 实例</returns>
        public static JsonRpcError CreateInvalidParams(string message = "Invalid params")
        {
            return new JsonRpcError(JsonRpcErrorCode.InvalidParams, message);
        }

        /// <summary>
        /// 创建一个内部错误的错误对象。
        /// </summary>
        /// <param name="message">错误消息</param>
        /// <returns>JsonRpcError 实例</returns>
        public static JsonRpcError CreateInternalError(string message = "Internal error")
        {
            return new JsonRpcError(JsonRpcErrorCode.InternalError, message);
        }

        /// <summary>
        /// 判断错误码是否为标准错误码。
        /// </summary>
        /// <returns>如果是标准错误码返回 true</returns>
        public bool IsStandardError()
        {
            return JsonRpcErrorCode.IsStandardErrorCode(Code);
        }

        /// <summary>
        /// 判断错误码是否为 ACP 扩展错误码。
        /// </summary>
        /// <returns>如果是 ACP 扩展错误码返回 true</returns>
        public bool IsAcpError()
        {
            return JsonRpcErrorCode.IsAcpErrorCode(Code);
        }
    }
}
