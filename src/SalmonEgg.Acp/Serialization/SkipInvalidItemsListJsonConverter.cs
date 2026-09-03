using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace SalmonEgg.Acp.Serialization;

/// <summary>
/// Reads a JSON array element by element, dropping any element that cannot be deserialized.
/// </summary>
/// <remarks>
/// <para>
/// Implements <c>x-deserialize-skip-invalid-items</c>, which the upstream ACP schema puts on array
/// fields such as <c>Plan.entries</c>, <c>ToolCallUpdate.content</c> and <c>ToolCallUpdate.locations</c>.
/// The marker says one malformed element must not take the rest of the array - or the message that
/// carries it - down with it, so a single bad tool-call content chunk cannot discard an entire
/// <c>session/update</c>. See <c>src/SalmonEgg.Acp/SchemaTolerance.Fields.txt</c> for the full list and
/// AGENTS.md "protocol looseness must not be tightened in reverse" for why this is not optional.
/// </para>
/// <para>
/// Skipping requires buffering: an element whose own converter throws leaves the reader parked partway
/// through that element, so there is no way to resume from it. Parsing the array into a document first
/// gives each element an independent read that can fail in isolation. Apply per property rather than per
/// type, because the marker belongs to the field: the same element type read at an unmarked field must
/// still fail loudly.
/// </para>
/// </remarks>
/// <typeparam name="T">The element type, which must be registered on the serialization context.</typeparam>
internal sealed class SkipInvalidItemsListJsonConverter<T> : JsonConverter<List<T>?>
    where T : class
{
    public override List<T>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            // Paired x-deserialize-default-on-error: null at this field degrades to "not provided"
            // instead of failing the whole message. Some arrays (e.g. PlanUpdate.entries) are [JsonRequired]
            // so they reject a missing key, but must still tolerate a present-but-null one.
            reader.Skip();
            return null;
        }

        if (reader.TokenType != JsonTokenType.StartArray)
        {
            // Paired x-deserialize-default-on-error: a value that is not an array at all degrades to
            // "not provided" instead of failing the enclosing message.
            reader.Skip();
            return null;
        }

        using var document = JsonDocument.ParseValue(ref reader);
        var elementInfo = (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T));
        var items = new List<T>();

        foreach (var element in document.RootElement.EnumerateArray())
        {
            // skip-invalid-items: null elements and malformed elements both drop silently.
            if (element.ValueKind == JsonValueKind.Null)
            {
                continue;
            }

            T? item;
            try
            {
                item = element.Deserialize(elementInfo);
            }
            catch (JsonException)
            {
                // skip-invalid-items: drop this element, keep reading the rest.
                continue;
            }

            if (item is not null)
            {
                items.Add(item);
            }
        }

        return items;
    }

    public override void Write(Utf8JsonWriter writer, List<T>? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        var elementInfo = (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T));
        writer.WriteStartArray();
        foreach (var item in value)
        {
            JsonSerializer.Serialize(writer, item, elementInfo);
        }

        writer.WriteEndArray();
    }
}
