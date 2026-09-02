using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SalmonEgg.Acp.Protocol
{
    /// <summary>
    /// A single value in an accepted elicitation's content object.
    /// </summary>
    /// <remarks>
    /// The wire union allows a string, an integer, a number, a boolean, or a string array — matching the
    /// primitive property schemas a form may declare. The value is kept as the raw JSON token so an
    /// integer is not silently widened to a double on the way back out.
    /// </remarks>
    public sealed record ElicitationContentValue
    {
        private ElicitationContentValue(JsonElement rawValue)
        {
            RawValue = rawValue;
        }

        /// <summary>
        /// The raw JSON token for this value.
        /// </summary>
        public JsonElement RawValue { get; }

        /// <summary>
        /// Creates a string value.
        /// </summary>
        /// <param name="value">The string to carry.</param>
        public static ElicitationContentValue FromString(string value)
        {
            ArgumentNullException.ThrowIfNull(value);
            return new ElicitationContentValue(Write(writer => writer.WriteStringValue(value)));
        }

        /// <summary>
        /// Creates an integer value.
        /// </summary>
        /// <param name="value">The integer to carry.</param>
        public static ElicitationContentValue FromInteger(long value)
            => new(Write(writer => writer.WriteNumberValue(value)));

        /// <summary>
        /// Creates a floating-point value.
        /// </summary>
        /// <param name="value">The number to carry.</param>
        public static ElicitationContentValue FromNumber(double value)
            => new(Write(writer => writer.WriteNumberValue(value)));

        /// <summary>
        /// Creates a boolean value.
        /// </summary>
        /// <param name="value">The flag to carry.</param>
        public static ElicitationContentValue FromBoolean(bool value)
            => new(Write(writer => writer.WriteBooleanValue(value)));

        /// <summary>
        /// Creates a string-array value, as produced by a multi-select field.
        /// </summary>
        /// <param name="values">The selected values.</param>
        public static ElicitationContentValue FromStringArray(IEnumerable<string> values)
        {
            ArgumentNullException.ThrowIfNull(values);
            return new ElicitationContentValue(Write(writer =>
            {
                writer.WriteStartArray();
                foreach (var value in values)
                {
                    writer.WriteStringValue(value);
                }

                writer.WriteEndArray();
            }));
        }

        /// <summary>
        /// Creates a value from an already-parsed JSON token.
        /// </summary>
        /// <param name="value">The token to carry.</param>
        public static ElicitationContentValue FromJson(JsonElement value) => new(value.Clone());

        /// <remarks>
        /// The token is produced with <see cref="Utf8JsonWriter"/> rather than
        /// <c>JsonSerializer.Serialize</c>: the reflection-based overload is not trim- or AOT-safe, and
        /// this SDK ships AOT-hardened.
        /// </remarks>
        private static JsonElement Write(Action<Utf8JsonWriter> writeValue)
        {
            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writeValue(writer);
            }

            using var document = JsonDocument.Parse(buffer.WrittenMemory);
            return document.RootElement.Clone();
        }
    }

    /// <summary>
    /// Client-to-agent response to an <c>elicitation/create</c> request.
    /// </summary>
    [JsonConverter(typeof(CreateElicitationResponseJsonConverter))]
    public abstract record CreateElicitationResponse : AcpProtocolObject
    {
        /// <summary>
        /// The raw <c>action</c> discriminator value carried on the wire.
        /// </summary>
        [JsonIgnore]
        public abstract string Action { get; }
    }

    /// <summary>
    /// The user submitted the form, or consented to the URL interaction.
    /// </summary>
    /// <remarks>
    /// <c>content</c> is optional and only meaningful on accept: for a form it should conform to the
    /// requested schema, and for a URL elicitation it is normally omitted because the interaction happens
    /// out of band.
    /// </remarks>
    public sealed record ElicitationAcceptResponse : CreateElicitationResponse
    {
        /// <summary>
        /// The user-provided content, keyed by field name, or <c>null</c> when there is none.
        /// </summary>
        public Dictionary<string, ElicitationContentValue>? Content { get; init; }

        /// <inheritdoc />
        [JsonIgnore]
        public override string Action => ElicitationActions.Accept;
    }

    /// <summary>
    /// The user explicitly declined the elicitation.
    /// </summary>
    public sealed record ElicitationDeclineResponse : CreateElicitationResponse
    {
        /// <inheritdoc />
        [JsonIgnore]
        public override string Action => ElicitationActions.Decline;
    }

    /// <summary>
    /// The user dismissed the elicitation without choosing.
    /// </summary>
    public sealed record ElicitationCancelResponse : CreateElicitationResponse
    {
        /// <inheritdoc />
        [JsonIgnore]
        public override string Action => ElicitationActions.Cancel;
    }

    /// <summary>
    /// Custom or future elicitation action.
    /// </summary>
    /// <remarks>
    /// Present so a response parsed from the wire (for example when proxying) round-trips an action this
    /// client does not know, without being treated as a known action.
    /// </remarks>
    public sealed record CustomElicitationResponse : CreateElicitationResponse
    {
        /// <summary>
        /// The raw <c>action</c> value (a <c>_</c>-prefixed extension or a future ACP variant value).
        /// </summary>
        public string RawAction { get; init; } = string.Empty;

        /// <summary>
        /// The raw response object, preserved verbatim for passthrough.
        /// </summary>
        [JsonIgnore]
        public JsonElement RawPayload { get; init; }

        /// <inheritdoc />
        [JsonIgnore]
        public override string Action => RawAction;
    }

    /// <summary>
    /// Agent-to-client notification that a URL-based elicitation completed out of band.
    /// </summary>
    /// <remarks>
    /// The client must treat the id as opaque and ignore unknown or already-completed ids.
    /// </remarks>
    public sealed record CompleteElicitationNotification : AcpProtocolObject
    {
        /// <summary>
        /// The id of the elicitation that completed.
        /// </summary>
        [JsonPropertyName("elicitationId")]
        public string ElicitationId { get; init; } = string.Empty;
    }

    internal sealed class CreateElicitationResponseJsonConverter : JsonConverter<CreateElicitationResponse>
    {
        public override CreateElicitationResponse? Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("elicitation/create response must be an object.");
            }

            var action = ElicitationSchemaJson.ReadOptionalString(root, "action");
            var meta = AcpMetaJson.Read(root);

            return action switch
            {
                ElicitationActions.Accept => new ElicitationAcceptResponse
                {
                    Content = ReadContent(root),
                    Meta = meta
                },
                ElicitationActions.Decline => new ElicitationDeclineResponse { Meta = meta },
                ElicitationActions.Cancel => new ElicitationCancelResponse { Meta = meta },
                _ => new CustomElicitationResponse
                {
                    RawAction = action ?? string.Empty,
                    RawPayload = root.Clone(),
                    Meta = meta
                }
            };
        }

        public override void Write(
            Utf8JsonWriter writer,
            CreateElicitationResponse value,
            JsonSerializerOptions options)
        {
            if (value is CustomElicitationResponse custom)
            {
                ElicitationSchemaJson.WritePassthrough(writer, custom.RawPayload, custom.RawAction, custom.Meta);
                return;
            }

            writer.WriteStartObject();
            writer.WriteString("action", value.Action);

            // content is written only for accept: the spec says receivers ignore it for decline and
            // cancel, so emitting it there would be noise the Agent must discard.
            if (value is ElicitationAcceptResponse accept && accept.Content is not null)
            {
                writer.WritePropertyName("content");
                writer.WriteStartObject();
                foreach (var field in accept.Content)
                {
                    writer.WritePropertyName(field.Key);
                    writer.WriteRawValue(field.Value.RawValue.GetRawText());
                }

                writer.WriteEndObject();
            }

            AcpMetaJson.Write(writer, value.Meta);
            writer.WriteEndObject();
        }

        private static Dictionary<string, ElicitationContentValue>? ReadContent(JsonElement root)
        {
            if (!root.TryGetProperty("content", out var content) || content.ValueKind == JsonValueKind.Null)
            {
                // Omitted and null are equivalent and mean no content was provided.
                return null;
            }

            if (content.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("elicitation/create response 'content' must be an object.");
            }

            var values = new Dictionary<string, ElicitationContentValue>(StringComparer.Ordinal);
            foreach (var field in content.EnumerateObject())
            {
                values[field.Name] = ElicitationContentValue.FromJson(field.Value);
            }

            return values;
        }
    }
}
