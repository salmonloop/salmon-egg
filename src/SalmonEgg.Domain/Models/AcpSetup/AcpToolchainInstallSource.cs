using System;
using System.Runtime.InteropServices;

namespace SalmonEgg.Domain.Models.AcpSetup;

/// <summary>
/// Selects a published toolchain archive for an operating system and CPU architecture.
/// </summary>
/// <remarks>
/// This is data selection, not networking: it maps one supported platform to the vendor's documented URL
/// pattern. Keeping it pure makes every URL and archive shape testable without reaching the network — a
/// test that happens to download today's release only proves today's vendor availability, not that the app
/// constructs the right URL tomorrow.
///
/// The two vendors agree on almost nothing, which is why each has its own resolver rather than one
/// parameterized by vendor. Verified against the live releases:
/// <list type="bullet">
/// <item>Node tags its releases <c>v24.20.0</c>; uv tags them <c>0.12.8</c>. Prefixing uv's tag with
/// <c>v</c> returns 404, so the prefix is a per-vendor fact rather than a convention to share.</item>
/// <item>Node publishes one <c>SHASUMS256.txt</c> listing every artifact; uv publishes a <c>.sha256</c>
/// beside each asset. Hence <see cref="AcpChecksumFormat"/>.</item>
/// <item>Node's POSIX archive nests executables under <c>bin/</c>; uv's puts them at its single root, and
/// uv's Windows zip has no root directory at all.</item>
/// </list>
/// </remarks>
public static class AcpToolchainInstallSource
{
    /// <summary>
    /// The Node LTS version shipped by the wizard's automatic installer.
    /// </summary>
    /// <remarks>
    /// Pinned rather than fetched from nodejs.org's index at runtime. The installer must remain a
    /// deterministic capability: asking a network index whether the app can construct a download turns a
    /// temporary vendor outage into a false "this platform is unsupported" answer, while silently tracking
    /// whatever release appeared today changes what users install without a reviewed code change.
    /// </remarks>
    public const string NodeVersion = "v24.20.0";

    /// <summary>
    /// The uv version shipped by the wizard's automatic installer.
    /// </summary>
    /// <remarks>
    /// Deliberately carries no <c>v</c> prefix, unlike <see cref="NodeVersion"/>: uv's release tags are bare
    /// versions, and <c>.../download/v0.12.8/...</c> returns 404 while <c>.../download/0.12.8/...</c>
    /// resolves. Pinned for the same reason Node's is — a network-derived version would make "can this
    /// platform install uv" depend on vendor uptime, and would change what users install without review.
    /// </remarks>
    public const string UvVersion = "0.12.8";

    /// <summary>
    /// Returns the official Node archive for <paramref name="operatingSystem"/> and
    /// <paramref name="architecture"/>, or null when that pair has no supported automatic install.
    /// </summary>
    public static AcpToolchainDownload? ResolveNode(
        OSPlatform operatingSystem,
        Architecture architecture,
        string? version = null)
    {
        var effectiveVersion = string.IsNullOrWhiteSpace(version) ? NodeVersion : version.Trim();
        if (!effectiveVersion.StartsWith('v'))
        {
            effectiveVersion = "v" + effectiveVersion;
        }

        if (ResolveNodeBuild(operatingSystem, architecture) is not { } build)
        {
            return null;
        }

        var (platform, architectureSegment) = build;

        var archiveFormat = operatingSystem == OSPlatform.Windows
            ? AcpArchiveFormat.Zip
            : AcpArchiveFormat.TarGzip;
        var extension = archiveFormat == AcpArchiveFormat.Zip ? "zip" : "tar.gz";
        var archiveName = $"node-{effectiveVersion}-{platform}-{architectureSegment}.{extension}";
        var rootDirectory = $"node-{effectiveVersion}-{platform}-{architectureSegment}";
        var isWindows = operatingSystem == OSPlatform.Windows;

        return new AcpToolchainDownload
        {
            Archive = new Uri($"https://nodejs.org/dist/{effectiveVersion}/{archiveName}"),
            Checksum = new Uri($"https://nodejs.org/dist/{effectiveVersion}/SHASUMS256.txt"),
            ChecksumFormat = AcpChecksumFormat.ShasumList,
            ArchiveFormat = archiveFormat,
            RootDirectory = rootDirectory,
            // Node's POSIX archive puts the launchers under bin/, while its Windows zip puts the .exe/.cmd
            // launchers beside its root directory. This is data, not an OperatingSystem branch in the
            // installer, so a future source with either shape needs no extractor rewrite.
            BinSubdirectory = isWindows ? string.Empty : "bin",
            VerifyExecutables = isWindows
                ? new[] { "node.exe", "npm.cmd", "npx.cmd" }
                : new[] { "node", "npm", "npx" },
            VerifyCommand = isWindows ? "node.exe" : "node",
            VerifyArguments = new[] { "--version" }
        };
    }

