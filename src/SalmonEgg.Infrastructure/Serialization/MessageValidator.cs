using System;
using System.Text.Json;
using SalmonEgg.Domain.Interfaces;
using SalmonEgg.Domain.Models.JsonRpc;

namespace SalmonEgg.Infrastructure.Serialization
{
    /// <summary>
    /// JSON-RPC 2.0 消息验证器实现。
    /// 验证消息格式和必需字段的完整性。
    /// </summary>
    public class MessageValidator : IMessageValidator
    {
        /// <summary>
        /// 验证请求消息的格式和必需字段。
        /// 请求消息必须包含：jsonrpc, method, id
        /// </summary>
        public ValidationResult ValidateRequest(JsonRpcRequest request)
        {
            if (request == null)
            {
                return ValidationResult.Failure(
                    JsonRpcErrorCode.InvalidRequest,
                    "Request message cannot be null");
            }

            var errors = new System.Collections.Generic.List<string>();

            // 验证 jsonrpc 字段
            if (string.IsNullOrWhiteSpace(request.JsonRpc) || request.JsonRpc != "2.0")
            {
                errors.Add("Invalid or missing 'jsonrpc' field. Must be '2.0'");
            }

            // 验证 method 字段
            if (string.IsNullOrWhiteSpace(request.Method))
            {
                errors.Add("Missing or empty 'method' field");
            }

            // 验证 id 字段
            if (request.Id == null || request.Id.Equals(false))
            {
                // id 可以是 null，但不能是布尔值 false
                if (request.Id is bool)
                {
                    errors.Add("Invalid 'id' field. Cannot be a boolean value");
                }
                else if (request.Id == null)
                {
                    errors.Add("Missing 'id' field. Request messages must have an 'id'");
                }
            }

            if (errors.Count > 0)
            {
                return ValidationResult.Failure(
                    JsonRpcErrorCode.InvalidRequest,
                    errors);
            }

            return ValidationResult.Success();
        }

        /// <summary>
        /// 验证通知消息的格式和必需字段。
        /// 通知消息必须包含：jsonrpc, method
        /// 通知消息不能包含：id
        /// </summary>
        public ValidationResult ValidateNotification(JsonRpcNotification notification)
        {
            if (notification == null)
            {
                return ValidationResult.Failure(
                    JsonRpcErrorCode.InvalidRequest,
                    "Notification message cannot be null");
            }

            var errors = new System.Collections.Generic.List<string>();

            // 验证 jsonrpc 字段
            if (string.IsNullOrWhiteSpace(notification.JsonRpc) || notification.JsonRpc != "2.0")
            {
                errors.Add("Invalid or missing 'jsonrpc' field. Must be '2.0'");
            }

            // 验证 method 字段
            if (string.IsNullOrWhiteSpace(notification.Method))
            {
                errors.Add("Missing or empty 'method' field");
            }

            // 验证没有 id 字段（通知不应有 id）
            // 注意：在 C# 对象中我们无法直接检查 JSON 中是否存在某个字段，
            // 这里假设如果 Id 被设置为默认值，则没有设置 id
            // 实际验证需要在 JSON 层面进行

            if (errors.Count > 0)
            {
                return ValidationResult.Failure(
                    JsonRpcErrorCode.InvalidRequest,
                    errors);
            }

            return ValidationResult.Success();
        }

        /// <summary>
        /// 验证响应消息的格式和必需字段。
        /// 响应消息必须包含：jsonrpc, id
        /// 响应消息必须恰好包含 result 或 error 之一
        /// </summary>
        public ValidationResult ValidateResponse(JsonRpcResponse response)
        {
            if (response == null)
            {
                return ValidationResult.Failure(
                    JsonRpcErrorCode.InvalidRequest,
                    "Response message cannot be null");
            }

            var errors = new System.Collections.Generic.List<string>();

            // 验证 jsonrpc 字段
            if (string.IsNullOrWhiteSpace(response.JsonRpc) || response.JsonRpc != "2.0")
            {
                errors.Add("Invalid or missing 'jsonrpc' field. Must be '2.0'");
            }

            // 验证 id 字段
            if (response.Id == null || response.Id.Equals(false))
            {
                if (response.Id is bool)
                {
                    errors.Add("Invalid 'id' field. Cannot be a boolean value");
                }
                else if (response.Id == null)
                {
                    errors.Add("Missing 'id' field. Response messages must have an 'id'");
                }
            }

            // 验证恰好有 result 或 error 之一
            var hasResult = response.Result.HasValue;
            var hasError = response.Error != null;

            if (hasResult && hasError)
            {
                errors.Add("Response must have either 'result' or 'error', not both");
            }
            else if (!hasResult && !hasError)
            {
                errors.Add("Response must have either 'result' or 'error'");
            }

            // 如果有 error，验证 error 对象的格式
            if (hasError && response.Error != null)
            {
                var error = response.Error;

                // 验证 error.code 是数字
                // 在 C# 中 Code 是 int 属性，所以总是数字

                // 验证 error.message 是非空字符串
                if (string.IsNullOrWhiteSpace(error.Message))
                {
                    errors.Add("Error 'message' field cannot be empty");
                }

                // 验证错误码是有效的 JSON-RPC 错误码范围
                if (error.Code > -32768 || error.Code < -32000)
                {
                    // 允许 ACP 扩展错误码（-32099 到 -32000）
                    if (error.Code < -32099 && !JsonRpcErrorCode.IsStandardErrorCode(error.Code))
                    {
                        errors.Add($"Invalid error code: {error.Code}. Must be in range -32768 to -32000, or -32099 to -32000 for ACP extensions");
                    }
                }
            }

            if (errors.Count > 0)
            {
                return ValidationResult.Failure(
                    JsonRpcErrorCode.InvalidRequest,
                    errors);
            }

            return ValidationResult.Success();
        }
    }
}
