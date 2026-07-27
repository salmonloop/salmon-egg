using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using SalmonEgg.Acp.Mcp;

namespace SalmonEgg.Acp.Protocol
{
    /// <summary>
    /// Session/Set_Mode 方法的请求参数。
    /// 用于切换会话的工作模式。
    /// </summary>
    public class SessionSetModeParams : AcpProtocolObject
    {
        /// <summary>
        /// 会话 ID（必填）。
        /// </summary>
        [JsonPropertyName("sessionId")]
        public string SessionId { get; set; } = string.Empty;

        /// <summary>
        /// 要切换到的目标模式 ID（必填）。
        /// </summary>
        [JsonPropertyName("modeId")]
        public string ModeId { get; set; } = string.Empty;

        /// <summary>
        /// 创建新的 SessionSetModeParams 实例。
        /// </summary>
        public SessionSetModeParams()
        {
        }

        /// <summary>
        /// 创建新的 SessionSetModeParams 实例。
        /// </summary>
        /// <param name="sessionId">会话 ID</param>
        /// <param name="modeId">目标模式 ID</param>
        public SessionSetModeParams(string sessionId, string modeId)
        {
            SessionId = sessionId;
            ModeId = modeId;
        }
    }

    /// <summary>
    /// Session/Set_Mode 方法的响应。
    /// </summary>
    public class SessionSetModeResponse : AcpProtocolObject
    {
        /// <summary>
        /// 协议扩展字段（_meta）。
        /// </summary>
        /// <summary>
        /// 创建新的 SessionSetModeResponse 实例。
        /// </summary>
        public SessionSetModeResponse()
        {
        }
    }

    /// <summary>
    /// ACP <c>session/cancel</c> notification parameters.
    /// </summary>
    public class SessionCancelParams : AcpProtocolObject
    {
        [JsonPropertyName("sessionId")]
        public string SessionId { get; set; } = string.Empty;

        public SessionCancelParams()
        {
        }

        public SessionCancelParams(string sessionId)
        {
            SessionId = sessionId;
        }
    }

    /// <summary>
    /// Session/Load 方法的请求参数。
    /// 用于加载已存在的会话历史。
    /// </summary>
    public class SessionLoadParams : AcpProtocolObject
    {
        /// <summary>
        /// 会话 ID（必填）。
        /// </summary>
        [JsonPropertyName("sessionId")]
        public string SessionId { get; set; } = string.Empty;

        /// <summary>
        /// 会话的工作目录（必填）。
        /// </summary>
        [JsonPropertyName("cwd")]
        public string Cwd { get; set; } = string.Empty;

        /// <summary>
        /// MCP 服务器配置列表。
        /// ACP session/load 要求该字段始终为数组，即使当前没有任何 MCP server 也必须发送 []。
        /// </summary>
        [JsonPropertyName("mcpServers")]
        public List<McpServer> McpServers { get; set; } = new List<McpServer>();

        /// <summary>
        /// 附加工作目录。非空时要求 Agent 声明 sessionCapabilities.additionalDirectories。
        /// </summary>
        [JsonPropertyName("additionalDirectories")]
        public List<string>? AdditionalDirectories { get; set; }

        /// <summary>
        /// 创建新的 SessionLoadParams 实例。
        /// </summary>
        public SessionLoadParams()
        {
        }

        /// <summary>
        /// 创建新的 SessionLoadParams 实例。
        /// </summary>
        /// <param name="sessionId">会话 ID</param>
        /// <param name="cwd">工作目录</param>
        /// <param name="mcpServers">MCP 服务器配置</param>
        /// <param name="additionalDirectories">附加工作目录</param>
        public SessionLoadParams(
            string sessionId,
            string cwd,
            List<McpServer>? mcpServers = null,
            List<string>? additionalDirectories = null)
        {
            SessionId = sessionId;
            Cwd = cwd;
            McpServers = mcpServers ?? new List<McpServer>();
            AdditionalDirectories = additionalDirectories;
        }
    }

