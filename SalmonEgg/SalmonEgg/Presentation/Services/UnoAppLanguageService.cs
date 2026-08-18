using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Infrastructure.Services;
using SalmonEgg.Presentation.Core.Services;

namespace SalmonEgg.Presentation.Services;

public sealed class UnoAppLanguageService : IAppLanguageService
{
    private readonly AppCultureService _cultureService;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly ILogger<UnoAppLanguageService> _logger;
    private string _currentLanguageTag = AppLanguageCatalog.SystemTag;

    public UnoAppLanguageService(
        AppCultureService cultureService,
        IUiDispatcher uiDispatcher,
        ILogger<UnoAppLanguageService> logger)
    {
        _cultureService = cultureService ?? throw new ArgumentNullException(nameof(cultureService));
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool IsSupported => true;

    public string CurrentLanguageTag => _currentLanguageTag;

    public event EventHandler? LanguageChanged;

    public Task ApplyLanguageOverrideAsync(string languageTag)
    {
        var normalizedTag = AppLanguageCatalog.NormalizeTag(languageTag);
#if DEBUG
        _logger.LogDebug(
            "Language override enqueue requested. RequestedLanguageTag={RequestedLanguageTag} NormalizedLanguageTag={NormalizedLanguageTag} CurrentLanguageTag={CurrentLanguageTag}",
            languageTag,
            normalizedTag,
            _currentLanguageTag);
#endif
        return _uiDispatcher.EnqueueAsync(() => ApplyLanguageOverride(normalizedTag));
    }

    private void ApplyLanguageOverride(string normalizedTag)
    {
        var platformTag = AppLanguageCatalog.ToPlatformOverrideTag(normalizedTag);
#if DEBUG
        var previousLanguageTag = _currentLanguageTag;
        _logger.LogDebug(
            "Applying platform language override. NormalizedLanguageTag={NormalizedLanguageTag} PlatformLanguageTag={PlatformLanguageTag} PreviousLanguageTag={PreviousLanguageTag} IsSystemLanguage={IsSystemLanguage}",
            normalizedTag,
            platformTag,
            previousLanguageTag,
            string.Equals(normalizedTag, AppLanguageCatalog.SystemTag, StringComparison.Ordinal));
#endif
#if WINDOWS
        Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = platformTag;
#else
        Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = platformTag;
#endif
#if DEBUG
        _logger.LogDebug(
            "Platform language override accepted. NormalizedLanguageTag={NormalizedLanguageTag} PlatformLanguageTag={PlatformLanguageTag}",
            normalizedTag,
            platformTag);
#endif
        _cultureService.ApplyCultureOverride(normalizedTag);

        if (string.Equals(_currentLanguageTag, normalizedTag, StringComparison.Ordinal))
        {
#if DEBUG
            _logger.LogDebug(
                "Language override completed without LanguageChanged event. CurrentLanguageTag={CurrentLanguageTag}",
                _currentLanguageTag);
#endif
            return;
        }

        _currentLanguageTag = normalizedTag;
#if DEBUG
        _logger.LogDebug(
            "Language override completed; raising LanguageChanged. PreviousLanguageTag={PreviousLanguageTag} CurrentLanguageTag={CurrentLanguageTag}",
            previousLanguageTag,
            _currentLanguageTag);
#endif
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }
}
