using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SalmonEgg.Acp.Protocol;

/// <summary>
/// ACP Session Config Options types.
/// https://agentclientprotocol.com/protocol/session-config-options
/// </summary>
[JsonConverter(typeof(ConfigOptionJsonConverter))]
public sealed record ConfigOption : AcpProtocolObject
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("category")]
    public string? Category { get; init; }

    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonIgnore]
    public string? CurrentValue { get; init; }

    [JsonIgnore]
    public bool? CurrentBooleanValue { get; init; }

    [JsonIgnore]
    public List<ConfigOptionValue> Options { get; init; } = new();

    [JsonIgnore]
    public List<ConfigOptionGroup> OptionGroups { get; init; } = new();
}

public sealed record ConfigOptionValue : AcpProtocolObject
{
    [JsonPropertyName("value")]
    public string Value { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; init; }
}

public sealed record ConfigOptionGroup : AcpProtocolObject
{
    [JsonPropertyName("group")]
    public string Group { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("options")]
    public List<ConfigOptionValue> Options { get; init; } = new();
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

        var type = ReadRequiredString(root, "type");
        string? currentValueText = null;
        bool? currentBoolean = null;
        var selectOptions = new List<ConfigOptionValue>();
        var optionGroups = new List<ConfigOptionGroup>();

        if (string.Equals(type, "select", System.StringComparison.Ordinal))
        {
            currentValueText = ReadRequiredString(root, "currentValue");
            ReadSelectOptions(root, selectOptions, optionGroups);
        }
        else if (string.Equals(type, "boolean", System.StringComparison.Ordinal))
        {
            if (!root.TryGetProperty("currentValue", out var currentValue)
                || currentValue.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
            {
                throw new JsonException("ACP boolean session config option requires a boolean currentValue.");
            }

            currentBoolean = currentValue.GetBoolean();
        }

        return new ConfigOption
        {
            // v1 calls this id; v2 renamed it configId. Reading accepts both versions, while writing
            // branches on the negotiated context so neither peer sees the other's field.
            Id = ReadRequiredString(root, "configId", "id"),
            Name = ReadRequiredString(root, "name"),
            Description = ReadOptionalString(root, "description"),
            Category = ReadOptionalString(root, "category"),
            Type = type,
            CurrentValue = currentValueText,
            CurrentBooleanValue = currentBoolean,
            Options = selectOptions,
            OptionGroups = optionGroups,
            Meta = AcpMetaJson.Read(root)
        };
    }

    public override void Write(Utf8JsonWriter writer, ConfigOption value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString(
            AcpProtocolWriteContext.Current == AcpProtocolVersion.V2 ? "configId" : "id",
            value.Id);
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

    private static void ReadSelectOptions(
        JsonElement root,
        List<ConfigOptionValue> options,
        List<ConfigOptionGroup> optionGroups)
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
                optionGroups.Add(ReadGroup(item));
            }
            else
            {
                options.Add(ReadOption(item));
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

        var groupOptions = new List<ConfigOptionValue>();
        foreach (var item in optionsElement.EnumerateArray())
        {
            groupOptions.Add(ReadOption(item));
        }

        return new ConfigOptionGroup
        {
            Group = ReadRequiredString(element, "group"),
            Name = ReadRequiredString(element, "name"),
            Options = groupOptions,
            Meta = AcpMetaJson.Read(element)
        };
    }

    private static ConfigOptionValue ReadOption(JsonElement element)
        => new()
        {
            Value = ReadRequiredString(element, "value"),
            Name = ReadRequiredString(element, "name"),
            Description = ReadOptionalString(element, "description"),
            Meta = AcpMetaJson.Read(element)
        };

    private static string ReadRequiredString(JsonElement root, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (root.TryGetProperty(propertyName, out var property)
                && property.ValueKind == JsonValueKind.String)
            {
                return property.GetString() ?? string.Empty;
            }
        }

        throw new JsonException($"ACP session config option requires string property '{propertyNames[0]}'.");
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
