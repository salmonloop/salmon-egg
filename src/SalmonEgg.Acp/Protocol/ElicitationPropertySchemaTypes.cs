using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SalmonEgg.Acp.Protocol
{
    /// <summary>
    /// Schema for a single elicitation form field. Each variant corresponds to a JSON Schema
    /// <c>"type"</c> value.
    /// </summary>
    /// <remarks>
    /// Single-select enums are the string variant with <c>enum</c> or <c>oneOf</c> set; multi-select
    /// enums are the array variant.
    /// </remarks>
    [JsonConverter(typeof(ElicitationPropertySchemaJsonConverter))]
    public abstract record ElicitationPropertySchema : AcpProtocolObject
    {
        /// <summary>
        /// Human-readable description. <c>null</c> means no description was provided.
        /// </summary>
        [JsonPropertyName("description")]
        public string? Description { get; init; }

        /// <summary>
        /// Optional title for the property. <c>null</c> means no title was provided.
        /// </summary>
        [JsonPropertyName("title")]
        public string? Title { get; init; }
    }

    /// <summary>
    /// String property, or a single-select enum when <c>enum</c> or <c>oneOf</c> is set.
    /// </summary>
    public sealed record StringPropertySchema : ElicitationPropertySchema
    {
        /// <summary>
        /// Default value. <c>null</c> means no default was provided.
        /// </summary>
        [JsonPropertyName("default")]
        public string? Default { get; init; }

        /// <summary>
        /// Enum values for untitled single-select enums. <c>null</c> means none were declared.
        /// </summary>
        [JsonPropertyName("enum")]
        public List<string>? Enum { get; init; }

        /// <summary>
        /// Titled enum options for titled single-select enums. <c>null</c> means none were declared.
        /// </summary>
        [JsonPropertyName("oneOf")]
        public List<EnumOption>? OneOf { get; init; }

        /// <summary>
        /// String format constraint. <c>null</c> means there is no format constraint.
        /// </summary>
        [JsonPropertyName("format")]
        public StringFormat? Format { get; init; }

        /// <summary>
        /// Maximum string length. <c>null</c> means there is no maximum.
        /// </summary>
        [JsonPropertyName("maxLength")]
        public uint? MaxLength { get; init; }

        /// <summary>
        /// Minimum string length. <c>null</c> means there is no minimum.
        /// </summary>
        [JsonPropertyName("minLength")]
        public uint? MinLength { get; init; }

        /// <summary>
        /// Pattern the string must match. <c>null</c> means there is no pattern constraint.
        /// </summary>
        [JsonPropertyName("pattern")]
        public string? Pattern { get; init; }
    }

    /// <summary>
    /// Integer property.
    /// </summary>
    public sealed record IntegerPropertySchema : ElicitationPropertySchema
    {
        /// <summary>
        /// Default value. <c>null</c> means no default was provided.
        /// </summary>
        [JsonPropertyName("default")]
        public long? Default { get; init; }

        /// <summary>
        /// Inclusive upper bound. <c>null</c> means there is none.
        /// </summary>
        [JsonPropertyName("maximum")]
        public long? Maximum { get; init; }

        /// <summary>
        /// Inclusive lower bound. <c>null</c> means there is none.
        /// </summary>
        [JsonPropertyName("minimum")]
        public long? Minimum { get; init; }
    }

    /// <summary>
    /// Number (floating-point) property.
    /// </summary>
    public sealed record NumberPropertySchema : ElicitationPropertySchema
    {
        /// <summary>
        /// Default value. <c>null</c> means no default was provided.
        /// </summary>
        [JsonPropertyName("default")]
        public double? Default { get; init; }

        /// <summary>
        /// Inclusive upper bound. <c>null</c> means there is none.
        /// </summary>
        [JsonPropertyName("maximum")]
        public double? Maximum { get; init; }

        /// <summary>
        /// Inclusive lower bound. <c>null</c> means there is none.
        /// </summary>
        [JsonPropertyName("minimum")]
        public double? Minimum { get; init; }
    }

    /// <summary>
    /// Boolean property.
    /// </summary>
    public sealed record BooleanPropertySchema : ElicitationPropertySchema
    {
        /// <summary>
        /// Default value. <c>null</c> means no default was provided.
        /// </summary>
        [JsonPropertyName("default")]
        public bool? Default { get; init; }
    }

    /// <summary>
    /// Multi-select array property.
    /// </summary>
    public sealed record MultiSelectPropertySchema : ElicitationPropertySchema
    {
        /// <summary>
        /// Default selected values. <c>null</c> means no default selections were provided.
        /// </summary>
        [JsonPropertyName("default")]
        public List<string>? Default { get; init; }

        /// <summary>
        /// The items definition describing allowed values.
        /// </summary>
        [JsonPropertyName("items")]
        public MultiSelectItems Items { get; init; } = new StringMultiSelectItems();

        /// <summary>
        /// Maximum number of items to select. <c>null</c> means there is no maximum.
        /// </summary>
        [JsonPropertyName("maxItems")]
        public uint? MaxItems { get; init; }

        /// <summary>
        /// Minimum number of items to select. <c>null</c> means there is no minimum.
        /// </summary>
        [JsonPropertyName("minItems")]
        public uint? MinItems { get; init; }
    }

    /// <summary>
    /// Custom or future elicitation property schema type.
    /// </summary>
    /// <remarks>
    /// The spec requires a client that does not understand the type to preserve the raw schema when
    /// storing, replaying, proxying, or forwarding the request, and forbids rendering it as a known input
    /// control. <see cref="RawPayload"/> is the authoritative source for this variant.
    /// </remarks>
    public sealed record CustomPropertySchema : ElicitationPropertySchema
    {
        /// <summary>
        /// The raw <c>type</c> value (a <c>_</c>-prefixed extension or a future ACP variant value).
        /// </summary>
        [JsonPropertyName("type")]
        public string SchemaType { get; init; } = string.Empty;

        /// <summary>
        /// The raw property schema object, preserved verbatim for passthrough.
        /// Read and written by <see cref="ElicitationPropertySchemaJsonConverter"/>, bypassing default
        /// serialization.
        /// </summary>
        [JsonIgnore]
        public JsonElement RawPayload { get; init; }
    }

    internal sealed class ElicitationPropertySchemaJsonConverter : JsonConverter<ElicitationPropertySchema>
    {
        public override ElicitationPropertySchema? Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("Elicitation property schema must be an object.");
            }

            var schemaType = ElicitationSchemaJson.ReadOptionalString(root, "type");
            return schemaType switch
            {
                "string" => ReadString(root),
                "integer" => ReadInteger(root),
                "number" => ReadNumber(root),
                "boolean" => ReadBoolean(root),
                "array" => ReadMultiSelect(root, options),
                // Any other type value (including `_` extensions and future ACP variants) must preserve
                // the raw schema, leaving it to the Agent rather than the client to tighten.
                _ => ReadCustom(root, schemaType)
            };
        }

        public override void Write(
            Utf8JsonWriter writer,
            ElicitationPropertySchema value,
            JsonSerializerOptions options)
        {
            switch (value)
            {
                case StringPropertySchema stringSchema:
                    WriteString(writer, stringSchema);
                    break;
                case IntegerPropertySchema integerSchema:
                    WriteInteger(writer, integerSchema);
                    break;
                case NumberPropertySchema numberSchema:
                    WriteNumber(writer, numberSchema);
                    break;
                case BooleanPropertySchema booleanSchema:
                    WriteBoolean(writer, booleanSchema);
                    break;
                case MultiSelectPropertySchema multiSelectSchema:
                    WriteMultiSelect(writer, multiSelectSchema, options);
                    break;
                case CustomPropertySchema customSchema:
                    ElicitationSchemaJson.WritePassthrough(
                        writer,
                        customSchema.RawPayload,
                        customSchema.SchemaType,
                        customSchema.Meta);
                    break;
                default:
                    throw new JsonException(
                        $"Unsupported elicitation property schema type: {value.GetType().FullName}");
            }
        }

        private static StringPropertySchema ReadString(JsonElement root)
            => new()
            {
                Default = ElicitationSchemaJson.ReadOptionalString(root, "default"),
                Description = ElicitationSchemaJson.ReadOptionalString(root, "description"),
                Enum = ElicitationSchemaJson.ReadOptionalStringArray(root, "enum"),
                Format = ElicitationSchemaJson.ReadOptionalStringFormat(root, "format"),
                MaxLength = ElicitationSchemaJson.ReadOptionalUInt32(root, "maxLength"),
                MinLength = ElicitationSchemaJson.ReadOptionalUInt32(root, "minLength"),
                OneOf = ElicitationSchemaJson.ReadOptionalEnumOptions(root, "oneOf"),
                Pattern = ElicitationSchemaJson.ReadOptionalString(root, "pattern"),
                Title = ElicitationSchemaJson.ReadOptionalString(root, "title"),
                Meta = AcpMetaJson.Read(root)
            };

        private static IntegerPropertySchema ReadInteger(JsonElement root)
            => new()
            {
                Default = ElicitationSchemaJson.ReadOptionalInt64(root, "default"),
                Description = ElicitationSchemaJson.ReadOptionalString(root, "description"),
                Maximum = ElicitationSchemaJson.ReadOptionalInt64(root, "maximum"),
                Minimum = ElicitationSchemaJson.ReadOptionalInt64(root, "minimum"),
                Title = ElicitationSchemaJson.ReadOptionalString(root, "title"),
                Meta = AcpMetaJson.Read(root)
            };

        private static NumberPropertySchema ReadNumber(JsonElement root)
            => new()
            {
                Default = ElicitationSchemaJson.ReadOptionalDouble(root, "default"),
                Description = ElicitationSchemaJson.ReadOptionalString(root, "description"),
                Maximum = ElicitationSchemaJson.ReadOptionalDouble(root, "maximum"),
                Minimum = ElicitationSchemaJson.ReadOptionalDouble(root, "minimum"),
                Title = ElicitationSchemaJson.ReadOptionalString(root, "title"),
                Meta = AcpMetaJson.Read(root)
            };

        private static BooleanPropertySchema ReadBoolean(JsonElement root)
            => new()
            {
                Default = ElicitationSchemaJson.ReadOptionalBoolean(root, "default"),
                Description = ElicitationSchemaJson.ReadOptionalString(root, "description"),
                Title = ElicitationSchemaJson.ReadOptionalString(root, "title"),
                Meta = AcpMetaJson.Read(root)
            };

        private static MultiSelectPropertySchema ReadMultiSelect(JsonElement root, JsonSerializerOptions options)
            => new()
            {
                Default = ElicitationSchemaJson.ReadOptionalStringArray(root, "default"),
                Description = ElicitationSchemaJson.ReadOptionalString(root, "description"),
                Items = ElicitationSchemaJson.ReadRequiredMultiSelectItems(root, "items", options),
                MaxItems = ElicitationSchemaJson.ReadOptionalUInt32(root, "maxItems"),
                MinItems = ElicitationSchemaJson.ReadOptionalUInt32(root, "minItems"),
                Title = ElicitationSchemaJson.ReadOptionalString(root, "title"),
                Meta = AcpMetaJson.Read(root)
            };

        private static CustomPropertySchema ReadCustom(JsonElement root, string? schemaType)
            => new()
            {
                SchemaType = schemaType ?? string.Empty,
                RawPayload = root.Clone(),
                Description = ElicitationSchemaJson.ReadOptionalString(root, "description"),
                Title = ElicitationSchemaJson.ReadOptionalString(root, "title"),
                Meta = AcpMetaJson.Read(root)
            };

        private static void WriteString(Utf8JsonWriter writer, StringPropertySchema schema)
        {
            writer.WriteStartObject();
            writer.WriteString("type", "string");
            ElicitationSchemaJson.WriteOptionalString(writer, "default", schema.Default);
            ElicitationSchemaJson.WriteOptionalString(writer, "description", schema.Description);
            ElicitationSchemaJson.WriteOptionalStringArray(writer, "enum", schema.Enum);
            if (schema.Format.HasValue)
            {
                writer.WriteString("format", schema.Format.Value.Value);
            }

            ElicitationSchemaJson.WriteOptionalUInt32(writer, "maxLength", schema.MaxLength);
            ElicitationSchemaJson.WriteOptionalUInt32(writer, "minLength", schema.MinLength);
            ElicitationSchemaJson.WriteOptionalEnumOptions(writer, "oneOf", schema.OneOf);
            ElicitationSchemaJson.WriteOptionalString(writer, "pattern", schema.Pattern);
            ElicitationSchemaJson.WriteOptionalString(writer, "title", schema.Title);
            AcpMetaJson.Write(writer, schema.Meta);
            writer.WriteEndObject();
        }

        private static void WriteInteger(Utf8JsonWriter writer, IntegerPropertySchema schema)
        {
            writer.WriteStartObject();
            writer.WriteString("type", "integer");
            ElicitationSchemaJson.WriteOptionalInt64(writer, "default", schema.Default);
            ElicitationSchemaJson.WriteOptionalString(writer, "description", schema.Description);
            ElicitationSchemaJson.WriteOptionalInt64(writer, "maximum", schema.Maximum);
            ElicitationSchemaJson.WriteOptionalInt64(writer, "minimum", schema.Minimum);
            ElicitationSchemaJson.WriteOptionalString(writer, "title", schema.Title);
            AcpMetaJson.Write(writer, schema.Meta);
            writer.WriteEndObject();
        }

        private static void WriteNumber(Utf8JsonWriter writer, NumberPropertySchema schema)
        {
            writer.WriteStartObject();
            writer.WriteString("type", "number");
            ElicitationSchemaJson.WriteOptionalDouble(writer, "default", schema.Default);
            ElicitationSchemaJson.WriteOptionalString(writer, "description", schema.Description);
            ElicitationSchemaJson.WriteOptionalDouble(writer, "maximum", schema.Maximum);
            ElicitationSchemaJson.WriteOptionalDouble(writer, "minimum", schema.Minimum);
            ElicitationSchemaJson.WriteOptionalString(writer, "title", schema.Title);
            AcpMetaJson.Write(writer, schema.Meta);
            writer.WriteEndObject();
        }

        private static void WriteBoolean(Utf8JsonWriter writer, BooleanPropertySchema schema)
        {
            writer.WriteStartObject();
            writer.WriteString("type", "boolean");
            if (schema.Default.HasValue)
            {
                writer.WriteBoolean("default", schema.Default.Value);
            }

            ElicitationSchemaJson.WriteOptionalString(writer, "description", schema.Description);
            ElicitationSchemaJson.WriteOptionalString(writer, "title", schema.Title);
            AcpMetaJson.Write(writer, schema.Meta);
            writer.WriteEndObject();
        }

        private static void WriteMultiSelect(Utf8JsonWriter writer, MultiSelectPropertySchema schema, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("type", "array");
            ElicitationSchemaJson.WriteOptionalStringArray(writer, "default", schema.Default);
            ElicitationSchemaJson.WriteOptionalString(writer, "description", schema.Description);
            writer.WritePropertyName("items");
            ElicitationSchemaJson.WriteMultiSelectItems(writer, schema.Items, options);
            ElicitationSchemaJson.WriteOptionalUInt32(writer, "maxItems", schema.MaxItems);
            ElicitationSchemaJson.WriteOptionalUInt32(writer, "minItems", schema.MinItems);
            ElicitationSchemaJson.WriteOptionalString(writer, "title", schema.Title);
            AcpMetaJson.Write(writer, schema.Meta);
            writer.WriteEndObject();
        }
    }
}
