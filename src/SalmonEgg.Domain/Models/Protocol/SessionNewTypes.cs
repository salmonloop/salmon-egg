using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using SalmonEgg.Domain.Models.Mcp;

namespace SalmonEgg.Domain.Models.Protocol
{
    /// <summary>
    /// Session/New 方法的请求参数。
    /// 用于创建新的会话。
    /// </summary>
    public class SessionNewParams
    {
        /// <summary>
        /// 会话的工作目录（必填）。
        /// </summary>
        [JsonPropertyName("cwd")]
        public string Cwd { get; set; } = string.Empty;

        /// <summary>
        /// MCP 服务器配置列表（必填，根据协议要求为数组）。
        /// </summary>
        [JsonPropertyName("mcpServers")]
        public List<McpServer> McpServers { get; set; } = new List<McpServer>();

        /// <summary>
        /// 创建新的 SessionNewParams 实例。
        /// </summary>
        public SessionNewParams()
        {
        }

        /// <summary>
        /// 创建新的 SessionNewParams 实例。
        /// </summary>
        /// <param name="cwd">工作目录</param>
        /// <param name="mcpServers">MCP 服务器配置</param>
        public SessionNewParams(string cwd, List<McpServer>? mcpServers = null)
        {
            Cwd = cwd;
            McpServers = mcpServers ?? new List<McpServer>();
        }
    }

    /// <summary>
    /// Session/New 方法的响应。
    /// Agent 对创建会话请求的响应。
    /// </summary>
    public class SessionNewResponse
    {
        /// <summary>
        /// 新创建的会话 ID。
        /// </summary>
        [JsonPropertyName("sessionId")]
        public string SessionId { get; set; } = string.Empty;

        /// <summary>
        /// 会话模式状态（可选，ACP 标准形态为 SessionModeState 对象）。
        /// </summary>
        [JsonPropertyName("modes")]
        [JsonConverter(typeof(SessionModesStateJsonConverter))]
        public SessionModesState? Modes { get; set; }


        /// <summary>
        /// 可用的配置选项列表（可选）。
        /// </summary>
        [JsonPropertyName("configOptions")]
        public List<ConfigOption>? ConfigOptions { get; set; }


        /// <summary>
        /// 创建新的 SessionNewResponse 实例。
        /// </summary>
        public SessionNewResponse()
        {
        }

        /// <summary>
        /// 创建新的 SessionNewResponse 实例。
        /// </summary>
        /// <param name="sessionId">会话 ID</param>
        /// <param name="modes">可用模式列表</param>
        /// <param name="configOptions">配置选项</param>
        public SessionNewResponse(string sessionId, SessionModesState? modes = null, List<ConfigOption>? configOptions = null)
        {
            SessionId = sessionId;
            Modes = modes;
            ConfigOptions = configOptions;
        }
    }

    /// <summary>
    /// 会话模式状态（用于 Session/New 响应）。
    /// https://agentclientprotocol.com/protocol/session-modes
    /// </summary>
    public class SessionModesState
    {
        /// <summary>
        /// 当前模式 ID。
        /// </summary>
        [JsonPropertyName("currentModeId")]
        public string CurrentModeId { get; set; } = string.Empty;

        /// <summary>
        /// 可用模式列表。
        /// </summary>
        [JsonPropertyName("availableModes")]
        public List<SessionMode> AvailableModes { get; set; } = new();
    }

