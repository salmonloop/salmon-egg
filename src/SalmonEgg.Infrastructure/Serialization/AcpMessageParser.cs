using System;
using System.Text.Json;
using SalmonEgg.Domain.Exceptions;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Infrastructure.Serialization
{
    /// <summary>
    /// 实现 ACP 协议消息的解析和序列化
    /// </summary>
    public class AcpMessageParser : IAcpProtocolService
    {
        private readonly JsonSerializerOptions _options;
        
        /// <summary>
        /// 有效的消息类型
        /// </summary>
        private static readonly string[] ValidMessageTypes = { "request", "response", "notification", "initialize" };

        public AcpMessageParser()
        {
            _options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };
        }

        /// <summary>
        /// 解析 JSON 字符串为 ACP 消息对象
        /// </summary>
        public AcpMessage ParseMessage(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new AcpProtocolException(
                    "Message JSON cannot be null or empty",
                    AcpErrorCode.InvalidMessage);
            }

            try
            {
                var message = JsonSerializer.Deserialize<AcpMessage>(json, _options);
                
                if (message == null)
                {
                    throw new AcpProtocolException(
                        "Failed to deserialize message: result is null",
                        AcpErrorCode.InvalidMessage);
                }

                // 验证必需字段
                ValidateRequiredFields(message);
                
                // 验证消息类型
                ValidateMessageType(message);
                
                // 验证消息类型特定的字段
                ValidateMessageTypeSpecificFields(message);

                return message;
            }
            catch (JsonException ex)
            {
                throw new AcpProtocolException(
                    $"Invalid JSON format: {ex.Message}",
                    AcpErrorCode.SerializationError,
                    ex);
            }
        }

        /// <summary>
        /// 将 ACP 消息对象序列化为 JSON 字符串
        /// </summary>
        public string SerializeMessage(AcpMessage message)
        {
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message), "Message cannot be null");
            }

            try
            {
                return JsonSerializer.Serialize(message, _options);
            }
            catch (JsonException ex)
            {
                throw new AcpProtocolException(
                    $"Failed to serialize message: {ex.Message}",
                    AcpErrorCode.SerializationError,
                    ex);
            }
        }

        /// <summary>
        /// 验证 ACP 消息的有效性
        /// </summary>
        public bool ValidateMessage(AcpMessage message)
        {
            if (message == null)
            {
                return false;
            }

            try
            {
                ValidateRequiredFields(message);
                ValidateMessageType(message);
                ValidateMessageTypeSpecificFields(message);
                return true;
            }
            catch (AcpProtocolException)
            {
                return false;
            }
        }

        /// <summary>
        /// 协商客户端和服务器之间的协议版本
        /// </summary>
        public string NegotiateVersion(string clientVersion, string serverVersion)
        {
            if (string.IsNullOrWhiteSpace(clientVersion))
            {
                throw new ArgumentException("Client version cannot be null or empty", nameof(clientVersion));
            }

            if (string.IsNullOrWhiteSpace(serverVersion))
            {
                throw new ArgumentException("Server version cannot be null or empty", nameof(serverVersion));
            }

            // 简单的版本比较逻辑：返回两者中较低的版本
            // 假设版本格式为 "major.minor"
            if (!TryParseVersion(clientVersion, out var clientMajor, out var clientMinor))
            {
                throw new AcpProtocolException(
                    $"Invalid client version format: {clientVersion}",
                    AcpErrorCode.UnsupportedVersion);
            }

            if (!TryParseVersion(serverVersion, out var serverMajor, out var serverMinor))
            {
                throw new AcpProtocolException(
                    $"Invalid server version format: {serverVersion}",
                    AcpErrorCode.UnsupportedVersion);
            }

            // 如果主版本号不同，不兼容
            if (clientMajor != serverMajor)
            {
                throw new AcpProtocolException(
                    $"Incompatible protocol versions: client {clientVersion}, server {serverVersion}",
                    AcpErrorCode.UnsupportedVersion);
            }

            // 返回较低的次版本号
            var negotiatedMinor = Math.Min(clientMinor, serverMinor);
            return $"{clientMajor}.{negotiatedMinor}";
        }

        /// <summary>
        /// 验证必需字段
        /// </summary>
        private void ValidateRequiredFields(AcpMessage message)
        {
            if (string.IsNullOrWhiteSpace(message.Id))
            {
                throw new AcpProtocolException(
                    "Message ID is required",
                    AcpErrorCode.MissingRequiredField);
            }

            if (string.IsNullOrWhiteSpace(message.Type))
            {
                throw new AcpProtocolException(
                    "Message type is required",
                    AcpErrorCode.MissingRequiredField,
                    message.Id);
            }
        }

        /// <summary>
        /// 验证消息类型
        /// </summary>
        private void ValidateMessageType(AcpMessage message)
        {
            bool isValidType = false;
            foreach (var validType in ValidMessageTypes)
            {
                if (string.Equals(message.Type, validType, StringComparison.OrdinalIgnoreCase))
                {
                    isValidType = true;
                    break;
                }
            }

            if (!isValidType)
            {
                throw new AcpProtocolException(
                    $"Invalid message type: {message.Type}. Valid types are: request, response, notification, initialize",
                    AcpErrorCode.InvalidMessageType,
                    message.Id);
            }
        }

        /// <summary>
        /// 验证消息类型特定的字段
        /// </summary>
        private void ValidateMessageTypeSpecificFields(AcpMessage message)
        {
            var messageType = message.Type.ToLowerInvariant();

            // request 和 notification 类型必须包含 Method
            if (messageType == "request" || messageType == "notification")
            {
                if (string.IsNullOrWhiteSpace(message.Method))
                {
                    throw new AcpProtocolException(
                        $"Method is required for {message.Type} messages",
                        AcpErrorCode.MissingRequiredField,
                        message.Id);
                }
            }

            // response 类型必须包含 Result 或 Error（但不能同时包含）
            if (messageType == "response")
            {
                bool hasResult = message.Result.HasValue && message.Result.Value.ValueKind != JsonValueKind.Null;
                bool hasError = message.Error != null;

                if (!hasResult && !hasError)
                {
                    throw new AcpProtocolException(
                        "Response message must contain either Result or Error",
                        AcpErrorCode.MissingRequiredField,
                        message.Id);
                }

                if (hasResult && hasError)
                {
                    throw new AcpProtocolException(
                        "Response message cannot contain both Result and Error",
                        AcpErrorCode.InvalidMessage,
                        message.Id);
                }
            }
        }

        /// <summary>
        /// 尝试解析版本字符串
        /// </summary>
        private bool TryParseVersion(string version, out int major, out int minor)
        {
            major = 0;
            minor = 0;

            if (string.IsNullOrWhiteSpace(version))
            {
                return false;
            }

            var parts = version.Split('.');
            if (parts.Length != 2)
            {
                return false;
            }

            return int.TryParse(parts[0], out major) && int.TryParse(parts[1], out minor);
        }
    }
}
