using System;
using Xunit;
using System.Runtime.InteropServices;
using RuntimeArchitecture = System.Runtime.InteropServices.Architecture;
using SalmonEgg.Domain.Models.AcpSetup;

namespace SalmonEgg.Domain.Tests.Models.AcpSetup;

public sealed class AcpToolchainInstallSourceTests
{
    [Theory]
    [InlineData("LINUX", "X64", "node-v24.20.0-linux-x64.tar.gz", "node-v24.20.0-linux-x64", "bin")]
    [InlineData("LINUX", "Arm64", "node-v24.20.0-linux-arm64.tar.gz", "node-v24.20.0-linux-arm64", "bin")]
    [InlineData("OSX", "X64", "node-v24.20.0-darwin-x64.tar.gz", "node-v24.20.0-darwin-x64", "bin")]
    [InlineData("OSX", "Arm64", "node-v24.20.0-darwin-arm64.tar.gz", "node-v24.20.0-darwin-arm64", "bin")]
    [InlineData("WINDOWS", "X64", "node-v24.20.0-win-x64.zip", "node-v24.20.0-win-x64", "")]
    [InlineData("WINDOWS", "Arm64", "node-v24.20.0-win-arm64.zip", "node-v24.20.0-win-arm64", "")]
    public void ResolveNode_PublishedBuilds_ShouldDescribeRealArchive(
        string osName,
        string architectureName,
        string archiveName,
        string root,
        string bin)
    {
        var download = AcpToolchainInstallSource.ResolveNode(
            ParseOs(osName),
            ParseArchitecture(architectureName));

        Assert.NotNull(download);
        Assert.Equal(archiveName, download.Archive.Segments[^1]);
        Assert.Equal(root, download.RootDirectory);
        Assert.Equal(bin, download.BinSubdirectory);
        Assert.Equal("https://nodejs.org/dist/v24.20.0/SHASUMS256.txt", download.Checksum.AbsoluteUri);
    }

    [Theory]
    [InlineData("LINUX", "X86")]
    [InlineData("LINUX", "Arm")]
    [InlineData("OSX", "X86")]
    [InlineData("OSX", "Arm")]
    [InlineData("WINDOWS", "X86")]
    [InlineData("WINDOWS", "Arm")]
    [InlineData("FreeBSD", "X64")]
    public void ResolveNode_UnpublishedBuild_ShouldReturnNull(string osName, string architectureName)
        => Assert.Null(AcpToolchainInstallSource.ResolveNode(
            ParseOs(osName),
            ParseArchitecture(architectureName)));

    [Fact]
    public void BothToolchains_ShouldPublishAnAutomaticInstallPath()
    {
        Assert.True(AcpToolchainRequirement.Node.HasAutomaticInstallPath);
        Assert.True(AcpToolchainRequirement.Uv.HasAutomaticInstallPath);
    }

    [Theory]
    [InlineData("LINUX", "X64", false, "uv-x86_64-unknown-linux-gnu.tar.gz")]
    [InlineData("LINUX", "X64", true, "uv-x86_64-unknown-linux-musl.tar.gz")]
    [InlineData("LINUX", "Arm64", false, "uv-aarch64-unknown-linux-gnu.tar.gz")]
    [InlineData("LINUX", "Arm64", true, "uv-aarch64-unknown-linux-musl.tar.gz")]
    [InlineData("LINUX", "X86", false, "uv-i686-unknown-linux-gnu.tar.gz")]
    [InlineData("LINUX", "X86", true, "uv-i686-unknown-linux-musl.tar.gz")]
    [InlineData("LINUX", "Arm", false, "uv-armv7-unknown-linux-gnueabihf.tar.gz")]
    [InlineData("LINUX", "Arm", true, "uv-armv7-unknown-linux-musleabihf.tar.gz")]
    [InlineData("OSX", "X64", false, "uv-x86_64-apple-darwin.tar.gz")]
    [InlineData("OSX", "Arm64", false, "uv-aarch64-apple-darwin.tar.gz")]
    [InlineData("WINDOWS", "X64", false, "uv-x86_64-pc-windows-msvc.zip")]
    [InlineData("WINDOWS", "Arm64", false, "uv-aarch64-pc-windows-msvc.zip")]
    [InlineData("WINDOWS", "X86", false, "uv-i686-pc-windows-msvc.zip")]
    public void ResolveUv_PublishedBuilds_ShouldNameTheRealAsset(
        string osName,
        string architectureName,
        bool preferMusl,
        string archiveName)
    {
        var download = AcpToolchainInstallSource.ResolveUv(
            ParseOs(osName),
            ParseArchitecture(architectureName),
            preferMusl);

        Assert.NotNull(download);
        Assert.Equal(archiveName, download.Archive.Segments[^1]);
        // Each asset carries its own digest, unlike Node's single shared listing.
        Assert.Equal(download.Archive.AbsoluteUri + ".sha256", download.Checksum.AbsoluteUri);
        Assert.Equal(AcpChecksumFormat.SingleHash, download.ChecksumFormat);
        // Neither layout nests executables, so there is never a bin/ segment to descend into.
        Assert.Equal(string.Empty, download.BinSubdirectory);
    }

