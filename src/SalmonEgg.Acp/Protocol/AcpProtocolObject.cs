using System.Collections;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SalmonEgg.Acp.Protocol;

/// <summary>
/// Base contract for ACP specification types that support protocol extension metadata.
/// </summary>
public abstract class AcpProtocolObject
{
    [JsonPropertyName("_meta")]
    public Dictionary<string, object?>? Meta { get; set; }
}

internal static class AcpMetaJson
{
    public static Dictionary<string, object?>? Read(JsonElement root)
    {
        if (!root.TryGetProperty("_meta", out var metaElement)
            || metaElement.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (metaElement.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("ACP '_meta' must be an object or null.");
        }

        var meta = new Dictionary<string, object?>();
        foreach (var property in metaElement.EnumerateObject())
        {
            meta[property.Name] = property.Value.Clone();
        }

        return meta;
    }

    public static Dictionary<string, object?>? ReadValue(ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("ACP '_meta' must be an object or null.");
        }

        using var document = JsonDocument.ParseValue(ref reader);
        var meta = new Dictionary<string, object?>();
        foreach (var property in document.RootElement.EnumerateObject())
        {
            meta[property.Name] = property.Value.Clone();
        }

        return meta;
    }

    public static void Write(Utf8JsonWriter writer, Dictionary<string, object?>? meta)
    {
        if (meta == null)
        {
            return;
        }

        writer.WritePropertyName("_meta");
        WriteObject(writer, meta);
    }

    public static Dictionary<string, object?>? Clone(Dictionary<string, object?>? meta)
    {
        if (meta == null)
        {
            return null;
        }

        var clone = new Dictionary<string, object?>(meta.Comparer);
        foreach (var item in meta)
        {
            clone[item.Key] = CloneValue(item.Value);
        }

        return clone;
    }

    private static object? CloneValue(object? value)
    {
        switch (value)
        {
            case null:
            case string:
            case bool:
            case byte:
            case sbyte:
            case short:
            case ushort:
            case int:
            case uint:
            case long:
            case ulong:
            case float:
            case double:
            case decimal:
                return value;
            case JsonElement element:
                return element.Clone();
            case JsonDocument document:
                return document.RootElement.Clone();
            case IReadOnlyDictionary<string, object?> readOnlyDictionary:
                return CloneReadOnlyDictionary(readOnlyDictionary);
            case IDictionary dictionary:
                return CloneDictionary(dictionary);
            case IEnumerable values:
                return CloneArray(values);
            default:
                throw new JsonException($"Unsupported ACP '_meta' value type: {value.GetType().FullName}");
        }
    }

    private static Dictionary<string, object?> CloneDictionary(IDictionary dictionary)
    {
        var clone = new Dictionary<string, object?>();
        foreach (DictionaryEntry item in dictionary)
        {
            if (item.Key is not string key)
            {
                throw new JsonException("ACP '_meta' object keys must be strings.");
            }

            clone[key] = CloneValue(item.Value);
        }

        return clone;
    }

    private static Dictionary<string, object?> CloneReadOnlyDictionary(
        IReadOnlyDictionary<string, object?> dictionary)
    {
        var clone = new Dictionary<string, object?>();
        foreach (var item in dictionary)
        {
            clone[item.Key] = CloneValue(item.Value);
        }

        return clone;
    }

    private static List<object?> CloneArray(IEnumerable values)
    {
        var clone = new List<object?>();
        foreach (var item in values)
        {
            clone.Add(CloneValue(item));
        }

        return clone;
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
                throw new JsonException("ACP '_meta' object keys must be strings.");
            }

            writer.WritePropertyName(key);
            WriteValue(writer, item.Value);
        }

        writer.WriteEndObject();
    }

    private static void WriteObject(
        Utf8JsonWriter writer,
        IEnumerable<KeyValuePair<string, object?>> values)
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
                element.WriteTo(writer);
                break;
            case JsonDocument document:
                document.RootElement.WriteTo(writer);
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
                throw new JsonException($"Unsupported ACP '_meta' value type: {value.GetType().FullName}");
        }
    }
}
