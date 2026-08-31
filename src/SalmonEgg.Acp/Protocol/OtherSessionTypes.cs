using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using SalmonEgg.Acp.Mcp;

namespace SalmonEgg.Acp.Protocol
{
    /// <summary>
    /// Request parameters for the Session/Set_Mode method.
    /// Switches the working mode of a session.
    /// </summary>
    public sealed record SessionSetModeParams : AcpProtocolObject
    {
        /// <summary>
        /// Session ID (required).
        /// </summary>
        [JsonPropertyName("sessionId")]
        public string SessionId { get; init; } = string.Empty;

        /// <summary>
        /// The target mode ID to switch to (required).
        /// </summary>
        [JsonPropertyName("modeId")]
        public string ModeId { get; init; } = string.Empty;

        /// <summary>
        /// Creates a new SessionSetModeParams instance.
        /// </summary>
        public SessionSetModeParams()
        {
        }

        /// <summary>
        /// Creates a new SessionSetModeParams instance.
        /// </summary>
        /// <param name="sessionId">Session ID</param>
        /// <param name="modeId">Target mode ID</param>
        public SessionSetModeParams(string sessionId, string modeId)
        {
            SessionId = sessionId;
            ModeId = modeId;
        }
    }

    /// <summary>
    /// Response for the Session/Set_Mode method.
    /// </summary>
    public sealed record SessionSetModeResponse : AcpProtocolObject
    {
        /// <summary>
        /// Protocol extension field (_meta).
        /// </summary>
        /// <summary>
        /// Creates a new SessionSetModeResponse instance.
        /// </summary>
        public SessionSetModeResponse()
        {
        }
    }

    /// <summary>
    /// ACP <c>session/cancel</c> notification parameters.
    /// </summary>
    public sealed record SessionCancelParams : AcpProtocolObject
    {
        [JsonPropertyName("sessionId")]
        public string SessionId { get; init; } = string.Empty;

        public SessionCancelParams()
        {
        }

        public SessionCancelParams(string sessionId)
        {
            SessionId = sessionId;
        }
    }

    /// <summary>
    /// Request parameters for the Session/Load method.
    /// Loads the history of an existing session.
    /// </summary>
    public sealed record SessionLoadParams : AcpProtocolObject
    {
        /// <summary>
        /// Session ID (required).
        /// </summary>
        [JsonPropertyName("sessionId")]
        public string SessionId { get; init; } = string.Empty;

        /// <summary>
        /// The working directory of the session (required).
        /// </summary>
        [JsonPropertyName("cwd")]
        public string Cwd { get; init; } = string.Empty;

        /// <summary>
        /// List of MCP server configurations.
        /// ACP session/load requires this field to always be an array; send [] even when there is no MCP server.
        /// </summary>
        [JsonPropertyName("mcpServers")]
        public List<McpServer> McpServers { get; init; } = new List<McpServer>();

        /// <summary>
        /// Additional working directories. When non-empty, requires the Agent to declare
        /// sessionCapabilities.additionalDirectories.
        /// </summary>
        [JsonPropertyName("additionalDirectories")]
        public List<string>? AdditionalDirectories { get; init; }

        /// <summary>
        /// Creates a new SessionLoadParams instance.
        /// </summary>
        public SessionLoadParams()
        {
        }

        /// <summary>
        /// Creates a new SessionLoadParams instance.
        /// </summary>
        /// <param name="sessionId">Session ID</param>
        /// <param name="cwd">Working directory</param>
        /// <param name="mcpServers">MCP server configurations</param>
        /// <param name="additionalDirectories">Additional working directories</param>
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
    /// Response for the Session/Load method.
    /// May be null / an empty object, or carry a snapshot of modes and configuration options.
    /// </summary>
    public sealed record SessionLoadResponse : AcpProtocolObject
    {
        /// <summary>
        /// Session mode state (optional; the standard ACP form is a SessionModeState object).
        /// </summary>
        [JsonPropertyName("modes")]
        [JsonConverter(typeof(SessionModesStateJsonConverter))]
        public SessionModesState? Modes { get; init; }

