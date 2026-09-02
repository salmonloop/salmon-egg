using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SalmonEgg.Acp.Tool
{
    /// <summary>
    /// Well-known <c>fileType</c> values on a <see cref="DiffChange"/>.
    /// </summary>
    [Experimental(AcpDraftProtocol.DiagnosticId, Message = AcpDraftProtocol.Message, UrlFormat = AcpDraftProtocol.UrlFormat)]
    public static class DiffFileTypeKind
    {
        /// <summary>A text file.</summary>
        public const string Text = "text";

        /// <summary>A binary file.</summary>
        public const string Binary = "binary";

        /// <summary>A directory.</summary>
        public const string Directory = "directory";

        /// <summary>A symbolic link.</summary>
        public const string Symlink = "symlink";
    }

    /// <summary>
    /// Well-known <c>operation</c> values on a <see cref="DiffChange"/>.
    /// </summary>
    [Experimental(AcpDraftProtocol.DiagnosticId, Message = AcpDraftProtocol.Message, UrlFormat = AcpDraftProtocol.UrlFormat)]
    public static class DiffOperationKind
    {
        /// <summary>The file was created.</summary>
        public const string Add = "add";

        /// <summary>The file was removed.</summary>
        public const string Delete = "delete";

        /// <summary>The file's contents changed.</summary>
        public const string Modify = "modify";

        /// <summary>The file was moved from <c>oldPath</c> to <c>path</c>.</summary>
        public const string Move = "move";

        /// <summary>The file was copied from <c>oldPath</c> to <c>path</c>.</summary>
        public const string Copy = "copy";
    }

    /// <summary>
    /// Well-known <c>format</c> values on a <see cref="DiffPatch"/>.
    /// </summary>
    [Experimental(AcpDraftProtocol.DiagnosticId, Message = AcpDraftProtocol.Message, UrlFormat = AcpDraftProtocol.UrlFormat)]
    public static class DiffPatchFormatKind
    {
        /// <summary>A git-style unified patch. Paths within it are absolute.</summary>
        public const string GitPatch = "git_patch";
    }

    /// <summary>
    /// A renderable patch accompanying a <see cref="StructuredDiff"/>.
    /// </summary>
    /// <remarks>
    /// Optional and advisory: <see cref="StructuredDiff.Changes"/> is authoritative, and the patch must
    /// be consistent with it. Clients must handle the patch being omitted.
    /// </remarks>
    [Experimental(AcpDraftProtocol.DiagnosticId, Message = AcpDraftProtocol.Message, UrlFormat = AcpDraftProtocol.UrlFormat)]
    public sealed record DiffPatch
    {
        /// <summary>
        /// The patch format. Required by the protocol.
        /// </summary>
        [JsonPropertyName("format")]
        public string Format { get; init; } = DiffPatchFormatKind.GitPatch;

        /// <summary>
        /// The patch text. Required by the protocol.
        /// </summary>
        [JsonPropertyName("text")]
        public string Text { get; init; } = string.Empty;
    }

    /// <summary>
    /// One file-level change within a <see cref="StructuredDiff"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <c>operation</c> discriminator selects which path fields are present: <c>add</c>,
    /// <c>delete</c>, and <c>modify</c> carry only <see cref="Path"/>, while <c>move</c> and
    /// <c>copy</c> also carry <see cref="OldPath"/>. <c>fileType</c> and <c>mimeType</c> are siblings
    /// of the discriminator rather than per-variant fields.
    /// </para>
    /// <para>
    /// Modeled as one open record rather than a closed variant hierarchy: the schema's trailing
    /// unconstrained member makes any <c>operation</c> string valid, and unknown values that do not
    /// begin with <c>_</c> are reserved for future ACP, so they are preserved rather than rejected.
    /// </para>
    /// </remarks>
    [Experimental(AcpDraftProtocol.DiagnosticId, Message = AcpDraftProtocol.Message, UrlFormat = AcpDraftProtocol.UrlFormat)]
    public sealed record DiffChange
    {
        /// <summary>
        /// What happened to the file. Required by the protocol.
        /// </summary>
        [JsonPropertyName("operation")]
        public string Operation { get; init; } = string.Empty;

        /// <summary>
        /// The absolute path the change applies to. Required for every known operation.
        /// </summary>
        [JsonPropertyName("path")]
        public string Path { get; init; } = string.Empty;

        /// <summary>
        /// The absolute source path, present only for <c>move</c> and <c>copy</c>.
        /// </summary>
        [JsonPropertyName("oldPath")]
        public string? OldPath { get; init; }

        /// <summary>
        /// The kind of file changed, when the Agent reports it.
        /// </summary>
        [JsonPropertyName("fileType")]
        public string? FileType { get; init; }

        /// <summary>
        /// The media type of the changed file, when the Agent reports it.
        /// </summary>
        [JsonPropertyName("mimeType")]
        public string? MimeType { get; init; }
    }

    /// <summary>
    /// V2 structured file diff produced by a tool call.
    /// </summary>
    /// <remarks>
    /// This is the v2 replacement for v1's flat <c>path</c>/<c>oldText</c>/<c>newText</c> diff, which
    /// could only describe one modified text file. The v1 shape remains on the public surface and stays
    /// correct for v1 connections; the two are separate variants rather than one type with dual meaning.
    /// </remarks>
    [Experimental(AcpDraftProtocol.DiagnosticId, Message = AcpDraftProtocol.Message, UrlFormat = AcpDraftProtocol.UrlFormat)]
    public sealed record StructuredDiff : ToolCallContent
    {
        private readonly List<DiffChange> _changes = new();

        /// <summary>
        /// The authoritative list of file changes. Required by the protocol, and always an array on the
        /// wire.
        /// </summary>
        [JsonPropertyName("changes")]
        public List<DiffChange> Changes
        {
            get => _changes;
            init
            {
                _changes.Clear();
                if (value is not null)
                {
                    _changes.AddRange(value);
                }
            }
        }

        /// <summary>
        /// An optional renderable patch, consistent with <see cref="Changes"/>.
        /// </summary>
        [JsonPropertyName("patch")]
        public DiffPatch? Patch { get; init; }
    }

    /// <summary>
    /// Reads and writes <see cref="StructuredDiff"/>, whose <c>type</c> discriminator is
    /// <c>diff</c> - the same value v1 uses for its flat diff shape.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two diff shapes share a discriminator, so they are told apart by structure: a <c>changes</c>
    /// array means the v2 form. Reading stays tolerant and version-agnostic, because a parser must keep
    /// accepting whatever the peer sends.
    /// </para>
    /// <para>
    /// Writing is fail-closed on the negotiated version: the structured form does not exist in v1, so
    /// emitting it under a v1 write context would hand a v1 Agent a <c>diff</c> payload missing the
    /// <c>oldText</c>/<c>newText</c> it expects.
    /// </para>
    /// </remarks>
    internal static class StructuredDiffWireFormat
    {
        internal const string V2OnlyMessage =
            "ACP structured tool call diff content is only available in protocolVersion 2.";

        internal static bool IsStructured(JsonElement root) =>
            root.TryGetProperty("changes", out var changes)
                && changes.ValueKind == JsonValueKind.Array;

        internal static StructuredDiff Read(JsonElement root)
        {
            var changes = new List<DiffChange>();
            if (root.TryGetProperty("changes", out var changesElement)
                && changesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in changesElement.EnumerateArray())
                {
                    // changes is marked x-deserialize-skip-invalid-items: drop an element this SDK cannot
                    // read rather than losing the whole diff along with the valid changes beside it.
                    if (element.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    changes.Add(ReadChange(element));
                }
            }

            return new StructuredDiff
            {
                Changes = changes,
                Patch = ReadPatch(root),
                Meta = Protocol.AcpMetaJson.Read(root)
            };
        }

        private static DiffChange ReadChange(JsonElement element) => new()
        {
            Operation = ReadString(element, "operation") ?? string.Empty,
            Path = ReadString(element, "path") ?? string.Empty,
            OldPath = ReadString(element, "oldPath"),
            FileType = ReadString(element, "fileType"),
            MimeType = ReadString(element, "mimeType")
        };

        private static DiffPatch? ReadPatch(JsonElement root)
        {
            if (!root.TryGetProperty("patch", out var patch) || patch.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return new DiffPatch
            {
                Format = ReadString(patch, "format") ?? string.Empty,
                Text = ReadString(patch, "text") ?? string.Empty
            };
        }

        private static string? ReadString(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        internal static void Write(Utf8JsonWriter writer, StructuredDiff value)
        {
            ArgumentNullException.ThrowIfNull(value);

            if (Protocol.AcpProtocolWriteContext.Current != Protocol.AcpProtocolVersion.V2)
            {
                throw new JsonException(V2OnlyMessage);
            }

            writer.WriteStartObject();
            writer.WriteString("type", "diff");
            writer.WritePropertyName("changes");
            writer.WriteStartArray();
            foreach (var change in value.Changes)
            {
                writer.WriteStartObject();
                writer.WriteString("operation", change.Operation);
                writer.WriteString("path", change.Path);
                if (change.OldPath is not null)
                {
                    writer.WriteString("oldPath", change.OldPath);
                }

                if (change.FileType is not null)
                {
                    writer.WriteString("fileType", change.FileType);
                }

                if (change.MimeType is not null)
                {
                    writer.WriteString("mimeType", change.MimeType);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            if (value.Patch is { } patch)
            {
                writer.WritePropertyName("patch");
                writer.WriteStartObject();
                writer.WriteString("format", patch.Format);
                writer.WriteString("text", patch.Text);
                writer.WriteEndObject();
            }

            Protocol.AcpMetaJson.Write(writer, value.Meta);
            writer.WriteEndObject();
        }
    }
}
