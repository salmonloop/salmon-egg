using System;
using System.IO;

namespace SalmonEgg.Presentation.ViewModels.Navigation;

public static class NavTimeFormatter
{
    public static string ToRelativeText(DateTime utcTimestamp)
    {
        if (utcTimestamp == default)
        {
            return string.Empty;
        }

        var now = DateTime.UtcNow;
        var delta = now - utcTimestamp;
        if (delta < TimeSpan.Zero)
        {
            delta = TimeSpan.Zero;
        }

        if (delta < TimeSpan.FromMinutes(1))
        {
            return "Just now";
        }

        if (delta < TimeSpan.FromHours(1))
        {
            return $"{Math.Max(1, (int)delta.TotalMinutes)} min";
        }

        if (delta < TimeSpan.FromDays(1))
        {
            return $"{Math.Max(1, (int)delta.TotalHours)} hr";
        }

        return $"{Math.Max(1, (int)delta.TotalDays)} d";
    }

    public static string NormalizePathForPrefixMatch(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var trimmed = path.Trim();
        try
        {
            trimmed = Path.GetFullPath(trimmed);
        }
        catch
        {
        }

        trimmed = trimmed.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        return trimmed + Path.DirectorySeparatorChar;
    }
}
