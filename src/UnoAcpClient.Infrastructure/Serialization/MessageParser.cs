using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using UnoAcpClient.Domain.Interfaces;
using System.Linq;
using UnoAcpClient.Domain.Models.JsonRpc;

namespace UnoAcpClient.Infrastructure.Serialization
{
    /// <summary>
    /// JSON-RPC 2.0 消息解析器实现。
    /// 使用 System.Text.Json 进行消息的解析和序列化。
    /// </summary>
    public class MessageParser : IMessageParser
    {
        private readonly JsonSerializerOptions _options;

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
                Converters =
                {
                    // 添加 JsonRpcMessage 的多态转换器
                    new JsonRpcMessageConverter()
                }
            };
        }

        /// <summary>
        /// 创建新的 MessageParser 实例，使用自定义的 JsonSerializerOptions。
        /// </summary>
        /// <param name="options">JSON 序列化选项</param>
        public MessageParser(JsonSerializerOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));

            // 确保添加了必要的转换器
            if (!_options.Converters.OfType<JsonRpcMessageConverter>().Any())
            {
                _options.Converters.Add(new JsonRpcMessageConverter());
            }
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
                    return JsonSerializer.Deserialize<JsonRpcResponse>(json, _options)
                        ?? throw new AcpException(JsonRpcErrorCode.ParseError, "Failed to parse response");
                }
                else if (hasId)
                {
                    // 请求消息
                    return JsonSerializer.Deserialize<JsonRpcRequest>(json, _options)
                        ?? throw new AcpException(JsonRpcErrorCode.ParseError, "Failed to parse request");
                }
                else
                {
                    // 通知消息（无 id）
                    return JsonSerializer.Deserialize<JsonRpcNotification>(json, _options)
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
                return JsonSerializer.Serialize(message, message.GetType(), _options);
            }
            catch (JsonException ex)
            {
                throw new AcpException(
                    JsonRpcErrorCode.InternalError,
                    $"Failed to serialize message: {ex.Message}",
                    ex);
            }
        }
    }

    /// <summary>
    /// JsonRpcMessage 的多态 JSON 转换器。
    /// 根据消息结构自动选择正确的派生类型进行反序列化。
    /// </summary>
    public class JsonRpcMessageConverter : JsonConverter<JsonRpcMessage>
    {
        public override JsonRpcMessage? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;

            // 检测消息类型
            var hasId = root.TryGetProperty("id", out _);
            var hasResult = root.TryGetProperty("result", out _);
            var hasError = root.TryGetProperty("error", out _);

            var json = root.GetRawText();

            if (hasResult || hasError)
            {
                return JsonSerializer.Deserialize<JsonRpcResponse>(json, options);
            }
            else if (hasId)
            {
                return JsonSerializer.Deserialize<JsonRpcRequest>(json, options);
            }
            else
            {
                return JsonSerializer.Deserialize<JsonRpcNotification>(json, options);
            }
        }

        public override void Write(Utf8JsonWriter writer, JsonRpcMessage value, JsonSerializerOptions options)
        {
            switch (value)
            {
                case JsonRpcRequest request:
                    JsonSerializer.Serialize(writer, request, options);
                    break;
                case JsonRpcNotification notification:
                    JsonSerializer.Serialize(writer, notification, options);
                    break;
                case JsonRpcResponse response:
                    JsonSerializer.Serialize(writer, response, options);
                    break;
                default:
                    throw new JsonException($"Unknown JsonRpcMessage type: {value.GetType().Name}");
            }
        }
    }
}
