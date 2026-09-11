using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Acp.Serialization;

namespace SalmonEgg.Acp.Content
{
    /// <summary>
    /// Base type for content blocks.
    /// Represents the various kinds of content exchanged in a session (text, image, audio, resource, and so on).
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
        /// Raw payload of a content block with an unknown type discriminator, kept verbatim for lossless passthrough.
        /// The spec requires a client to preserve the original shape of unknown content types, leaving the decision
        /// to accept or reject them to the Agent rather than the client; the known types
        /// (text/image/audio/resource/resource_link) do not use this field.
        /// Read and written manually by <see cref="ContentBlockJsonConverter"/>, bypassing default serialization.
        /// </summary>
        [JsonIgnore]
        internal JsonElement? RawPayload { get; init; }

        /// <summary>
        /// Type discriminator of the content block.
        /// Used for polymorphic serialization and deserialization.
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
                "text" => ReadText(root, options),
                "image" => ReadImage(root, options),
                "audio" => ReadAudio(root, options),
                "resource_link" => ReadResourceLink(root, options),
                "resource" => ReadResource(root, options),
                _ => ReadUnknown(root, discriminator, options)
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

        private static TextContentBlock ReadText(JsonElement root, JsonSerializerOptions options)
        {
            var block = new TextContentBlock
            {
                Text = ReadRequiredContentString(root, "text", options)!,
                Annotations = ReadAnnotations(root, options),
                Meta = ReadMetadata(root, options)
            };
            return block;
        }

        private static ImageContentBlock ReadImage(JsonElement root, JsonSerializerOptions options)
        {
            var block = new ImageContentBlock
            {
                Data = ReadRequiredContentString(root, "data", options)!,
                MimeType = ReadRequiredContentString(root, "mimeType", options)!,
                Uri = ReadOptionalContentString(root, "uri", options),
                Annotations = ReadAnnotations(root, options),
                Meta = ReadMetadata(root, options)
            };
            return block;
        }

        private static AudioContentBlock ReadAudio(JsonElement root, JsonSerializerOptions options)
        {
            var block = new AudioContentBlock
            {
                Data = ReadRequiredContentString(root, "data", options)!,
                MimeType = ReadRequiredContentString(root, "mimeType", options)!,
                Annotations = ReadAnnotations(root, options),
                Meta = ReadMetadata(root, options)
            };
            return block;
        }

        internal static ResourceLinkContentBlock ReadResourceLink(JsonElement root, JsonSerializerOptions options)
        {
            var block = new ResourceLinkContentBlock
            {
                Uri = ReadRequiredContentString(root, "uri", options)!,
                Name = ReadRequiredContentString(root, "name", options),
                MimeType = ReadOptionalContentString(root, "mimeType", options),
                Title = ReadOptionalContentString(root, "title", options),
                Description = ReadOptionalContentString(root, "description", options),
                Size = ReadOptionalContentSize(root, options),
                RawIcons = root.TryGetProperty("icons", out var icons) ? icons.Clone() : null,
                Annotations = ReadAnnotations(root, options),
                Meta = ReadMetadata(root, options)
            };
            return block;
        }

        private static ResourceContentBlock ReadResource(JsonElement root, JsonSerializerOptions options)
        {
            if (!root.TryGetProperty("resource", out var resourceElement)
                || resourceElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                throw new JsonException("ContentBlock 'resource' is required for the resource content type.");
            }

            var block = new ResourceContentBlock
            {
                Resource = ReadEmbeddedResource(resourceElement, options),
                Annotations = ReadAnnotations(root, options),
                Meta = ReadMetadata(root, options)
            };
            return block;
        }

        private static ContentBlock ReadUnknown(JsonElement root, string discriminator, JsonSerializerOptions options)
        {
            // Unknown discriminators take the passthrough path: the spec requires a receiver to preserve the raw
            // payload of content types it does not recognize, leaving the decision to accept or reject them to the
            // Agent rather than the client. The whole block object is kept verbatim so the round-trip is lossless
            // and fields beyond type/annotations/_meta are not dropped (mirrors the RawPayload pattern in
            // McpServerJsonConverter).
            return new ContentBlock
            {
                UnknownTypeDiscriminator = discriminator,
                Annotations = ReadAnnotations(root, options),
                Meta = ReadMetadata(root, options),
                RawPayload = root.Clone()
            };
        }

