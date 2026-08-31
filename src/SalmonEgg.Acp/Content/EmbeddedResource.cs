using System.Text.Json.Serialization;
using SalmonEgg.Acp.Protocol;

namespace SalmonEgg.Acp.Content
{
    /// <summary>
    /// An embedded resource object.
    /// Carries the actual resource data contained in a ResourceContentBlock.
    /// </summary>
    public sealed record EmbeddedResource : AcpProtocolObject
    {
        /// <summary>
        /// The URI identifier of the resource.
        /// </summary>
        [JsonPropertyName("uri")]
        public string Uri { get; init; } = string.Empty;

        /// <summary>
        /// The MIME type of the resource (for example "text/plain", "application/json").
        /// </summary>
        [JsonPropertyName("mimeType")]
        public string MimeType { get; init; } = "text/plain";

        /// <summary>
        /// The textual content of the resource, when the resource is text.
        /// Mutually exclusive with Blob.
        /// </summary>
        [JsonPropertyName("text")]
        public string? Text { get; init; }

        /// <summary>
        /// The binary data of the resource (Base64 encoded), when the resource is binary.
        /// Mutually exclusive with Text.
        /// </summary>
        [JsonPropertyName("blob")]
        public string? Blob { get; init; }

        /// <summary>
        /// Creates a new EmbeddedResource instance.
        /// </summary>
        public EmbeddedResource()
        {
        }

        /// <summary>
        /// Creates a new text resource instance.
        /// </summary>
        /// <param name="uri">The resource URI</param>
        /// <param name="text">The textual content</param>
        /// <param name="mimeType">The MIME type</param>
        public EmbeddedResource(string uri, string text, string mimeType = "text/plain")
        {
            Uri = uri;
            Text = text;
            MimeType = mimeType;
        }

        /// <summary>
        /// Creates a new binary resource instance.
        /// </summary>
        /// <param name="uri">The resource URI</param>
        /// <param name="blob">The Base64 encoded binary data</param>
        /// <param name="mimeType">The MIME type</param>
        /// <param name="isBinary">Unused; distinguishes this overload from the text constructor</param>
        public EmbeddedResource(string uri, string blob, string mimeType, bool isBinary)
        {
            Uri = uri;
            Blob = blob;
            MimeType = mimeType;
        }

        /// <summary>
        /// Static helper method: creates a text resource.
        /// </summary>
        /// <param name="uri">The resource URI</param>
        /// <param name="text">The textual content</param>
        /// <param name="mimeType">The MIME type</param>
        /// <returns>An EmbeddedResource instance</returns>
        public static EmbeddedResource CreateText(string uri, string text, string mimeType = "text/plain")
        {
            return new EmbeddedResource(uri, text, mimeType);
        }

        /// <summary>
        /// Static helper method: creates a binary resource.
        /// </summary>
        /// <param name="uri">The resource URI</param>
        /// <param name="blob">The Base64 encoded binary data</param>
        /// <param name="mimeType">The MIME type</param>
        /// <returns>An EmbeddedResource instance</returns>
        public static EmbeddedResource CreateBinary(string uri, string blob, string mimeType)
        {
            return new EmbeddedResource(uri, blob, mimeType, true);
        }

        /// <summary>
        /// Indicates whether the resource is a text resource.
        /// </summary>
        public bool IsText => !string.IsNullOrEmpty(Text);

        /// <summary>
        /// Indicates whether the resource is a binary resource.
        /// </summary>
        public bool IsBinary => !string.IsNullOrEmpty(Blob);
    }
}
