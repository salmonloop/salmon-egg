using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using SalmonEgg.Acp.Content;
using SalmonEgg.Acp.Plan;
using SalmonEgg.Acp.Tool;

namespace SalmonEgg.Acp.Protocol
{
    /// <summary>
    /// Parameters for the session/update notification.
    /// Used by the agent to send session updates to the client.
    /// </summary>
    [JsonConverter(typeof(SessionUpdateParamsJsonConverter))]
    public record SessionUpdateParams : AcpProtocolObject
    {
        /// <summary>
        /// Session ID (required).
        /// </summary>
        [JsonPropertyName("sessionId")]
        public string SessionId { get; init; } = string.Empty;

        /// <summary>
        /// The update payload (polymorphic type).
        /// May be text, a tool call, a plan, a mode switch, and so on.
        /// </summary>
        [JsonPropertyName("update")]
        public SessionUpdate Update { get; init; } = null!;

        /// <summary>
        /// Creates a new SessionUpdateParams instance.
        /// </summary>
        public SessionUpdateParams()
        {
        }

        /// <summary>
        /// Creates a new SessionUpdateParams instance.
        /// </summary>
        /// <param name="sessionId">Session ID.</param>
        /// <param name="update">The update payload.</param>
        public SessionUpdateParams(string sessionId, SessionUpdate update)
        {
            SessionId = sessionId;
            Update = update;
        }
    }

    /// <summary>
    /// Base polymorphic type for session updates.
    /// Uses the JsonPolymorphic attribute to support different kinds of updates.
    /// </summary>
    [JsonPolymorphic(
        TypeDiscriminatorPropertyName = "sessionUpdate",
        UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FallBackToBaseType,
        IgnoreUnrecognizedTypeDiscriminators = true)]
    [JsonDerivedType(typeof(AgentMessageUpdate), "agent_message_chunk")]
    [JsonDerivedType(typeof(UserMessageUpdate), "user_message_chunk")]
    [JsonDerivedType(typeof(AgentThoughtUpdate), "agent_thought_chunk")]
    [JsonDerivedType(typeof(ToolCallUpdate), "tool_call")]
    [JsonDerivedType(typeof(ToolCallStatusUpdate), "tool_call_update")]
    [JsonDerivedType(typeof(PlanUpdate), "plan")]
    [JsonDerivedType(typeof(CurrentModeUpdate), "current_mode_update")]
    [JsonDerivedType(typeof(AvailableCommandsUpdate), "available_commands_update")]
    [JsonDerivedType(typeof(ConfigOptionUpdate), "config_option_update")]
    [JsonDerivedType(typeof(SessionInfoUpdate), "session_info_update")]
    [JsonDerivedType(typeof(UsageUpdate), "usage_update")]
    public record SessionUpdate : AcpProtocolObject
    {
        /// <summary>
        /// Forward-compatible fields that are not bound to a known contract (including the complete payload of an
        /// unknown sessionUpdate discriminator value).
        /// The protocol requires unknown updates to be preserved verbatim and to round-trip; the client neither
        /// interprets nor discards them, and their semantics are decided by the agent
        /// (AGENTS.md: protocol leniency must never be tightened in reverse).
        /// </summary>
        // STJ requires JsonExtensionData binders to be settable (not init-only) when the
        // polymorphic record hierarchy uses a deserialization constructor. Keep mutation
        // confined to serializer/converter paths; protocol consumers should treat this as
        // opaque forward-compat payload.
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtensionData { get; set; }

        /// <summary>
        /// The raw discriminator value of an unknown update kind; non-null only when this instance is the base-type
        /// fallback produced by an unrecognized discriminator value.
        /// </summary>
        [JsonIgnore]
        public string? UnknownUpdateKind =>
            ExtensionData is not null
                && ExtensionData.TryGetValue("sessionUpdate", out var kind)
                && kind.ValueKind == JsonValueKind.String
            ? kind.GetString()
            : null;
    }

