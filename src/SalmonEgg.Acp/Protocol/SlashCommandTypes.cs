using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using SalmonEgg.Acp.Serialization;

namespace SalmonEgg.Acp.Protocol;

/// <summary>
/// ACP Slash Commands types.
/// https://agentclientprotocol.com/protocol/slash-commands
/// </summary>
public sealed record AvailableCommandsUpdate : SessionUpdate
{
    [JsonPropertyName("availableCommands")]
    public List<AvailableCommand> AvailableCommands { get; init; } = new();
}

public sealed record AvailableCommand : AcpProtocolObject
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("input")]
    [JsonConverter(typeof(DefaultableAvailableCommandInputJsonConverter))]
    public AvailableCommandInput? Input { get; init; }
}

[JsonConverter(typeof(AvailableCommandInputJsonConverter))]
public sealed record AvailableCommandInput : AcpProtocolObject
{
    [JsonPropertyName("hint")]
    public string Hint { get; init; } = string.Empty;

    internal JsonElement? RawPayload { get; init; }
}

internal sealed class AvailableCommandInputJsonConverter : JsonConverter<AvailableCommandInput>
{
    public override AvailableCommandInput? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Command input must be an object.");
        }

        if (AcpWireFormat.NegotiatedVersion(options) == AcpProtocolVersion.V2)
        {
            if (!root.TryGetProperty("type", out var type))
            {
                throw new JsonException("ACP v2 command input requires 'type'.");
            }

            if (type.ValueKind != JsonValueKind.String)
            {
                throw new JsonException("Command input 'type' must be a string.");
            }

            if (type.GetString() != "text")
            {
                return new AvailableCommandInput { RawPayload = root.Clone() };
            }
        }

        if (!root.TryGetProperty("hint", out var hint) || hint.ValueKind != JsonValueKind.String)
        {
            throw new JsonException("Text command input requires string 'hint'.");
        }

        return new AvailableCommandInput { Hint = hint.GetString()!, Meta = AcpMetaJson.Read(root), RawPayload = root.Clone() };
    }

    public override void Write(Utf8JsonWriter writer, AvailableCommandInput value, JsonSerializerOptions options)
    {
        var isV2 = AcpWireFormat.NegotiatedVersion(options) == AcpProtocolVersion.V2;
        if (isV2 && value.RawPayload is { } raw && raw.TryGetProperty("type", out var type)
            && type.ValueKind == JsonValueKind.String && type.GetString() != "text")
        {
            writer.WriteRawValue(raw.GetRawText());
            return;
        }

        writer.WriteStartObject();
        if (isV2)
        {
            writer.WriteString("type", "text");
        }

        writer.WriteString("hint", value.Hint);
        AcpMetaJson.Write(writer, value.Meta);
        if (value.RawPayload is { } payload)
        {
            foreach (var property in payload.EnumerateObject())
            {
                if (property.Name is not "hint" and not "_meta" && (!isV2 || property.Name != "type"))
                {
                    writer.WritePropertyName(property.Name);
                    writer.WriteRawValue(property.Value.GetRawText());
                }
            }
        }

        writer.WriteEndObject();
    }
}

internal sealed class DefaultableAvailableCommandInputJsonConverter : JsonConverter<AvailableCommandInput>
{
    public override AvailableCommandInput? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        try
        {
            return document.RootElement.Deserialize((JsonTypeInfo<AvailableCommandInput>)options.GetTypeInfo(typeof(AvailableCommandInput)));
        }
        catch (JsonException)
        {
            // AvailableCommand.input explicitly permits default-on-error; the union root does not.
            return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, AvailableCommandInput value, JsonSerializerOptions options)
        => JsonSerializer.Serialize(writer, value, (JsonTypeInfo<AvailableCommandInput>)options.GetTypeInfo(typeof(AvailableCommandInput)));
}
