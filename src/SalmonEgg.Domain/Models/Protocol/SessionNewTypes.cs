using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

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
        /// MCP 服务器配置列表（根据对端Agent要求，似乎是必填的数组）。
        /// </summary>
        [JsonPropertyName("mcpServers")]
        public object McpServers { get; set; } = new object[0];



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
        public SessionNewParams(string cwd, object? mcpServers = null)
        {
            Cwd = cwd;
            McpServers = mcpServers ?? new object[0];
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
        /// 会话模式状态（可选，兼容旧 Agent 可能直接返回数组）。
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
        public string? CurrentModeId { get; set; }

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

    internal sealed class SessionModesStateJsonConverter : JsonConverter<SessionModesState?>
    {
        public override SessionModesState? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return null;
            }

            if (reader.TokenType == JsonTokenType.StartArray)
            {
                var modes = JsonSerializer.Deserialize<List<SessionMode>>(ref reader, options) ?? new List<SessionMode>();
                return new SessionModesState { AvailableModes = modes };
            }

            if (reader.TokenType == JsonTokenType.StartObject)
            {
                return JsonSerializer.Deserialize<SessionModesState>(ref reader, options);
            }

            using var doc = JsonDocument.ParseValue(ref reader);
            return doc.RootElement.ValueKind switch
            {
                JsonValueKind.Array => new SessionModesState { AvailableModes = JsonSerializer.Deserialize<List<SessionMode>>(doc.RootElement.GetRawText(), options) ?? new List<SessionMode>() },
                JsonValueKind.Object => JsonSerializer.Deserialize<SessionModesState>(doc.RootElement.GetRawText(), options),
                _ => null
            };
        }

        public override void Write(Utf8JsonWriter writer, SessionModesState? value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value, options);
        }
    }
}
