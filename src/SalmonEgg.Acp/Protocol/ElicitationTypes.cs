using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using SalmonEgg.Acp.Serialization;

namespace SalmonEgg.Acp.Protocol
{
    /// <summary>
    /// ACP method names for the elicitation family.
    /// </summary>
    public static class ElicitationMethods
    {
        /// <summary>
        /// Agent-to-client request asking for structured user input.
        /// </summary>
        public const string Create = "elicitation/create";

        /// <summary>
        /// Agent-to-client notification that a URL-based elicitation finished out of band.
        /// </summary>
        public const string Complete = "elicitation/complete";
    }

    /// <summary>
    /// Elicitation mode discriminator values.
    /// </summary>
    public static class ElicitationModes
    {
        /// <summary>
        /// Form mode: the client renders a form from the requested schema.
        /// </summary>
        public const string Form = "form";

        /// <summary>
        /// URL mode: the client directs the user to a URL after obtaining consent.
        /// </summary>
        public const string Url = "url";
    }

    /// <summary>
    /// Elicitation response action discriminator values.
    /// </summary>
    public static class ElicitationActions
    {
        /// <summary>
        /// The user submitted the form, or consented to opening the URL.
        /// </summary>
        public const string Accept = "accept";

        /// <summary>
        /// The user explicitly declined.
        /// </summary>
        public const string Decline = "decline";

        /// <summary>
        /// The user dismissed the interaction without choosing.
        /// </summary>
        public const string Cancel = "cancel";
    }

    /// <summary>
    /// The JSON Schema object describing the fields of a form-mode elicitation.
    /// </summary>
    /// <remarks>
    /// Always an object schema whose properties are primitive-typed, as the elicitation specification
    /// requires.
    /// </remarks>
    [JsonConverter(typeof(ElicitationSchemaJsonConverter))]
    public sealed record ElicitationSchema : AcpProtocolObject
    {
        /// <summary>
        /// Type discriminator. Always <c>"object"</c>.
        /// </summary>
        [JsonPropertyName("type")]
        public string Type { get; init; } = "object";

        /// <summary>
        /// Property definitions, keyed by field name. Insertion order is preserved so the client renders
        /// the fields in the order the Agent declared them.
        /// </summary>
        [JsonPropertyName("properties")]
        public Dictionary<string, ElicitationPropertySchema> Properties { get; init; } =
            new(StringComparer.Ordinal);

        /// <summary>
        /// Required property names. <c>null</c> means no property is required.
        /// </summary>
        [JsonPropertyName("required")]
        public List<string>? Required { get; init; }

        /// <summary>
        /// Optional description of what this schema represents.
        /// </summary>
        [JsonPropertyName("description")]
        public string? Description { get; init; }

        /// <summary>
        /// Optional title for the schema.
        /// </summary>
        [JsonPropertyName("title")]
        public string? Title { get; init; }
    }

    internal sealed class ElicitationSchemaJsonConverter : JsonConverter<ElicitationSchema>
    {
        public override ElicitationSchema? Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("Elicitation requestedSchema must be an object.");
            }

            return new ElicitationSchema
            {
                // The schema defaults type to "object"; an omitted value is restored to that default
                // rather than rejected.
                Type = ElicitationSchemaJson.ReadOptionalString(root, "type") ?? "object",
                Properties = ReadProperties(root),
                Required = ElicitationSchemaJson.ReadOptionalStringArray(root, "required"),
                Description = ElicitationSchemaJson.ReadOptionalString(root, "description"),
                Title = ElicitationSchemaJson.ReadOptionalString(root, "title"),
                Meta = AcpMetaJson.Read(root)
            };
        }

        public override void Write(Utf8JsonWriter writer, ElicitationSchema value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("type", value.Type);
            writer.WritePropertyName("properties");
            writer.WriteStartObject();
            foreach (var property in value.Properties)
            {
                writer.WritePropertyName(property.Key);
                JsonSerializer.Serialize(
                    writer,
                    property.Value,
                    AcpJsonContext.Default.ElicitationPropertySchema);
            }

            writer.WriteEndObject();
            ElicitationSchemaJson.WriteOptionalStringArray(writer, "required", value.Required);
            ElicitationSchemaJson.WriteOptionalString(writer, "description", value.Description);
            ElicitationSchemaJson.WriteOptionalString(writer, "title", value.Title);
            AcpMetaJson.Write(writer, value.Meta);
            writer.WriteEndObject();
        }

        private static Dictionary<string, ElicitationPropertySchema> ReadProperties(JsonElement root)
        {
            var properties = new Dictionary<string, ElicitationPropertySchema>(StringComparer.Ordinal);
            if (!root.TryGetProperty("properties", out var propertiesElement)
                || propertiesElement.ValueKind == JsonValueKind.Null)
            {
                // The schema defaults properties to {}, so an omitted value is an empty form rather than
                // an error.
                return properties;
            }

            if (propertiesElement.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("Elicitation schema 'properties' must be an object.");
            }

            foreach (var property in propertiesElement.EnumerateObject())
            {
                var schema = property.Value.Deserialize(AcpJsonContext.Default.ElicitationPropertySchema);
                if (schema is null)
                {
                    throw new JsonException(
                        $"Elicitation schema property '{property.Name}' must be an object.");
                }

                properties[property.Name] = schema;
            }

            return properties;
        }
    }
}
