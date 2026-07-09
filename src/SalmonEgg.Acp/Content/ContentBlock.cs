using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SalmonEgg.Acp.Content
{
    /// <summary>
    /// 内容块的基类。
    /// 用于表示会话中的各种类型的内容（文本、图片、音频、资源等）。
    /// ContentBlock uses a dedicated converter so unknown ACP content can round-trip losslessly.
    /// </summary>
    [JsonConverter(typeof(ContentBlockJsonConverter))]
    public class ContentBlock
    {
        /// <summary>
        /// Optional ACP annotations that guide how the content should be used or displayed.
        /// </summary>
        [JsonPropertyName("annotations")]
        public Annotations? Annotations { get; set; }

        /// <summary>
        /// Preserves unknown payload members when the content discriminator is not recognized.
        /// </summary>
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtensionData { get; set; }

        [JsonIgnore]
        internal string? UnknownTypeDiscriminator { get; set; }

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
    public sealed class Annotations
    {
        /// <summary>
        /// Intended audience for the content.
        /// </summary>
        [JsonPropertyName("audience")]
        public List<string>? Audience { get; set; }

        /// <summary>
        /// Relative priority from 0.0 to 1.0.
        /// </summary>
        [JsonPropertyName("priority")]
        public decimal? Priority { get; set; }

        /// <summary>
        /// ISO 8601 timestamp for the last modification time.
        /// </summary>
        [JsonPropertyName("lastModified")]
        public string? LastModified { get; set; }
    }

    public sealed class ContentBlockJsonConverter : JsonConverter<ContentBlock>
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
                Annotations = ReadAnnotations(root)
            };
            block.ExtensionData = ReadExtensionData(root, "text");
            return block;
        }

        private static ImageContentBlock ReadImage(JsonElement root)
        {
            var block = new ImageContentBlock
            {
                Data = ReadString(root, "data")!,
                MimeType = ReadString(root, "mimeType")!,
                Uri = ReadString(root, "uri"),
                Annotations = ReadAnnotations(root)
            };
            block.ExtensionData = ReadExtensionData(root, "data", "mimeType", "uri");
            return block;
        }

        private static AudioContentBlock ReadAudio(JsonElement root)
        {
            var block = new AudioContentBlock
            {
                Data = ReadString(root, "data")!,
                MimeType = ReadString(root, "mimeType")!,
                Annotations = ReadAnnotations(root)
            };
            block.ExtensionData = ReadExtensionData(root, "data", "mimeType");
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
                Annotations = ReadAnnotations(root)
            };
            block.ExtensionData = ReadExtensionData(root, "uri", "name", "mimeType", "title", "description", "size");
            return block;
        }

        private static ResourceContentBlock ReadResource(JsonElement root)
        {
            var block = new ResourceContentBlock
            {
                Resource = root.TryGetProperty("resource", out var resourceElement)
                    ? ReadEmbeddedResource(resourceElement)
                    : null!,
                Annotations = ReadAnnotations(root)
            };
            block.ExtensionData = ReadExtensionData(root, "resource");
            return block;
        }

        private static ContentBlock ReadUnknown(JsonElement root, string discriminator)
        {
            return new ContentBlock
            {
                UnknownTypeDiscriminator = discriminator,
                Annotations = ReadAnnotations(root),
                ExtensionData = ReadExtensionData(root)
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
                Blob = ReadString(element, "blob")
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
                Priority = ReadDecimal(annotationsElement, "priority"),
                LastModified = ReadString(annotationsElement, "lastModified")
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
                values.Add(item.GetString()!);
            }

            return values;
        }

        private static string? ReadString(JsonElement root, string propertyName)
        {
            return root.TryGetProperty(propertyName, out var property)
                ? property.ValueKind == JsonValueKind.Null ? null : property.GetString()
                : null;
        }

        private static decimal? ReadDecimal(JsonElement root, string propertyName)
        {
            return root.TryGetProperty(propertyName, out var property)
                && property.ValueKind == JsonValueKind.Number
                && property.TryGetDecimal(out var value)
                    ? value
                    : null;
        }

        private static long? ReadInt64(JsonElement root, string propertyName)
        {
            return root.TryGetProperty(propertyName, out var property)
                && property.ValueKind == JsonValueKind.Number
                && property.TryGetInt64(out var value)
                    ? value
                    : null;
        }

        private static Dictionary<string, JsonElement>? ReadExtensionData(JsonElement root, params string[] knownPropertyNames)
        {
            Dictionary<string, JsonElement>? extensionData = null;

            foreach (var property in root.EnumerateObject())
            {
                if (IsKnownProperty(property.Name, knownPropertyNames))
                {
                    continue;
                }

                extensionData ??= new Dictionary<string, JsonElement>();
                extensionData[property.Name] = property.Value.Clone();
            }

            return extensionData;
        }

        private static bool IsKnownProperty(string propertyName, string[] knownPropertyNames)
        {
            if (string.Equals(propertyName, "type", StringComparison.Ordinal)
                || string.Equals(propertyName, "annotations", StringComparison.Ordinal))
            {
                return true;
            }

            for (var i = 0; i < knownPropertyNames.Length; i++)
            {
                if (string.Equals(propertyName, knownPropertyNames[i], StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static void WriteText(Utf8JsonWriter writer, TextContentBlock value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("type", value.Type);
            WriteAnnotations(writer, value.Annotations, options);
            writer.WriteString("text", value.Text);
            WriteExtensionData(writer, value.ExtensionData, "text");
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
            WriteExtensionData(writer, value.ExtensionData, "data", "mimeType", "uri");
            writer.WriteEndObject();
        }

        private static void WriteAudio(Utf8JsonWriter writer, AudioContentBlock value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("type", value.Type);
            WriteAnnotations(writer, value.Annotations, options);
            writer.WriteString("data", value.Data);
            writer.WriteString("mimeType", value.MimeType);
            WriteExtensionData(writer, value.ExtensionData, "data", "mimeType");
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
            WriteExtensionData(writer, value.ExtensionData, "uri", "name", "mimeType", "title", "description", "size");
            writer.WriteEndObject();
        }

        private static void WriteResource(Utf8JsonWriter writer, ResourceContentBlock value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("type", value.Type);
            WriteAnnotations(writer, value.Annotations, options);
            writer.WritePropertyName("resource");
            WriteEmbeddedResource(writer, value.Resource, options);
            WriteExtensionData(writer, value.ExtensionData, "resource");
            writer.WriteEndObject();
        }

        private static void WriteEmbeddedResource(Utf8JsonWriter writer, EmbeddedResource value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("uri", value.Uri);
            writer.WriteString("mimeType", value.MimeType);
            WriteNullableString(writer, "text", value.Text, options);
            WriteNullableString(writer, "blob", value.Blob, options);
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

            WriteExtensionData(writer, value.ExtensionData);
            writer.WriteEndObject();
        }

        private static void WriteExtensionData(Utf8JsonWriter writer, Dictionary<string, JsonElement>? extensionData, params string[] knownPropertyNames)
        {
            if (extensionData == null)
            {
                return;
            }

            foreach (var property in extensionData)
            {
                if (IsKnownProperty(property.Key, knownPropertyNames))
                {
                    continue;
                }

                writer.WritePropertyName(property.Key);
                property.Value.WriteTo(writer);
            }
        }

        private static bool ShouldWriteNull(JsonSerializerOptions options)
        {
            return options.DefaultIgnoreCondition != JsonIgnoreCondition.WhenWritingNull
                && options.DefaultIgnoreCondition != JsonIgnoreCondition.WhenWritingDefault;
        }
    }
}
