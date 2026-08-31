using System.Globalization;
using Microsoft.Extensions.Localization;
using SalmonEgg.Presentation.Core.Resources;

namespace SalmonEgg.Presentation.Core.Localization;

/// <summary>
/// Resolves a CoreStrings resource key to display text, falling back when the key is unresolvable.
/// </summary>
/// <remarks>
/// A missing resource must not reach the screen as an empty string: a blank label is
/// indistinguishable from a layout bug, while the caller's fallback (usually the key itself) is
/// diagnosable. <see cref="IStringLocalizer"/> reports that case through
/// <see cref="LocalizedString.ResourceNotFound"/>, but also returns a whitespace-only value for a
/// resource that exists and is blank, so both are treated as unresolved.
///
/// The localizer is optional because view models are constructed in tests and on platforms where no
/// resource loader is registered; those callers get the fallback rather than a null check at every
/// use site.
/// </remarks>
public static class CoreStringResolver
{
    /// <summary>
    /// Returns the localized value for <paramref name="key"/>, or <paramref name="fallback"/> when
    /// there is no localizer, the key is empty, or the resource is missing or blank.
    /// </summary>
    public static string Resolve(IStringLocalizer<CoreStrings>? localizer, string? key, string fallback)
    {
        if (localizer is null || string.IsNullOrEmpty(key))
        {
            return fallback;
        }

        var localized = localizer[key];
        return localized.ResourceNotFound || string.IsNullOrWhiteSpace(localized.Value)
            ? fallback
            : localized.Value;
    }

    /// <summary>
    /// Same contract as <see cref="Resolve"/> for a resource carrying <c>{0}</c>-style placeholders:
    /// the arguments are formatted into whichever string wins, so the fallback is a format string too.
    /// </summary>
    /// <remarks>
    /// Parameterized resolution lives here rather than at the call site because the unresolved cases
    /// are the same three, and a second implementation of them drifts from this one the first time
    /// either is corrected. Formatting uses the current culture, matching how the resource itself was
    /// selected.
    /// </remarks>
    public static string ResolveFormat(
        IStringLocalizer<CoreStrings>? localizer,
        string? key,
        string fallbackFormat,
        params object[] arguments)
    {
        if (localizer is null || string.IsNullOrEmpty(key))
        {
            return string.Format(CultureInfo.CurrentCulture, fallbackFormat, arguments);
        }

        var localized = localizer[key, arguments];
        return localized.ResourceNotFound || string.IsNullOrWhiteSpace(localized.Value)
            ? string.Format(CultureInfo.CurrentCulture, fallbackFormat, arguments)
            : localized.Value;
    }
}