        /// <summary>
        /// List of available configuration options (optional).
        /// </summary>
        [JsonPropertyName("configOptions")]
        public List<ConfigOption>? ConfigOptions { get; init; }

        /// <summary>
        /// Creates a new SessionLoadResponse instance.
        /// </summary>
        public SessionLoadResponse()
        {
        }

        /// <summary>
        /// Creates a new SessionLoadResponse instance.
        /// </summary>
        /// <param name="modes">Mode state</param>
        /// <param name="configOptions">List of configuration options</param>
        public SessionLoadResponse(SessionModesState? modes, List<ConfigOption>? configOptions = null)
        {
            Modes = modes;
            ConfigOptions = configOptions;
        }

        /// <summary>
        /// A static instance representing load completion.
        /// </summary>
        public static readonly SessionLoadResponse Completed = new SessionLoadResponse();
    }

    /// <summary>
    /// ACP session/resume replay cursor.
    /// Official V2 known form is <c>{ "type": "start" }</c> for full history replay.
    /// Other <c>type</c> values remain open for custom/future cursors.
    /// </summary>
    [JsonConverter(typeof(SessionReplayFromJsonConverter))]
    public sealed record SessionReplayFrom : AcpProtocolObject
    {
        /// <summary>
        /// Replay cursor type. Official full-history replay uses <c>start</c>.
        /// </summary>
        [JsonPropertyName("type")]
        public string Type { get; init; } = string.Empty;

        /// <summary>
        /// Complete raw object for a custom or future cursor variant.
        /// This is the sole wire source when present so unknown fields, order, escapes, and number tokens survive forwarding.
        /// </summary>
        [JsonIgnore]
        public JsonElement RawPayload { get; init; }

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

    internal sealed class SessionReplayFromJsonConverter : JsonConverter<SessionReplayFrom>
    {
        internal const string V2OnlyMessage =
            "ACP session/resume replayFrom is only available in protocolVersion 2.";

        public override SessionReplayFrom? Read(
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
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("ACP session/resume replayFrom must be an object or null.");
            }

            if (!root.TryGetProperty("type", out var typeElement))
            {
                throw new JsonException(
                    "ACP session/resume replayFrom must include required string property 'type'.");
            }

            if (typeElement.ValueKind != JsonValueKind.String)
            {
                throw new JsonException("ACP session/resume replayFrom.type must be a string.");
            }

            var type = typeElement.GetString()!;

            return new SessionReplayFrom
            {
                Type = type,
                RawPayload = string.Equals(type, "start", StringComparison.Ordinal)
                    ? default
                    : root.Clone(),
                Meta = AcpMetaJson.Read(root)
            };
        }

        public override void Write(
            Utf8JsonWriter writer,
            SessionReplayFrom value,
            JsonSerializerOptions options)
        {
            if (AcpProtocolWriteContext.Current != AcpProtocolVersion.V2)
            {
                throw new JsonException(V2OnlyMessage);
            }

            if (value.Type is null)
            {
                throw new JsonException("ACP session/resume replayFrom.type must be a string.");
            }

            // Unknown cursor payloads are opaque protocol facts. Forward the complete object verbatim so
            // duplicate keys, property order, escape spelling, and number token spelling are not normalized.
            if (value.RawPayload.ValueKind == JsonValueKind.Object)
            {
                writer.WriteRawValue(value.RawPayload.GetRawText());
                return;
            }

            writer.WriteStartObject();
            writer.WriteString("type", value.Type);
            AcpMetaJson.Write(writer, value.Meta);
            writer.WriteEndObject();
        }
    }

    /// <summary>
    /// Request parameters for the Session/Resume method.
    /// Resumes the context of an existing session; omitting <see cref="ReplayFrom"/> does not require the Agent
    /// to replay history, while <c>replayFrom: { type: "start" }</c> requests a full history replay (the V2
    /// alternative to session/load).
    /// </summary>
    public sealed record SessionResumeParams : AcpProtocolObject
    {
        /// <summary>
        /// Session ID (required).
        /// </summary>
        [JsonPropertyName("sessionId")]
        public string SessionId { get; init; } = string.Empty;

