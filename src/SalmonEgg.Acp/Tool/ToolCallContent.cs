using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using SalmonEgg.Acp.Content;
using SalmonEgg.Acp.Protocol;

namespace SalmonEgg.Acp.Tool
{
    /// <summary>
    /// Tool call content types.
    /// Represents different types of content that can be produced by a tool call.
    /// 多态读写由 <see cref="ToolCallContentJsonConverter"/> 手动分发:已知 <c>type</c>
    /// (content/diff/terminal) 映射到强类型子类;未知 <c>type</c> 按 spec「preserve the raw
    /// payload」原样保留为 <see cref="CustomToolCallContent"/> 并可字节级 round-trip,由 Agent
    /// 而非 client 决定语义(AGENTS.md「协议宽松度不得反向收紧」,与 McpServer 同一 RawPayload 范式)。
    /// 不用 <see cref="JsonPolymorphicAttribute"/>+FallBackToBaseType:STJ 回落基类时会把判别值
    /// <c>type</c> 当多态元数据消费掉,导致未知判别值无法 round-trip。
    /// </summary>
    [JsonConverter(typeof(ToolCallContentJsonConverter))]
    public abstract class ToolCallContent : AcpProtocolObject
    {
    }

    /// <summary>
    /// Regular content produced by a tool call.
    /// </summary>
    public class ContentToolCallContent : ToolCallContent
    {
        /// <summary>
        /// The content block.
        /// </summary>
        [JsonPropertyName("content")]
        public ContentBlock? Content { get; set; }

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
    public class DiffToolCallContent : ToolCallContent
    {
        /// <summary>
        /// The absolute file path being modified.
        /// </summary>
        [JsonPropertyName("path")]
        public string? Path { get; set; }

        /// <summary>
        /// The original content (null for new files).
        /// </summary>
        [JsonPropertyName("oldText")]
        public string? OldText { get; set; }

        /// <summary>
        /// The new content after modification.
        /// </summary>
        [JsonPropertyName("newText")]
        public string? NewText { get; set; }

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
    public class TerminalToolCallContent : ToolCallContent
    {
        /// <summary>
        /// The ID of a terminal created with terminal/create.
        /// </summary>
        [JsonPropertyName("terminalId")]
        public string? TerminalId { get; set; }

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
    /// 未知 <c>type</c> 的 tool call content 前向兼容载体。按 spec「preserve the raw payload」
    /// 原样保留整个对象(含判别值与所有未知字段),由 Agent 而非 client 决定语义;client 不解释、
    /// 不丢弃、不收紧。
    /// </summary>
    public class CustomToolCallContent : ToolCallContent
    {
        /// <summary>
        /// 原始 <c>type</c> 判别值。
        /// </summary>
        [JsonIgnore]
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// 读入时保留的完整原始 payload,由 <see cref="ToolCallContentJsonConverter"/> 手动读写,
        /// 是本载体的唯一权威事实源。
        /// </summary>
        [JsonIgnore]
        public JsonElement RawPayload { get; set; }

        /// <summary>
        /// Creates a new CustomToolCallContent instance.
        /// </summary>
        public CustomToolCallContent()
        {
        }

        /// <summary>
        /// Creates a new CustomToolCallContent instance.
        /// </summary>
        /// <param name="type">原始 type 判别值</param>
        /// <param name="rawPayload">完整原始 payload</param>
        public CustomToolCallContent(string type, JsonElement rawPayload)
        {
            Type = type;
            RawPayload = rawPayload;
        }
    }

    /// <summary>
    /// tool call content 的多态读写。已知 <c>type</c> 映射强类型子类;未知 <c>type</c> 保留
    /// 完整 raw payload 到 <see cref="CustomToolCallContent"/> 并原样写回,保证未知判别值
    /// 字节级 round-trip。缺失 <c>type</c> 按已知最宽松分支(content)处理,不抛错。
    /// </summary>
    public sealed class ToolCallContentJsonConverter : JsonConverter<ToolCallContent>
    {
        public override ToolCallContent? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("Tool call content must be a JSON object.");
            }

            var type = root.TryGetProperty("type", out var typeElement)
                && typeElement.ValueKind == JsonValueKind.String
                    ? typeElement.GetString()
                    : null;

            return type switch
            {
                "content" => ReadContent(root, options),
                "diff" => ReadDiff(root, options),
                "terminal" => ReadTerminal(root, options),
                // 未知或缺失判别值:保留 raw payload 前向透传,由 Agent 决定语义。
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
                        JsonSerializer.Serialize(writer, content.Content, options);
                    }

                    AcpMetaJson.Write(writer, content.Meta);
                    writer.WriteEndObject();
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
                    // 前向兼容透传:原样写回读入时保留的 raw payload,不重排字段、不丢弃未知属性。
                    // 用 WriteRawValue(GetRawText()) 实现字节级保真,与 McpServer 一致。
                    // RawPayload 为空(手工构造)时退化为最小写出,仍携带原始 type 值。
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
                    ? contentElement.Deserialize<ContentBlock>(options)
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
