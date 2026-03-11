using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using SalmonEgg.Domain.Models.Content;

namespace SalmonEgg.Domain.Models.Tool
{
    /// <summary>
    /// Tool call content types.
    /// Represents different types of content that can be produced by a tool call.
    /// </summary>
    [JsonPolymorphic(
        TypeDiscriminatorPropertyName = "type",
        UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FallBackToBaseType,
        IgnoreUnrecognizedTypeDiscriminators = true)]
    [JsonDerivedType(typeof(ContentToolCallContent), "content")]
    [JsonDerivedType(typeof(DiffToolCallContent), "diff")]
    [JsonDerivedType(typeof(TerminalToolCallContent), "terminal")]
    public abstract class ToolCallContent
    {
        /// <summary>
        /// Extension data for future protocol extensions.
        /// </summary>
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    /// <summary>
    /// Regular content produced by a tool call.
    /// </summary>
    public class ContentToolCallContent : ToolCallContent
    {
        /// <summary>
        /// The content block.
        /// </summary>
        [JsonPropertyName("content")]
        public ContentBlock? Content { get; set; }

        /// <summary>
        /// Creates a new ContentToolCallContent instance.
        /// </summary>
        public ContentToolCallContent()
        {
        }

        /// <summary>
        /// Creates a new ContentToolCallContent instance.
        /// </summary>
        /// <param name="content">The content block</param>
        public ContentToolCallContent(ContentBlock? content)
        {
            Content = content;
        }
    }

    /// <summary>
    /// File diff produced by a tool call.
    /// </summary>
    public class DiffToolCallContent : ToolCallContent
    {
        /// <summary>
        /// The absolute file path being modified.
        /// </summary>
        [JsonPropertyName("path")]
        public string? Path { get; set; }

        /// <summary>
        /// The original content (null for new files).
        /// </summary>
        [JsonPropertyName("oldText")]
        public string? OldText { get; set; }

        /// <summary>
        /// The new content after modification.
        /// </summary>
        [JsonPropertyName("newText")]
        public string? NewText { get; set; }

        /// <summary>
        /// Creates a new DiffToolCallContent instance.
        /// </summary>
        public DiffToolCallContent()
        {
        }

        /// <summary>
        /// Creates a new DiffToolCallContent instance.
        /// </summary>
        /// <param name="path">The absolute file path being modified</param>
        /// <param name="oldText">The original content (null for new files)</param>
        /// <param name="newText">The new content after modification</param>
        public DiffToolCallContent(string? path = null, string? oldText = null, string? newText = null)
        {
            Path = path;
            OldText = oldText;
            NewText = newText;
        }
    }

    /// <summary>
    /// Terminal output produced by a tool call.
    /// </summary>
    public class TerminalToolCallContent : ToolCallContent
    {
        /// <summary>
        /// The ID of a terminal created with terminal/create.
        /// </summary>
        [JsonPropertyName("terminalId")]
        public string? TerminalId { get; set; }

        /// <summary>
        /// Creates a new TerminalToolCallContent instance.
        /// </summary>
        public TerminalToolCallContent()
        {
        }

        /// <summary>
        /// Creates a new TerminalToolCallContent instance.
        /// </summary>
        /// <param name="terminalId">The ID of a terminal</param>
        public TerminalToolCallContent(string? terminalId)
        {
            TerminalId = terminalId;
        }
    }
}