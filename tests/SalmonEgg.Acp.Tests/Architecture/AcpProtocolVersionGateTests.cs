using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using SalmonEgg.Acp.Protocol;
using Xunit;

namespace SalmonEgg.Acp.Tests.Architecture;

/// <summary>
/// Guards the wiring that turns the <see cref="AcpProtocolVersion.Latest"/> deprecation into a
/// repository-wide error. AGENTS.md requires that production code never reference the
/// modeled-version ceiling, and only SalmonEgg.Acp itself sets TreatWarningsAsErrors: every other
/// project would treat a reference to the draft ceiling as a warning. The escalation therefore
/// lives in Directory.Build.props, keyed by the diagnostic id declared next to the attribute.
/// </summary>
public sealed class AcpProtocolVersionGateTests
{
    // Scope note: this asserts that the escalation is declared, not that a compilation observes it.
    // Proving the latter needs a throwaway project that references Latest without suppression, which
    // belongs to the gate scripts rather than to an in-process test. What it does catch is the
    // failure mode that would otherwise be silent - renaming the id on one side only, which disarms
    // the gate without breaking any build.
    [Fact]
    public void DirectoryBuildProps_EscalatesTheLatestCeilingDiagnostic()
    {
        var props = XDocument.Load(FindRepositoryProps());

        var escalated = props
            .Descendants()
            .Where(static element => element.Name.LocalName == "WarningsAsErrors")
            .SelectMany(static element => element.Value.Split(';', StringSplitOptions.RemoveEmptyEntries))
            .Select(static id => id.Trim())
            .ToArray();

        Assert.Contains(AcpProtocolVersion.LatestRenamedDiagnosticId, escalated);
    }

    private static string FindRepositoryProps()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "Directory.Build.props"));
        if (File.Exists(path))
        {
            return path;
        }

        throw new FileNotFoundException($"Repository Directory.Build.props was not found at {path}.");
    }
}
