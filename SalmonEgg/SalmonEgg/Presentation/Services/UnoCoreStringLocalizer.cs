using System.Globalization;
using Microsoft.Extensions.Localization;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Resources;
using Windows.ApplicationModel.Resources;

namespace SalmonEgg.Presentation.Services;

public sealed class UnoCoreStringLocalizer : IStringLocalizer<CoreStrings>
{
    private static readonly ResourceLoader ResourceLoader =
        ResourceLoader.GetForViewIndependentUse("CoreStrings");

    private readonly IAppLanguageService _languageService;

    public UnoCoreStringLocalizer(IAppLanguageService languageService)
    {
        _languageService = languageService ?? throw new ArgumentNullException(nameof(languageService));
    }

    public LocalizedString this[string name] => GetLocalizedString(name, arguments: null);

    public LocalizedString this[string name, params object[] arguments] => GetLocalizedString(name, arguments);

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];

    private LocalizedString GetLocalizedString(string name, object[]? arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var value = ResourceLoader.GetString(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return new LocalizedString(name, name, resourceNotFound: true, "CoreStrings");
        }

        var localizedValue = arguments is { Length: > 0 }
            ? string.Format(ResolveCulture(), value, arguments)
            : value;
        return new LocalizedString(name, localizedValue, resourceNotFound: false, "CoreStrings");
    }

    private CultureInfo ResolveCulture()
    {
        var languageTag = _languageService.CurrentLanguageTag;
        return string.Equals(languageTag, AppLanguageCatalog.SystemTag, StringComparison.Ordinal)
            ? CultureInfo.CurrentUICulture
            : CultureInfo.GetCultureInfo(languageTag);
    }
}
