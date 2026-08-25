using System.Text.Json.Serialization;

namespace SalmonEgg.Acp.Content
{
    /// <summary>
    /// A text content block.
    /// Represents plain text content.
    /// </summary>
    public sealed record TextContentBlock : ContentBlock
    {
        /// <summary>
        /// The content block type identifier, always "text".
        /// This property is marked [JsonIgnore]; the wire discriminator is read and written by hand in
        /// ContentBlockJsonConverter (which keeps RawPayload pass-through for unknown types).
        /// </summary>
        [JsonIgnore]
        public override string Type => "text";

        /// <summary>
        /// The text content.
        /// </summary>
        [JsonPropertyName("text")]
        public string Text { get; init; } = string.Empty;

        /// <summary>
        /// Creates a new text content block instance.
        /// </summary>
        public TextContentBlock()
        {
        }

        /// <summary>
        /// Creates a new text content block instance.
        /// </summary>
        /// <param name="text">The text content.</param>
        public TextContentBlock(string text)
        {
            Text = text;
        }
    }
}
