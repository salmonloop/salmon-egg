using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models.AcpSetup;
using SalmonEgg.Infrastructure.Desktop.AcpSetup;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.AcpSetup;

/// <summary>
/// Guards the on-disk scan that reveals toolchain versions the user has not activated.
/// </summary>
/// <remarks>
/// A version manager puts only its current version on PATH, so capturing the shell environment reports one
/// node however many are installed. This source supplies the rest, which is what lets the wizard say an
/// agent exists under a version the user is not currently using — otherwise indistinguishable from not
/// having it at all.
/// </remarks>
public sealed class ToolchainScanSearchPathSourceTests : IDisposable
{
    private readonly string _home = Path.Combine(
        Path.GetTempPath(),
        "toolchain-scan-" + Guid.NewGuid().ToString("N"));

    public ToolchainScanSearchPathSourceTests() => Directory.CreateDirectory(_home);

    public void Dispose()
    {
        if (Directory.Exists(_home))
        {
            Directory.Delete(_home, recursive: true);
        }
    }

    /// <summary>
    /// The point of the source: every installed version is reported, not only the active one.
    /// </summary>
    [Fact]
    public async Task GetSearchDirectoriesAsync_WithSeveralVersions_ShouldReportAllOfThem()
    {
        var expected = new[]
        {
            CreateDirectory("versions", "v18.0.0", "bin"),
            CreateDirectory("versions", "v20.1.0", "bin"),
            CreateDirectory("versions", "v24.14.1", "bin")
        };

        var directories = await ScanAsync(VersionedLayout());

        Assert.Equal(expected.Length, directories.Count);
        Assert.All(expected, path => Assert.Contains(path, directories));
    }

    /// <summary>
    /// Newest first, so the user is not made to read a list to find the version they most likely want.
    /// </summary>
    [Fact]
    public async Task GetSearchDirectoriesAsync_WithSeveralVersions_ShouldOfferNewestFirst()
    {
        CreateDirectory("versions", "v18.0.0", "bin");
        CreateDirectory("versions", "v20.1.0", "bin");
        var newest = CreateDirectory("versions", "v24.14.1", "bin");

        var directories = await ScanAsync(VersionedLayout());

        Assert.Equal(newest, directories[0]);
    }

    /// <summary>
    /// A version directory without the expected bin subdirectory is skipped: it is a partial install, and
    /// naming a directory that holds no executables would offer the user a candidate that cannot run.
    /// </summary>
    [Fact]
    public async Task GetSearchDirectoriesAsync_WhenVersionHasNoBinDirectory_ShouldSkipIt()
    {
        var complete = CreateDirectory("versions", "v20.0.0", "bin");
        CreateDirectory("versions", "v21.0.0");

        var directories = await ScanAsync(VersionedLayout());

        Assert.Equal(complete, Assert.Single(directories));
    }

    /// <summary>A layout whose directories do not exist contributes nothing rather than failing.</summary>
    [Fact]
    public async Task GetSearchDirectoriesAsync_WithAbsentLayout_ShouldReturnEmpty()
        => Assert.Empty(await ScanAsync(VersionedLayout()));

    /// <summary>A fixed layout is reported when present, without wildcard expansion.</summary>
    [Fact]
    public async Task GetSearchDirectoriesAsync_WithFixedLayout_ShouldReportTheDirectory()
    {
        var shims = CreateDirectory("shims");

        var directories = await ScanAsync(FixedLayout("shims"));

        Assert.Equal(shims, Assert.Single(directories));
    }

    /// <summary>
    /// Layout order is preserved, because it encodes precedence: a version manager outranks a system
    /// directory, mirroring how a shell's PATH is built.
    /// </summary>
    [Fact]
    public async Task GetSearchDirectoriesAsync_WithSeveralLayouts_ShouldPreserveLayoutOrder()
    {
        var first = CreateDirectory("first");
        var second = CreateDirectory("second");

        var directories = await ScanAsync(FixedLayout("first"), FixedLayout("second"));

        Assert.Equal(new[] { first, second }, directories);
    }

