namespace SalmonEgg.Acp.Tests.Architecture;

/// <summary>
/// Verifies that the tolerance manifest exists and can be parsed. Does not police the implementation —
/// the manifest is the source of truth for "what the protocol allows"; separate probes verify the reader
/// honours it.
/// </summary>
public sealed class SchemaToleranceManifestTests
{
    [Fact]
    public void ManifestFileExists()
    {
        var manifest = SchemaToleranceManifest.Load();
        Assert.NotNull(manifest);
    }

    [Fact]
    public void ManifestContainsKnownToleranceFields()
    {
        var manifest = SchemaToleranceManifest.Load();
        Assert.NotEmpty(manifest.Fields);

        // At least one of each marker type should exist
        Assert.Contains(manifest.Fields, f => f.DefaultsOnError);
        Assert.Contains(manifest.Fields, f => f.SkipsInvalidItems);
    }

    [Fact]
    public void ManifestCoversKnownProbePoints()
    {
        // Every field a tolerance probe exercises, with the leniency the upstream schema states for it.
        // Asserting the marker and not merely the line means a future upstream edit that downgrades a
        // field from skip-items to default-only turns this red, instead of passing on name presence.
        // These are schema paths: the SDK models ContentBlock as one discriminated union, while the
        // schema splits it into TextContent, ImageContent and friends.
        var expected = new (string Version, string SchemaType, string FieldPath, bool Default, bool SkipItems)[]
        {
            ("v1", "TextContent", "_meta", true, false),
            ("v1", "Annotations", "priority", true, false),
            ("v1", "Annotations", "audience", true, true),
            ("v1", "ResourceLink", "size", true, false),
            ("v1", "ToolCallUpdate", "status", true, false),
            ("v1", "ToolCallUpdate", "kind", true, false),
            ("v1", "ToolCallUpdate", "content", true, true),
            ("v1", "Plan", "entries", true, true),
            ("v1", "SessionModeState", "availableModes", true, true),
            ("v1", "SessionMode", "description", true, false),
            ("v1", "SessionConfigSelectGroup", "options", true, true),
        };

        var manifest = SchemaToleranceManifest.Load();
        var lookup = manifest.Fields.ToDictionary(
            marked => (marked.Version, marked.SchemaType, marked.FieldPath),
            marked => marked);

        var mismatches = new List<string>();
        foreach (var (version, schemaType, fieldPath, wantDefault, wantSkipItems) in expected)
        {
            if (!lookup.TryGetValue((version, schemaType, fieldPath), out var marked))
            {
                mismatches.Add($"{version} {schemaType}.{fieldPath}: absent from the manifest");
                continue;
            }

            if (marked.DefaultsOnError != wantDefault || marked.SkipsInvalidItems != wantSkipItems)
            {
                mismatches.Add(
                    $"{version} {schemaType}.{fieldPath}: manifest says "
                    + $"default={marked.DefaultsOnError} skip-items={marked.SkipsInvalidItems}, "
                    + $"probe assumes default={wantDefault} skip-items={wantSkipItems}");
            }
        }

        Assert.Empty(mismatches);
    }

    [Fact]
    public void ManifestCoversMultipleProtocolVersions()
    {
        var manifest = SchemaToleranceManifest.Load();
        var versions = manifest.Fields.Select(f => f.Version).Distinct().ToList();

        Assert.Contains("v1", versions);
        Assert.Contains("v2", versions);
    }

    [Fact]
    public void ManifestCoversMultipleSchemaTypes()
    {
        var manifest = SchemaToleranceManifest.Load();
        Assert.True(manifest.MarkedTypeNames.Count >= 10,
            "Expected at least 10 distinct schema types with tolerance markers");
    }
}
