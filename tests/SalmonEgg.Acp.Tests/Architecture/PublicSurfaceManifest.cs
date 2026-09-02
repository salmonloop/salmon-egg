using System.Collections.Immutable;

namespace SalmonEgg.Acp.Tests.Architecture;

/// <summary>
/// Reader for <c>src/SalmonEgg.Acp/PublicSurface.Types.txt</c>, the pinned and classified public
/// surface of the SDK.
/// </summary>
/// <remarks>
/// Shared by every gate that reads the manifest so the format is parsed once. The parse is strict on
/// purpose: an entry without a classification is an error rather than a defaulted "stable", because
/// defaulting is exactly how a new draft type would slip onto the supported surface unnoticed.
/// </remarks>
internal sealed record PublicSurfaceManifest
{
    private const string DraftTag = "draft";
    private const string StableTag = "stable";

    private PublicSurfaceManifest(ImmutableArray<string> allTypeNames, ImmutableHashSet<string> draftTypeNames)
    {
        AllTypeNames = allTypeNames;
        DraftTypeNames = draftTypeNames;
    }

    /// <summary>Every pinned exported type name, in file order (ordinal by name).</summary>
    internal ImmutableArray<string> AllTypeNames { get; }

    /// <summary>The subset classified as v2 draft surface.</summary>
    internal ImmutableHashSet<string> DraftTypeNames { get; }

    internal static PublicSurfaceManifest Load()
    {
        var path = FindManifest();
        var names = ImmutableArray.CreateBuilder<string>();
        var draft = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);

        var lineNumber = 0;
        foreach (var raw in File.ReadAllLines(path))
        {
            lineNumber++;
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            Assert.True(
                fields.Length == 2,
                $"{path}({lineNumber}): expected '<type name> <{StableTag}|{DraftTag}>', found '{line}'. "
                + "Every exported type must declare whether it is stable or v2 draft surface.");

            var name = fields[0];
            var tag = fields[1];
            Assert.True(
                tag is StableTag or DraftTag,
                $"{path}({lineNumber}): '{name}' is classified '{tag}', which is neither "
                + $"'{StableTag}' nor '{DraftTag}'.");

            names.Add(name);
            if (tag == DraftTag)
            {
                draft.Add(name);
            }
        }

        Assert.NotEmpty(names);
        return new PublicSurfaceManifest(names.ToImmutable(), draft.ToImmutable());
    }

    private static string FindManifest()
    {
        // Copied next to the test binary by the test project; the repository copy is the fallback so
        // the gate still runs when the manifest is read outside a normal test output layout.
        var outputManifest = Path.Combine(AppContext.BaseDirectory, "PublicSurface.Types.txt");
        if (File.Exists(outputManifest))
        {
            return outputManifest;
        }

        var repoManifest = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "SalmonEgg.Acp", "PublicSurface.Types.txt"));
        if (File.Exists(repoManifest))
        {
            return repoManifest;
        }

        throw new FileNotFoundException("PublicSurface.Types.txt manifest was not found.");
    }
}
