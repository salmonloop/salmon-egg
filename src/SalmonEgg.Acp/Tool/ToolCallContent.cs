using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using SalmonEgg.Acp.Content;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Acp.Serialization;

namespace SalmonEgg.Acp.Tool
{
    /// <summary>
    /// Tool call content types.
    /// Represents different types of content that can be produced by a tool call.
    /// Polymorphic reading and writing is dispatched manually by <see cref="ToolCallContentJsonConverter"/>: a known
    /// <c>type</c> (content/diff/terminal) maps to a strongly typed subtype; an unknown <c>type</c> is preserved
    /// verbatim as <see cref="CustomToolCallContent"/> per the spec rule "preserve the raw payload" and can round-trip
    /// byte for byte, leaving the semantics to the Agent rather than the client (AGENTS.md: protocol leniency must
    /// never be tightened in reverse; the same RawPayload pattern as McpServer).
    /// <see cref="JsonPolymorphicAttribute"/> plus FallBackToBaseType is not used: when STJ falls back to the base
    /// type it consumes the <c>type</c> discriminator as polymorphic metadata, which makes unknown discriminator
    /// values impossible to round-trip.
    /// </summary>
    [JsonConverter(typeof(ToolCallContentJsonConverter))]
    public abstract record ToolCallContent : AcpProtocolObject
    {
    }

    /// <summary>
    /// Regular content produced by a tool call.
    /// </summary>
    public sealed record ContentToolCallContent : ToolCallContent
    {
        /// <summary>
        /// The content block.
        /// </summary>
        [JsonPropertyName("content")]
        public ContentBlock? Content { get; init; }

        /// <summary>
        /// Creates a new ContentToolCallContent instance.
        /// </summary>
        public ContentToolCallContent()
        {
        }

        /// <summary>
        /// Creates a new ContentToolCallContent instance.
        /// </summary>
        /// <param name="content">The content block</param>
        public ContentToolCallContent(ContentBlock? content)
        {
            Content = content;
        }
    }

    /// <summary>
    /// File diff produced by a tool call.
    /// </summary>
    public sealed record DiffToolCallContent : ToolCallContent
    {
        /// <summary>
        /// The absolute file path being modified.
        /// </summary>
        [JsonPropertyName("path")]
        public string? Path { get; init; }

        /// <summary>
        /// The original content (null for new files).
        /// </summary>
        [JsonPropertyName("oldText")]
        public string? OldText { get; init; }

        /// <summary>
        /// The new content after modification.
        /// </summary>
        [JsonPropertyName("newText")]
        public string? NewText { get; init; }

        /// <summary>
        /// Creates a new DiffToolCallContent instance.
        /// </summary>
        public DiffToolCallContent()
        {
        }

        /// <summary>
        /// Creates a new DiffToolCallContent instance.
        /// </summary>
        /// <param name="path">The absolute file path being modified</param>
        /// <param name="oldText">The original content (null for new files)</param>
        /// <param name="newText">The new content after modification</param>
        public DiffToolCallContent(string? path = null, string? oldText = null, string? newText = null)
        {
            Path = path;
            OldText = oldText;
            NewText = newText;
        }
    }

    /// <summary>
    /// Terminal output produced by a tool call.
    /// </summary>
    public sealed record TerminalToolCallContent : ToolCallContent
    {
        /// <summary>
        /// The ID of a terminal created with terminal/create.
        /// </summary>
        [JsonPropertyName("terminalId")]
        public string? TerminalId { get; init; }

        /// <summary>
        /// Creates a new TerminalToolCallContent instance.
        /// </summary>
        public TerminalToolCallContent()
        {
        }

        /// <summary>
        /// Creates a new TerminalToolCallContent instance.
        /// </summary>
        /// <param name="terminalId">The ID of a terminal</param>
        public TerminalToolCallContent(string? terminalId)
        {
            TerminalId = terminalId;
        }
    }

    /// <summary>
    /// Forward-compatible carrier for tool call content with an unknown <c>type</c>. Per the spec rule "preserve the
    /// raw payload" the whole object is kept verbatim (including the discriminator and every unknown field), leaving
    /// the semantics to the Agent rather than the client; the client does not interpret, discard, or tighten it.
    /// </summary>
    public sealed record CustomToolCallContent : ToolCallContent
    {
        /// <summary>
        /// The original <c>type</c> discriminator value.
        /// </summary>
        [JsonIgnore]
        public string Type { get; init; } = string.Empty;

        /// <summary>
        /// The complete raw payload preserved on read, read and written manually by
        /// <see cref="ToolCallContentJsonConverter"/>; it is the single authoritative source of truth for this carrier.
        /// </summary>
        [JsonIgnore]
        public JsonElement RawPayload { get; init; }

        /// <summary>
        /// Creates a new CustomToolCallContent instance.
        /// </summary>
        public CustomToolCallContent()
        {
        }

        /// <summary>
        /// Creates a new CustomToolCallContent instance.
        /// </summary>
        /// <param name="type">The original type discriminator value</param>
        /// <param name="rawPayload">The complete raw payload</param>
        public CustomToolCallContent(string type, JsonElement rawPayload)
        {
            Type = type;
            RawPayload = rawPayload;
        }
    }

