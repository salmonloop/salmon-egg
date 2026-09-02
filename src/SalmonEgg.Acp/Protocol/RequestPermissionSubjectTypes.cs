using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace SalmonEgg.Acp.Protocol;

/// <summary>
/// V2 subject a permission prompt is about. The subject is optional: the prompt title and description
/// describe the request itself, while a subject lets the Client show what operation is affected.
/// </summary>
[JsonConverter(typeof(RequestPermissionSubjectJsonConverter))]
[Experimental(AcpDraftProtocol.DiagnosticId, Message = AcpDraftProtocol.Message, UrlFormat = AcpDraftProtocol.UrlFormat)]
public abstract record RequestPermissionSubject : AcpProtocolObject
{
    /// <summary>The raw <c>type</c> discriminator.</summary>
    public abstract string Type { get; }
}

/// <summary>A permission subject referring to a tool-call upsert.</summary>
[Experimental(AcpDraftProtocol.DiagnosticId, Message = AcpDraftProtocol.Message, UrlFormat = AcpDraftProtocol.UrlFormat)]
public sealed record ToolCallPermissionSubject : RequestPermissionSubject
{
    /// <inheritdoc />
    public override string Type => "tool_call";

    /// <summary>The affected tool call. Required by the protocol.</summary>
    [JsonPropertyName("toolCall")]
    public ToolCallUpdate ToolCall { get; init; } = new();
}

/// <summary>A permission subject referring to a command the Agent intends to execute.</summary>
[Experimental(AcpDraftProtocol.DiagnosticId, Message = AcpDraftProtocol.Message, UrlFormat = AcpDraftProtocol.UrlFormat)]
public sealed record CommandPermissionSubject : RequestPermissionSubject
{
    /// <inheritdoc />
    public override string Type => "command";

    /// <summary>The command. Required by the protocol.</summary>
    [JsonPropertyName("command")]
    public string Command { get; init; } = string.Empty;

    /// <summary>The absolute working directory. Required by the protocol.</summary>
    [JsonPropertyName("cwd")]
    public string Cwd { get; init; } = string.Empty;

    /// <summary>The related tool-call id, when any.</summary>
    [JsonPropertyName("toolCallId")]
    public string? ToolCallId { get; init; }

    /// <summary>The related Agent-owned terminal id, when any.</summary>
    [JsonPropertyName("terminalId")]
    public string? TerminalId { get; init; }
}

/// <summary>Raw carrier for an unmodeled permission subject.</summary>
[Experimental(AcpDraftProtocol.DiagnosticId, Message = AcpDraftProtocol.Message, UrlFormat = AcpDraftProtocol.UrlFormat)]
public sealed record CustomRequestPermissionSubject : RequestPermissionSubject
{
    private readonly string _type = string.Empty;
    /// <inheritdoc />
    public override string Type => _type;

    /// <summary>Complete raw payload, preserved for forward compatibility.</summary>
    [JsonIgnore]
    public JsonElement RawPayload { get; init; }

    /// <summary>Creates an empty carrier.</summary>
    public CustomRequestPermissionSubject()
    {
    }
    /// <summary>Creates a carrier for an unmodeled type.</summary>
    public CustomRequestPermissionSubject(string type, JsonElement rawPayload)
    {
        _type = type ?? throw new ArgumentNullException(nameof(type));
        RawPayload = rawPayload;
    }
}

/// <summary>Reads and writes the open v2 permission-subject union.</summary>
internal sealed class RequestPermissionSubjectJsonConverter : JsonConverter<RequestPermissionSubject>
{
    public override RequestPermissionSubject? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new JsonException("ACP permission subject must be an object.");
        if (!root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String)
            throw new JsonException("ACP permission subject requires a string 'type'.");
        return type.GetString() switch
        {
            "tool_call" => new ToolCallPermissionSubject
            {
                ToolCall = root.TryGetProperty("toolCall", out var toolCall) && toolCall.ValueKind == JsonValueKind.Object
                    ? toolCall.Deserialize((JsonTypeInfo<ToolCallUpdate>)options.GetTypeInfo(typeof(ToolCallUpdate))) ?? new ToolCallUpdate()
                    : throw new JsonException("ACP tool_call permission subject requires toolCall."),
                Meta = AcpMetaJson.Read(root)
            },
            "command" => new CommandPermissionSubject
            {
                Command = ReadRequired(root, "command"),
                Cwd = ReadRequired(root, "cwd"),
                ToolCallId = ReadOptional(root, "toolCallId"),
                TerminalId = ReadOptional(root, "terminalId"),
                Meta = AcpMetaJson.Read(root)
            },
            var custom => new CustomRequestPermissionSubject(custom ?? string.Empty, root.Clone()) { Meta = AcpMetaJson.Read(root) }
        };
    }

    public override void Write(Utf8JsonWriter writer, RequestPermissionSubject value, JsonSerializerOptions options)
    {
        if (value is CustomRequestPermissionSubject custom && custom.RawPayload.ValueKind == JsonValueKind.Object) { custom.RawPayload.WriteTo(writer); return; }
        writer.WriteStartObject(); writer.WriteString("type", value.Type);
        switch (value)
        {
            case ToolCallPermissionSubject tool:
                writer.WritePropertyName("toolCall"); JsonSerializer.Serialize(writer, tool.ToolCall, (JsonTypeInfo<ToolCallUpdate>)options.GetTypeInfo(typeof(ToolCallUpdate))); break;
            case CommandPermissionSubject command:
                writer.WriteString("command", command.Command); writer.WriteString("cwd", command.Cwd);
                if (command.ToolCallId is not null) writer.WriteString("toolCallId", command.ToolCallId);
                if (command.TerminalId is not null) writer.WriteString("terminalId", command.TerminalId); break;
        }
        AcpMetaJson.Write(writer, value.Meta); writer.WriteEndObject();
    }

    private static string ReadRequired(JsonElement root, string name) => ReadOptional(root, name) ?? throw new JsonException($"ACP command permission subject requires {name}.");
    private static string? ReadOptional(JsonElement root, string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
