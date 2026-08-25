using System.Text.Json.Serialization;

namespace SalmonEgg.Acp.Content
{
    /// <summary>
    /// An audio content block.
    /// Represents Base64-encoded audio data.
    /// </summary>
    public sealed record AudioContentBlock : ContentBlock
    {
        /// <summary>
        /// The content block type identifier; always "audio".
        /// This property is ignored via [JsonIgnore]; the wire discriminator is read and written by hand in
        /// ContentBlockJsonConverter (which preserves RawPayload pass-through for unknown types).
        /// </summary>
        [JsonIgnore]
        public override string Type => "audio";

        /// <summary>
        /// The Base64-encoded audio data.
        /// </summary>
        [JsonPropertyName("data")]
        public string Data { get; init; } = string.Empty;

        /// <summary>
        /// The MIME type of the audio (for example "audio/wav" or "audio/mp3").
        /// </summary>
        [JsonPropertyName("mimeType")]
        public string MimeType { get; init; } = "audio/wav";

        /// <summary>
        /// Creates a new audio content block instance.
        /// </summary>
        public AudioContentBlock()
        {
        }

        /// <summary>
        /// Creates a new audio content block instance.
        /// </summary>
        /// <param name="data">The Base64-encoded audio data.</param>
        /// <param name="mimeType">The MIME type of the audio.</param>
        public AudioContentBlock(string data, string mimeType = "audio/wav")
        {
            Data = data;
            MimeType = mimeType;
        }
    }
}