        private static EmbeddedResource ReadEmbeddedResource(JsonElement element, JsonSerializerOptions options)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("Embedded resource payload must be a JSON object.");
            }

            if (AcpWireFormat.NegotiatedVersion(options) == AcpProtocolVersion.V2)
            {
                return ReadDraftEmbeddedResource(element, options);
            }

            return new EmbeddedResource
            {
                Uri = ReadRequiredContentString(element, "uri", options)!,
                MimeType = ReadString(element, "mimeType")!,
                Text = ReadString(element, "text"),
                Blob = ReadString(element, "blob"),
                Meta = ReadMetadata(element, options)
            };
        }

        private static EmbeddedResource ReadDraftEmbeddedResource(JsonElement element, JsonSerializerOptions options)
        {
            // The pinned v2 resource union is untagged: try text before blob, as the upstream
            // reader does. The other branch's fields cannot invalidate an otherwise valid branch.
            string? text = null;
            string? blob = null;
            if (element.TryGetProperty("text", out var rawText) && rawText.ValueKind == JsonValueKind.String)
            {
                text = rawText.GetString();
            }
            else if (element.TryGetProperty("blob", out var rawBlob) && rawBlob.ValueKind == JsonValueKind.String)
            {
                blob = rawBlob.GetString();
            }
            else
            {
                throw new JsonException("ACP v2 embedded resource requires string 'text' or 'blob'.");
            }

            return new EmbeddedResource
            {
                Uri = ReadRequiredContentString(element, "uri", options)!,
                MimeType = ReadOptionalContentString(element, "mimeType", options)!,
                Text = text,
                Blob = blob,
                Meta = ReadMetadata(element, options)
            };
        }

        private static Annotations? ReadAnnotations(JsonElement root, JsonSerializerOptions options)
        {
            if (!root.TryGetProperty("annotations", out var annotationsElement)
                || annotationsElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                return null;
            }

            if (AcpWireFormat.NegotiatedVersion(options) == AcpProtocolVersion.V2)
            {
                // The v2 content contract at 5ebaf0aceb04a4ba6574cd63fa6355352dc6d931 defaults each optional field before the
                // containing content list decides whether an item is invalid. The generated
                // annotation contract also preserves valid siblings when one hint is malformed.
                return DefaultableObjectJsonConverter<Annotations>.ReadValue(annotationsElement, options);
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

        private static Dictionary<string, object?>? ReadMetadata(JsonElement root, JsonSerializerOptions options)
            => AcpWireFormat.NegotiatedVersion(options) == AcpProtocolVersion.V2
                ? AcpMetaJson.ReadOrDefault(root)
                : AcpMetaJson.Read(root);

        // Only the v2 fields marked x-deserialize-default-on-error call these readers. Required
        // strings keep the strict reader, and unversioned/v1 contracts keep their existing behavior.
        private static string? ReadOptionalContentString(JsonElement root, string propertyName, JsonSerializerOptions options)
            => AcpWireFormat.NegotiatedVersion(options) == AcpProtocolVersion.V2
                ? root.TryGetProperty(propertyName, out var field) && field.ValueKind == JsonValueKind.String
                    ? field.GetString() : null
                : ReadString(root, propertyName);

        private static long? ReadOptionalContentSize(JsonElement root, JsonSerializerOptions options)
            => AcpWireFormat.NegotiatedVersion(options) == AcpProtocolVersion.V2
                ? root.TryGetProperty("size", out var field) && field.ValueKind == JsonValueKind.Number && field.TryGetInt64(out var size)
                    ? size : null
                : ReadInt64(root, "size");

        private static string? ReadRequiredContentString(JsonElement root, string propertyName, JsonSerializerOptions options)
        {
            var value = ReadString(root, propertyName);
            if (value is null && AcpWireFormat.NegotiatedVersion(options) == AcpProtocolVersion.V2)
            {
                throw new JsonException($"ACP v2 content requires string '{propertyName}'.");
            }
            return value;
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

        internal static void WriteResourceLink(Utf8JsonWriter writer, ResourceLinkContentBlock value, JsonSerializerOptions options)
        {
            if (value.HasDraftIcons && AcpWireFormat.NegotiatedVersion(options) != AcpProtocolVersion.V2)
            {
                throw new JsonException("Authored resource icons require ACP v2 wire; received unknown fields remain passthrough.");
            }

            writer.WriteStartObject();
            writer.WriteString("type", value.Type);
            WriteAnnotations(writer, value.Annotations, options);
            writer.WriteString("uri", value.Uri);
            WriteNullableString(writer, "name", value.Name, options);
            WriteNullableString(writer, "mimeType", value.MimeType, options);
            WriteNullableString(writer, "title", value.Title, options);
            WriteNullableString(writer, "description", value.Description, options);
            WriteNullableNumber(writer, "size", value.Size, options);
            if (value.RawIcons is { } icons)
            {
                writer.WritePropertyName("icons");
                writer.WriteRawValue(icons.GetRawText());
            }

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

            // Lossless passthrough: write back the raw payload captured on read, without reordering fields or
            // dropping unknown properties. RawPayload is the single authoritative source of truth for an unknown
            // block (including its annotations/_meta), so nothing else is written alongside it, which avoids a
            // second state owner. WriteRawValue(GetRawText()) guarantees byte-level fidelity (mirrors
            // CustomMcpServer). When RawPayload is absent (for example a hand-constructed unknown block), fall back
            // to a minimal write of the known fields, still carrying the original type.
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
