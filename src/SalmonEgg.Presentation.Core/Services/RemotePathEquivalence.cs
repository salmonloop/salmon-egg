using System;
using SalmonEgg.Acp.Protocol;

namespace SalmonEgg.Presentation.Core.Services;

/// <summary>
/// Equivalence semantics for remote agent paths (ACP cwd / additionalDirectories /
/// AgentRemoteDirectory.RemotePath). Remote paths are compared as forward-slash absolute
/// strings with trailing separators removed; case-insensitivity applies only where the
/// path shape carries Windows drive/UNC semantics (see
/// <see cref="UsesCaseInsensitivePathSemantics"/>). Local filesystem paths must not run
/// through this type: <see cref="System.IO.Path"/> resolution would rewrite remote POSIX
/// paths against the local platform.
/// </summary>
public static class RemotePathEquivalence
{
    /// <summary>
    /// Normalizes a remote path for comparison and persistence: trimmed, backslashes
    /// folded to forward slashes, trailing separators removed. Empty input normalizes to
    /// the empty string.
    /// </summary>
    public static string Normalize(string? path)
    {
        var trimmed = path?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        return trimmed.Replace('\\', '/').TrimEnd('/');
    }

    /// <summary>
    /// Remote-path equality: equal after <see cref="Normalize"/>, case-insensitive only
    /// when either side carries Windows drive/UNC semantics.
    /// </summary>
    public static bool Equals(string? left, string? right)
    {
        var normalizedLeft = Normalize(left);
        var normalizedRight = Normalize(right);
        var comparison = UsesCaseInsensitivePathSemantics(normalizedLeft) || UsesCaseInsensitivePathSemantics(normalizedRight)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(normalizedLeft, normalizedRight, comparison);
    }

    private static bool UsesCaseInsensitivePathSemantics(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return ProtocolPathRules.IsAbsolutePath(path)
            && (path.StartsWith(@"\\", StringComparison.Ordinal)
                || (path.Length >= 3
                    && char.IsLetter(path[0])
                    && path[1] == ':'
                    && path[2] == '/'));
    }
}
