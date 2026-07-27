using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SalmonEgg.Acp.Protocol;

/// <summary>
/// ACP Session Config Options types.
/// https://agentclientprotocol.com/protocol/session-config-options
/// </summary>
[JsonConverter(typeof(ConfigOptionJsonConverter))]
public record ConfigOption : AcpProtocolObject
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonIgnore]
    public string? CurrentValue { get; set; }

    [JsonIgnore]
    public bool? CurrentBooleanValue { get; set; }

    [JsonIgnore]
    public List<ConfigOptionValue> Options { get; set; } = new();

    [JsonIgnore]
    public List<ConfigOptionGroup> OptionGroups { get; set; } = new();
}

public record ConfigOptionValue : AcpProtocolObject
{
    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

public record ConfigOptionGroup : AcpProtocolObject
{
    [JsonPropertyName("group")]
    public string Group { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("options")]
    public List<ConfigOptionValue> Options { get; set; } = new();
}

internal sealed class ConfigOptionJsonConverter : JsonConverter<ConfigOption>
{
    public override ConfigOption? Read(ref Utf8JsonReader reader, System.Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("ACP session config option must be an object.");
        }

        var result = new ConfigOption
        {
            Id = ReadRequiredString(root, "id"),
            Name = ReadRequiredString(root, "name"),
            Description = ReadOptionalString(root, "description"),
            Category = ReadOptionalString(root, "category"),
            Type = ReadRequiredString(root, "type"),
            Meta = AcpMetaJson.Read(root)
        };

        if (string.Equals(result.Type, "select", System.StringComparison.Ordinal))
        {
            result.CurrentValue = ReadRequiredString(root, "currentValue");
            ReadSelectOptions(root, result);
        }
        else if (string.Equals(result.Type, "boolean", System.StringComparison.Ordinal))
        {
            if (!root.TryGetProperty("currentValue", out var currentValue)
                || currentValue.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
            {
                throw new JsonException("ACP boolean session config option requires a boolean currentValue.");
            }

            result.CurrentBooleanValue = currentValue.GetBoolean();
        }

        return result;
    }

    public override void Write(Utf8JsonWriter writer, ConfigOption value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("id", value.Id);
        writer.WriteString("name", value.Name);
        WriteOptionalString(writer, "description", value.Description, options);
        WriteOptionalString(writer, "category", value.Category, options);
        writer.WriteString("type", value.Type);

        if (string.Equals(value.Type, "select", System.StringComparison.Ordinal))
        {
            writer.WriteString("currentValue", value.CurrentValue);
            writer.WritePropertyName("options");
            if (value.OptionGroups.Count > 0)
            {
                if (value.Options.Count > 0)
                {
                    throw new JsonException("ACP select config options cannot mix grouped and ungrouped values.");
                }

                WriteGroups(writer, value.OptionGroups, options);
            }
            else
            {
                WriteOptions(writer, value.Options, options);
            }
        }
        else if (string.Equals(value.Type, "boolean", System.StringComparison.Ordinal))
        {
            if (!value.CurrentBooleanValue.HasValue)
            {
                throw new JsonException("ACP boolean session config option requires a current value.");
            }

            writer.WriteBoolean("currentValue", value.CurrentBooleanValue.Value);
        }
        else
        {
            throw new JsonException($"Unsupported ACP session config option type '{value.Type}'.");
        }

        AcpMetaJson.Write(writer, value.Meta);
        writer.WriteEndObject();
    }

    private static void ReadSelectOptions(JsonElement root, ConfigOption result)
    {
        if (!root.TryGetProperty("options", out var optionsElement)
            || optionsElement.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("ACP select session config option requires an options array.");
        }

        bool? grouped = null;
        foreach (var item in optionsElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("ACP select option entries must be objects.");
            }

            var isGroup = item.TryGetProperty("group", out _);
            if (grouped.HasValue && grouped.Value != isGroup)
            {
                throw new JsonException("ACP select config options cannot mix grouped and ungrouped values.");
            }

            grouped = isGroup;
            if (isGroup)
            {
                result.OptionGroups.Add(ReadGroup(item));
            }
            else
            {
                result.Options.Add(ReadOption(item));
            }
        }
    }

    private static ConfigOptionGroup ReadGroup(JsonElement element)
    {
        if (!element.TryGetProperty("options", out var optionsElement)
            || optionsElement.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("ACP session config option group requires an options array.");
        }

        var group = new ConfigOptionGroup
        {
            Group = ReadRequiredString(element, "group"),
            Name = ReadRequiredString(element, "name"),
            Meta = AcpMetaJson.Read(element)
        };
        foreach (var item in optionsElement.EnumerateArray())
        {
            group.Options.Add(ReadOption(item));
        }

        return group;
    }

    private static ConfigOptionValue ReadOption(JsonElement element)
        => new()
        {
            Value = ReadRequiredString(element, "value"),
            Name = ReadRequiredString(element, "name"),
            Description = ReadOptionalString(element, "description"),
            Meta = AcpMetaJson.Read(element)
        };

    private static string ReadRequiredString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String)
        {
            throw new JsonException($"ACP session config option requires string property '{propertyName}'.");
        }

        return property.GetString() ?? string.Empty;
    }

    private static string? ReadOptionalString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : throw new JsonException($"ACP session config option property '{propertyName}' must be a string or null.");
    }

    private static void WriteGroups(
        Utf8JsonWriter writer,
        IReadOnlyList<ConfigOptionGroup> groups,
        JsonSerializerOptions serializerOptions)
    {
        writer.WriteStartArray();
        foreach (var group in groups)
        {
            writer.WriteStartObject();
            writer.WriteString("group", group.Group);
            writer.WriteString("name", group.Name);
            writer.WritePropertyName("options");
            WriteOptions(writer, group.Options, serializerOptions);
            AcpMetaJson.Write(writer, group.Meta);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteOptions(
        Utf8JsonWriter writer,
        IReadOnlyList<ConfigOptionValue> configOptions,
        JsonSerializerOptions serializerOptions)
    {
        writer.WriteStartArray();
        foreach (var option in configOptions)
        {
            writer.WriteStartObject();
            writer.WriteString("value", option.Value);
            writer.WriteString("name", option.Name);
            WriteOptionalString(writer, "description", option.Description, serializerOptions);
            AcpMetaJson.Write(writer, option.Meta);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteOptionalString(
        Utf8JsonWriter writer,
        string propertyName,
        string? value,
        JsonSerializerOptions options)
    {
        if (value != null)
        {
            writer.WriteString(propertyName, value);
        }
        else if (options.DefaultIgnoreCondition is not JsonIgnoreCondition.WhenWritingNull
                 and not JsonIgnoreCondition.WhenWritingDefault)
        {
            writer.WriteNull(propertyName);
        }
    }
}
