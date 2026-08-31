using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using SalmonEgg.Acp.Mcp;

namespace SalmonEgg.Acp.Protocol
{
    /// <summary>
    /// Request parameters for the Session/New method.
    /// Used to create a new session.
    /// </summary>
    public sealed record SessionNewParams : AcpProtocolObject
    {
        /// <summary>
        /// The working directory for the session (required).
        /// </summary>
        [JsonPropertyName("cwd")]
        public string Cwd { get; init; } = string.Empty;

        /// <summary>
        /// List of MCP server configurations (required; the protocol requires this value to be an array).
        /// </summary>
        [JsonPropertyName("mcpServers")]
        public List<McpServer> McpServers { get; init; } = new List<McpServer>();

        /// <summary>
        /// Additional working directories. When non-empty, the Agent is required to declare
        /// sessionCapabilities.additionalDirectories.
        /// </summary>
        [JsonPropertyName("additionalDirectories")]
        public List<string>? AdditionalDirectories { get; init; }

        /// <summary>
        /// Creates a new SessionNewParams instance.
        /// </summary>
        public SessionNewParams()
        {
        }

        /// <summary>
        /// Creates a new SessionNewParams instance.
        /// </summary>
        /// <param name="cwd">The working directory</param>
        /// <param name="mcpServers">MCP server configurations</param>
        /// <param name="additionalDirectories">Additional working directories</param>
        public SessionNewParams(
            string cwd,
            List<McpServer>? mcpServers = null,
            List<string>? additionalDirectories = null)
        {
            Cwd = cwd;
            McpServers = mcpServers ?? new List<McpServer>();
            AdditionalDirectories = additionalDirectories;
        }
    }

    /// <summary>
    /// Response for the Session/New method.
    /// The Agent's response to a session creation request.
    /// </summary>
    public sealed record SessionNewResponse : AcpProtocolObject
    {
        /// <summary>
        /// The ID of the newly created session.
        /// </summary>
        [JsonPropertyName("sessionId")]
        public string SessionId { get; init; } = string.Empty;

        /// <summary>
        /// Session mode state (optional; the ACP standard shape is a SessionModeState object).
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
        /// Creates a new SessionNewResponse instance.
        /// </summary>
        public SessionNewResponse()
        {
        }

        /// <summary>
        /// Creates a new SessionNewResponse instance.
        /// </summary>
        /// <param name="sessionId">The session ID</param>
        /// <param name="modes">The list of available modes</param>
        /// <param name="configOptions">Configuration options</param>
        public SessionNewResponse(string sessionId, SessionModesState? modes = null, List<ConfigOption>? configOptions = null)
        {
            SessionId = sessionId;
            Modes = modes;
            ConfigOptions = configOptions;
        }
    }

    /// <summary>
    /// Session mode state (used in the Session/New response).
    /// https://agentclientprotocol.com/protocol/session-modes
    /// </summary>
    public sealed record SessionModesState : AcpProtocolObject
    {
        /// <summary>
        /// The current mode ID.
        /// </summary>
        [JsonPropertyName("currentModeId")]
        public string CurrentModeId { get; init; } = string.Empty;

        /// <summary>
        /// The list of available modes.
        /// </summary>
        [JsonPropertyName("availableModes")]
        public List<SessionMode> AvailableModes { get; init; } = new();
    }

    public sealed record SessionMode : AcpProtocolObject
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("description")]
        public string? Description { get; init; }
    }

    internal sealed class SessionModesStateJsonConverter : JsonConverter<SessionModesState?>
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
            AcpMetaJson.Write(writer, value.Meta);
            writer.WriteEndObject();
        }

        private static SessionModesState ReadModesObject(ref Utf8JsonReader reader)
        {
            string? currentModeId = null;
            List<SessionMode>? availableModes = null;
            Dictionary<string, object?>? meta = null;
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

                    return new SessionModesState
                    {
                        CurrentModeId = currentModeId ?? string.Empty,
                        AvailableModes = availableModes ?? new List<SessionMode>(),
                        Meta = meta
                    };
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

                        currentModeId = reader.GetString() ?? string.Empty;
                        hasCurrentModeId = true;
                        break;
                    case "availableModes":
                        if (reader.TokenType != JsonTokenType.StartArray)
                        {
                            throw new JsonException("Session modes availableModes must be an array.");
                        }

                        availableModes = ReadModesArray(ref reader);
                        hasAvailableModes = true;
                        break;
                    case "_meta":
                        meta = ReadMetaObject(ref reader);
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
            string? id = null;
            string? name = null;
            string? description = null;
            Dictionary<string, object?>? meta = null;
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

                    return new SessionMode
                    {
                        Id = id ?? string.Empty,
                        Name = name ?? string.Empty,
                        Description = description,
                        Meta = meta
                    };
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

                        id = reader.GetString() ?? string.Empty;
                        hasId = true;
                        break;
                    case "name":
                        if (reader.TokenType != JsonTokenType.String)
                        {
                            throw new JsonException("Session mode name must be a string.");
                        }

                        name = reader.GetString() ?? string.Empty;
                        hasName = true;
                        break;
                    case "description":
                        if (reader.TokenType != JsonTokenType.Null && reader.TokenType != JsonTokenType.String)
                        {
                            throw new JsonException("Session mode description must be a string or null.");
                        }

                        description = reader.TokenType == JsonTokenType.Null ? null : reader.GetString();
                        break;
                    case "_meta":
                        meta = ReadMetaObject(ref reader);
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

            AcpMetaJson.Write(writer, mode.Meta);
            writer.WriteEndObject();
        }

        private static Dictionary<string, object?>? ReadMetaObject(ref Utf8JsonReader reader)
            => AcpMetaJson.ReadValue(ref reader);

        private static bool ShouldWriteNull(JsonSerializerOptions options)
        {
            return options.DefaultIgnoreCondition != JsonIgnoreCondition.WhenWritingNull
                && options.DefaultIgnoreCondition != JsonIgnoreCondition.WhenWritingDefault;
        }
    }
}