    /// <summary>
    /// Reads and writes session/update parameters. Known updates are delegated entirely to the polymorphic
    /// contract; when an unrecognized discriminator value falls back to the base type, STJ discards that
    /// discriminator as polymorphic metadata, so it is restored here into
    /// <see cref="SessionUpdate.ExtensionData"/> to guarantee that unknown updates round-trip verbatim instead of
    /// being silently downgraded by the client.
    /// </summary>
    internal sealed class SessionUpdateParamsJsonConverter : JsonConverter<SessionUpdateParams>
    {
        public override SessionUpdateParams? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("session/update params must be a JSON object.");
            }

            var sessionId = root.TryGetProperty("sessionId", out var sessionIdElement)
                    && sessionIdElement.ValueKind == JsonValueKind.String
                ? sessionIdElement.GetString() ?? string.Empty
                : string.Empty;

            SessionUpdate? update = null;
            if (root.TryGetProperty("update", out var updateElement) && updateElement.ValueKind == JsonValueKind.Object)
            {
                update = updateElement.Deserialize(
                    (JsonTypeInfo<SessionUpdate>)options.GetTypeInfo(typeof(SessionUpdate)));
                if (update is not null
                    && update.GetType() == typeof(SessionUpdate)
                    && updateElement.TryGetProperty("sessionUpdate", out var kindElement))
                {
                    var extensionData = update.ExtensionData is null
                        ? new Dictionary<string, JsonElement>()
                        : new Dictionary<string, JsonElement>(update.ExtensionData);
                    extensionData["sessionUpdate"] = kindElement.Clone();
                    update = update with { ExtensionData = extensionData };
                }
            }

