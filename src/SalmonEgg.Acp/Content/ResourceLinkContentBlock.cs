using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SalmonEgg.Acp.Content
{
    /// <summary>
    /// Resource link content block.
    /// Represents a reference to an external resource (a URI link).
    /// </summary>
    [JsonConverter(typeof(ResourceLinkContentBlockJsonConverter))]
    public sealed record ResourceLinkContentBlock : ContentBlock
    {
        /// <summary>
        /// Content block type identifier, always "resource_link".
        /// This property is excluded by [JsonIgnore]; the wire discriminator is read and written by hand in
        /// ContentBlockJsonConverter (which preserves RawPayload passthrough for unknown types).
        /// </summary>
        [JsonIgnore]
        public override string Type => "resource_link";

        /// <summary>
        /// URI identifying the resource.
        /// </summary>
        [JsonPropertyName("uri")]
        public string Uri { get; init; } = string.Empty;

        /// <summary>
        /// Name of the resource (optional).
        /// </summary>
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        /// <summary>
        /// MIME type of the resource (optional).
        /// </summary>
        [JsonPropertyName("mimeType")]
        public string? MimeType { get; init; }

        /// <summary>
        /// Title of the resource (optional).
        /// </summary>
        [JsonPropertyName("title")]
        public string? Title { get; init; }

        /// <summary>
        /// Description of the resource (optional).
        /// </summary>
        [JsonPropertyName("description")]
        public string? Description { get; init; }

        /// <summary>
        /// Size of the resource, in bytes (optional).
        /// </summary>
        [JsonPropertyName("size")]
        public long? Size { get; init; }

        [JsonIgnore]
        internal JsonElement? RawIcons { get; init; }

        [JsonIgnore]
        internal bool HasDraftIcons { get; init; }

        /// <summary>
        /// Creates a new resource link content block instance.
        /// </summary>
        public ResourceLinkContentBlock()
        {
        }

        /// <summary>
        /// Creates a new resource link content block instance.
        /// </summary>
        /// <param name="uri">Resource URI</param>
        /// <param name="name">Resource name</param>
        /// <param name="mimeType">MIME type</param>
        /// <param name="title">Title</param>
        /// <param name="description">Description</param>
        /// <param name="size">Size, in bytes</param>
        public ResourceLinkContentBlock(
            string uri,
            string? name = null,
            string? mimeType = null,
            string? title = null,
            string? description = null,
            long? size = null)
        {
            Uri = uri;
            Name = name;
            MimeType = mimeType;
            Title = title;
            Description = description;
            Size = size;
        }
    }

    internal sealed class ResourceLinkContentBlockJsonConverter : JsonConverter<ResourceLinkContentBlock>
    {
        public override ResourceLinkContentBlock? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            return ContentBlockJsonConverter.ReadResourceLink(document.RootElement);
        }

        public override void Write(Utf8JsonWriter writer, ResourceLinkContentBlock value, JsonSerializerOptions options)
            => ContentBlockJsonConverter.WriteResourceLink(writer, value, options);
    }
}
