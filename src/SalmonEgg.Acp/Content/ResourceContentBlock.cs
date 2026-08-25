using System.Text.Json.Serialization;
using SalmonEgg.Acp.Content;

namespace SalmonEgg.Acp.Content
{
    /// <summary>
    /// A resource content block.
    /// Represents embedded resource data (either text or binary).
    /// </summary>
    public sealed record ResourceContentBlock : ContentBlock
    {
        /// <summary>
        /// The content block type identifier, always "resource".
        /// This property is excluded by [JsonIgnore]; the wire discriminator is read and written by hand in
        /// ContentBlockJsonConverter (which preserves RawPayload pass-through for unknown types).
        /// </summary>
        [JsonIgnore]
        public override string Type => "resource";

        /// <summary>
        /// The embedded resource object.
        /// Holds the resource's actual data (uri, mimeType, text or blob).
        /// </summary>
        [JsonPropertyName("resource")]
        public EmbeddedResource Resource { get; init; } = null!;

        /// <summary>
        /// Creates a new resource content block instance.
        /// </summary>
        public ResourceContentBlock()
        {
        }

        /// <summary>
        /// Creates a new resource content block instance.
        /// </summary>
        /// <param name="resource">The embedded resource object</param>
        public ResourceContentBlock(EmbeddedResource resource)
        {
            Resource = resource;
        }

        /// <summary>
        /// Creates a new text resource content block instance.
        /// </summary>
        /// <param name="uri">The resource URI</param>
        /// <param name="text">The text content</param>
        /// <param name="mimeType">The MIME type</param>
        public static ResourceContentBlock CreateText(string uri, string text, string mimeType = "text/plain")
        {
            return new ResourceContentBlock(EmbeddedResource.CreateText(uri, text, mimeType));
        }

        /// <summary>
        /// Creates a new binary resource content block instance.
        /// </summary>
        /// <param name="uri">The resource URI</param>
        /// <param name="blob">The Base64-encoded binary data</param>
        /// <param name="mimeType">The MIME type</param>
        public static ResourceContentBlock CreateBinary(string uri, string blob, string mimeType)
        {
            return new ResourceContentBlock(EmbeddedResource.CreateBinary(uri, blob, mimeType));
        }
    }
}