    /// <summary>
    /// One directory reached by two layouts is reported once. Overlap is ordinary — ~/.local/bin is both a
    /// manager target and a general per-user directory — and a duplicate would offer the user a choice
    /// between a path and itself.
    /// </summary>
    [Fact]
    public async Task GetSearchDirectoriesAsync_WithOverlappingLayouts_ShouldDeduplicate()
    {
        var shared = CreateDirectory("shared");

        var directories = await ScanAsync(FixedLayout("shared"), FixedLayout("shared"));

        Assert.Equal(shared, Assert.Single(directories));
    }

    /// <summary>
    /// Scanned once. The filesystem shape does not change while the wizard runs, and it probes many
    /// components.
    /// </summary>
    [Fact]
    public async Task GetSearchDirectoriesAsync_CalledRepeatedly_ShouldReturnTheSameInstance()
    {
        CreateDirectory("shims");
        var source = new ToolchainScanSearchPathSource(new[] { FixedLayout("shims") });

        var first = await source.GetSearchDirectoriesAsync(TestContext.Current.CancellationToken);
        var second = await source.GetSearchDirectoriesAsync(TestContext.Current.CancellationToken);

        Assert.Same(first, second);
    }

    /// <summary>
    /// The Windows-only root contributes nothing elsewhere. It must not silently resolve against the
    /// process working directory, which is what an empty base path would do.
    /// </summary>
    [Fact]
    public async Task GetSearchDirectoriesAsync_WithWindowsOnlyRoot_ShouldContributeNothingOffWindows()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The assertion is about non-Windows platforms.");

        var source = new ToolchainScanSearchPathSource(new[]
        {
            LayoutFor(AcpToolchainLayoutRoot.WindowsRoamingAppData, "npm")
        });

        Assert.Empty(await source.GetSearchDirectoriesAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>The shipped layout list is data the wizard depends on, so it must not be empty.</summary>
    [Fact]
    public void Known_ShouldDeclareLayouts()
    {
        Assert.NotEmpty(AcpToolchainLayout.Known);
        Assert.Contains(AcpToolchainLayout.Known, layout => layout.IsVersioned);
        Assert.Contains(AcpToolchainLayout.Known, layout => !layout.IsVersioned);
    }

    /// <summary>
    /// A second wildcard is refused at declaration. Expansion resolves exactly one version directory, so
    /// accepting it would silently yield no directories instead of naming the authoring mistake.
    /// </summary>
    [Fact]
    public void Create_WithTwoWildcards_ShouldThrow()
        => Assert.Throws<ArgumentException>(() => AcpToolchainLayout.Create(
            AcpToolchainLayoutRoot.UserHome,
            "a",
            AcpToolchainLayout.VersionWildcard,
            "b",
            AcpToolchainLayout.VersionWildcard));

    private async Task<IReadOnlyList<string>> ScanAsync(params AcpToolchainLayout[] layouts)
        => await new ToolchainScanSearchPathSource(layouts)
            .GetSearchDirectoriesAsync(TestContext.Current.CancellationToken);

    /// <summary>A versioned layout rooted at this test's temporary home.</summary>
    private AcpToolchainLayout VersionedLayout()
        => LayoutFor(AcpToolchainLayoutRoot.Absolute, Path.Combine(_home, "versions"), AcpToolchainLayout.VersionWildcard, "bin");

    private AcpToolchainLayout FixedLayout(params string[] segments)
        => LayoutFor(AcpToolchainLayoutRoot.Absolute, Path.Combine(new[] { _home }.Concat(segments).ToArray()));

    private static AcpToolchainLayout LayoutFor(AcpToolchainLayoutRoot root, params string[] segments)
        => AcpToolchainLayout.Create(root, segments);

    private string CreateDirectory(params string[] segments)
    {
        var path = Path.Combine(new[] { _home }.Concat(segments).ToArray());
        Directory.CreateDirectory(path);
        return Path.GetFullPath(path);
    }
}
