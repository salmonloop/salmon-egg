using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SalmonEgg.Domain.Models.Content
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

            var rawJson = root.GetRawText();
            var discriminator = typeElement.GetString() ?? throw new JsonException("ContentBlock type discriminator cannot be null.");

            return discriminator switch
            {
                "text" => JsonSerializer.Deserialize<TextContentBlock>(rawJson, options),
                "image" => JsonSerializer.Deserialize<ImageContentBlock>(rawJson, options),
                "audio" => JsonSerializer.Deserialize<AudioContentBlock>(rawJson, options),
                "resource_link" => JsonSerializer.Deserialize<ResourceLinkContentBlock>(rawJson, options),
                "resource" => JsonSerializer.Deserialize<ResourceContentBlock>(rawJson, options),
                _ => ReadUnknown(rawJson, discriminator, options)
            };
        }

        public override void Write(Utf8JsonWriter writer, ContentBlock value, JsonSerializerOptions options)
        {
            switch (value)
            {
                case TextContentBlock text:
                    WriteKnown(writer, text.Type, text, options);
                    return;
                case ImageContentBlock image:
                    WriteKnown(writer, image.Type, image, options);
                    return;
                case AudioContentBlock audio:
                    WriteKnown(writer, audio.Type, audio, options);
                    return;
                case ResourceLinkContentBlock resourceLink:
                    WriteKnown(writer, resourceLink.Type, resourceLink, options);
                    return;
                case ResourceContentBlock resource:
                    WriteKnown(writer, resource.Type, resource, options);
                    return;
                default:
                    WriteUnknown(writer, value, options);
                    return;
            }
        }

        private static ContentBlock ReadUnknown(string rawJson, string discriminator, JsonSerializerOptions options)
        {
            var payload = JsonSerializer.Deserialize<UnknownContentBlockPayload>(rawJson, options)
                ?? throw new JsonException("Failed to deserialize unknown content payload.");

            return new ContentBlock
            {
                UnknownTypeDiscriminator = discriminator,
                Annotations = payload.Annotations,
                ExtensionData = payload.ExtensionData
            };
        }

        private static void WriteKnown<TContent>(Utf8JsonWriter writer, string discriminator, TContent value, JsonSerializerOptions options)
            where TContent : ContentBlock
        {
            var element = JsonSerializer.SerializeToElement(value, value.GetType(), options);

            writer.WriteStartObject();
            writer.WriteString("type", discriminator);

            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, "type", StringComparison.Ordinal))
                {
                    continue;
                }

                property.WriteTo(writer);
            }

            writer.WriteEndObject();
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
                writer.WritePropertyName("annotations");
                JsonSerializer.Serialize(writer, value.Annotations, options);
            }

            if (value.ExtensionData != null)
            {
                foreach (var property in value.ExtensionData)
                {
                    if (string.Equals(property.Key, "type", StringComparison.Ordinal)
                        || string.Equals(property.Key, "annotations", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    writer.WritePropertyName(property.Key);
                    property.Value.WriteTo(writer);
                }
            }

            writer.WriteEndObject();
        }

        private sealed class UnknownContentBlockPayload
        {
            [JsonPropertyName("annotations")]
            public Annotations? Annotations { get; set; }

            [JsonExtensionData]
            public Dictionary<string, JsonElement>? ExtensionData { get; set; }
        }
    }
}
