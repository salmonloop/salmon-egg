using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using SalmonEgg.Acp.Plan;

namespace SalmonEgg.Acp.Protocol;

/// <summary>V2 presence marker for image prompt support; an empty object means supported.</summary>
[Experimental(AcpDraftProtocol.DiagnosticId, Message = AcpDraftProtocol.Message, UrlFormat = AcpDraftProtocol.UrlFormat)]
public sealed record PromptImageCapabilities : AcpProtocolObject;

/// <summary>V2 presence marker for audio prompt support; an empty object means supported.</summary>
[Experimental(AcpDraftProtocol.DiagnosticId, Message = AcpDraftProtocol.Message, UrlFormat = AcpDraftProtocol.UrlFormat)]
public sealed record PromptAudioCapabilities : AcpProtocolObject;

/// <summary>V2 presence marker for embedded-context prompt support; an empty object means supported.</summary>
[Experimental(AcpDraftProtocol.DiagnosticId, Message = AcpDraftProtocol.Message, UrlFormat = AcpDraftProtocol.UrlFormat)]
public sealed record PromptEmbeddedContextCapabilities : AcpProtocolObject;

/// <summary>V2 presence marker for MCP HTTP support; an empty object means supported.</summary>
[Experimental(AcpDraftProtocol.DiagnosticId, Message = AcpDraftProtocol.Message, UrlFormat = AcpDraftProtocol.UrlFormat)]
public sealed record McpHttpCapabilities : AcpProtocolObject;

/// <summary>V2 presence marker that the Client can reproduce terminal-based authentication.</summary>
[Experimental(AcpDraftProtocol.DiagnosticId, Message = AcpDraftProtocol.Message, UrlFormat = AcpDraftProtocol.UrlFormat)]
public sealed record TerminalAuthCapabilities : AcpProtocolObject;

/// <summary>Theme for an <see cref="Icon"/>. Unknown strings remain valid for forward compatibility.</summary>
[Experimental(AcpDraftProtocol.DiagnosticId, Message = AcpDraftProtocol.Message, UrlFormat = AcpDraftProtocol.UrlFormat)]
public static class IconThemeKind
{
    /// <summary>Icon intended for a light surface.</summary>
    public const string Light = "light";

    /// <summary>Icon intended for a dark surface.</summary>
    public const string Dark = "dark";
}

/// <summary>An icon supplied by an Agent in v2 metadata.</summary>
[Experimental(AcpDraftProtocol.DiagnosticId, Message = AcpDraftProtocol.Message, UrlFormat = AcpDraftProtocol.UrlFormat)]
public sealed record Icon
{
    /// <summary>URI of the icon resource. Required.</summary>
    [JsonPropertyName("src")]
    public string Src { get; init; } = string.Empty;

    /// <summary>Media type, when known.</summary>
    [JsonPropertyName("mimeType")]
    public string? MimeType { get; init; }

    /// <summary>Advertised sizes such as <c>48x48</c> or <c>any</c>.</summary>
    [JsonPropertyName("sizes")]
    public List<string>? Sizes { get; init; }

    /// <summary>Preferred display theme, when any.</summary>
    [JsonPropertyName("theme")]
    public string? Theme { get; init; }
}

/// <summary>V2 command input specification for free text after the command name.</summary>
[Experimental(AcpDraftProtocol.DiagnosticId, Message = AcpDraftProtocol.Message, UrlFormat = AcpDraftProtocol.UrlFormat)]
public sealed record TextCommandInput : AcpProtocolObject
{
    /// <summary>Input hint shown to the user. Required.</summary>
    [JsonPropertyName("hint")]
    public string Hint { get; init; } = string.Empty;
}

/// <summary>
/// V2 <c>plan_update</c> content. Today <c>items</c> is the sole known form; unknown forms preserve
/// their payload so new Agent plan representations do not get silently destroyed by a proxy.
/// </summary>
[JsonConverter(typeof(PlanUpdateContentJsonConverter))]
[Experimental(AcpDraftProtocol.DiagnosticId, Message = AcpDraftProtocol.Message, UrlFormat = AcpDraftProtocol.UrlFormat)]
public abstract record PlanUpdateContent : AcpProtocolObject
{
    /// <summary>Raw type discriminator.</summary>
    public abstract string Type { get; }
}