    /// <summary>
    /// Session/Load 方法的响应。
    /// 可能返回 null / 空对象，或返回模式与配置选项快照。
    /// </summary>
    public class SessionLoadResponse : AcpProtocolObject
    {
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
        /// 创建新的 SessionLoadResponse 实例。
        /// </summary>
        public SessionLoadResponse()
        {
        }

        /// <summary>
        /// 创建新的 SessionLoadResponse 实例。
        /// </summary>
        /// <param name="modes">模式状态</param>
        /// <param name="configOptions">配置选项列表</param>
        public SessionLoadResponse(SessionModesState? modes, List<ConfigOption>? configOptions = null)
        {
            Modes = modes;
            ConfigOptions = configOptions;
        }

        /// <summary>
        /// 表示加载完成的静态实例。
        /// </summary>
        public static readonly SessionLoadResponse Completed = new SessionLoadResponse();
    }

    /// <summary>
    /// ACP session/resume replay cursor.
    /// Official V2 known form is <c>{ "type": "start" }</c> for full history replay.
    /// Other <c>type</c> values remain open for custom/future cursors.
    /// </summary>
    public class SessionReplayFrom : AcpProtocolObject
    {
        /// <summary>
        /// Replay cursor type. Official full-history replay uses <c>start</c>.
        /// </summary>
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// Creates an empty replay cursor.
        /// </summary>
        public SessionReplayFrom()
        {
        }

        /// <summary>
        /// Creates a replay cursor with the given type.
        /// </summary>
        /// <param name="type">Replay cursor type.</param>
        public SessionReplayFrom(string type)
        {
            Type = type;
        }

        /// <summary>
        /// Official V2 full-history replay cursor: <c>{ "type": "start" }</c>.
        /// </summary>
        public static SessionReplayFrom Start { get; } = new("start");
    }

    /// <summary>
    /// Session/Resume 方法的请求参数。
    /// 用于恢复已存在的会话上下文；省略 <see cref="ReplayFrom"/> 时不要求 Agent 重放历史，
    /// 设置 <c>replayFrom: { type: "start" }</c> 时请求完整历史重放（V2 对 session/load 的替代路径）。
    /// </summary>
    public class SessionResumeParams : AcpProtocolObject
    {
        /// <summary>
        /// 会话 ID（必填）。
        /// </summary>
        [JsonPropertyName("sessionId")]
        public string SessionId { get; set; } = string.Empty;

        /// <summary>
        /// 会话的工作目录（必填）。
        /// </summary>
        [JsonPropertyName("cwd")]
        public string Cwd { get; set; } = string.Empty;

        /// <summary>
        /// MCP 服务器配置列表。
        /// ACP session/resume 要求该字段始终为数组，即使当前没有任何 MCP server 也必须发送 []。
        /// </summary>
        [JsonPropertyName("mcpServers")]
        public List<McpServer> McpServers { get; set; } = new List<McpServer>();

        /// <summary>
        /// 附加工作目录。非空时要求 Agent 声明 sessionCapabilities.additionalDirectories。
        /// </summary>
        [JsonPropertyName("additionalDirectories")]
        public List<string>? AdditionalDirectories { get; set; }

        /// <summary>
        /// Optional V2 history replay cursor.
        /// Omit/null resumes without replaying history; <see cref="SessionReplayFrom.Start"/> requests full history.
        /// </summary>
        [JsonPropertyName("replayFrom")]
        public SessionReplayFrom? ReplayFrom { get; set; }

        /// <summary>
        /// 创建新的 SessionResumeParams 实例。
        /// </summary>
        public SessionResumeParams()
        {
        }

        /// <summary>
        /// 创建新的 SessionResumeParams 实例。
        /// </summary>
        /// <param name="sessionId">会话 ID</param>
        /// <param name="cwd">工作目录</param>
        /// <param name="mcpServers">MCP 服务器配置</param>
        /// <param name="additionalDirectories">附加工作目录</param>
        /// <param name="replayFrom">Optional V2 history replay cursor</param>
        public SessionResumeParams(
            string sessionId,
            string cwd,
            List<McpServer>? mcpServers = null,
            List<string>? additionalDirectories = null,
            SessionReplayFrom? replayFrom = null)
        {
            SessionId = sessionId;
            Cwd = cwd;
            McpServers = mcpServers ?? new List<McpServer>();
            AdditionalDirectories = additionalDirectories;
            ReplayFrom = replayFrom;
        }
    }

