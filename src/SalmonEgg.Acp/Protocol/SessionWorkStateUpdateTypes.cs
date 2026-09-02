using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SalmonEgg.Acp.Protocol
{
    /// <summary>
    /// ACP v2 foreground-work state carried by the <c>state_update</c> session update.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the v2 replacement for v1's terminal <c>session/prompt</c> response: in v2 the prompt
    /// response is a bare acknowledgement, and a turn ends when the Agent reports
    /// <see cref="SessionWorkStateKind.Idle"/> carrying the <see cref="IdleSessionWorkState.StopReason"/>.
    /// </para>
    /// <para>
    /// The wire form is doubly flattened. <c>state</c> and the payload fields are siblings of the
    /// outer <c>sessionUpdate</c> discriminator, so an idle transition is
    /// <c>{"sessionUpdate":"state_update","state":"idle","stopReason":"end_turn"}</c> - there is no
    /// nested envelope for either level.
    /// </para>
    /// <para>
    /// Modeled as an open hierarchy rather than a closed enum: the schema's trailing unconstrained
    /// member means any <c>state</c> string is valid, and unknown values that do not begin with
    /// <c>_</c> are reserved for future ACP variants, so a client must preserve rather than reject
    /// them.
    /// </para>
    /// <para>
    /// Named for the work it describes rather than the bare <c>SessionState</c> the schema field
    /// suggests: <c>SalmonEgg.Domain.Models.Session.SessionState</c> is an unrelated lifecycle enum,
    /// and the two namespaces are imported together across the chat layer. A shared short name there
    /// is an ambiguous reference, not a stylistic preference.
    /// </para>
    /// </remarks>
    [JsonConverter(typeof(SessionWorkStateJsonConverter))]
    public abstract record SessionWorkState : AcpProtocolObject
    {
        /// <summary>
        /// The raw <c>state</c> wire value.
        /// </summary>
        public abstract string State { get; }
    }

    /// <summary>
    /// Well-known <c>state</c> discriminator values.
    /// </summary>
    public static class SessionWorkStateKind
    {
        /// <summary>Foreground work is in progress.</summary>
        public const string Running = "running";

        /// <summary>The Agent is ready to process a new prompt.</summary>
        public const string Idle = "idle";

        /// <summary>Foreground work is blocked on user action.</summary>
        public const string RequiresAction = "requires_action";
    }

    /// <summary>
    /// Foreground work is in progress. The Agent must send this when foreground work starts or resumes.
    /// </summary>
    public sealed record RunningSessionWorkState : SessionWorkState
    {
        /// <inheritdoc />
        public override string State => SessionWorkStateKind.Running;
    }

    /// <summary>
    /// The Agent is ready to process a new prompt. This is the v2 end-of-turn signal.
    /// </summary>
    public sealed record IdleSessionWorkState : SessionWorkState
    {
        /// <inheritdoc />
        public override string State => SessionWorkStateKind.Idle;

        /// <summary>
        /// Why foreground work stopped. Omitted and <c>null</c> both mean the Agent is not reporting a
        /// stop reason; the Agent should include one when the idle transition ends foreground work.
        /// </summary>
        [JsonPropertyName("stopReason")]
        public StopReason? StopReason { get; init; }
    }

    /// <summary>
    /// Foreground work is blocked on user action, such as an outstanding permission request.
    /// </summary>
    public sealed record RequiresActionSessionWorkState : SessionWorkState
    {
        /// <inheritdoc />
        public override string State => SessionWorkStateKind.RequiresAction;
    }

    /// <summary>
    /// Forward-compatibility carrier for a <c>state</c> value this SDK does not model, preserving the
    /// payload verbatim so it round-trips instead of being downgraded by the client.
    /// </summary>
    public sealed record CustomSessionWorkState : SessionWorkState
    {
        private readonly string _state = string.Empty;

        /// <inheritdoc />
        public override string State => _state;

        /// <summary>
        /// The raw object as received, including the <c>state</c> field itself.
        /// </summary>
        [JsonIgnore]
        public JsonElement RawPayload { get; init; }

        /// <summary>
        /// Creates an empty carrier.
        /// </summary>
        public CustomSessionWorkState()
        {
        }

        /// <summary>
        /// Creates a carrier for an unmodeled state value.
        /// </summary>
        /// <param name="state">The raw <c>state</c> wire value.</param>
        /// <param name="rawPayload">The complete object as received.</param>
        public CustomSessionWorkState(string state, JsonElement rawPayload)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            RawPayload = rawPayload;
        }
    }

    /// <summary>
    /// Reads and writes the doubly-flattened <c>state_update</c> payload.
    /// </summary>
    /// <remarks>
    /// Reading is deliberately version-agnostic and tolerant: a parser must keep accepting whatever the
    /// peer sends, and tightening on read would make the client the arbiter of the Agent's semantics.
    /// Writing is fail-closed on the negotiated version, matching
    /// <see cref="SessionReplayFromJsonConverter"/>: <c>state_update</c> does not exist in v1, so
    /// emitting one under a v1 write context would put a field on the wire that a v1 Agent has no
    /// contract for. Throwing is the only outcome that cannot silently corrupt a v1 conversation.
    /// </remarks>
    internal sealed class SessionWorkStateJsonConverter : JsonConverter<SessionWorkState>
    {
        internal const string V2OnlyMessage =
            "ACP session/update state_update is only available in protocolVersion 2.";

        internal const string MissingStateMessage =
            "ACP state_update requires a string 'state' discriminator.";

        public override SessionWorkState? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("ACP state_update must be a JSON object.");
            }

            if (!root.TryGetProperty("state", out var stateElement)
                || stateElement.ValueKind != JsonValueKind.String)
            {
                throw new JsonException(MissingStateMessage);
            }

            var state = stateElement.GetString() ?? string.Empty;
            var meta = AcpMetaJson.Read(root);

            return state switch
            {
                SessionWorkStateKind.Running => new RunningSessionWorkState { Meta = meta },
                SessionWorkStateKind.Idle => new IdleSessionWorkState
                {
                    StopReason = ReadStopReason(root),
                    Meta = meta
                },
                SessionWorkStateKind.RequiresAction => new RequiresActionSessionWorkState { Meta = meta },
                _ => new CustomSessionWorkState(state, root.Clone()) { Meta = meta }
            };
        }

        // The schema marks stopReason x-deserialize-default-on-error, so a malformed value degrades to
        // "no stop reason reported" rather than failing the whole notification: losing the reason is
        // recoverable, dropping the end-of-turn signal is not.
        private static StopReason? ReadStopReason(JsonElement root)
        {
            if (!root.TryGetProperty("stopReason", out var element)
                || element.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var value = element.GetString();
            return value is null ? null : new StopReason(value);
        }

        public override void Write(Utf8JsonWriter writer, SessionWorkState value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(value);

            if (AcpProtocolWriteContext.Current != AcpProtocolVersion.V2)
            {
                throw new JsonException(V2OnlyMessage);
            }

            if (value is CustomSessionWorkState custom && custom.RawPayload.ValueKind == JsonValueKind.Object)
            {
                // Byte-for-byte passthrough of an unmodeled state, matching McpServer and
                // CustomToolCallContent: reserializing field by field would reorder and drop.
                custom.RawPayload.WriteTo(writer);
                return;
            }

            writer.WriteStartObject();
            writer.WriteString("state", value.State);

            if (value is IdleSessionWorkState idle && idle.StopReason is { } stopReason)
            {
                writer.WriteString("stopReason", stopReason.Value);
            }

            AcpMetaJson.Write(writer, value.Meta);
            writer.WriteEndObject();
        }
    }
}