        /// <summary>
        /// The working directory of the session (required).
        /// </summary>
        [JsonPropertyName("cwd")]
        public string Cwd { get; init; } = string.Empty;

        /// <summary>
        /// List of MCP server configurations.
        /// ACP session/resume requires this field to always be an array; send [] even when there is no MCP server.
        /// </summary>
        [JsonPropertyName("mcpServers")]
        public List<McpServer> McpServers { get; init; } = new List<McpServer>();

        /// <summary>
        /// Additional working directories. When non-empty, requires the Agent to declare
        /// sessionCapabilities.additionalDirectories.
        /// </summary>
        [JsonPropertyName("additionalDirectories")]
        public List<string>? AdditionalDirectories { get; init; }

        /// <summary>
        /// Optional V2 history replay cursor.
        /// Omit/null resumes without replaying history; <see cref="SessionReplayFrom.Start"/> requests full history.
        /// </summary>
        [JsonPropertyName("replayFrom")]
        public SessionReplayFrom? ReplayFrom { get; init; }

        /// <summary>
        /// Creates a new SessionResumeParams instance.
        /// </summary>
        public SessionResumeParams()
        {
        }

        /// <summary>
        /// Creates a new SessionResumeParams instance.
        /// </summary>
        /// <param name="sessionId">Session ID</param>
        /// <param name="cwd">Working directory</param>
        /// <param name="mcpServers">MCP server configurations</param>
        /// <param name="additionalDirectories">Additional working directories</param>
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
    /// Response for the Session/Resume method.
    /// May be null / an empty object, or carry a snapshot of modes and configuration options.
    /// </summary>
    public sealed record SessionResumeResponse : AcpProtocolObject
    {
        /// <summary>
        /// Session mode state (optional; the standard ACP form is a SessionModeState object).
        /// </summary>
        [JsonPropertyName("modes")]
        [JsonConverter(typeof(SessionModesStateJsonConverter))]
        public SessionModesState? Modes { get; init; }

        /// <summary>
        /// List of available configuration options (optional).
        /// </summary>
        [JsonPropertyName("configOptions")]
        public List<ConfigOption>? ConfigOptions { get; init; }

        /// <summary>
        /// Creates a new SessionResumeResponse instance.
        /// </summary>
        public SessionResumeResponse()
        {
        }

        /// <summary>
        /// Creates a new SessionResumeResponse instance.
        /// </summary>
        /// <param name="modes">Mode state</param>
        /// <param name="configOptions">List of configuration options</param>
        public SessionResumeResponse(SessionModesState? modes, List<ConfigOption>? configOptions = null)
        {
            Modes = modes;
            ConfigOptions = configOptions;
        }

        /// <summary>
        /// A static instance representing resume completion.
        /// </summary>
        public static readonly SessionResumeResponse Completed = new SessionResumeResponse();
    }

    /// <summary>
    /// Request parameters for the Session/Close method.
    /// Closes an existing session and releases the Agent-side resources.
    /// </summary>
    public sealed record SessionCloseParams : AcpProtocolObject
    {
        /// <summary>
        /// Session ID (required).
        /// </summary>
        [JsonPropertyName("sessionId")]
        public string SessionId { get; init; } = string.Empty;

        /// <summary>
        /// Creates a new SessionCloseParams instance.
        /// </summary>
        public SessionCloseParams()
        {
        }

        /// <summary>
        /// Creates a new SessionCloseParams instance.
        /// </summary>
        /// <param name="sessionId">Session ID</param>
        public SessionCloseParams(string sessionId)
        {
            SessionId = sessionId;
        }
    }

    /// <summary>
    /// Response for the Session/Close method.
    /// </summary>
    public sealed record SessionCloseResponse : AcpProtocolObject
    {
        /// <summary>
        /// Creates a new SessionCloseResponse instance.
        /// </summary>
        public SessionCloseResponse()
        {
        }