    /// <summary>
    /// Session/Resume 方法的响应。
    /// 可能返回 null / 空对象，或返回模式与配置选项快照。
    /// </summary>
    public class SessionResumeResponse : AcpProtocolObject
    {
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
        /// 创建新的 SessionResumeResponse 实例。
        /// </summary>
        public SessionResumeResponse()
        {
        }

        /// <summary>
        /// 创建新的 SessionResumeResponse 实例。
        /// </summary>
        /// <param name="modes">模式状态</param>
        /// <param name="configOptions">配置选项列表</param>
        public SessionResumeResponse(SessionModesState? modes, List<ConfigOption>? configOptions = null)
        {
            Modes = modes;
            ConfigOptions = configOptions;
        }

        /// <summary>
        /// 表示恢复完成的静态实例。
        /// </summary>
        public static readonly SessionResumeResponse Completed = new SessionResumeResponse();
    }

    /// <summary>
    /// Session/Close 方法的请求参数。
    /// 用于关闭已存在的会话并释放 Agent 侧资源。
    /// </summary>
    public class SessionCloseParams : AcpProtocolObject
    {
        /// <summary>
        /// 会话 ID（必填）。
        /// </summary>
        [JsonPropertyName("sessionId")]
        public string SessionId { get; set; } = string.Empty;

        /// <summary>
        /// 创建新的 SessionCloseParams 实例。
        /// </summary>
        public SessionCloseParams()
        {
        }

        /// <summary>
        /// 创建新的 SessionCloseParams 实例。
        /// </summary>
        /// <param name="sessionId">会话 ID</param>
        public SessionCloseParams(string sessionId)
        {
            SessionId = sessionId;
        }
    }

    /// <summary>
    /// Session/Close 方法的响应。
    /// </summary>
    public class SessionCloseResponse : AcpProtocolObject
    {
        /// <summary>
        /// 创建新的 SessionCloseResponse 实例。
        /// </summary>
        public SessionCloseResponse()
        {
        }

        /// <summary>
        /// 表示关闭完成的静态实例。
        /// </summary>
        public static readonly SessionCloseResponse Completed = new SessionCloseResponse();
    }

    /// <summary>
    /// Session/Delete 方法的请求参数。
    /// 用于删除 session/list 中的已有会话。
    /// </summary>
    public class SessionDeleteParams : AcpProtocolObject
    {
        /// <summary>
        /// 会话 ID（必填）。
        /// </summary>
        [JsonPropertyName("sessionId")]
        public string SessionId { get; set; } = string.Empty;

        public SessionDeleteParams()
        {
        }

        public SessionDeleteParams(string sessionId)
        {
            SessionId = sessionId;
        }
    }

    /// <summary>
    /// Session/Delete 方法的响应。
    /// </summary>
    public class SessionDeleteResponse : AcpProtocolObject
    {
        public static readonly SessionDeleteResponse Completed = new SessionDeleteResponse();
    }

    /// <summary>
    /// Session/Set_Config_Option 方法的请求参数。
    /// 用于设置会话的配置选项。
    /// </summary>
    [JsonConverter(typeof(SessionSetConfigOptionParamsJsonConverter))]
    public class SessionSetConfigOptionParams : AcpProtocolObject
    {
        /// <summary>
        /// 会话 ID（必填）。
        /// </summary>
        [JsonPropertyName("sessionId")]
        public string SessionId { get; set; } = string.Empty;

        /// <summary>
        /// 配置选项 ID（必填）。
        /// </summary>
        [JsonPropertyName("configId")]
        public string ConfigId { get; set; } = string.Empty;

        /// <summary>
        /// 配置选项的值（必填）。
        /// </summary>
        [JsonIgnore]
        public string? Value { get; set; }

