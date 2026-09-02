using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SalmonEgg.Acp.Protocol
{
    /// <summary>
    /// String format constraint for an elicitation string field.
    /// </summary>
    /// <remarks>
    /// Modeled as an extensible value type rather than a closed enum so unknown wire values are preserved
    /// and round-tripped losslessly: per the ACP extensibility contract, values a client does not
    /// recognize are reserved for future ACP variants and MUST NOT be rejected.
    /// </remarks>
    [JsonConverter(typeof(StringFormatJsonConverter))]
    public readonly struct StringFormat : IEquatable<StringFormat>
    {
        private readonly string? _value;

        /// <summary>
        /// Creates a string format carrying the given wire value.
        /// </summary>
        /// <param name="value">The protocol string value.</param>
        public StringFormat(string value)
        {
            _value = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>
        /// Email address format.
        /// </summary>
        public static StringFormat Email { get; } = new("email");

        /// <summary>
        /// URI format.
        /// </summary>
        public static StringFormat Uri { get; } = new("uri");

        /// <summary>
        /// Date format (YYYY-MM-DD).
        /// </summary>
        public static StringFormat Date { get; } = new("date");

        /// <summary>
        /// Date-time format (ISO 8601).
        /// </summary>
        public static StringFormat DateTime { get; } = new("date-time");

        /// <summary>
        /// The wire value carried by this format.
        /// </summary>
        public string Value => _value ?? string.Empty;

        /// <inheritdoc />
        public bool Equals(StringFormat other) => string.Equals(_value, other._value, StringComparison.Ordinal);

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is StringFormat other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => _value is null ? 0 : StringComparer.Ordinal.GetHashCode(_value);

        /// <summary>
        /// Determines whether two formats carry the same wire value.
        /// </summary>
        public static bool operator ==(StringFormat left, StringFormat right) => left.Equals(right);

        /// <summary>
        /// Determines whether two formats carry different wire values.
        /// </summary>
        public static bool operator !=(StringFormat left, StringFormat right) => !left.Equals(right);

        /// <inheritdoc />
        public override string ToString() => Value;
    }

    internal sealed class StringFormatJsonConverter : JsonConverter<StringFormat>
    {
        public override StringFormat Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException("Elicitation string format must be a string.");
            }

            return new StringFormat(reader.GetString()!);
        }

        public override void Write(Utf8JsonWriter writer, StringFormat value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.Value);
        }
    }

    /// <summary>
    /// A titled enum option carrying a constant value, a human-readable title, and an optional description.
    /// </summary>
    public sealed record EnumOption : AcpProtocolObject
    {
        /// <summary>
        /// The constant value for this option.
        /// </summary>
        [JsonPropertyName("const")]
        public string Const { get; init; } = string.Empty;

        /// <summary>
        /// Human-readable title for this option.
        /// </summary>
        [JsonPropertyName("title")]
        public string Title { get; init; } = string.Empty;

        /// <summary>
        /// Human-readable description. <c>null</c> means no description was provided.
        /// </summary>
        [JsonPropertyName("description")]
        public string? Description { get; init; }
    }

    /// <summary>
    /// Items definition for a multi-select (array) elicitation property.
    /// </summary>
    [JsonConverter(typeof(MultiSelectItemsJsonConverter))]
    public abstract record MultiSelectItems : AcpProtocolObject
    {
    }

    /// <summary>
    /// Multi-select items whose allowed values are plain strings.
    /// </summary>
    public sealed record StringMultiSelectItems : MultiSelectItems
    {
        /// <summary>
        /// Allowed enum values.
        /// </summary>
        [JsonPropertyName("enum")]
        public List<string> Enum { get; init; } = new();
    }

    /// <summary>
    /// Multi-select items whose allowed values carry human-readable labels.
    /// </summary>
    public sealed record TitledMultiSelectItems : MultiSelectItems
    {
        /// <summary>
        /// Titled enum options.
        /// </summary>
        [JsonPropertyName("anyOf")]
        public List<EnumOption> AnyOf { get; init; } = new();
    }

    /// <summary>
    /// Custom or future multi-select items type.
    /// </summary>
    /// <remarks>
    /// The spec requires a client that does not understand the item type to preserve the raw payload when
    /// storing, replaying, proxying, or forwarding the request, leaving acceptance to the Agent rather
    /// than tightening it here. <see cref="RawPayload"/> is the authoritative source for this variant.
    /// </remarks>
    public sealed record CustomMultiSelectItems : MultiSelectItems
    {
        /// <summary>
        /// The raw <c>type</c> value (a <c>_</c>-prefixed extension or a future ACP variant value).
        /// </summary>
        [JsonPropertyName("type")]
        public string ItemsType { get; init; } = string.Empty;

        /// <summary>
        /// The raw items object, preserved verbatim for passthrough.
        /// Read and written by <see cref="MultiSelectItemsJsonConverter"/>, bypassing default serialization.
        /// </summary>
        [JsonIgnore]
        public JsonElement RawPayload { get; init; }
    }

    internal sealed class MultiSelectItemsJsonConverter : JsonConverter<MultiSelectItems>
    {
        public override MultiSelectItems? Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("Elicitation multi-select items must be an object.");
            }

            // The titled variant is identified by anyOf rather than by a type discriminator: the schema
            // gives it no "type" at all, so probing anyOf first is what keeps it from being mistaken for
            // an unknown type.
            if (root.TryGetProperty("anyOf", out var anyOf))
            {
                return new TitledMultiSelectItems
                {
                    AnyOf = ElicitationSchemaJson.ReadEnumOptions(anyOf),
                    Meta = AcpMetaJson.Read(root)
                };
            }

            var itemsType = ElicitationSchemaJson.ReadOptionalString(root, "type");
            if (string.Equals(itemsType, "string", StringComparison.Ordinal))
            {
                return new StringMultiSelectItems
                {
                    Enum = ElicitationSchemaJson.ReadRequiredStringArray(root, "enum"),
                    Meta = AcpMetaJson.Read(root)
                };
            }

            return new CustomMultiSelectItems
            {
                ItemsType = itemsType ?? string.Empty,
                RawPayload = root.Clone(),
                Meta = AcpMetaJson.Read(root)
            };
        }

        public override void Write(Utf8JsonWriter writer, MultiSelectItems value, JsonSerializerOptions options)
        {
            switch (value)
            {
                case StringMultiSelectItems stringItems:
                    writer.WriteStartObject();
                    writer.WriteString("type", "string");
                    ElicitationSchemaJson.WriteStringArray(writer, "enum", stringItems.Enum);
                    AcpMetaJson.Write(writer, stringItems.Meta);
                    writer.WriteEndObject();
                    break;
                case TitledMultiSelectItems titledItems:
                    writer.WriteStartObject();
                    ElicitationSchemaJson.WriteEnumOptions(writer, "anyOf", titledItems.AnyOf);
                    AcpMetaJson.Write(writer, titledItems.Meta);
                    writer.WriteEndObject();
                    break;
                case CustomMultiSelectItems customItems:
                    ElicitationSchemaJson.WritePassthrough(
                        writer,
                        customItems.RawPayload,
                        customItems.ItemsType,
                        customItems.Meta);
                    break;
                default:
                    throw new JsonException(
                        $"Unsupported elicitation multi-select items type: {value.GetType().FullName}");
            }
        }
    }
}