            return new SessionUpdateParams
            {
                SessionId = sessionId,
                Update = update!,
                Meta = AcpMetaJson.Read(root)
            };
        }

        public override void Write(Utf8JsonWriter writer, SessionUpdateParams value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("sessionId", value.SessionId);
            if (value.Update is not null)
            {
                writer.WritePropertyName("update");
                JsonSerializer.Serialize(
                    writer,
                    value.Update,
                    (JsonTypeInfo<SessionUpdate>)options.GetTypeInfo(typeof(SessionUpdate)));
            }

            AcpMetaJson.Write(writer, value.Meta);
            writer.WriteEndObject();
        }
    }

    public abstract record ContentChunkUpdate : SessionUpdate
    {
        [JsonPropertyName("messageId")]
        public string? MessageId { get; init; }
    }

    /// <summary>
    /// Usage update extension.
    /// Represents resource usage or other telemetry sent by the agent.
    /// </summary>
    public sealed record UsageUpdate : SessionUpdate
    {
        [JsonPropertyName("used")]
        public ulong Used { get; init; }

        [JsonPropertyName("size")]
        public ulong Size { get; init; }

        [JsonPropertyName("cost")]
        public UsageCost? Cost { get; init; }
    }

    public sealed record UsageCost : AcpProtocolObject
    {
        [JsonPropertyName("amount")]
        public double Amount { get; init; }

        [JsonPropertyName("currency")]
        public string Currency { get; init; } = string.Empty;
    }

    /// <summary>
    /// Agent message chunk update.
    /// Used to stream the agent's text response.
    /// </summary>
    public sealed record AgentMessageUpdate : ContentChunkUpdate
    {
        [JsonPropertyName("content")]
        public ContentBlock? Content { get; init; }

        /// <summary>
        /// Creates a new AgentMessageUpdate instance.
        /// </summary>
        public AgentMessageUpdate()
        {
        }

        /// <summary>
        /// Creates a new AgentMessageUpdate instance.
        /// </summary>
        /// <param name="content">The content block.</param>
        public AgentMessageUpdate(ContentBlock? content)
        {
            Content = content;
        }
    }

    /// <summary>
    /// User message chunk update (used for session/load replay or multi-client synchronization).
    /// </summary>
    public sealed record UserMessageUpdate : ContentChunkUpdate
    {
        [JsonPropertyName("content")]
        public ContentBlock? Content { get; init; }

        public UserMessageUpdate()
        {
        }

        public UserMessageUpdate(ContentBlock? content)
        {
            Content = content;
        }
    }

    /// <summary>
    /// Agent thought chunk update (usually not shown to the user directly, but it must remain parsable/skippable).
    /// </summary>
    public sealed record AgentThoughtUpdate : ContentChunkUpdate
    {
        /// <summary>
        /// The message content block.
        /// </summary>
        [JsonPropertyName("content")]
        public ContentBlock? Content { get; init; }
    }

    /// <summary>
    /// Tool call update.
    /// Used to notify the client that the state of a tool call has changed.
    /// </summary>
    public sealed record ToolCallUpdate : SessionUpdate
    {
        /// <summary>
        /// Tool call ID.
        /// </summary>
        [JsonPropertyName("toolCallId")]
        public string? ToolCallId { get; init; }

        /// <summary>
        /// Tool call kind.
        /// </summary>
        [JsonPropertyName("kind")]
        public ToolCallKind? Kind { get; init; }

        /// <summary>
        /// Tool call status.
        /// </summary>
        [JsonPropertyName("status")]
        public ToolCallStatus? Status { get; init; }

        /// <summary>
        /// Title (optional).
        /// </summary>
        [JsonPropertyName("title")]
        public string? Title { get; init; }

        /// <summary>
        /// Content produced by the tool call.
        /// </summary>
        [JsonPropertyName("content")]
        public List<ToolCallContent>? Content { get; init; }

        /// <summary>
        /// List of file locations, indicating the files affected by the tool call.
        /// </summary>
        [JsonPropertyName("locations")]
        public List<ToolCallLocation>? Locations { get; init; }

        /// <summary>
        /// Raw input parameters.
        /// </summary>
        [JsonPropertyName("rawInput")]
        public JsonElement? RawInput { get; init; }

        /// <summary>
        /// Raw output result.
        /// </summary>
        [JsonPropertyName("rawOutput")]
        public JsonElement? RawOutput { get; init; }

        /// <summary>
        /// Creates a new ToolCallUpdate instance.
        /// </summary>
        public ToolCallUpdate()
        {
        }

        /// <summary>
        /// Creates a new ToolCallUpdate instance.
        /// </summary>
        /// <param name="toolCallId">Tool call ID.</param>
        /// <param name="kind">Tool call kind.</param>
        /// <param name="status">Tool call status.</param>
        /// <param name="title">Title.</param>
        /// <param name="content">Content produced by the tool call.</param>
        /// <param name="locations">List of file locations.</param>
        /// <param name="rawInput">Raw input parameters.</param>
        /// <param name="rawOutput">Raw output result.</param>
        public ToolCallUpdate(
            string? toolCallId = null,
            ToolCallKind? kind = null,
            ToolCallStatus? status = null,
            string? title = null,
            List<ToolCallContent>? content = null,
            List<ToolCallLocation>? locations = null,
            JsonElement? rawInput = null,
            JsonElement? rawOutput = null)
        {
            ToolCallId = toolCallId;
            Kind = kind;
            Status = status;
            Title = title;
            Content = content;
            Locations = locations;
            RawInput = rawInput;
            RawOutput = rawOutput;
        }
    }

    /// <summary>
    /// Plan update.
    /// Used to notify the client that the agent's action plan has changed.
    /// </summary>
    public sealed record PlanUpdate : SessionUpdate
    {
        private readonly List<PlanEntry> _entries = new();

        /// <summary>
        /// List of plan entries (used by the plan update kind).
        /// </summary>
        [JsonRequired]
        [JsonPropertyName("entries")]
        public List<PlanEntry> Entries
        {
            get => _entries;
            init => _entries = global::SalmonEgg.Acp.Plan.Plan.ValidateEntries(value);
        }

        /// <summary>
        /// Creates a new PlanUpdate instance.
        /// </summary>
        public PlanUpdate()
        {
        }

        /// <summary>
        /// Creates a new PlanUpdate instance.
        /// </summary>
        /// <param name="entries">List of plan entries.</param>
        public PlanUpdate(List<PlanEntry> entries)
        {
            Entries = entries;
        }
    }

    /// <summary>
    /// Current mode update (current_mode_update).
    /// ACP sends changes to the current mode through the session/update notification.
    /// </summary>
    public sealed record CurrentModeUpdate : SessionUpdate
    {
        [JsonPropertyName("currentModeId")]
        public string ModeId { get; init; } = string.Empty;

        public CurrentModeUpdate()
        {
        }

        public CurrentModeUpdate(string modeId)
        {
            ModeId = modeId;
        }
    }

    /// <summary>
    /// Tool call status update (tool_call_update).
    /// Some agents do not send the complete toolCall object in a tool_call update and push only the status and the
    /// output content.
    /// </summary>
    public sealed record ToolCallStatusUpdate : SessionUpdate
    {
        /// <summary>
        /// Tool call ID.
        /// </summary>
        [JsonPropertyName("toolCallId")]
        public string? ToolCallId { get; init; }

        /// <summary>
        /// Tool call kind.
        /// </summary>
        [JsonPropertyName("kind")]
        public ToolCallKind? Kind { get; init; }

        /// <summary>
        /// Title (optional).
        /// </summary>
        [JsonPropertyName("title")]
        public string? Title { get; init; }

        /// <summary>
        /// Tool call status.
        /// </summary>
        [JsonPropertyName("status")]
        public ToolCallStatus? Status { get; init; }

        /// <summary>
        /// Content produced by the tool call.
        /// </summary>
        [JsonPropertyName("content")]
        public List<ToolCallContent>? Content { get; init; }

        /// <summary>
        /// List of file locations, indicating the files affected by the tool call.
        /// </summary>
        [JsonPropertyName("locations")]
        public List<ToolCallLocation>? Locations { get; init; }

        /// <summary>
        /// Raw input parameters.
        /// </summary>
        [JsonPropertyName("rawInput")]
        public JsonElement? RawInput { get; init; }

        /// <summary>
        /// Raw output result.
        /// </summary>
        [JsonPropertyName("rawOutput")]
        public JsonElement? RawOutput { get; init; }
    }

    /// <summary>
    /// Configuration option update (config_option_update).
    /// </summary>
    public sealed record ConfigOptionUpdate : SessionUpdate
    {
        /// <summary>
        /// List of configuration options.
        /// </summary>
        [JsonPropertyName("configOptions")]
        public List<ConfigOption>? ConfigOptions { get; init; }
    }

    /// <summary>
    /// Session info update (session_info_update).
    /// </summary>
    public sealed record SessionInfoUpdate : SessionUpdate
    {
        private string? _title;
        private string? _updatedAt;

        /// <summary>
        /// Session title (optional).
        /// Setter tracks JSON presence so omitted vs explicit-null can be distinguished after deserialize.
        /// </summary>
        [JsonPropertyName("title")]
        public string? Title
        {
            get => _title;
            set
            {
                _title = value;
                HasTitle = true;
            }
        }

        [JsonIgnore]
        public bool HasTitle { get; private set; }

        /// <summary>
        /// Last updated timestamp (UTC iso8601).
        /// Setter tracks JSON presence so omitted vs explicit-null can be distinguished after deserialize.
        /// </summary>
        [JsonPropertyName("updatedAt")]
        public string? UpdatedAt
        {
            get => _updatedAt;
            set
            {
                _updatedAt = value;
                HasUpdatedAt = true;
            }
        }

        [JsonIgnore]
        public bool HasUpdatedAt { get; private set; }
    }
}