    /// <summary>
    /// Returns the official uv archive for this platform, or null when that combination has no published
    /// build.
    /// </summary>
    /// <param name="preferMusl">
    /// Selects the musl build over the glibc one on Linux. The caller decides because only the running
    /// process can tell which C library the host uses; this type performs no environment inspection.
    /// </param>
    /// <remarks>
    /// uv ships one archive per Rust target triple, so the triple is the whole address of a build. Both
    /// libc variants are offered because uv publishes both and they are not interchangeable: a glibc binary
    /// on a musl host unpacks perfectly and then fails to exec.
    /// </remarks>
    public static AcpToolchainDownload? ResolveUv(
        OSPlatform operatingSystem,
        Architecture architecture,
        bool preferMusl = false,
        string? version = null)
    {
        // Trimmed of a leading 'v' rather than given one: a caller passing "v0.12.8" means that release, and
        // uv's own tag for it has no prefix. Silently building a 404 URL would surface as a download failure
        // on a platform that is in fact supported.
        var effectiveVersion = (string.IsNullOrWhiteSpace(version) ? UvVersion : version.Trim())
            .TrimStart('v', 'V');

        if (ResolveUvTarget(operatingSystem, architecture, preferMusl) is not { } target)
        {
            return null;
        }

        var isWindows = operatingSystem == OSPlatform.Windows;
        var archiveFormat = isWindows ? AcpArchiveFormat.Zip : AcpArchiveFormat.TarGzip;
        var archiveName = isWindows ? $"uv-{target}.zip" : $"uv-{target}.tar.gz";
        var archive = new Uri(
            $"https://github.com/astral-sh/uv/releases/download/{effectiveVersion}/{archiveName}");

        return new AcpToolchainDownload
        {
            Archive = archive,
            // Each asset carries its own digest file, so the checksum URL is the archive's plus a suffix.
            Checksum = new Uri(archive.AbsoluteUri + ".sha256"),
            ChecksumFormat = AcpChecksumFormat.SingleHash,
            ArchiveFormat = archiveFormat,
            // The POSIX tarball nests everything under one directory named for the target; the Windows zip
            // is flat, with the executables at the archive root and no directory to strip.
            RootDirectory = isWindows ? null : $"uv-{target}",
            // Neither layout has a bin/ subdirectory: uv and uvx sit directly at the extracted root.
            BinSubdirectory = string.Empty,
            // uvx is required, not incidental: it is the launcher every Uvx-distributed component runs
            // through, so a uv install without it cannot serve the purpose the wizard wanted it for.
            VerifyExecutables = isWindows
                ? new[] { "uv.exe", "uvx.exe" }
                : new[] { "uv", "uvx" },
            VerifyCommand = isWindows ? "uv.exe" : "uv",
            VerifyArguments = new[] { "--version" }
        };
    }

