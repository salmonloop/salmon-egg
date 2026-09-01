using System;
using SalmonEgg.Domain.Models.AcpSetup;

namespace SalmonEgg.Infrastructure.Desktop.AcpSetup;

/// <summary>
/// Reads the expected SHA-256 digest for one archive out of a vendor's published checksum document.
/// </summary>
internal static class AcpToolchainChecksum
{
    /// <summary>
    /// Returns the digest for <paramref name="fileName"/>, or null when the document does not state one.
    /// </summary>
    /// <remarks>
    /// The file name is matched rather than assumed to be the document's only subject. Node publishes one
    /// <c>SHASUMS256.txt</c> covering every artifact of a release — over thirty lines — so taking the first
    /// digest would verify the archive against a different file's hash and reject a perfectly good
    /// download. Returning null on no match is what makes the caller refuse to install rather than proceed
    /// unverified.
    /// </remarks>
    internal static string? Parse(string document, AcpChecksumFormat format, string fileName)
    {
        if (string.IsNullOrWhiteSpace(document))
        {
            return null;
        }

        return format switch
        {
            AcpChecksumFormat.ShasumList => ParseList(document, fileName),
            AcpChecksumFormat.SingleHash => ParseSingle(document),
            _ => null
        };
    }

    private static string? ParseList(string document, string fileName)
    {
        foreach (var rawLine in document.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            // "<hash>  <name>", with the name sometimes prefixed by a binary marker ('*') or a directory.
            var separator = line.IndexOf(' ');
            if (separator <= 0)
            {
                continue;
            }

            var digest = line[..separator].Trim();
            var name = line[(separator + 1)..].Trim().TrimStart('*');
            if (!IsSha256(digest))
            {
                continue;
            }

            // The trailing segment is compared so a line naming a path still matches a bare file name.
            var slash = name.LastIndexOfAny(new[] { '/', '\\' });
            if (slash >= 0)
            {
                name = name[(slash + 1)..];
            }

            if (string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase))
            {
                return digest.ToLowerInvariant();
            }
        }

        return null;
    }

    /// <summary>
    /// Reads a lone digest, tolerating the <c>sha256sum</c> convention of a trailing file name.
    /// </summary>
    private static string? ParseSingle(string document)
    {
        foreach (var rawLine in document.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var separator = line.IndexOf(' ');
            var digest = (separator > 0 ? line[..separator] : line).Trim();
            if (IsSha256(digest))
            {
                return digest.ToLowerInvariant();
            }
        }

        return null;
    }

    private static bool IsSha256(string value)
    {
        if (value.Length != 64)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!Uri.IsHexDigit(character))
            {
                return false;
            }
        }

        return true;
    }
}
