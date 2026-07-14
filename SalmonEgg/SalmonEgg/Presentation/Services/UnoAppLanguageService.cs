using System;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Infrastructure.Services;
using SalmonEgg.Presentation.Core.Services;

namespace SalmonEgg.Presentation.Services;

public sealed class UnoAppLanguageService : IAppLanguageService
{
    private readonly AppCultureService _cultureService;
    private readonly IUiDispatcher _uiDispatcher;
    private string _currentLanguageTag = AppLanguageCatalog.SystemTag;

    public UnoAppLanguageService(
        AppCultureService cultureService,
        IUiDispatcher uiDispatcher)
    {
        _cultureService = cultureService ?? throw new ArgumentNullException(nameof(cultureService));
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
    }

    public bool IsSupported => true;

    public string CurrentLanguageTag => _currentLanguageTag;

    public event EventHandler? LanguageChanged;

    public Task ApplyLanguageOverrideAsync(string languageTag)
    {
        var normalizedTag = AppLanguageCatalog.NormalizeTag(languageTag);
        return _uiDispatcher.EnqueueAsync(() => ApplyLanguageOverride(normalizedTag));
    }

    private void ApplyLanguageOverride(string normalizedTag)
    {
        var platformTag = AppLanguageCatalog.ToPlatformOverrideTag(normalizedTag);
#if WINDOWS
        Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = platformTag;
#else
        Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = platformTag;
#endif
        _cultureService.ApplyCultureOverride(normalizedTag);

        if (string.Equals(_currentLanguageTag, normalizedTag, StringComparison.Ordinal))
        {
            return;
        }

        _currentLanguageTag = normalizedTag;
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }
}
