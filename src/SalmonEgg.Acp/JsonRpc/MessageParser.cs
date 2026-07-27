using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using SalmonEgg.Acp.Serialization;

namespace SalmonEgg.Acp.JsonRpc
{
    /// <summary>
    /// JSON-RPC 2.0 消息解析器实现。
    /// 使用 System.Text.Json 进行消息的解析和序列化。
    /// </summary>
    internal sealed class MessageParser
    {
        private readonly JsonSerializerOptions _options;

        /// <summary>
        /// 获取 JsonSerializerOptions 实例供外部使用。
        /// </summary>
        public JsonSerializerOptions Options => _options;

        /// <summary>
        /// 创建新的 MessageParser 实例。
        /// </summary>
        public MessageParser()
        {
            _options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true,
                IncludeFields = false,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = false,
                // ACP agents can be strict about optional fields; omit nulls rather than writing `"foo": null`.
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                // ACP session/update payloads may place protocol extension fields like `_meta`
                // before the polymorphic discriminator (`sessionUpdate`).
                AllowOutOfOrderMetadataProperties = true,
                // Public protocol contracts + internal JSON-RPC envelopes.
                TypeInfoResolver = JsonTypeInfoResolver.Combine(
                    AcpJsonContext.Default,
                    AcpJsonRpcContext.Default)
            };
        }

        /// <summary>
        /// 解析 JSON 字符串为 JSON-RPC 消息。
        /// </summary>
        public JsonRpcMessage ParseMessage(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new AcpException(
                    JsonRpcErrorCode.ParseError,
                    "Empty or null JSON message");
            }

            try
            {
                // 首先尝试作为基础对象解析以检测类型
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // 检测消息类型
                var hasId = root.TryGetProperty("id", out _);
                var hasResult = root.TryGetProperty("result", out _);
                var hasError = root.TryGetProperty("error", out _);

                if (hasResult || hasError)
                {
                    // 响应消息
                    return JsonSerializer.Deserialize(json, GetTypeInfo<JsonRpcResponse>())
                        ?? throw new AcpException(JsonRpcErrorCode.ParseError, "Failed to parse response");
                }
                else if (hasId)
                {
                    // 请求消息
                    return JsonSerializer.Deserialize(json, GetTypeInfo<JsonRpcRequest>())
                        ?? throw new AcpException(JsonRpcErrorCode.ParseError, "Failed to parse request");
                }
                else
                {
                    // 通知消息（无 id）
                    return JsonSerializer.Deserialize(json, GetTypeInfo<JsonRpcNotification>())
                        ?? throw new AcpException(JsonRpcErrorCode.ParseError, "Failed to parse notification");
                }
            }
            catch (JsonException ex)
            {
                throw new AcpException(
                    JsonRpcErrorCode.ParseError,
                    $"Invalid JSON: {ex.Message}",
                    ex);
            }
            catch (AcpException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new AcpException(
                    JsonRpcErrorCode.ParseError,
                    $"Error parsing message: {ex.Message}",
                    ex);
            }
        }

        /// <summary>
        /// 解析 JSON 字符串为请求消息。
        /// </summary>
        public JsonRpcRequest ParseRequest(string json)
        {
            var message = ParseMessage(json);

            if (message is not JsonRpcRequest request)
            {
                throw new AcpException(
                    JsonRpcErrorCode.InvalidRequest,
                    "Message is not a request (missing 'id' field or wrong type)");
            }

            return request;
        }

        /// <summary>
        /// 解析 JSON 字符串为通知消息。
        /// </summary>
        public JsonRpcNotification ParseNotification(string json)
        {
            var message = ParseMessage(json);

            if (message is not JsonRpcNotification notification)
            {
                throw new AcpException(
                    JsonRpcErrorCode.InvalidRequest,
                    "Message is not a notification (should not have 'id' field)");
            }

            return notification;
        }

        /// <summary>
        /// 解析 JSON 字符串为响应消息。
        /// </summary>
        public JsonRpcResponse ParseResponse(string json)
        {
            var message = ParseMessage(json);

            if (message is not JsonRpcResponse response)
            {
                throw new AcpException(
                    JsonRpcErrorCode.InvalidRequest,
                    "Message is not a response (missing 'result' or 'error' field)");
            }

            return response;
        }

        /// <summary>
        /// 将 JSON-RPC 消息序列化为 JSON 字符串。
        /// </summary>
        public string SerializeMessage(JsonRpcMessage message)
        {
            if (message == null)
            {
                throw new AcpException(
                    JsonRpcErrorCode.InvalidRequest,
                    "Cannot serialize null message");
            }

            try
            {
                return message switch
                {
                    JsonRpcRequest request => JsonSerializer.Serialize(request, GetTypeInfo<JsonRpcRequest>()),
                    JsonRpcNotification notification => JsonSerializer.Serialize(notification, GetTypeInfo<JsonRpcNotification>()),
                    JsonRpcResponse response => JsonSerializer.Serialize(response, GetTypeInfo<JsonRpcResponse>()),
                    _ => throw new JsonException($"Unknown JsonRpcMessage type: {message.GetType().Name}")
                };
            }
            catch (JsonException ex)
            {
                throw new AcpException(
                    JsonRpcErrorCode.InternalError,
                    $"Failed to serialize message: {ex.Message}",
                    ex);
            }
        }

        private JsonTypeInfo<T> GetTypeInfo<T>()
        {
            return (JsonTypeInfo<T>)_options.GetTypeInfo(typeof(T));
        }
    }
}