        [JsonIgnore]
        public bool? BooleanValue { get; set; }

        /// <summary>
        /// 创建新的 SessionSetConfigOptionParams 实例。
        /// </summary>
        public SessionSetConfigOptionParams()
        {
        }

        /// <summary>
        /// 创建新的 SessionSetConfigOptionParams 实例。
        /// </summary>
        /// <param name="sessionId">会话 ID</param>
        /// <param name="configId">配置选项 ID</param>
        /// <param name="value">配置选项的值</param>
        public SessionSetConfigOptionParams(string sessionId, string configId, string value)
        {
            SessionId = sessionId;
            ConfigId = configId;
            Value = value;
        }

        public SessionSetConfigOptionParams(string sessionId, string configId, bool value)
        {
            SessionId = sessionId;
            ConfigId = configId;
            BooleanValue = value;
        }
    }

    internal sealed class SessionSetConfigOptionParamsJsonConverter : JsonConverter<SessionSetConfigOptionParams>
    {
        public override SessionSetConfigOptionParams? Read(
            ref Utf8JsonReader reader,
            System.Type typeToConvert,
            JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return null;
            }

            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            var result = new SessionSetConfigOptionParams
            {
                SessionId = ReadRequiredString(root, "sessionId"),
                ConfigId = ReadRequiredString(root, "configId"),
                Meta = AcpMetaJson.Read(root)
            };
            if (!root.TryGetProperty("value", out var value))
            {
                throw new JsonException("ACP session/set_config_option requires value.");
            }

            if (root.TryGetProperty("type", out var type)
                && type.ValueKind == JsonValueKind.String
                && string.Equals(type.GetString(), "boolean", System.StringComparison.Ordinal))
            {
                if (value.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
                {
                    throw new JsonException("ACP boolean session config value must be a boolean.");
                }

                result.BooleanValue = value.GetBoolean();
            }
            else if (value.ValueKind == JsonValueKind.String)
            {
                result.Value = value.GetString() ?? string.Empty;
            }
            else
            {
                throw new JsonException("ACP session config value must be a string value ID or declared boolean.");
            }

            return result;
        }

        public override void Write(
            Utf8JsonWriter writer,
            SessionSetConfigOptionParams value,
            JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("sessionId", value.SessionId);
            writer.WriteString("configId", value.ConfigId);
            if (value.BooleanValue.HasValue)
            {
                if (value.Value != null)
                {
                    throw new JsonException("ACP session config request cannot contain both string and boolean values.");
                }

                writer.WriteBoolean("value", value.BooleanValue.Value);
                writer.WriteString("type", "boolean");
            }
            else if (value.Value != null)
            {
                writer.WriteString("value", value.Value);
            }
            else
            {
                throw new JsonException("ACP session/set_config_option requires a value.");
            }

            AcpMetaJson.Write(writer, value.Meta);
            writer.WriteEndObject();
        }

        private static string ReadRequiredString(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out var property)
                || property.ValueKind != JsonValueKind.String)
            {
                throw new JsonException($"ACP session/set_config_option requires string property '{propertyName}'.");
            }

            return property.GetString() ?? string.Empty;
        }
    }

    /// <summary>
    /// Session/Set_Config_Option 方法的响应。
    /// </summary>
    public class SessionSetConfigOptionResponse : AcpProtocolObject
    {
        /// <summary>
        /// 更新后的配置选项列表（完整状态）。
        /// </summary>
        [JsonPropertyName("configOptions")]
        public List<ConfigOption>? ConfigOptions { get; set; }

        /// <summary>
        /// 创建新的 SessionSetConfigOptionResponse 实例。
        /// </summary>
        public SessionSetConfigOptionResponse()
        {
        }

        /// <summary>
        /// 创建新的 SessionSetConfigOptionResponse 实例。
        /// </summary>
        /// <param name="configOptions">配置选项列表</param>
        public SessionSetConfigOptionResponse(List<ConfigOption>? configOptions = null)
        {
            ConfigOptions = configOptions;
        }
    }
}
