using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using SalmonEgg.Acp.Protocol;

namespace SalmonEgg.Acp.Serialization;

internal sealed class IgnoredProtocolPropertyJsonConverter<T> : JsonConverter<T>
{
    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        reader.Skip();
        return default;
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        => writer.WriteNullValue();
}

internal sealed class DefaultableStringJsonConverter : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return reader.GetString();
        }

        reader.Skip();
        return null;
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        => writer.WriteStringValue(value);
}

internal sealed class DefaultableObjectJsonConverter<T> : JsonConverter<T> where T : class
{
    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return ReadValue(document.RootElement, options);
    }

    internal static T? ReadValue(JsonElement value, JsonSerializerOptions options)
    {
        try
        {
            return value.Deserialize((JsonTypeInfo<T>)options.GetTypeInfo(typeof(T)));
        }
        catch (JsonException)
        {
            // Attach only to properties whose schema explicitly permits default-on-error.
            // Typed root contracts remain strict, and valid sibling properties are retained.
            return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        => JsonSerializer.Serialize(writer, value, (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T)));
}

internal sealed class DefaultableProtocolListJsonConverter<T> : JsonConverter<List<T>> where T : class
{
    public override bool HandleNull => true;

    public override List<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var result = new List<T>();
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        var typeInfo = (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T));
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            try
            {
                if (item.Deserialize(typeInfo) is { } option)
                {
                    result.Add(option);
                }
            }
            catch (JsonException)
            {
                // Attach only to object-array properties with both schema recovery annotations:
                // default-on-error and skip-invalid-items. Standalone roots remain strict.
            }
        }

        return result;
    }

    public override void Write(Utf8JsonWriter writer, List<T> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        if (value is not null)
        {
            var typeInfo = (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T));
            foreach (var option in value)
            {
                JsonSerializer.Serialize(writer, option, typeInfo);
            }
        }

        writer.WriteEndArray();
    }
}