    public class SessionMode
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string? Description { get; set; }
    }

    public sealed class SessionModesStateJsonConverter : JsonConverter<SessionModesState?>
    {
        public override SessionModesState? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return null;
            }

            if (reader.TokenType == JsonTokenType.StartObject)
            {
                return ReadModesObject(ref reader);
            }

            throw new JsonException("Session modes state must be a JSON object or null.");
        }

        public override void Write(Utf8JsonWriter writer, SessionModesState? value, JsonSerializerOptions options)
        {
            if (value == null)
            {
                writer.WriteNullValue();
                return;
            }

            writer.WriteStartObject();
            if (value.CurrentModeId != null)
            {
                writer.WriteString("currentModeId", value.CurrentModeId);
            }
            else if (ShouldWriteNull(options))
            {
                writer.WriteNull("currentModeId");
            }

            writer.WritePropertyName("availableModes");
            writer.WriteStartArray();
            foreach (var mode in value.AvailableModes)
            {
                WriteMode(writer, mode, options);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        private static SessionModesState ReadModesObject(ref Utf8JsonReader reader)
        {
            var state = new SessionModesState();
            var hasCurrentModeId = false;
            var hasAvailableModes = false;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    if (!hasCurrentModeId)
                    {
                        throw new JsonException("Session modes state is missing required currentModeId.");
                    }

                    if (!hasAvailableModes)
                    {
                        throw new JsonException("Session modes state is missing required availableModes.");
                    }

                    return state;
                }

                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    throw new JsonException("Session modes state must contain JSON properties.");
                }

                var propertyName = reader.GetString();
                if (!reader.Read())
                {
                    throw new JsonException("Unexpected end of session modes state.");
                }

                switch (propertyName)
                {
                    case "currentModeId":
                        if (reader.TokenType != JsonTokenType.String)
                        {
                            throw new JsonException("Session modes currentModeId must be a string.");
                        }

                        state.CurrentModeId = reader.GetString() ?? string.Empty;
                        hasCurrentModeId = true;
                        break;
                    case "availableModes":
                        if (reader.TokenType != JsonTokenType.StartArray)
                        {
                            throw new JsonException("Session modes availableModes must be an array.");
                        }

                        state.AvailableModes = ReadModesArray(ref reader);
                        hasAvailableModes = true;
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }

            throw new JsonException("Unexpected end of session modes state.");
        }

        private static List<SessionMode> ReadModesArray(ref Utf8JsonReader reader)
        {
            var modes = new List<SessionMode>();

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray)
                {
                    return modes;
                }

                if (reader.TokenType != JsonTokenType.StartObject)
                {
                    throw new JsonException("Session mode entry must be a JSON object.");
                }

                modes.Add(ReadModeObject(ref reader));
            }

            throw new JsonException("Unexpected end of session modes array.");
        }

        private static SessionMode ReadModeObject(ref Utf8JsonReader reader)
        {
            var mode = new SessionMode();
            var hasId = false;
            var hasName = false;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    if (!hasId)
                    {
                        throw new JsonException("Session mode is missing required id.");
                    }

                    if (!hasName)
                    {
                        throw new JsonException("Session mode is missing required name.");
                    }

                    return mode;
                }

                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    throw new JsonException("Session mode must contain JSON properties.");
                }

                var propertyName = reader.GetString();
                if (!reader.Read())
                {
                    throw new JsonException("Unexpected end of session mode.");
                }

                switch (propertyName)
                {
                    case "id":
                        if (reader.TokenType != JsonTokenType.String)
                        {
                            throw new JsonException("Session mode id must be a string.");
                        }

                        mode.Id = reader.GetString() ?? string.Empty;
                        hasId = true;
                        break;
                    case "name":
                        if (reader.TokenType != JsonTokenType.String)
                        {
                            throw new JsonException("Session mode name must be a string.");
                        }

                        mode.Name = reader.GetString() ?? string.Empty;
                        hasName = true;
                        break;
                    case "description":
                        if (reader.TokenType != JsonTokenType.Null && reader.TokenType != JsonTokenType.String)
                        {
                            throw new JsonException("Session mode description must be a string or null.");
                        }

                        mode.Description = reader.TokenType == JsonTokenType.Null ? null : reader.GetString();
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }

            throw new JsonException("Unexpected end of session mode.");
        }

        private static void WriteMode(Utf8JsonWriter writer, SessionMode mode, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("id", mode.Id);
            writer.WriteString("name", mode.Name);

            if (mode.Description != null)
            {
                writer.WriteString("description", mode.Description);
            }
            else if (ShouldWriteNull(options))
            {
                writer.WriteNull("description");
            }

            writer.WriteEndObject();
        }

        private static bool ShouldWriteNull(JsonSerializerOptions options)
        {
            return options.DefaultIgnoreCondition != JsonIgnoreCondition.WhenWritingNull
                && options.DefaultIgnoreCondition != JsonIgnoreCondition.WhenWritingDefault;
        }
    }
}
