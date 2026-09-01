using System;
using System.IO;
using SalmonEgg.Infrastructure.Desktop.AcpSetup;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.AcpSetup;

/// <summary>
/// Guards which search-path sources an installation gets, and in what order.
/// </summary>
/// <remarks>
/// The two sources answer different questions: the login shell reports the toolchain the user activated —
/// the only route to a version manager that is a shell function — while the disk scan reports every
/// installed version, which the shell cannot. Order is the claim that the activated one wins, matching what
/// the user's own terminal would run.
/// </remarks>
public sealed class AcpSearchPathSourcesTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "search-sources-" + Guid.NewGuid().ToString("N"));

    public AcpSearchPathSourcesTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>
    /// With the CLI present, the shell capture leads: it reports the toolchain the user has activated, so
    /// its directories are the ones their terminal would search first.
    /// </summary>
    [Fact]
    public void Create_WithCliAvailable_ShouldPutTheShellSourceFirst()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows is deliberately not captured.");

        var cli = Path.Combine(_root, "salmon-egg");
        File.WriteAllText(cli, string.Empty);

        var sources = AcpSearchPathSources.Create(() => cli);

        Assert.Equal(2, sources.Count);
        Assert.IsType<LoginShellSearchPathSource>(sources[0]);
        Assert.IsType<ToolchainScanSearchPathSource>(sources[1]);
    }

    /// <summary>
    /// An explicitly absent provider leaves the scan as the only widening. The normal composition does
    /// not take this branch: it falls back to the running desktop app, which implements the mode itself.
    /// Keeping this injection seam matters because it proves a missing provider degrades to the scan
    /// rather than making construction fail.
    /// </summary>
    [Fact]
    public void Create_WithExplicitlyAbsentProvider_ShouldStillProvideTheDiskScan()
    {
        var sources = AcpSearchPathSources.Create(() => null);

        Assert.IsType<ToolchainScanSearchPathSource>(Assert.Single(sources));
    }

    /// <summary>
    /// A provider path that does not exist is treated as absent. Building a command around it would
    /// produce a shell failure indistinguishable from a user's broken rc file.
    /// </summary>
    [Fact]
    public void Create_WithMissingProviderPath_ShouldStillProvideTheDiskScan()
    {
        var sources = AcpSearchPathSources.Create(() => Path.Combine(_root, "not-installed"));

        Assert.IsType<ToolchainScanSearchPathSource>(Assert.Single(sources));
    }

    /// <summary>
    /// The scan is always present, on every platform: it is the only widening available on Windows, where
    /// there is no profile-built PATH to recover.
    /// </summary>
    [Fact]
    public void Create_OnAnyPlatform_ShouldAlwaysIncludeTheDiskScan()
        => Assert.Contains(
            AcpSearchPathSources.Create(() => null),
            source => source is ToolchainScanSearchPathSource);

    /// <summary>
    /// The factory and the writer share one constant now, so the protocol cannot drift merely because a
    /// second executable was added. This pins its externally observed spelling.
    /// </summary>
    [Fact]
    public void PrintEnvironmentOptionName_ShouldBeStable()
        => Assert.Equal("--printenv", PrintEnvironmentCommandFactory.OptionName);

    /// <summary>
    /// A desktop-only install still has an executable that answers the printing protocol: this process.
    /// Without that fallback nvm users lose the only source that can report their activated version.
    /// </summary>
    [Fact]
    public void ResolvePrintEnvironmentExecutable_ShouldFallBackToAnExistingProcess()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows is deliberately not captured.");

        var executable = AcpSearchPathSources.ResolvePrintEnvironmentExecutable();

        Assert.False(string.IsNullOrWhiteSpace(executable));
        Assert.True(File.Exists(executable));
    }

    /// <summary>
    /// The normal composition includes the login shell source even without the separately packaged CLI,
    /// because the desktop process can answer --printenv itself.
    /// </summary>
    [Fact]
    public void Create_DefaultComposition_ShouldIncludeShellSourceOffWindows()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows is deliberately not captured.");

        var sources = AcpSearchPathSources.Create();

        Assert.Equal(2, sources.Count);
        Assert.IsType<LoginShellSearchPathSource>(sources[0]);
        Assert.IsType<ToolchainScanSearchPathSource>(sources[1]);
    }

    /// <summary>
    /// The command quotes every path, because a shell parses the whole string and paths routinely contain
    /// spaces.
    /// </summary>
    [Fact]
    public void TryCreate_WithPathContainingSpaces_ShouldQuoteIt()
    {
        var directory = Path.Combine(_root, "Program Files");
        Directory.CreateDirectory(directory);
        var executable = Path.Combine(directory, "salmon-egg");
        File.WriteAllText(executable, string.Empty);

        var command = PrintEnvironmentCommandFactory.TryCreate(executable)!("MARK");

        Assert.Contains("'" + executable + "'", command, StringComparison.Ordinal);
        Assert.Contains("'--printenv=MARK'", command, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryCreate_WithoutAnExecutable_ShouldReturnNull(string? executable)
        => Assert.Null(PrintEnvironmentCommandFactory.TryCreate(executable));
}