/// <summary>Full replacement list of plan items.</summary>
[Experimental(AcpDraftProtocol.DiagnosticId, Message = AcpDraftProtocol.Message, UrlFormat = AcpDraftProtocol.UrlFormat)]
public sealed record PlanItemsUpdateContent : PlanUpdateContent
{
    /// <inheritdoc />
    public override string Type => "items";

    /// <summary>Agent-owned plan identifier. Required.</summary>
    [JsonPropertyName("planId")]
    public string PlanId { get; init; } = string.Empty;

    /// <summary>Complete replacement list. Required.</summary>
    [JsonPropertyName("entries")]
    public List<PlanEntry> Entries { get; init; } = new();
}

/// <summary>Forward-compatible raw carrier for a plan content type this SDK does not model.</summary>
[Experimental(AcpDraftProtocol.DiagnosticId, Message = AcpDraftProtocol.Message, UrlFormat = AcpDraftProtocol.UrlFormat)]
public sealed record CustomPlanUpdateContent : PlanUpdateContent
{
    private readonly string _type = string.Empty;
    /// <inheritdoc />
    public override string Type => _type;
    /// <summary>Complete raw payload.</summary>
    [JsonIgnore]
    public JsonElement RawPayload { get; init; }
    /// <summary>Creates an empty carrier.</summary>
    public CustomPlanUpdateContent() { }
    /// <summary>Creates a raw carrier.</summary>
    public CustomPlanUpdateContent(string type, JsonElement rawPayload)
    {
        _type = type ?? throw new ArgumentNullException(nameof(type));
        RawPayload = rawPayload;
    }
}

/// <summary>V2 <c>plan_update</c> session update.</summary>
[Experimental(AcpDraftProtocol.DiagnosticId, Message = AcpDraftProtocol.Message, UrlFormat = AcpDraftProtocol.UrlFormat)]
public sealed record V2PlanUpdate : SessionUpdate
{
    /// <summary>Plan content. Required.</summary>
    [JsonPropertyName("plan")]
    public PlanUpdateContent Plan { get; init; } = null!;
}

internal sealed class PlanUpdateContentJsonConverter : JsonConverter<PlanUpdateContent>
{
    public override PlanUpdateContent? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String)
            throw new JsonException("ACP plan update content requires a string 'type'.");
        if (type.GetString() != "items") return new CustomPlanUpdateContent(type.GetString() ?? string.Empty, root.Clone()) { Meta = AcpMetaJson.Read(root) };
        var entries = root.TryGetProperty("entries", out var e) && e.ValueKind == JsonValueKind.Array
            ? JsonSerializer.Deserialize(e.GetRawText(), (JsonTypeInfo<List<PlanEntry>>)options.GetTypeInfo(typeof(List<PlanEntry>))) ?? new()
            : throw new JsonException("ACP items plan update requires entries.");
        return new PlanItemsUpdateContent { PlanId = root.GetProperty("planId").GetString() ?? string.Empty, Entries = entries, Meta = AcpMetaJson.Read(root) };
    }

    public override void Write(Utf8JsonWriter writer, PlanUpdateContent value, JsonSerializerOptions options)
    {
        if (value is CustomPlanUpdateContent custom && custom.RawPayload.ValueKind == JsonValueKind.Object) { custom.RawPayload.WriteTo(writer); return; }
        var items = (PlanItemsUpdateContent)value;
        writer.WriteStartObject(); writer.WriteString("type", items.Type); writer.WriteString("planId", items.PlanId);
        writer.WritePropertyName("entries"); JsonSerializer.Serialize(writer, items.Entries, (JsonTypeInfo<List<PlanEntry>>)options.GetTypeInfo(typeof(List<PlanEntry>)));
        AcpMetaJson.Write(writer, items.Meta); writer.WriteEndObject();
    }
}
