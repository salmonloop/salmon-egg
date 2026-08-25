using System.Text.Json.Serialization;

namespace SalmonEgg.Acp.Content
{
    /// <summary>
    /// An image content block.
    /// Represents Base64-encoded image data.
    /// </summary>
    public sealed record ImageContentBlock : ContentBlock
    {
        /// <summary>
        /// The content block type identifier; always "image".
        /// This property is excluded by [JsonIgnore]; the wire discriminator is read and written by hand in
        /// ContentBlockJsonConverter (which also passes unknown types through via RawPayload).
        /// </summary>
        [JsonIgnore]
        public override string Type => "image";

        /// <summary>
        /// The Base64-encoded image data.
        /// </summary>
        [JsonPropertyName("data")]
        public string Data { get; init; } = string.Empty;

        /// <summary>
        /// Optional URI reference for the image source.
        /// </summary>
        [JsonPropertyName("uri")]
        public string? Uri { get; init; }

        /// <summary>
        /// The MIME type of the image (for example "image/png", "image/jpeg").
        /// </summary>
        [JsonPropertyName("mimeType")]
        public string MimeType { get; init; } = "image/png";

        /// <summary>
        /// Creates a new image content block instance.
        /// </summary>
        public ImageContentBlock()
        {
        }

        /// <summary>
        /// Creates a new image content block instance.
        /// </summary>
        /// <param name="data">The Base64-encoded image data</param>
        /// <param name="mimeType">The MIME type of the image</param>
        /// <param name="uri">Optional URI reference for the image source</param>
        public ImageContentBlock(string data, string mimeType = "image/png", string? uri = null)
        {
            Data = data;
            MimeType = mimeType;
            Uri = uri;
        }
    }
}
