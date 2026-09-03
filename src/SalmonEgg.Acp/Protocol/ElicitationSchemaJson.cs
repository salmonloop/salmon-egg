using System.Collections.Generic;
using System.Text.Json;
using SalmonEgg.Acp.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace SalmonEgg.Acp.Protocol
{
    /// <summary>
    /// Shared JSON read/write helpers for the elicitation schema unions.
    /// </summary>
    /// <remarks>
    /// Optional fields follow the ACP looseness contract: an omitted or <c>null</c> field reads back as
    /// "not provided" rather than throwing. The type contract is not relaxed, though — a field that is
    /// present with the wrong JSON type still throws, so an Agent sending a malformed schema is not
    /// silently tolerated.
    /// </remarks>
    internal static class ElicitationSchemaJson
    {
        internal static string? ReadOptionalString(JsonElement root, string propertyName)
        {
            if (!TryGetPresent(root, propertyName, out var value))
            {
                return null;
            }

            if (value.ValueKind != JsonValueKind.String)
            {
                throw new JsonException($"Elicitation '{propertyName}' must be a string.");
            }

            return value.GetString();
        }

        internal static bool? ReadOptionalBoolean(JsonElement root, string propertyName)
        {
            if (!TryGetPresent(root, propertyName, out var value))
            {
                return null;
            }

            return value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => throw new JsonException($"Elicitation '{propertyName}' must be a boolean.")
            };
        }

        internal static long? ReadOptionalInt64(JsonElement root, string propertyName)
        {
            if (!TryGetPresent(root, propertyName, out var value))
            {
                return null;
            }

            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var number))
            {
                throw new JsonException($"Elicitation '{propertyName}' must be an integer.");
            }

            return number;
        }

        internal static double? ReadOptionalDouble(JsonElement root, string propertyName)
        {
            if (!TryGetPresent(root, propertyName, out var value))
            {
                return null;
            }

            if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number))
            {
                throw new JsonException($"Elicitation '{propertyName}' must be a number.");
            }

            return number;
        }

        internal static uint? ReadOptionalUInt32(JsonElement root, string propertyName)
        {
            if (!TryGetPresent(root, propertyName, out var value))
            {
                return null;
            }

            if (value.ValueKind != JsonValueKind.Number || !value.TryGetUInt32(out var number))
            {
                throw new JsonException($"Elicitation '{propertyName}' must be a non-negative integer.");
            }

            return number;
        }

        internal static StringFormat? ReadOptionalStringFormat(JsonElement root, string propertyName)
        {
            var raw = ReadOptionalString(root, propertyName);
            return raw is null ? null : new StringFormat(raw);
        }

        internal static List<string>? ReadOptionalStringArray(JsonElement root, string propertyName)
        {
            if (!TryGetPresent(root, propertyName, out var value))
            {
                return null;
            }

            return ReadStringArray(value, propertyName);
        }

        internal static List<string> ReadRequiredStringArray(JsonElement root, string propertyName)
        {
            if (!TryGetPresent(root, propertyName, out var value))
            {
                throw new JsonException($"Elicitation schema is missing required '{propertyName}'.");
            }

            return ReadStringArray(value, propertyName);
        }

        internal static List<EnumOption>? ReadOptionalEnumOptions(JsonElement root, string propertyName)
        {
            if (!TryGetPresent(root, propertyName, out var value))
            {
                return null;
            }

            return ReadEnumOptions(value);
        }

        internal static List<EnumOption> ReadEnumOptions(JsonElement value)
        {
            if (value.ValueKind != JsonValueKind.Array)
            {
                throw new JsonException("Elicitation enum options must be an array.");
            }

            var options = new List<EnumOption>();
            foreach (var item in value.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    throw new JsonException("Elicitation enum option must be an object.");
                }

                options.Add(new EnumOption
                {
                    Const = ReadRequiredString(item, "const"),
                    Title = ReadRequiredString(item, "title"),
                    Description = ReadOptionalString(item, "description"),
                    Meta = AcpMetaJson.Read(item)
                });
            }

            return options;
        }

        internal static MultiSelectItems ReadRequiredMultiSelectItems(JsonElement root, string propertyName, JsonSerializerOptions options)
        {
            if (!TryGetPresent(root, propertyName, out var value))
            {
                throw new JsonException($"Elicitation schema is missing required '{propertyName}'.");
            }

            var items = value.Deserialize((JsonTypeInfo<MultiSelectItems>)options.GetTypeInfo(typeof(MultiSelectItems)));
            return items ?? throw new JsonException($"Elicitation '{propertyName}' must be an object.");
        }

        internal static void WriteMultiSelectItems(Utf8JsonWriter writer, MultiSelectItems items, JsonSerializerOptions options)
            => JsonSerializer.Serialize(writer, items, (JsonTypeInfo<MultiSelectItems>)options.GetTypeInfo(typeof(MultiSelectItems)));

        internal static void WriteOptionalString(Utf8JsonWriter writer, string propertyName, string? value)
        {
            if (value is null)
            {
                return;
            }

            writer.WriteString(propertyName, value);
        }

        internal static void WriteOptionalInt64(Utf8JsonWriter writer, string propertyName, long? value)
        {
            if (!value.HasValue)
            {
                return;
            }

            writer.WriteNumber(propertyName, value.Value);
        }

        internal static void WriteOptionalDouble(Utf8JsonWriter writer, string propertyName, double? value)
        {
            if (!value.HasValue)
            {
                return;
            }

            writer.WriteNumber(propertyName, value.Value);
        }

        internal static void WriteOptionalUInt32(Utf8JsonWriter writer, string propertyName, uint? value)
        {
            if (!value.HasValue)
            {
                return;
            }

            writer.WriteNumber(propertyName, value.Value);
        }

        internal static void WriteStringArray(Utf8JsonWriter writer, string propertyName, List<string> values)
        {
            writer.WritePropertyName(propertyName);
            writer.WriteStartArray();
            for (var index = 0; index < values.Count; index++)
            {
                writer.WriteStringValue(values[index]);
            }

            writer.WriteEndArray();
        }

        internal static void WriteOptionalStringArray(
            Utf8JsonWriter writer,
            string propertyName,
            List<string>? values)
        {
            if (values is null)
            {
                return;
            }

            WriteStringArray(writer, propertyName, values);
        }

        internal static void WriteEnumOptions(
            Utf8JsonWriter writer,
            string propertyName,
            List<EnumOption> options)
        {
            writer.WritePropertyName(propertyName);
            writer.WriteStartArray();
            for (var index = 0; index < options.Count; index++)
            {
                var option = options[index];
                writer.WriteStartObject();
                writer.WriteString("const", option.Const);
                writer.WriteString("title", option.Title);
                WriteOptionalString(writer, "description", option.Description);
                AcpMetaJson.Write(writer, option.Meta);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        internal static void WriteOptionalEnumOptions(
            Utf8JsonWriter writer,
            string propertyName,
            List<EnumOption>? options)
        {
            if (options is null)
            {
                return;
            }

            WriteEnumOptions(writer, propertyName, options);
        }

        /// <summary>
        /// Writes an unknown-discriminator variant back verbatim.
        /// </summary>
        /// <remarks>
        /// <c>WriteRawValue(GetRawText())</c> rather than <c>WriteTo</c>: consistent with
        /// <see cref="AcpMetaJson"/>, this avoids re-encoding escape sequences and numeric token shapes,
        /// so the payload survives byte-for-byte. The raw payload is the single authoritative source for
        /// the variant (including its <c>_meta</c>), so the parsed <c>_meta</c> is not written on top of
        /// it, which would create a second state owner. A hand-constructed value with no raw payload falls
        /// back to a minimal write that still carries the original discriminator.
        /// </remarks>
        internal static void WritePassthrough(
            Utf8JsonWriter writer,
            JsonElement rawPayload,
            string discriminator,
            Dictionary<string, object?>? meta)
        {
            if (rawPayload.ValueKind == JsonValueKind.Object)
            {
                writer.WriteRawValue(rawPayload.GetRawText());
                return;
            }

            writer.WriteStartObject();
            writer.WriteString("type", discriminator);
            AcpMetaJson.Write(writer, meta);
            writer.WriteEndObject();
        }

        private static List<string> ReadStringArray(JsonElement value, string propertyName)
        {
            if (value.ValueKind != JsonValueKind.Array)
            {
                throw new JsonException($"Elicitation '{propertyName}' must be an array of strings.");
            }

            var values = new List<string>();
            foreach (var item in value.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                {
                    throw new JsonException($"Elicitation '{propertyName}' must be an array of strings.");
                }

                values.Add(item.GetString()!);
            }

            return values;
        }

        private static string ReadRequiredString(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
            {
                throw new JsonException($"Elicitation schema is missing required '{propertyName}'.");
            }

            return value.GetString()!;
        }

        private static bool TryGetPresent(JsonElement root, string propertyName, out JsonElement value)
        {
            if (!root.TryGetProperty(propertyName, out value) || value.ValueKind == JsonValueKind.Null)
            {
                value = default;
                return false;
            }

            return true;
        }
    }
}
