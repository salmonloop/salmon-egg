using System.Collections.Generic;
using SalmonEgg.Domain.Models.JsonRpc;

namespace SalmonEgg.Domain.Interfaces
{
    /// <summary>
    /// JSON-RPC 2.0 消息验证器接口。
    /// 提供消息格式和字段的验证功能。
    /// </summary>
    public interface IMessageValidator
    {
        /// <summary>
        /// 验证请求消息的格式和必需字段。
        /// </summary>
        /// <param name="request">要验证的请求消息</param>
        /// <returns>验证结果</returns>
        ValidationResult ValidateRequest(JsonRpcRequest request);

        /// <summary>
        /// 验证通知消息的格式和必需字段。
        /// </summary>
        /// <param name="notification">要验证的通知消息</param>
        /// <returns>验证结果</returns>
        ValidationResult ValidateNotification(JsonRpcNotification notification);

        /// <summary>
        /// 验证响应消息的格式和必需字段。
        /// </summary>
        /// <param name="response">要验证的响应消息</param>
        /// <returns>验证结果</returns>
        ValidationResult ValidateResponse(JsonRpcResponse response);
    }

    /// <summary>
    /// 验证结果类。
    /// 包含验证是否通过以及相关的错误信息。
    /// </summary>
    public class ValidationResult
    {
        /// <summary>
        /// 验证是否通过。
        /// </summary>
        public bool IsValid { get; set; }

        /// <summary>
        /// 错误消息列表（当 IsValid 为 false 时包含错误）。
        /// </summary>
        public List<string> Errors { get; set; } = new List<string>();

        /// <summary>
        /// 错误码（如果验证失败）。
        /// </summary>
        public int? ErrorCode { get; set; }

        /// <summary>
        /// 创建成功的验证结果。
        /// </summary>
        public static ValidationResult Success()
        {
            return new ValidationResult { IsValid = true };
        }

        /// <summary>
        /// 创建失败的验证结果。
        /// </summary>
        /// <param name="errorCode">错误码</param>
        /// <param name="errors">错误消息列表</param>
        public static ValidationResult Failure(int errorCode, List<string> errors)
        {
            return new ValidationResult
            {
                IsValid = false,
                ErrorCode = errorCode,
                Errors = errors
            };
        }

        /// <summary>
        /// 创建失败的验证结果（单个错误）。
        /// </summary>
        /// <param name="errorCode">错误码</param>
        /// <param name="error">错误消息</param>
        public static ValidationResult Failure(int errorCode, string error)
        {
            return new ValidationResult
            {
                IsValid = false,
                ErrorCode = errorCode,
                Errors = new List<string> { error }
            };
        }
    }
}
