using System.Text.Json.Serialization;

namespace SalmonEgg.Domain.Models.Tool
{
    /// <summary>
    /// Represents a file location affected by a tool call.
    /// Used for "follow-along" features that track which files the Agent is accessing or modifying.
    /// </summary>
    public class ToolCallLocation
    {
        /// <summary>
        /// The absolute file path being accessed or modified.
        /// </summary>
        [JsonPropertyName("path")]
        public string? Path { get; set; }

        /// <summary>
        /// Optional line number within the file.
        /// </summary>
        [JsonPropertyName("line")]
        public int? Line { get; set; }

        /// <summary>
        /// Creates a new ToolCallLocation instance.
        /// </summary>
        public ToolCallLocation()
        {
        }

        /// <summary>
        /// Creates a new ToolCallLocation instance.
        /// </summary>
        /// <param name="path">The absolute file path</param>
        /// <param name="line">Optional line number</param>
        public ToolCallLocation(string? path = null, int? line = null)
        {
            Path = path;
            Line = line;
        }
    }
}