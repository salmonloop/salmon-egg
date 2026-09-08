using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SalmonEgg.Acp.Protocol;

internal sealed class IconJsonConverter : JsonConverter<Icon>
{
    public override Icon? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("src", out var source) || source.ValueKind != JsonValueKind.String)
        {
            throw new JsonException("Icon requires string 'src'.");
        }

        List<string>? sizes = null;
        if (root.TryGetProperty("sizes", out var rawSizes) && rawSizes.ValueKind == JsonValueKind.Array)
        {
            sizes = new List<string>();
            foreach (var size in rawSizes.EnumerateArray())
            {
                if (size.ValueKind == JsonValueKind.String) sizes.Add(size.GetString()!);
            }
        }

        return new Icon
        {
            Src = source.GetString()!,
            MimeType = ReadOptionalString(root, "mimeType"),
            Theme = ReadOptionalString(root, "theme"),
            Sizes = sizes,
            RawPayload = root.Clone()
        };
    }

    public override void Write(Utf8JsonWriter writer, Icon value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("src", value.Src);
        if (value.MimeType is not null) writer.WriteString("mimeType", value.MimeType);
        if (value.Theme is not null) writer.WriteString("theme", value.Theme);
        if (value.Sizes is not null)
        {
            writer.WritePropertyName("sizes");
            writer.WriteStartArray();
            foreach (var size in value.Sizes) writer.WriteStringValue(size);
            writer.WriteEndArray();
        }

        if (value.RawPayload is { } root)
        {
            foreach (var property in root.EnumerateObject())
            {
                if (property.Name is not "src" and not "mimeType" and not "sizes" and not "theme")
                {
                    writer.WritePropertyName(property.Name);
                    writer.WriteRawValue(property.Value.GetRawText());
                }
            }
        }

        writer.WriteEndObject();
    }

    private static string? ReadOptionalString(JsonElement root, string name)
        => root.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;
}
