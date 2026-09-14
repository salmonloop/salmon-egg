using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using SalmonEgg.Acp.Serialization;

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
    [JsonConverter(typeof(DiffPatchJsonConverter))]
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
    [JsonConverter(typeof(DiffChangeJsonConverter))]
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

        /// <summary>Extension metadata supplied by the Agent for this change.</summary>
        [JsonPropertyName("_meta")]
        public Dictionary<string, object?>? Meta { get; init; }

        /// <summary>
        /// The complete raw payload of a change whose <c>operation</c> is not one of the known values.
        /// The schema's trailing <c>other</c> branch requires only a string <c>operation</c> and allows
        /// additional properties, so the client preserves the whole object verbatim instead of dropping
        /// unknown fields (AGENTS.md: forward-compatible raw preservation, same pattern as
        /// <see cref="CustomToolCallContent"/>). Empty for known operations and hand-built instances.
        /// </summary>
        [JsonIgnore]
        public JsonElement RawPayload { get; init; }
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
    [JsonConverter(typeof(StructuredDiffJsonConverter))]
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

    internal sealed class DiffChangeJsonConverter : JsonConverter<DiffChange>
    {
        public override DiffChange Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            return StructuredDiffWireFormat.ReadChange(document.RootElement)
                ?? throw new JsonException("Diff change is missing a required operation or path.");
        }

        public override void Write(Utf8JsonWriter writer, DiffChange value, JsonSerializerOptions options)
            => StructuredDiffWireFormat.WriteChange(writer, value);
    }

    internal sealed class DiffPatchJsonConverter : JsonConverter<DiffPatch>
    {
        public override DiffPatch Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            return StructuredDiffWireFormat.ReadPatch(document.RootElement)
                ?? throw new JsonException("Diff patch requires string format and text fields.");
        }

        public override void Write(Utf8JsonWriter writer, DiffPatch value, JsonSerializerOptions options)
            => StructuredDiffWireFormat.WritePatch(writer, value);
    }

    internal sealed class StructuredDiffJsonConverter : JsonConverter<StructuredDiff>
    {
        public override StructuredDiff Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            if (!StructuredDiffWireFormat.IsStructured(document.RootElement))
            {
                throw new JsonException("Structured diff content requires an array 'changes'.");
            }

            return StructuredDiffWireFormat.Read(document.RootElement);
        }

        public override void Write(Utf8JsonWriter writer, StructuredDiff value, JsonSerializerOptions options)
            => StructuredDiffWireFormat.Write(writer, value, options);
    }

    /// <summary>
    /// Reads and writes <see cref="StructuredDiff"/>, whose <c>type</c> discriminator is
    /// <c>diff</c> - the same value v1 uses for its flat diff shape.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two diff shapes share a discriminator, so they are told apart by structure: a <c>changes</c>
    /// array means the v2 form. The parent converter selects this contract only for v2; on v1 it
    /// preserves the structured payload as custom content without interpreting it.
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
            root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("changes", out var changes)
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
                    if (ReadChange(element) is not { } change)
                    {
                        continue;
                    }

                    changes.Add(change);
                }
            }

            return new StructuredDiff
            {
                Changes = changes,
                Patch = root.TryGetProperty("patch", out var patch) ? ReadPatch(patch) : null,
                Meta = Protocol.AcpMetaJson.ReadOrDefault(root)
            };
        }

        internal static DiffChange? ReadChange(JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var operation = ReadString(element, "operation");
            if (operation is null)
            {
                // Every DiffChange branch requires a string operation; without one the item is invalid.
                return null;
            }

            if (!IsKnownOperation(operation))
            {
                // Unknown operation: the "other" branch keeps the raw payload with its extra fields.
                return new DiffChange
                {
                    Operation = operation,
                    RawPayload = element.Clone(),
                    FileType = ReadString(element, "fileType"),
                    MimeType = ReadString(element, "mimeType"),
                    Meta = Protocol.AcpMetaJson.ReadOrDefault(element)
                };
            }

            var path = ReadString(element, "path");
            if (path is null)
            {
                // add/delete/modify require path; move/copy require both oldPath and path.
                return null;
            }

            if (operation is DiffOperationKind.Move or DiffOperationKind.Copy
                && ReadString(element, "oldPath") is null)
            {
                return null;
            }

            return new DiffChange
            {
                Operation = operation,
                Path = path,
                OldPath = ReadString(element, "oldPath"),
                FileType = ReadString(element, "fileType"),
                MimeType = ReadString(element, "mimeType"),
                Meta = Protocol.AcpMetaJson.ReadOrDefault(element)
            };
        }

        private static bool IsKnownOperation(string operation) =>
            operation is DiffOperationKind.Add
                or DiffOperationKind.Delete
                or DiffOperationKind.Modify
                or DiffOperationKind.Move
                or DiffOperationKind.Copy;

        internal static DiffPatch? ReadPatch(JsonElement patch)
        {
            if (patch.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            // DiffPatch.required=[format,text]; a patch that fails to bind is default-on-error -> null.
            var format = ReadString(patch, "format");
            var text = ReadString(patch, "text");
            if (format is null || text is null)
            {
                return null;
            }

            return new DiffPatch
            {
                Format = format,
                Text = text
            };
        }

        private static string? ReadString(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        internal static void WriteChange(Utf8JsonWriter writer, DiffChange change)
        {
            if (change.RawPayload.ValueKind == JsonValueKind.Object)
            {
                // Unknown operation preserved on read: write the raw payload back verbatim.
                writer.WriteRawValue(change.RawPayload.GetRawText());
                return;
            }

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

            Protocol.AcpMetaJson.Write(writer, change.Meta);
            writer.WriteEndObject();
        }

        internal static void WritePatch(Utf8JsonWriter writer, DiffPatch patch)
        {
            writer.WriteStartObject();
            writer.WriteString("format", patch.Format);
            writer.WriteString("text", patch.Text);
            writer.WriteEndObject();
        }

        internal static void Write(Utf8JsonWriter writer, StructuredDiff value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(value);

            if (Serialization.AcpWireFormat.NegotiatedVersion(options) != Protocol.AcpProtocolVersion.V2)
            {
                throw new JsonException(V2OnlyMessage);
            }

            writer.WriteStartObject();
            writer.WriteString("type", "diff");
            writer.WritePropertyName("changes");
            writer.WriteStartArray();
            foreach (var change in value.Changes)
            {
                WriteChange(writer, change);
            }

            writer.WriteEndArray();

            if (value.Patch is { } patch)
            {
                writer.WritePropertyName("patch");
                WritePatch(writer, patch);
            }

            Protocol.AcpMetaJson.Write(writer, value.Meta);
            writer.WriteEndObject();
        }
    }
}
