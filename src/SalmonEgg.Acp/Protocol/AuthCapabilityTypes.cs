using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using SalmonEgg.Acp.Serialization;

namespace SalmonEgg.Acp.Protocol;

/// <summary>Opt-in authentication method types the client can execute.</summary>
[JsonConverter(typeof(AuthCapabilitiesJsonConverter))]
public sealed record AuthCapabilities : AcpProtocolObject
{
    /// <summary>
    /// Whether the client can reproduce the configured agent invocation in an interactive terminal.
    /// This is a boolean in v1 and a presence marker in v2. An empty auth object advertises no support.
    /// </summary>
    [JsonPropertyName("terminal")]
    public bool Terminal { get; init; }

    internal JsonElement? RawPayload { get; init; }
}

internal sealed class AuthCapabilitiesJsonConverter : JsonConverter<AuthCapabilities>
{
    public override AuthCapabilities? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("ACP authentication capabilities must be an object.");
        }

        var terminal = root.TryGetProperty("terminal", out var value)
            && (AcpWireFormat.NegotiatedVersion(options) == AcpProtocolVersion.V2
                ? value.ValueKind == JsonValueKind.Object
                : value.ValueKind == JsonValueKind.True);

        return new AuthCapabilities
        {
            Terminal = terminal,
            Meta = root.TryGetProperty("_meta", out var meta) && meta.ValueKind == JsonValueKind.Object
                ? AcpMetaJson.Read(root) : null,
            RawPayload = root.Clone()
        };
    }

    public override void Write(Utf8JsonWriter writer, AuthCapabilities value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        if (AcpWireFormat.NegotiatedVersion(options) == AcpProtocolVersion.V1)
        {
            writer.WriteBoolean("terminal", value.Terminal);
        }
        else if (value.Terminal)
        {
            writer.WritePropertyName("terminal");
            if (value.RawPayload is { } raw && raw.TryGetProperty("terminal", out var terminal)
                && terminal.ValueKind == JsonValueKind.Object)
            {
                writer.WriteRawValue(terminal.GetRawText());
            }
            else
            {
                writer.WriteStartObject();
                writer.WriteEndObject();
            }
        }

        AcpMetaJson.Write(writer, value.Meta);
        if (value.RawPayload is { } payload)
        {
            foreach (var property in payload.EnumerateObject())
            {
                if (property.Name is not "terminal" and not "_meta")
                {
                    writer.WritePropertyName(property.Name);
                    writer.WriteRawValue(property.Value.GetRawText());
                }
            }
        }

        writer.WriteEndObject();
    }
}