    /// <summary>
    /// The Rust target triple for a uv build that is actually published, or null.
    /// </summary>
    /// <remarks>
    /// An explicit list for the same reason <see cref="ResolveNodeBuild"/> keeps one: composing triples from
    /// independent parts invents plausible names for builds that do not exist, and each would send a user to
    /// a 404 rather than to the honest answer that this platform has no automatic install.
    ///
    /// Narrower than everything uv publishes. The ppc64le, s390x and riscv64 Linux builds exist but are not
    /// offered, since this app has no coverage on those hosts. macOS has no musl variant to choose between,
    /// and Windows-on-arm is served by its own native archive rather than by emulation.
    /// </remarks>
    private static string? ResolveUvTarget(
        OSPlatform operatingSystem,
        Architecture architecture,
        bool preferMusl)
    {
        if (operatingSystem == OSPlatform.Linux)
        {
            return architecture switch
            {
                Architecture.X64 => preferMusl
                    ? "x86_64-unknown-linux-musl"
                    : "x86_64-unknown-linux-gnu",
                Architecture.Arm64 => preferMusl
                    ? "aarch64-unknown-linux-musl"
                    : "aarch64-unknown-linux-gnu",
                Architecture.X86 => preferMusl
                    ? "i686-unknown-linux-musl"
                    : "i686-unknown-linux-gnu",
                // 32-bit ARM names its ABI in the triple, and the hard-float spelling differs per libc.
                Architecture.Arm => preferMusl
                    ? "armv7-unknown-linux-musleabihf"
                    : "armv7-unknown-linux-gnueabihf",
                _ => null
            };
        }

        if (operatingSystem == OSPlatform.OSX)
        {
            return architecture switch
            {
                Architecture.X64 => "x86_64-apple-darwin",
                Architecture.Arm64 => "aarch64-apple-darwin",
                _ => null
            };
        }

        if (operatingSystem == OSPlatform.Windows)
        {
            return architecture switch
            {
                Architecture.X64 => "x86_64-pc-windows-msvc",
                Architecture.Arm64 => "aarch64-pc-windows-msvc",
                Architecture.X86 => "i686-pc-windows-msvc",
                _ => null
            };
        }

        return null;
    }

    /// <summary>
    /// The platform and architecture URL segments for a Node build that is actually published, or null.
    /// </summary>
    /// <remarks>
    /// An explicit list of the pairs Node ships, not a cross product of two independent mappings. The
    /// distinction matters: composing the segments freely produces plausible names for builds that do not
    /// exist — <c>node-v24.20.0-linux-x86.tar.gz</c>, <c>-darwin-x86</c>, <c>-win-armv7l</c> and
    /// <c>-darwin-armv7l</c> among them, verified absent from the release's own SHASUMS256 listing. Each
    /// would send a user to a 404 and surface as a download failure on a platform where the honest answer
    /// is that no automatic install exists, which is what null says.
    ///
    /// Deliberately narrower than everything Node publishes. linux-ppc64le, linux-s390x and the musl
    /// variants exist but are not offered: this app has no test coverage on them, and a musl host needs the
    /// musl archive rather than the glibc one — a distinction this type cannot make from
    /// <see cref="Architecture"/> alone, and getting it wrong yields a binary that unpacks and then refuses
    /// to exec.
    /// </remarks>
    private static (string Platform, string Architecture)? ResolveNodeBuild(
        OSPlatform operatingSystem,
        Architecture architecture)
    {
        if (operatingSystem == OSPlatform.Linux)
        {
            return architecture switch
            {
                Architecture.X64 => ("linux", "x64"),
                Architecture.Arm64 => ("linux", "arm64"),
                _ => null
            };
        }

        if (operatingSystem == OSPlatform.OSX)
        {
            return architecture switch
            {
                Architecture.X64 => ("darwin", "x64"),
                Architecture.Arm64 => ("darwin", "arm64"),
                _ => null
            };
        }

        if (operatingSystem == OSPlatform.Windows)
        {
            return architecture switch
            {
                Architecture.X64 => ("win", "x64"),
                Architecture.Arm64 => ("win", "arm64"),
                _ => null
            };
        }

        return null;
    }
}
