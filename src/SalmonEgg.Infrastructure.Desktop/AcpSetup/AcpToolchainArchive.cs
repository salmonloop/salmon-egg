using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models.AcpSetup;

namespace SalmonEgg.Infrastructure.Desktop.AcpSetup;

/// <summary>
/// Unpacks a verified toolchain archive into a destination directory.
/// </summary>
/// <remarks>
/// Separate from the installer because unpacking is where the archive-format hazards live and they are
/// worth testing on their own: path traversal, a missing execute bit, and a symlink that must survive.
/// </remarks>
internal static class AcpToolchainArchive
{
    /// <summary>
    /// Extracts <paramref name="archivePath"/> into <paramref name="destination"/>, stripping the
    /// archive's single root directory when it declares one.
    /// </summary>
    /// <remarks>
    /// The root is stripped so the installed layout is this app's own — one directory per version — rather
    /// than the vendor's naming, which differs per platform and would leak into every path the wizard later
    /// records.
    /// </remarks>
    internal static async Task ExtractAsync(
        string archivePath,
        string destination,
        AcpToolchainDownload download,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destination);

        if (download.ArchiveFormat == AcpArchiveFormat.TarGzip)
        {
            await ExtractTarGzipAsync(archivePath, destination, download.RootDirectory, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        ExtractZip(archivePath, destination, download.RootDirectory, cancellationToken);
    }

    /// <summary>
    /// Extracts a gzip-compressed tar, preserving symbolic links and Unix permissions.
    /// </summary>
    /// <remarks>
    /// Entries are read one at a time rather than through <c>TarFile.ExtractToDirectory</c> so the root
    /// directory can be stripped and each destination path checked before anything is written.
    ///
    /// Symbolic links are the reason a tar reader is used at all for Node: <c>npm</c> and <c>npx</c> in the
    /// official archive are links into <c>lib/node_modules</c>, and an extractor that resolved them into
    /// copies would produce launchers that cannot find their own package. <see cref="TarEntry"/> recreates
    /// them as links, and it restores the execute bit that makes <c>node</c> runnable.
    /// </remarks>
    private static async Task ExtractTarGzipAsync(
        string archivePath,
        string destination,
        string? rootDirectory,
        CancellationToken cancellationToken)
    {
        await using var file = File.OpenRead(archivePath);
        await using var gzip = new GZipStream(file, CompressionMode.Decompress);
        await using var reader = new TarReader(gzip);

        // Every symbolic link inside the destination was created by this extraction — the destination is a
        // fresh staging directory — so tracking them here is a complete picture of the tree's links.
        var createdLinks = new HashSet<string>(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        while (await reader.GetNextEntryAsync(copyData: false, cancellationToken).ConfigureAwait(false)
               is { } entry)
        {
            if (StripRoot(entry.Name, rootDirectory) is not { Length: > 0 } relative)
            {
                continue;
            }

            if (ResolveSafePath(destination, relative) is not { } target)
            {
                throw new InvalidDataException(
                    $"Archive entry '{entry.Name}' resolves outside the destination directory.");
            }

            // Checked before any filesystem write of any entry kind: a parent that is a link would redirect
            // even a directory creation, not just a file write.
            EnsureWriteDoesNotTraverseLink(createdLinks, destination, target);

            if (entry.EntryType is TarEntryType.SymbolicLink or TarEntryType.HardLink)
            {
                CreateLink(destination, createdLinks, target, entry.LinkName, entry.EntryType);
                continue;
            }

            if (entry.EntryType == TarEntryType.Directory)
            {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);

            // ExtractToFile applies the entry's mode, which is what preserves the execute bit.
            await entry.ExtractToFileAsync(target, overwrite: true, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Extracts a zip, restoring the execute bit that the format does not carry.
    /// </summary>
    /// <remarks>
    /// Zip has no Unix mode, so a naive extraction on a POSIX host yields files that are present and not
    /// executable — an install that verifies by existence and then fails at exec. Windows ignores the mode
    /// entirely, so setting it is harmless there and correct everywhere else. Node's Windows archive is the
    /// only zip in use today; the bit matters for any future POSIX-hosted zip.
    /// </remarks>
    private static void ExtractZip(
        string archivePath,
        string destination,
        string? rootDirectory,
        CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(archivePath);

        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (StripRoot(entry.FullName, rootDirectory) is not { Length: > 0 } relative)
            {
                continue;
            }

            if (ResolveSafePath(destination, relative) is not { } target)
            {
                throw new InvalidDataException(
                    $"Archive entry '{entry.FullName}' resolves outside the destination directory.");
            }

            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
            {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);

            if (!OperatingSystem.IsWindows())
            {
                MarkExecutable(target);
            }
        }
    }

    /// <summary>
    /// Recreates a link entry at <paramref name="target"/>.
    /// </summary>
    /// <remarks>
    /// A symbolic link is recreated with its stored target verbatim rather than resolved into a copy. Node's
    /// <c>npm</c> is a relative link into <c>lib/node_modules/npm/bin/npm-cli.js</c>; copying the script
    /// there instead would give it a different <c>__dirname</c> and break its own module resolution. Keeping
    /// it a link is what makes the extracted tree identical to a vendor-installed one.
    ///
    /// The symbolic link's target is deliberately not validated against the destination. It is written, not
    /// followed, so it grants no write access outside the tree — later entries that try to write through it
    /// are refused by <see cref="EnsureWriteDoesNotTraverseLink"/>, which is why the link must be recorded
    /// in <paramref name="createdLinks"/> here; and these archives use relative targets that only resolve
    /// correctly once the whole tree is in place, which is not yet true while extracting.
    ///
    /// A hard link is created as a hard link when the source is already extracted — tar orders it that way —
    /// and copied otherwise, since a hard link to a file that does not exist yet cannot be made. The copy
    /// reads the resolved source, so a source outside the destination would import outside content into the
    /// install: it is rejected rather than resolved.
    /// </remarks>
    private static void CreateLink(
        string destination,
        HashSet<string> createdLinks,
        string target,
        string? linkName,
        TarEntryType entryType)
    {
        if (string.IsNullOrEmpty(linkName))
        {
            return;
        }

        // A prior partial extraction may have left something here; the link cannot be created over it.
        if (File.Exists(target))
        {
            File.Delete(target);
        }

        if (entryType == TarEntryType.SymbolicLink)
        {
            File.CreateSymbolicLink(target, linkName);
            createdLinks.Add(Path.GetFullPath(target));
            return;
        }

        var directory = Path.GetDirectoryName(target)!;
        var source = Path.GetFullPath(Path.Combine(directory, linkName));
        var root = Path.GetFullPath(destination);
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        if (!source.StartsWith(
                rootWithSeparator,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Archive hard-link entry '{Path.GetFileName(target)}' points outside the destination directory.");
        }

        if (File.Exists(source))
        {
            File.Copy(source, target, overwrite: true);
        }
    }

    /// <summary>
    /// Throws when <paramref name="target"/> or any parent directory on its way down from
    /// <paramref name="destination"/> is a symbolic link the archive created earlier in this extraction.
    /// </summary>
    /// <remarks>
    /// Resolving the entry's own name is not enough: a crafted archive can declare
    /// <c>escape → /somewhere/outside</c> and then a regular file named <c>escape</c>, and the extractor's
    /// write would follow the link and land outside the tree. The same holds for a symlinked parent —
    /// <c>out → outside</c> then a file at <c>out/child</c> — which would redirect the write even though
    /// the entry's own name is innocent. A digest attests to the archive's bytes, not to their safety, so
    /// the shape itself is refused rather than trusted.
    ///
    /// Only links this extraction created are consulted, which is sound because the destination is a fresh
    /// staging directory: every symlink in it came from an earlier entry of the same archive. Vendor
    /// archives are unaffected — Node's links (<c>bin/npm</c>, <c>bin/npx</c>) are leaves that no later
    /// entry names as a path component.
    /// </remarks>
    private static void EnsureWriteDoesNotTraverseLink(
        HashSet<string> createdLinks,
        string destination,
        string target)
    {
        var current = Path.GetFullPath(destination);
        var relative = Path.GetRelativePath(current, Path.GetFullPath(target));
        if (relative is "." or "")
        {
            return;
        }

        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (segment.Length == 0)
            {
                continue;
            }

            current = Path.Combine(current, segment);
            if (createdLinks.Contains(current))
            {
                throw new InvalidDataException(
                    $"Archive entry '{Path.GetFileName(target)}' would be written through a symbolic link "
                    + "the archive itself created.");
            }
        }
    }

    private static void MarkExecutable(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var mode = File.GetUnixFileMode(path);
        File.SetUnixFileMode(
            path,
            mode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
    }

    /// <summary>
    /// Removes the archive's declared root directory from an entry name, or returns the name unchanged when
    /// the archive declares none.
    /// </summary>
    /// <remarks>
    /// Returns null for the root entry itself, and for an entry outside the declared root — the latter
    /// would mean the archive's shape disagrees with what the source declared, and extracting it would
    /// scatter files beside the version directory instead of inside it.
    /// </remarks>
    internal static string? StripRoot(string entryName, string? rootDirectory)
    {
        var normalized = entryName.Replace('\\', '/').TrimStart('/');
        if (string.IsNullOrEmpty(rootDirectory))
        {
            return normalized.TrimEnd('/');
        }

        var prefix = rootDirectory.TrimEnd('/') + "/";
        if (!normalized.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        return normalized[prefix.Length..].TrimEnd('/');
    }

    /// <summary>
    /// Resolves <paramref name="relative"/> under <paramref name="destination"/>, or null when the result
    /// escapes it.
    /// </summary>
    /// <remarks>
    /// An archive is untrusted input even from a vendor over HTTPS with a matching digest, because the
    /// digest attests to the bytes rather than to their safety. An entry named <c>../../.bashrc</c> would
    /// otherwise let a download overwrite files anywhere the user can write.
    /// </remarks>
    internal static string? ResolveSafePath(string destination, string relative)
    {
        var root = Path.GetFullPath(destination);
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        string candidate;
        try
        {
            candidate = Path.GetFullPath(Path.Combine(root, relative));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return candidate.StartsWith(rootWithSeparator, comparison) ? candidate : null;
    }
}