    /// <summary>
    /// uv's tags carry no <c>v</c>. Prefixing one yields a 404, so this pins the spelling that the URL
    /// builder must produce even when a caller supplies the prefixed form.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("0.12.8")]
    [InlineData("v0.12.8")]
    public void ResolveUv_ShouldBuildTagsWithoutAVersionPrefix(string? version)
    {
        var download = AcpToolchainInstallSource.ResolveUv(
            OSPlatform.Linux,
            RuntimeArchitecture.Arm64,
            preferMusl: false,
            version);

        Assert.NotNull(download);
        Assert.Contains("/releases/download/0.12.8/", download.Archive.AbsoluteUri, StringComparison.Ordinal);
        Assert.DoesNotContain("/download/v", download.Archive.AbsoluteUri, StringComparison.Ordinal);
    }

    /// <summary>
    /// The Windows archive is flat: its executables sit at the archive root with no directory to strip.
    /// Declaring a root that is not there would make every entry look like it fell outside it.
    /// </summary>
    [Fact]
    public void ResolveUv_OnWindows_ShouldDeclareNoRootDirectory()
    {
        var download = AcpToolchainInstallSource.ResolveUv(OSPlatform.Windows, RuntimeArchitecture.X64);

        Assert.NotNull(download);
        Assert.Null(download.RootDirectory);
        Assert.Equal(AcpArchiveFormat.Zip, download.ArchiveFormat);
        Assert.Equal(new[] { "uv.exe", "uvx.exe" }, download.VerifyExecutables);
    }

    /// <summary>The POSIX tarball nests under one directory named for the target, which is stripped.</summary>
    [Fact]
    public void ResolveUv_OnPosix_ShouldStripTheTargetNamedRoot()
    {
        var download = AcpToolchainInstallSource.ResolveUv(OSPlatform.OSX, RuntimeArchitecture.Arm64);

        Assert.NotNull(download);
        Assert.Equal("uv-aarch64-apple-darwin", download.RootDirectory);
        Assert.Equal(AcpArchiveFormat.TarGzip, download.ArchiveFormat);
        // uvx is checked too: it is the launcher every Uvx component runs through.
        Assert.Equal(new[] { "uv", "uvx" }, download.VerifyExecutables);
    }

    [Theory]
    [InlineData("LINUX", "Ppc64le")]
    [InlineData("OSX", "X86")]
    [InlineData("OSX", "Arm")]
    [InlineData("WINDOWS", "Arm")]
    [InlineData("FreeBSD", "X64")]
    public void ResolveUv_UnpublishedBuild_ShouldReturnNull(string osName, string architectureName)
        => Assert.Null(AcpToolchainInstallSource.ResolveUv(
            ParseOs(osName),
            ParseArchitecture(architectureName)));

    private static OSPlatform ParseOs(string name)
        => name switch
        {
            "LINUX" => OSPlatform.Linux,
            "OSX" => OSPlatform.OSX,
            "WINDOWS" => OSPlatform.Windows,
            _ => OSPlatform.Create(name)
        };

    private static RuntimeArchitecture ParseArchitecture(string name)
        => name switch
        {
            "X64" => RuntimeArchitecture.X64,
            "X86" => RuntimeArchitecture.X86,
            "Arm64" => RuntimeArchitecture.Arm64,
            "Arm" => RuntimeArchitecture.Arm,
            _ => RuntimeArchitecture.Ppc64le
        };
}
