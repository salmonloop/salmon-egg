using System;
using System.Globalization;
using SalmonEgg.Domain.Models;

namespace SalmonEgg.Infrastructure.Services;

public sealed class AppCultureService
{
    private static readonly CultureInfo InitialCulture = CultureInfo.CurrentCulture;
    private static readonly CultureInfo InitialUiCulture = CultureInfo.CurrentUICulture;

    public void ApplyCultureOverride(string languageTag)
    {
        var normalizedTag = AppLanguageCatalog.NormalizeTag(languageTag);
        ApplyDotNetCultureOverride(normalizedTag);
    }

    private static void ApplyDotNetCultureOverride(string languageTag)
    {
        var culture = string.Equals(languageTag, AppLanguageCatalog.SystemTag, StringComparison.Ordinal)
            ? InitialCulture
            : CultureInfo.GetCultureInfo(languageTag);
        var uiCulture = string.Equals(languageTag, AppLanguageCatalog.SystemTag, StringComparison.Ordinal)
            ? InitialUiCulture
            : CultureInfo.GetCultureInfo(languageTag);

        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = uiCulture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = uiCulture;
    }
}
