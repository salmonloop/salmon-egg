using System.Text.Json.Serialization;
using SalmonEgg.Acp.Protocol;

namespace SalmonEgg.Acp.Tool
{
    /// <summary>
    /// Represents a file location affected by a tool call.
    /// Used for "follow-along" features that track which files the Agent is accessing or modifying.
    /// </summary>
    public sealed record ToolCallLocation : AcpProtocolObject
    {
        /// <summary>
        /// The absolute file path being accessed or modified.
        /// </summary>
        [JsonPropertyName("path")]
        public string Path { get; init; } = string.Empty;

        /// <summary>
        /// Optional line number within the file.
        /// </summary>
        [JsonPropertyName("line")]
        public uint? Line { get; init; }

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
        public ToolCallLocation(string path, uint? line = null)
        {
            Path = path;
            Line = line;
        }
    }
}
