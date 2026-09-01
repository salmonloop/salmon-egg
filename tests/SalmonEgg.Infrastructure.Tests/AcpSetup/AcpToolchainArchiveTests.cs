using System;
using Xunit;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models.AcpSetup;
using SalmonEgg.Infrastructure.Desktop.AcpSetup;

namespace SalmonEgg.Infrastructure.Tests.AcpSetup;

/// <summary>Guards archive safety and layout preservation without downloading a vendor payload.</summary>
public sealed class AcpToolchainArchiveTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "toolchain-archive-" + Guid.NewGuid().ToString("N"));

    public AcpToolchainArchiveTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task ExtractAsync_TarWithNodeStyleSymlinks_ShouldPreserveLinksAndExecuteBit()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix modes and Node's POSIX symlink layout are tested on POSIX.");
        var archive = CreateNodeStyleArchive();
        var destination = Path.Combine(_root, "destination");

        await AcpToolchainArchive.ExtractAsync(archive, destination, Download(), TestContext.Current.CancellationToken);

        var bin = Path.Combine(destination, "bin");
        var node = Path.Combine(bin, "node");
        var npm = Path.Combine(bin, "npm");
        Assert.True(File.Exists(node));
        if (!OperatingSystem.IsWindows())
        {
            Assert.True(File.GetUnixFileMode(node).HasFlag(UnixFileMode.UserExecute));
        }

        Assert.Equal("../lib/npm-cli.js", new FileInfo(npm).LinkTarget);
        Assert.True(File.Exists(npm));
    }

    /// <summary>
    /// uv's zip is flat: the executables sit at the archive root with no directory to strip, and the format
    /// carries no Unix mode, so the extractor has to restore the execute bit itself.
    /// </summary>
    [Fact]
    public async Task ExtractAsync_FlatZipWithNoRootDirectory_ShouldLandExecutablesAtTheRoot()
    {
        var archive = CreateFlatZip("uv", "uvx");
        var destination = Path.Combine(_root, "flat-destination");

        await AcpToolchainArchive.ExtractAsync(
            archive,
            destination,
            FlatZipDownload(),
            TestContext.Current.CancellationToken);

        foreach (var name in new[] { "uv", "uvx" })
        {
            var path = Path.Combine(destination, name);
            Assert.True(File.Exists(path), $"{name} should be extracted to the destination root.");
            if (!OperatingSystem.IsWindows())
            {
                Assert.True(
                    File.GetUnixFileMode(path).HasFlag(UnixFileMode.UserExecute),
                    $"{name} must be executable; zip carries no mode of its own.");
            }
        }
    }

    /// <summary>
    /// Declaring a root the archive does not have must not silently extract nothing: every entry would be
    /// judged to fall outside the declared root, which is how a shape mismatch should read.
    /// </summary>
    [Fact]
    public void StripRoot_FlatEntryAgainstADeclaredRoot_ShouldReject()
        => Assert.Null(AcpToolchainArchive.StripRoot("uv.exe", "uv-x86_64-pc-windows-msvc"));

    /// <summary>With no declared root, a flat entry keeps its own name.</summary>
    [Fact]
    public void StripRoot_FlatEntryWithNoDeclaredRoot_ShouldKeepTheName()
        => Assert.Equal("uv.exe", AcpToolchainArchive.StripRoot("uv.exe", null));

    [Theory]
    [InlineData("../escape")]
    [InlineData("../../etc/passwd")]
    [InlineData("bin/../../escape")]
    public void ResolveSafePath_Traversal_ShouldReject(string entry)
        => Assert.Null(AcpToolchainArchive.ResolveSafePath(Path.Combine(_root, "destination"), entry));

    [Theory]
    [InlineData("bin/node")]
    [InlineData("nested/bin/npm")]
    public void ResolveSafePath_InternalEntry_ShouldStayInsideDestination(string entry)
    {
        var destination = Path.Combine(_root, "destination");
        var path = AcpToolchainArchive.ResolveSafePath(destination, entry);

        Assert.NotNull(path);
        Assert.StartsWith(Path.GetFullPath(destination) + Path.DirectorySeparatorChar, path, StringComparison.Ordinal);
    }

    [Fact]
    public void StripRoot_EntryOutsideDeclaredRoot_ShouldReject()
        => Assert.Null(AcpToolchainArchive.StripRoot("other/bin/node", "node-v24"));

    [Fact]
    public void StripRoot_DeclaredRoot_ShouldRemoveOnlyTheLeadingDirectory()
        => Assert.Equal("bin/node", AcpToolchainArchive.StripRoot("node-v24/bin/node", "node-v24"));

    private string CreateNodeStyleArchive()
    {
        var source = Path.Combine(_root, "source", "node-v24", "bin");
        Directory.CreateDirectory(Path.Combine(_root, "source", "node-v24", "lib"));
        Directory.CreateDirectory(source);
        var node = Path.Combine(source, "node");
        File.WriteAllText(node, "#!/bin/sh\necho node\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                node,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }

        File.WriteAllText(Path.Combine(_root, "source", "node-v24", "lib", "npm-cli.js"), "x");
        File.CreateSymbolicLink(Path.Combine(source, "npm"), "../lib/npm-cli.js");
        File.CreateSymbolicLink(Path.Combine(source, "npx"), "../lib/npm-cli.js");

        var archive = Path.Combine(_root, "node.tar.gz");
        using var file = File.Create(archive);
        using var gzip = new GZipStream(file, CompressionLevel.Fastest);
        TarFile.CreateFromDirectory(Path.Combine(_root, "source"), gzip, includeBaseDirectory: false);
        return archive;
    }

    /// <summary>Builds a zip with entries at its root, matching uv's Windows archive shape.</summary>
    private string CreateFlatZip(params string[] names)
    {
        var source = Path.Combine(_root, "flat-source");
        Directory.CreateDirectory(source);
        foreach (var name in names)
        {
            File.WriteAllText(Path.Combine(source, name), "binary-payload");
        }

        var archive = Path.Combine(_root, "uv.zip");
        ZipFile.CreateFromDirectory(source, archive);
        return archive;
    }

    private static AcpToolchainDownload FlatZipDownload() => new()
    {
        Archive = new Uri("https://example.invalid/uv.zip"),
        Checksum = new Uri("https://example.invalid/uv.zip.sha256"),
        ChecksumFormat = AcpChecksumFormat.SingleHash,
        ArchiveFormat = AcpArchiveFormat.Zip,
        RootDirectory = null,
        BinSubdirectory = string.Empty,
        VerifyExecutables = new[] { "uv", "uvx" },
        VerifyCommand = "uv",
        VerifyArguments = new[] { "--version" }
    };

    private static AcpToolchainDownload Download() => new()
    {
        Archive = new Uri("https://example.invalid/node.tar.gz"),
        Checksum = new Uri("https://example.invalid/SHASUMS"),
        ChecksumFormat = AcpChecksumFormat.ShasumList,
        ArchiveFormat = AcpArchiveFormat.TarGzip,
        RootDirectory = "node-v24",
        BinSubdirectory = "bin",
        VerifyExecutables = new[] { "node", "npm", "npx" },
        VerifyCommand = "node"
    };
}
