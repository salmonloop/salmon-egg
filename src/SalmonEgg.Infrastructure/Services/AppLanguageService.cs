using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Infrastructure.Services;

public sealed class AppLanguageService : IAppLanguageService
{
    public bool IsSupported => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public Task ApplyLanguageOverrideAsync(string languageTag)
    {
#if WINDOWS || WINDOWS_UWP
        try
        {
            var tag = AppLanguageCatalog.ToPlatformOverrideTag(languageTag);
            Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = tag;
        }
        catch
        {
        }
#endif
        return Task.CompletedTask;
    }
}
