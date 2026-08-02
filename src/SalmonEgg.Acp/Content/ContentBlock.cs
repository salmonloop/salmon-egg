using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using SalmonEgg.Acp.Protocol;

namespace SalmonEgg.Acp.Content
{
    /// <summary>
    /// 内容块的基类。
    /// 用于表示会话中的各种类型的内容（文本、图片、音频、资源等）。
    /// ContentBlock uses a dedicated converter so protocol fields retain their wire shape.
    /// </summary>
    [JsonConverter(typeof(ContentBlockJsonConverter))]
    public record ContentBlock : AcpProtocolObject
    {
        /// <summary>
        /// Optional ACP annotations that guide how the content should be used or displayed.
        /// </summary>
        [JsonPropertyName("annotations")]
        public Annotations? Annotations { get; init; }

        [JsonIgnore]
        internal string? UnknownTypeDiscriminator { get; init; }

        /// <summary>
        /// 未知判别值内容块的原始 payload，原样保留以供无损透传。
        /// spec 要求 client 对未知 content 类型保留原始形态，由 Agent 而非 client 决定接受或拒绝；
        /// 已知类型（text/image/audio/resource/resource_link）不使用此字段。
        /// 由 <see cref="ContentBlockJsonConverter"/> 手动读写，不经默认序列化。
        /// </summary>
        [JsonIgnore]
        internal JsonElement? RawPayload { get; init; }

        /// <summary>
        /// 内容块的类型标识符。
        /// 用于多态序列化和反序列化。
        /// </summary>
        [JsonIgnore]
        public virtual string Type => UnknownTypeDiscriminator ?? string.Empty;
    }

    /// <summary>
    /// Optional ACP annotations attached to a content block.
    /// </summary>
    public sealed record Annotations : AcpProtocolObject
    {
        /// <summary>
        /// Intended audience for the content.
        /// </summary>
        [JsonPropertyName("audience")]
        public List<string>? Audience { get; init; }

        /// <summary>
        /// Relative priority from 0.0 to 1.0.
        /// </summary>
        [JsonPropertyName("priority")]
        public double? Priority { get; init; }

        /// <summary>
        /// ISO 8601 timestamp for the last modification time.
        /// </summary>
        [JsonPropertyName("lastModified")]
        public string? LastModified { get; init; }
    }

    internal sealed class ContentBlockJsonConverter : JsonConverter<ContentBlock>
    {
        public override ContentBlock? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("ContentBlock payload must be a JSON object.");
            }

            if (!root.TryGetProperty("type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String)
            {
                throw new JsonException("ContentBlock payload must contain a string 'type' discriminator.");
            }

            var discriminator = typeElement.GetString() ?? throw new JsonException("ContentBlock type discriminator cannot be null.");

            return discriminator switch
            {
                "text" => ReadText(root),
                "image" => ReadImage(root),
                "audio" => ReadAudio(root),
                "resource_link" => ReadResourceLink(root),
                "resource" => ReadResource(root),
                _ => ReadUnknown(root, discriminator)
            };
        }

        public override void Write(Utf8JsonWriter writer, ContentBlock value, JsonSerializerOptions options)
        {
            switch (value)
            {
                case TextContentBlock text:
                    WriteText(writer, text, options);
                    return;
                case ImageContentBlock image:
                    WriteImage(writer, image, options);
                    return;
                case AudioContentBlock audio:
                    WriteAudio(writer, audio, options);
                    return;
                case ResourceLinkContentBlock resourceLink:
                    WriteResourceLink(writer, resourceLink, options);
                    return;
                case ResourceContentBlock resource:
                    WriteResource(writer, resource, options);
                    return;
                default:
                    WriteUnknown(writer, value, options);
                    return;
            }
        }

        private static TextContentBlock ReadText(JsonElement root)
        {
            var block = new TextContentBlock
            {
                Text = ReadString(root, "text")!,
                Annotations = ReadAnnotations(root),
                Meta = AcpMetaJson.Read(root)
            };
            return block;
        }

        private static ImageContentBlock ReadImage(JsonElement root)
        {
            var block = new ImageContentBlock
            {
                Data = ReadString(root, "data")!,
                MimeType = ReadString(root, "mimeType")!,
                Uri = ReadString(root, "uri"),
                Annotations = ReadAnnotations(root),
                Meta = AcpMetaJson.Read(root)
            };
            return block;
        }

        private static AudioContentBlock ReadAudio(JsonElement root)
        {
            var block = new AudioContentBlock
            {
                Data = ReadString(root, "data")!,
                MimeType = ReadString(root, "mimeType")!,
                Annotations = ReadAnnotations(root),
                Meta = AcpMetaJson.Read(root)
            };
            return block;
        }

        private static ResourceLinkContentBlock ReadResourceLink(JsonElement root)
        {
            var block = new ResourceLinkContentBlock
            {
                Uri = ReadString(root, "uri")!,
                Name = ReadString(root, "name"),
                MimeType = ReadString(root, "mimeType"),
                Title = ReadString(root, "title"),
                Description = ReadString(root, "description"),
                Size = ReadInt64(root, "size"),
                Annotations = ReadAnnotations(root),
                Meta = AcpMetaJson.Read(root)
            };
            return block;
        }

        private static ResourceContentBlock ReadResource(JsonElement root)
        {
            if (!root.TryGetProperty("resource", out var resourceElement)
                || resourceElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                throw new JsonException("ContentBlock 'resource' is required for the resource content type.");
            }

            var block = new ResourceContentBlock
            {
                Resource = ReadEmbeddedResource(resourceElement),
                Annotations = ReadAnnotations(root),
                Meta = AcpMetaJson.Read(root)
            };
            return block;
        }

        private static ContentBlock ReadUnknown(JsonElement root, string discriminator)
        {
            // 未知判别值走 passthrough:spec 要求 receiver 对不认识的 content 类型保留 raw payload,
            // 由 Agent 而非 client 决定接受或拒绝。原样保留整个 block object 以供无损 round-trip,
            // 不丢弃 type/annotations/_meta 之外的字段(对照 McpServerJsonConverter 的 RawPayload 范式)。
            return new ContentBlock
            {
                UnknownTypeDiscriminator = discriminator,
                Annotations = ReadAnnotations(root),
                Meta = AcpMetaJson.Read(root),
                RawPayload = root.Clone()
            };
        }

        private static EmbeddedResource ReadEmbeddedResource(JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("Embedded resource payload must be a JSON object.");
            }

            return new EmbeddedResource
            {
                Uri = ReadString(element, "uri")!,
                MimeType = ReadString(element, "mimeType")!,
                Text = ReadString(element, "text"),
                Blob = ReadString(element, "blob"),
                Meta = AcpMetaJson.Read(element)
            };
        }

