using System.Collections.Immutable;

namespace SalmonEgg.Acp.Tests.Architecture;

/// <summary>
/// Reader for <c>src/SalmonEgg.Acp/SchemaTolerance.Fields.txt</c>, the distilled list of fields the
/// upstream ACP schema marks as tolerant.
/// </summary>
/// <remarks>
/// The upstream schema states its own leniency through <c>x-deserialize-default-on-error</c> and
/// <c>x-deserialize-skip-invalid-items</c>. A reader that throws where the schema carries one of those
/// markers is stricter than the protocol, which AGENTS.md forbids, so the marker set is the ground
/// truth every tolerance gate reads. The parse is strict on purpose: a line whose tag column is not a
/// recognised combination fails rather than defaulting, because a silently ignored tag is how a new
/// tolerant field would arrive without anyone deciding whether the reader honours it.
/// </remarks>
internal sealed record SchemaToleranceManifest
{
    private const string DefaultTag = "default";
    private const string SkipItemsTag = "skip-items";

    private SchemaToleranceManifest(ImmutableArray<SchemaToleranceField> fields) => Fields = fields;

    /// <summary>Every marked field, in file order.</summary>
    internal ImmutableArray<SchemaToleranceField> Fields { get; }

    /// <summary>The distinct schema type names that carry at least one tolerance marker.</summary>
    internal ImmutableHashSet<string> MarkedTypeNames =>
        Fields.Select(marked => marked.SchemaType).ToImmutableHashSet(StringComparer.Ordinal);

    internal static SchemaToleranceManifest Load()
    {
        var path = FindManifest();
        var fields = ImmutableArray.CreateBuilder<SchemaToleranceField>();

        var lineNumber = 0;
        foreach (var raw in File.ReadAllLines(path))
        {
            lineNumber++;
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            var columns = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            Assert.True(
                columns.Length == 4,
                $"{path}({lineNumber}): expected '<v1|v2> <SchemaType> <fieldPath> <tags>', found '{line}'.");

            var version = columns[0];
            Assert.True(
                version is "v1" or "v2",
                $"{path}({lineNumber}): '{version}' is not a known protocol version.");

            var tags = columns[3].Split('+', StringSplitOptions.RemoveEmptyEntries);
            foreach (var tag in tags)
            {
                Assert.True(
                    tag is DefaultTag or SkipItemsTag,
                    $"{path}({lineNumber}): tag '{tag}' is neither '{DefaultTag}' nor '{SkipItemsTag}'.");
            }

            fields.Add(new SchemaToleranceField(
                version,
                columns[1],
                columns[2],
                DefaultsOnError: tags.Contains(DefaultTag),
                SkipsInvalidItems: tags.Contains(SkipItemsTag)));
        }

        Assert.NotEmpty(fields);
        return new SchemaToleranceManifest(fields.ToImmutable());
    }

    private static string FindManifest()
    {
        // Copied next to the test binary by the SDK project; the repository copy is the fallback so the
        // gate still runs when the manifest is read outside a normal test output layout.
        var outputManifest = Path.Combine(AppContext.BaseDirectory, "SchemaTolerance.Fields.txt");
        if (File.Exists(outputManifest))
        {
            return outputManifest;
        }

        var repoManifest = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "SalmonEgg.Acp", "SchemaTolerance.Fields.txt"));
        if (File.Exists(repoManifest))
        {
            return repoManifest;
        }

        throw new FileNotFoundException("SchemaTolerance.Fields.txt manifest was not found.");
    }
}

/// <summary>One field the upstream schema marks as tolerant.</summary>
/// <param name="Version">The protocol version whose schema carries the marker.</param>
/// <param name="SchemaType">The upstream <c>$defs</c> type name that owns the field.</param>
/// <param name="FieldPath">The field, with <c>[]</c> appended for a marker that sits on array items.</param>
/// <param name="DefaultsOnError">An unreadable value falls back to the field default.</param>
/// <param name="SkipsInvalidItems">An unreadable array element is dropped, the rest survive.</param>
internal sealed record SchemaToleranceField(
    string Version,
    string SchemaType,
    string FieldPath,
    bool DefaultsOnError,
    bool SkipsInvalidItems);
