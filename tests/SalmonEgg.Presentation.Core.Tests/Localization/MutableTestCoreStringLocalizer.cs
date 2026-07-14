using System.Collections.Generic;
using System.Globalization;
using Microsoft.Extensions.Localization;
using SalmonEgg.Presentation.Core.Resources;

namespace SalmonEgg.Presentation.Core.Tests.Localization;

internal sealed class MutableTestCoreStringLocalizer : IStringLocalizer<CoreStrings>
{
    private readonly Dictionary<string, Dictionary<string, string>> _localizedStrings = new(StringComparer.OrdinalIgnoreCase);
    private string _languageTag = "zh-Hans";

    public void SetLanguageTag(string languageTag)
        => _languageTag = string.IsNullOrWhiteSpace(languageTag) ? "zh-Hans" : languageTag;

    public void Set(string languageTag, string key, string value)
    {
        if (!_localizedStrings.TryGetValue(languageTag, out var languageStrings))
        {
            languageStrings = new Dictionary<string, string>(StringComparer.Ordinal);
            _localizedStrings[languageTag] = languageStrings;
        }

        languageStrings[key] = value;
    }

    public LocalizedString this[string name]
        => Resolve(name, arguments: null);

    public LocalizedString this[string name, params object[] arguments]
        => Resolve(name, arguments);

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
        => [];

    private LocalizedString Resolve(string name, object[]? arguments)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return new LocalizedString(name, name, resourceNotFound: true, "CoreStrings");
        }

        var value = ResolveValue(name);
        if (value is null)
        {
            return new LocalizedString(name, name, resourceNotFound: true, "CoreStrings");
        }

        var localizedValue = arguments is { Length: > 0 }
            ? string.Format(CultureInfo.InvariantCulture, value, arguments)
            : value;
        return new LocalizedString(name, localizedValue, resourceNotFound: false, "CoreStrings");
    }

    private string? ResolveValue(string key)
    {
        if (_localizedStrings.TryGetValue(_languageTag, out var languageStrings)
            && languageStrings.TryGetValue(key, out var value))
        {
            return value;
        }

        if (_localizedStrings.TryGetValue("zh-Hans", out var zhStrings)
            && zhStrings.TryGetValue(key, out var zhValue))
        {
            return zhValue;
        }

        if (_localizedStrings.TryGetValue("en-US", out var enStrings)
            && enStrings.TryGetValue(key, out var enValue))
        {
            return enValue;
        }

        return null;
    }
}
