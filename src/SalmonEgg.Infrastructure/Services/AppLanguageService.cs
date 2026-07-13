using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Infrastructure.Services;

public sealed class AppLanguageService : IAppLanguageService
{
    private static readonly CultureInfo InitialCulture = CultureInfo.CurrentCulture;
    private static readonly CultureInfo InitialUiCulture = CultureInfo.CurrentUICulture;

    public bool IsSupported => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public Task ApplyLanguageOverrideAsync(string languageTag)
    {
        var normalizedTag = AppLanguageCatalog.NormalizeTag(languageTag);

#if WINDOWS || WINDOWS_UWP
        try
        {
            var tag = AppLanguageCatalog.ToPlatformOverrideTag(normalizedTag);
            Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = tag;
        }
        catch
        {
        }
#endif
        if (IsSupported)
        {
            ApplyDotNetCultureOverride(normalizedTag);
        }

        return Task.CompletedTask;
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
