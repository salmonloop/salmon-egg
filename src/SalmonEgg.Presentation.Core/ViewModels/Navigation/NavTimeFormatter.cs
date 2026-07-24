using System;
using System.Globalization;
using System.IO;
using Microsoft.Extensions.Localization;
using SalmonEgg.Presentation.Core.Resources;

namespace SalmonEgg.Presentation.ViewModels.Navigation;

public static class NavTimeFormatter
{
    public static string ToRelativeText(
        DateTime utcTimestamp,
        IStringLocalizer<CoreStrings>? localizer = null)
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
            return Localize(localizer, "Nav_RelativeJustNow", "Just now");
        }

        if (delta < TimeSpan.FromHours(1))
        {
            var minutes = Math.Max(1, (int)delta.TotalMinutes);
            return FormatLocalize(
                localizer,
                "Nav_RelativeMinutesFormat",
                "{0} min",
                minutes);
        }

        if (delta < TimeSpan.FromDays(1))
        {
            var hours = Math.Max(1, (int)delta.TotalHours);
            return FormatLocalize(
                localizer,
                "Nav_RelativeHoursFormat",
                "{0} hr",
                hours);
        }

        var days = Math.Max(1, (int)delta.TotalDays);
        return FormatLocalize(
            localizer,
            "Nav_RelativeDaysFormat",
            "{0} d",
            days);
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

    private static string Localize(
        IStringLocalizer<CoreStrings>? localizer,
        string key,
        string fallback)
    {
        if (localizer is null)
        {
            return fallback;
        }

        // localizer[key] may return null for incomplete/mocked localizers; fall back rather
        // than throw so navigation tree rebuild cannot be aborted by relative-time labels.
        var localized = localizer[key];
        return localized is null || localized.ResourceNotFound || string.IsNullOrWhiteSpace(localized.Value)
            ? fallback
            : localized.Value;
    }

    private static string FormatLocalize(
        IStringLocalizer<CoreStrings>? localizer,
        string key,
        string fallback,
        object argument)
    {
        var format = Localize(localizer, key, fallback);
        try
        {
            return string.Format(CultureInfo.CurrentCulture, format, argument);
        }
        catch (FormatException)
        {
            return string.Format(CultureInfo.InvariantCulture, fallback, argument);
        }
    }
}