    /// <summary>
    /// Polymorphic reading and writing for tool call content. A known <c>type</c> maps to a strongly typed subtype; an
    /// unknown <c>type</c> keeps the complete raw payload in <see cref="CustomToolCallContent"/> and writes it back
    /// verbatim, guaranteeing a byte-for-byte round-trip of unknown discriminator values. A missing <c>type</c> is
    /// handled by the most lenient known branch (content) and does not throw.
    /// </summary>
    internal sealed class ToolCallContentJsonConverter : JsonConverter<ToolCallContent>
    {
        public override ToolCallContent? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                // ToolCallUpdate.content / ToolCall.content both carry default+skip-items, so a malformed
                // element degrades to being skipped instead of failing the entire content list. Passthrough
                // (rather than null) preserves the broken payload for diagnostic purposes: the agent can
                // see something arrived, just not parse it.
                return new CustomToolCallContent(string.Empty, root.Clone());
            }

            var type = root.TryGetProperty("type", out var typeElement)
                && typeElement.ValueKind == JsonValueKind.String
                    ? typeElement.GetString()
                    : null;

            return type switch
            {
                "content" => ReadContent(root, options),
                // v1 and v2 share the "diff" discriminator; only the payload shape tells them apart. On a
                // v1 connection a structured diff is not a diff this side can read, and it is not a flat
                // one either - reading it as flat would silently produce empty path/oldText/newText. So it
                // goes to passthrough, which is where it landed before the v2 shape was modeled: the
                // payload survives a round-trip instead of being half-read and then unwritable.
                "diff" when StructuredDiffWireFormat.IsStructured(root)
                    => AcpWireFormat.NegotiatedVersion(options) >= AcpProtocolVersion.V2
                        ? StructuredDiffWireFormat.Read(root)
                        : new CustomToolCallContent("diff", root.Clone()) { Meta = AcpMetaJson.Read(root) },
                "diff" => ReadDiff(root, options),
                "terminal" => ReadTerminal(root, options),
                // Unknown or missing discriminator: keep the raw payload for forward passthrough and let the Agent
                // decide the semantics.
                _ => new CustomToolCallContent(type ?? string.Empty, root.Clone())
            };
        }

        public override void Write(Utf8JsonWriter writer, ToolCallContent value, JsonSerializerOptions options)
        {
            switch (value)
            {
                case ContentToolCallContent content:
                    writer.WriteStartObject();
                    writer.WriteString("type", "content");
                    if (content.Content is not null)
                    {
                        writer.WritePropertyName("content");
                        JsonSerializer.Serialize(
                            writer,
                            content.Content,
                            (JsonTypeInfo<ContentBlock>)options.GetTypeInfo(typeof(ContentBlock)));
                    }

                    AcpMetaJson.Write(writer, content.Meta);
                    writer.WriteEndObject();
                    break;
                case StructuredDiff structuredDiff:
                    StructuredDiffWireFormat.Write(writer, structuredDiff, options);
                    break;
                case DiffToolCallContent diff:
                    writer.WriteStartObject();
                    writer.WriteString("type", "diff");
                    writer.WriteString("path", diff.Path);
                    writer.WriteString("oldText", diff.OldText);
                    writer.WriteString("newText", diff.NewText);
                    AcpMetaJson.Write(writer, diff.Meta);
                    writer.WriteEndObject();
                    break;
                case TerminalToolCallContent terminal:
                    writer.WriteStartObject();
                    writer.WriteString("type", "terminal");
                    writer.WriteString("terminalId", terminal.TerminalId);
                    AcpMetaJson.Write(writer, terminal.Meta);
                    writer.WriteEndObject();
                    break;
                case CustomToolCallContent custom:
                    // Forward-compatible passthrough: write back the raw payload preserved on read verbatim, without
                    // reordering fields or dropping unknown properties.
                    // WriteRawValue(GetRawText()) is used for byte-for-byte fidelity, matching McpServer.
                    // When RawPayload is empty (hand-constructed), fall back to a minimal write that still carries the
                    // original type value.
                    if (custom.RawPayload.ValueKind == JsonValueKind.Object)
                    {
                        writer.WriteRawValue(custom.RawPayload.GetRawText());
                        return;
                    }

                    writer.WriteStartObject();
                    writer.WriteString("type", custom.Type);
                    AcpMetaJson.Write(writer, custom.Meta);
                    writer.WriteEndObject();
                    break;
                default:
                    throw new JsonException($"Unsupported tool call content type: {value.GetType().FullName}");
            }
        }

        private static ContentToolCallContent ReadContent(JsonElement root, JsonSerializerOptions options)
        {
            var content = root.TryGetProperty("content", out var contentElement)
                && contentElement.ValueKind != JsonValueKind.Null
                    ? contentElement.Deserialize(
                        (JsonTypeInfo<ContentBlock>)options.GetTypeInfo(typeof(ContentBlock)))
                    : null;
            return new ContentToolCallContent(content)
            {
                Meta = AcpMetaJson.Read(root)
            };
        }

        private static DiffToolCallContent ReadDiff(JsonElement root, JsonSerializerOptions options)
        {
            return new DiffToolCallContent(
                ReadOptionalString(root, "path"),
                ReadOptionalString(root, "oldText"),
                ReadOptionalString(root, "newText"))
            {
                Meta = AcpMetaJson.Read(root)
            };
        }

        private static TerminalToolCallContent ReadTerminal(JsonElement root, JsonSerializerOptions options)
        {
            return new TerminalToolCallContent(ReadOptionalString(root, "terminalId"))
            {
                Meta = AcpMetaJson.Read(root)
            };
        }

        private static string? ReadOptionalString(JsonElement root, string propertyName)
            => root.TryGetProperty(propertyName, out var element) && element.ValueKind == JsonValueKind.String
                ? element.GetString()
                : null;
    }
}
