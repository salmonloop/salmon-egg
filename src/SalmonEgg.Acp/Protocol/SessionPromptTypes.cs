using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using SalmonEgg.Acp.Content;

namespace SalmonEgg.Acp.Protocol
{
    /// <summary>
    /// Request parameters for the <c>session/prompt</c> method.
    /// Sends a prompt to a session and requests a response from the Agent.
    /// </summary>
    public sealed record SessionPromptParams : AcpProtocolObject
    {
        /// <summary>
        /// The session id. Required.
        /// </summary>
        [JsonPropertyName("sessionId")]
        public string SessionId { get; init; } = string.Empty;

        /// <summary>
        /// The prompt content blocks to send. Required, and serialized as an array per the protocol.
        /// </summary>
        [JsonPropertyName("prompt")]
        public List<ContentBlock> Prompt { get; init; } = new List<ContentBlock>();

        /// <summary>
        /// Protocol extension field (<c>_meta</c>).
        /// </summary>
        /// <summary>
        /// Creates a new <see cref="SessionPromptParams"/> instance.
        /// </summary>
        public SessionPromptParams()
        {
        }

        /// <summary>
        /// Creates a new <see cref="SessionPromptParams"/> instance.
        /// </summary>

        /// <param name="sessionId">The session id.</param>
        /// <param name="prompt">The prompt content blocks.</param>
        public SessionPromptParams(string sessionId, List<ContentBlock> prompt)
        {
            SessionId = sessionId;
            Prompt = prompt;
        }
    }

    /// <summary>
    /// Response for the <c>session/prompt</c> method.
    /// The Agent's reply to a prompt request, carrying only the stop reason.
    /// </summary>
    [JsonConverter(typeof(SessionPromptResponseJsonConverter))]
    public sealed record SessionPromptResponse : AcpProtocolObject
    {
        /// <summary>
        /// The stop reason, indicating why the Agent stopped generating a response.
        /// </summary>
        [JsonPropertyName("stopReason")]
        public StopReason StopReason { get; init; } = StopReason.EndTurn;

        /// <summary>
        /// Whether <c>stopReason</c> was present on the wire, reported separately from
        /// <see cref="SessionPromptResponse.StopReason"/> so an absent field cannot be mistaken for
        /// the default <c>StopReason.EndTurn</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Version-independent by design, and meaningful on both protocol versions - which is why it
        /// carries no draft marking even though one of its two readings is about v2.
        /// </para>
        /// <para>
        /// On v1, <c>stopReason</c> is required, so <c>false</c> means the Agent violated the
        /// contract. It is the only observable signal for that violation: the field is typed, and a
        /// missing one silently reads as "the turn ended normally". A v1 client that wants to
        /// attribute the fault to the peer rather than absorb it has to read this.
        /// </para>
        /// <para>
        /// On v2, prompt responses are bare acknowledgements, so <c>false</c> is the expected shape
        /// and completion arrives later as an idle <c>state_update</c> instead.
        /// </para>
        /// </remarks>
        [JsonIgnore]
        public bool HasStopReason { get; init; }

        /// <summary>
        /// Creates a new <see cref="SessionPromptResponse"/> instance.
        /// </summary>
        public SessionPromptResponse()
        {
        }

        /// <summary>
        /// Creates a new <see cref="SessionPromptResponse"/> instance.
        /// </summary>
        /// <param name="stopReason">The stop reason.</param>
        public SessionPromptResponse(StopReason stopReason)
        {
            StopReason = stopReason;
        }
    }

    internal sealed class SessionPromptResponseJsonConverter : JsonConverter<SessionPromptResponse>
    {
        public override SessionPromptResponse? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new JsonException("ACP session/prompt response must be an object.");
            var has = root.TryGetProperty("stopReason", out var reason) && reason.ValueKind == JsonValueKind.String;
            return new SessionPromptResponse
            {
                StopReason = has ? new StopReason(reason.GetString() ?? string.Empty) : StopReason.EndTurn,
                HasStopReason = has,
                Meta = AcpMetaJson.Read(root)
            };
        }

        public override void Write(Utf8JsonWriter writer, SessionPromptResponse value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            if (AcpProtocolWriteContext.Current == AcpProtocolVersion.V1)
                writer.WriteString("stopReason", value.StopReason.Value);
            AcpMetaJson.Write(writer, value.Meta);
            writer.WriteEndObject();
        }
    }
}
