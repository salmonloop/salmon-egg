using System;
using System.Collections.Generic;

namespace SalmonEgg.Domain.Models.AcpSetup;

/// <summary>How a toolchain archive is compressed, which decides how it is unpacked.</summary>
public enum AcpArchiveFormat
{
    /// <summary>A tar archive compressed with gzip.</summary>
    TarGzip,

    /// <summary>A zip archive.</summary>
    Zip
}

/// <summary>How a vendor publishes the checksum for one archive.</summary>
/// <remarks>
/// Two shapes because the two vendors differ and neither can be parsed as the other: Node publishes one
/// <c>SHASUMS256.txt</c> per release listing every file, while uv publishes a <c>.sha256</c> beside each
/// asset. Reading a list as a single hash silently yields the first line's digest — the wrong file's — so
/// the shape is declared rather than sniffed.
/// </remarks>
public enum AcpChecksumFormat
{
    /// <summary>
    /// A <c>sha256sum</c>-style listing, one <c>&lt;hash&gt;  &lt;filename&gt;</c> per line. The line
    /// naming the archive is the one that counts.
    /// </summary>
    ShasumList,

    /// <summary>
    /// A single digest. May carry a trailing filename, as <c>sha256sum</c> output does, which is ignored.
    /// </summary>
    SingleHash
}

/// <summary>
/// Everything needed to fetch, verify, and unpack one toolchain build for one platform.
/// </summary>
/// <remarks>
/// The archive layout is declared rather than discovered because the four builds the app can encounter
/// disagree in every combination, and getting it wrong yields a directory with no executables in it
/// instead of an error. Measured against the real archives:
/// <list type="bullet">
/// <item>Node's POSIX tarball: one root directory, executables in <c>bin/</c> below it.</item>
/// <item>Node's Windows zip: one root directory, executables directly in it — no <c>bin/</c>.</item>
/// <item>uv's POSIX tarball: one root directory, executables directly in it.</item>
/// <item>uv's Windows zip: no root directory at all; executables at the archive root.</item>
/// </list>
/// No two agree, so no rule inferred from one holds for the others.
/// </remarks>
public sealed class AcpToolchainDownload
{
    /// <summary>The archive to fetch. Must be HTTPS: the digest below is only as trustworthy as its source.</summary>
    public required Uri Archive { get; init; }

    /// <summary>Where the expected digest is published.</summary>
    public required Uri Checksum { get; init; }

    /// <summary>How to read <see cref="Checksum"/>.</summary>
    public required AcpChecksumFormat ChecksumFormat { get; init; }

    /// <summary>How <see cref="Archive"/> is compressed.</summary>
    public required AcpArchiveFormat ArchiveFormat { get; init; }

    /// <summary>
    /// The single directory the archive's entries live under, or null when entries sit at its root.
    /// </summary>
    /// <remarks>
    /// Stripped during extraction so the installed layout is version-keyed by this app rather than by the
    /// vendor's naming — the same install path shape whichever toolchain and platform it came from.
    /// </remarks>
    public string? RootDirectory { get; init; }

    /// <summary>
    /// Path below the extracted root to the directory holding executables. Empty when they sit at the root.
    /// </summary>
    public string BinSubdirectory { get; init; } = string.Empty;

    /// <summary>
    /// File names that must exist in the bin directory for the install to count as complete.
    /// </summary>
    /// <remarks>
    /// Checked because an archive that unpacked without error can still be the wrong build: reporting
    /// success and letting the next probe find nothing is the failure this prevents. Includes the package
    /// manager, not just the runtime — a Node install whose <c>npm</c> is missing cannot install anything,
    /// which is the only reason the wizard wanted the toolchain.
    /// </remarks>
    public required IReadOnlyList<string> VerifyExecutables { get; init; }

    /// <summary>
    /// The executable to run, and the arguments to run it with, to confirm the install actually works.
    /// </summary>
    /// <remarks>
    /// Existence on disk is not evidence a binary runs: an archive for the wrong architecture unpacks
    /// perfectly and fails at exec. Running it once is what turns "unpacked" into "installed".
    /// </remarks>
    public required string VerifyCommand { get; init; }

    /// <summary>Arguments for <see cref="VerifyCommand"/>. Conventionally a version query.</summary>
    public IReadOnlyList<string> VerifyArguments { get; init; } = Array.Empty<string>();
}
