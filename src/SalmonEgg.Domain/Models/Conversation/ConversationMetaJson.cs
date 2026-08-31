using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.Json;

namespace SalmonEgg.Domain.Models.Conversation;

/// <summary>
/// Domain-owned lossless JSON helpers for conversation session-info <c>meta</c> payloads.
/// Preserves raw token text for <see cref="JsonElement"/> values so conversation persistence
/// does not depend on ACP protocol converters.
/// </summary>
internal static class ConversationMetaJson
{
    public static Dictionary<string, object?>? ReadValue(ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Conversation session-info meta must be an object or null.");
        }

        using var document = JsonDocument.ParseValue(ref reader);
        var meta = new Dictionary<string, object?>();
        foreach (var property in document.RootElement.EnumerateObject())
        {
            meta[property.Name] = property.Value.Clone();
        }

        return meta;
    }

    public static void WriteObject(Utf8JsonWriter writer, IEnumerable<KeyValuePair<string, object?>> values)
    {
        writer.WriteStartObject();
        foreach (var item in values)
        {
            writer.WritePropertyName(item.Key);
            WriteValue(writer, item.Value);
        }

        writer.WriteEndObject();
    }

    private static void WriteValue(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case JsonElement element:
                writer.WriteRawValue(element.GetRawText());
                break;
            case JsonDocument document:
                writer.WriteRawValue(document.RootElement.GetRawText());
                break;
            case string text:
                writer.WriteStringValue(text);
                break;
            case bool flag:
                writer.WriteBooleanValue(flag);
                break;
            case byte number:
                writer.WriteNumberValue(number);
                break;
            case sbyte number:
                writer.WriteNumberValue(number);
                break;
            case short number:
                writer.WriteNumberValue(number);
                break;
            case ushort number:
                writer.WriteNumberValue(number);
                break;
            case int number:
                writer.WriteNumberValue(number);
                break;
            case uint number:
                writer.WriteNumberValue(number);
                break;
            case long number:
                writer.WriteNumberValue(number);
                break;
            case ulong number:
                writer.WriteNumberValue(number);
                break;
            case float number:
                writer.WriteNumberValue(number);
                break;
            case double number:
                writer.WriteNumberValue(number);
                break;
            case decimal number:
                writer.WriteNumberValue(number);
                break;
            case IReadOnlyDictionary<string, object?> readOnlyDictionary:
                WriteObject(writer, readOnlyDictionary);
                break;
            case IDictionary dictionary:
                WriteDictionary(writer, dictionary);
                break;
            case IEnumerable values:
                WriteArray(writer, values);
                break;
            default:
                throw new JsonException($"Unsupported conversation meta value type: {value.GetType().FullName}");
        }
    }

    private static void WriteArray(Utf8JsonWriter writer, IEnumerable values)
    {
        writer.WriteStartArray();
        foreach (var item in values)
        {
            WriteValue(writer, item);
        }

        writer.WriteEndArray();
    }

    private static void WriteDictionary(Utf8JsonWriter writer, IDictionary dictionary)
    {
        writer.WriteStartObject();
        foreach (DictionaryEntry item in dictionary)
        {
            if (item.Key is not string key)
            {
                throw new JsonException("Conversation meta object keys must be strings.");
            }

            writer.WritePropertyName(key);
            WriteValue(writer, item.Value);
        }

        writer.WriteEndObject();
    }
}