        private static Annotations? ReadAnnotations(JsonElement root)
        {
            if (!root.TryGetProperty("annotations", out var annotationsElement)
                || annotationsElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                return null;
            }

            if (annotationsElement.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("ContentBlock annotations must be a JSON object.");
            }

            var annotations = new Annotations
            {
                Audience = ReadStringList(annotationsElement, "audience"),
                Priority = ReadDouble(annotationsElement, "priority"),
                LastModified = ReadString(annotationsElement, "lastModified"),
                Meta = AcpMetaJson.Read(annotationsElement)
            };

            return annotations;
        }

        private static List<string>? ReadStringList(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out var property)
                || property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                return null;
            }

            if (property.ValueKind != JsonValueKind.Array)
            {
                throw new JsonException($"ContentBlock '{propertyName}' must be a JSON array.");
            }

            var values = new List<string>();
            foreach (var item in property.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                {
                    throw new JsonException($"ContentBlock '{propertyName}' entries must be JSON strings.");
                }

                values.Add(item.GetString()!);
            }

            return values;
        }

        private static string? ReadString(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out var property)
                || property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                return null;
            }

            if (property.ValueKind != JsonValueKind.String)
            {
                throw new JsonException($"ContentBlock '{propertyName}' must be a JSON string.");
            }

            return property.GetString();
        }

        private static double? ReadDouble(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out var property)
                || property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                return null;
            }

            if (property.ValueKind != JsonValueKind.Number || !property.TryGetDouble(out var value))
            {
                throw new JsonException($"ContentBlock '{propertyName}' must be a JSON number.");
            }

            return value;
        }

        private static long? ReadInt64(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out var property)
                || property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                return null;
            }

            if (property.ValueKind != JsonValueKind.Number || !property.TryGetInt64(out var value))
            {
                throw new JsonException($"ContentBlock '{propertyName}' must be a JSON integer.");
            }

            return value;
        }

        private static void WriteText(Utf8JsonWriter writer, TextContentBlock value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("type", value.Type);
            WriteAnnotations(writer, value.Annotations, options);
            writer.WriteString("text", value.Text);
            AcpMetaJson.Write(writer, value.Meta);
            writer.WriteEndObject();
        }

        private static void WriteImage(Utf8JsonWriter writer, ImageContentBlock value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("type", value.Type);
            WriteAnnotations(writer, value.Annotations, options);
            writer.WriteString("data", value.Data);
            WriteNullableString(writer, "uri", value.Uri, options);
            writer.WriteString("mimeType", value.MimeType);
            AcpMetaJson.Write(writer, value.Meta);
            writer.WriteEndObject();
        }

        private static void WriteAudio(Utf8JsonWriter writer, AudioContentBlock value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("type", value.Type);
            WriteAnnotations(writer, value.Annotations, options);
            writer.WriteString("data", value.Data);
            writer.WriteString("mimeType", value.MimeType);
            AcpMetaJson.Write(writer, value.Meta);
            writer.WriteEndObject();
        }

        private static void WriteResourceLink(Utf8JsonWriter writer, ResourceLinkContentBlock value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("type", value.Type);
            WriteAnnotations(writer, value.Annotations, options);
            writer.WriteString("uri", value.Uri);
            WriteNullableString(writer, "name", value.Name, options);
            WriteNullableString(writer, "mimeType", value.MimeType, options);
            WriteNullableString(writer, "title", value.Title, options);
            WriteNullableString(writer, "description", value.Description, options);
            WriteNullableNumber(writer, "size", value.Size, options);
            AcpMetaJson.Write(writer, value.Meta);
            writer.WriteEndObject();
        }

        private static void WriteResource(Utf8JsonWriter writer, ResourceContentBlock value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("type", value.Type);
            WriteAnnotations(writer, value.Annotations, options);
            writer.WritePropertyName("resource");
            WriteEmbeddedResource(writer, value.Resource, options);
            AcpMetaJson.Write(writer, value.Meta);
            writer.WriteEndObject();
        }

        private static void WriteEmbeddedResource(Utf8JsonWriter writer, EmbeddedResource value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("uri", value.Uri);
            writer.WriteString("mimeType", value.MimeType);
            WriteNullableString(writer, "text", value.Text, options);
            WriteNullableString(writer, "blob", value.Blob, options);
            AcpMetaJson.Write(writer, value.Meta);
            writer.WriteEndObject();
        }

        private static void WriteAnnotations(Utf8JsonWriter writer, Annotations? value, JsonSerializerOptions options)
        {
            if (value == null)
            {
                if (ShouldWriteNull(options))
                {
                    writer.WriteNull("annotations");
                }

                return;
            }

            writer.WritePropertyName("annotations");
            writer.WriteStartObject();

            if (value.Audience != null)
            {
                writer.WritePropertyName("audience");
                writer.WriteStartArray();
                foreach (var audience in value.Audience)
                {
                    writer.WriteStringValue(audience);
                }

                writer.WriteEndArray();
            }
            else if (ShouldWriteNull(options))
            {
                writer.WriteNull("audience");
            }

            if (value.Priority.HasValue)
            {
                writer.WriteNumber("priority", value.Priority.Value);
            }
            else if (ShouldWriteNull(options))
            {
                writer.WriteNull("priority");
            }

            WriteNullableString(writer, "lastModified", value.LastModified, options);
            AcpMetaJson.Write(writer, value.Meta);
            writer.WriteEndObject();
        }

        private static void WriteNullableString(Utf8JsonWriter writer, string propertyName, string? value, JsonSerializerOptions options)
        {
            if (value != null)
            {
                writer.WriteString(propertyName, value);
                return;
            }

            if (ShouldWriteNull(options))
            {
                writer.WriteNull(propertyName);
            }
        }

        private static void WriteNullableNumber(Utf8JsonWriter writer, string propertyName, long? value, JsonSerializerOptions options)
        {
            if (value.HasValue)
            {
                writer.WriteNumber(propertyName, value.Value);
                return;
            }

            if (ShouldWriteNull(options))
            {
                writer.WriteNull(propertyName);
            }
        }

        private static void WriteUnknown(Utf8JsonWriter writer, ContentBlock value, JsonSerializerOptions options)
        {
            if (string.IsNullOrWhiteSpace(value.UnknownTypeDiscriminator))
            {
                throw new JsonException("Unknown ContentBlock instances must preserve their original type discriminator.");
            }

            // 无损透传:原样写回读入时保留的 raw payload,不重排字段、不丢弃未知属性。
            // RawPayload 是未知 block 的唯一权威事实源(含其 annotations/_meta),故不叠加另写,
            // 避免第二套状态 owner。用 WriteRawValue(GetRawText()) 保证字节级保真(对照 CustomMcpServer)。
            // 若 RawPayload 为空(如手工构造的未知 block),退化为按已知字段最小写出,仍携带原始 type。
            if (value.RawPayload is { ValueKind: JsonValueKind.Object } rawPayload)
            {
                writer.WriteRawValue(rawPayload.GetRawText());
                return;
            }

            writer.WriteStartObject();
            writer.WriteString("type", value.UnknownTypeDiscriminator);

            if (value.Annotations != null)
            {
                WriteAnnotations(writer, value.Annotations, options);
            }
            else if (ShouldWriteNull(options))
            {
                writer.WriteNull("annotations");
            }

            AcpMetaJson.Write(writer, value.Meta);
            writer.WriteEndObject();
        }

        private static bool ShouldWriteNull(JsonSerializerOptions options)
        {
            return options.DefaultIgnoreCondition != JsonIgnoreCondition.WhenWritingNull
                && options.DefaultIgnoreCondition != JsonIgnoreCondition.WhenWritingDefault;
        }
    }
}