        /// <summary>
        /// A static instance representing close completion.
        /// </summary>
        public static readonly SessionCloseResponse Completed = new SessionCloseResponse();
    }

    /// <summary>
    /// Request parameters for the Session/Delete method.
    /// Deletes an existing session listed by session/list.
    /// </summary>
    public sealed record SessionDeleteParams : AcpProtocolObject
    {
        /// <summary>
        /// Session ID (required).
        /// </summary>
        [JsonPropertyName("sessionId")]
        public string SessionId { get; init; } = string.Empty;

        public SessionDeleteParams()
        {
        }

        public SessionDeleteParams(string sessionId)
        {
            SessionId = sessionId;
        }
    }

    /// <summary>
    /// Response for the Session/Delete method.
    /// </summary>
    public sealed record SessionDeleteResponse : AcpProtocolObject
    {
        public static readonly SessionDeleteResponse Completed = new SessionDeleteResponse();
    }

    /// <summary>
    /// Request parameters for the Session/Set_Config_Option method.
    /// Sets a configuration option of the session.
    /// </summary>
    [JsonConverter(typeof(SessionSetConfigOptionParamsJsonConverter))]
    public sealed record SessionSetConfigOptionParams : AcpProtocolObject
    {
        /// <summary>
        /// Session ID (required).
        /// </summary>
        [JsonPropertyName("sessionId")]
        public string SessionId { get; init; } = string.Empty;

        /// <summary>
        /// Configuration option ID (required).
        /// </summary>
        [JsonPropertyName("configId")]
        public string ConfigId { get; init; } = string.Empty;

        /// <summary>
        /// The value of the configuration option (required).
        /// </summary>
        [JsonIgnore]
        public string? Value { get; init; }

        [JsonIgnore]
        public bool? BooleanValue { get; init; }

        /// <summary>
        /// Creates a new SessionSetConfigOptionParams instance.
        /// </summary>
        public SessionSetConfigOptionParams()
        {
        }

        /// <summary>
        /// Creates a new SessionSetConfigOptionParams instance.
        /// </summary>
        /// <param name="sessionId">Session ID</param>
        /// <param name="configId">Configuration option ID</param>
        /// <param name="value">The value of the configuration option</param>
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
            if (!root.TryGetProperty("value", out var value))
            {
                throw new JsonException("ACP session/set_config_option requires value.");
            }

            string? stringValue = null;
            bool? booleanValue = null;
            if (root.TryGetProperty("type", out var type)
                && type.ValueKind == JsonValueKind.String
                && string.Equals(type.GetString(), "boolean", System.StringComparison.Ordinal))
            {
                if (value.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
                {
                    throw new JsonException("ACP boolean session config value must be a boolean.");
                }

                booleanValue = value.GetBoolean();
            }
            else if (value.ValueKind == JsonValueKind.String)
            {
                stringValue = value.GetString() ?? string.Empty;
            }
            else
            {
                throw new JsonException("ACP session config value must be a string value ID or declared boolean.");
            }

            return new SessionSetConfigOptionParams
            {
                SessionId = ReadRequiredString(root, "sessionId"),
                ConfigId = ReadRequiredString(root, "configId"),
                Value = stringValue,
                BooleanValue = booleanValue,
                Meta = AcpMetaJson.Read(root)
            };
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
    /// Response for the Session/Set_Config_Option method.
    /// </summary>
    public sealed record SessionSetConfigOptionResponse : AcpProtocolObject
    {
        /// <summary>
        /// The updated list of configuration options (complete state).
        /// </summary>
        [JsonPropertyName("configOptions")]
        public List<ConfigOption>? ConfigOptions { get; init; }

        /// <summary>
        /// Creates a new SessionSetConfigOptionResponse instance.
        /// </summary>
        public SessionSetConfigOptionResponse()
        {
        }

        /// <summary>
        /// Creates a new SessionSetConfigOptionResponse instance.
        /// </summary>
        /// <param name="configOptions">List of configuration options</param>
        public SessionSetConfigOptionResponse(List<ConfigOption>? configOptions = null)
        {
            ConfigOptions = configOptions;
        }
    }
}
