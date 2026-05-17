using System;
using System.Collections.Generic;
using System.Linq;

namespace SalmonEgg.Domain.Models;

public static class AppLanguageCatalog
{
    public const string SystemTag = "System";
    public const string EnglishNeutralTag = "en";
    public const string EnglishUnitedStatesTag = "en-US";
    public const string SimplifiedChineseTag = "zh-Hans";

    private static readonly AppLanguageOption[] Options =
    [
        new(SystemTag, SystemTag, "General_LanguageSystem.Content", [], []),
        new(EnglishUnitedStatesTag, EnglishUnitedStatesTag, "General_LanguageEn.Content", [EnglishNeutralTag], [EnglishNeutralTag, EnglishUnitedStatesTag]),
        new(SimplifiedChineseTag, SimplifiedChineseTag, "General_LanguageZhCn.Content", ["zh", "zh-CN", "zh-SG", "zh-Hans-CN"])
    ];

    public static IReadOnlyList<AppLanguageOption> SupportedOptions => Options;

    public static IReadOnlyList<string> SupportedResourceLanguageTags { get; } = Options
        .SelectMany(option => option.ResourceLanguageTags)
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    public static IReadOnlyList<string> LegacyAliasTags { get; } = Options
        .SelectMany(option => option.Aliases)
        .Except(SupportedResourceLanguageTags, StringComparer.Ordinal)
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    public static string NormalizeTag(string? languageTag)
    {
        if (string.IsNullOrWhiteSpace(languageTag))
        {
            return SystemTag;
        }

        var tag = languageTag.Trim();
        var option = Options.FirstOrDefault(candidate =>
            string.Equals(candidate.Tag, tag, StringComparison.OrdinalIgnoreCase)
            || candidate.Aliases.Any(alias => string.Equals(alias, tag, StringComparison.OrdinalIgnoreCase)));

        return option?.Tag ?? SystemTag;
    }

    public static string ToPlatformOverrideTag(string? languageTag)
    {
        var tag = NormalizeTag(languageTag);
        return string.Equals(tag, SystemTag, StringComparison.Ordinal)
            ? string.Empty
            : tag;
    }
}

public sealed class AppLanguageOption
{
    public AppLanguageOption(
        string tag,
        string resourceLanguageTag,
        string displayNameResourceKey,
        IReadOnlyList<string> aliases,
        IReadOnlyList<string>? resourceLanguageTags = null)
    {
        Tag = tag;
        ResourceLanguageTag = resourceLanguageTag;
        DisplayNameResourceKey = displayNameResourceKey;
        Aliases = aliases;
        ResourceLanguageTags = resourceLanguageTags is not null
            ? resourceLanguageTags.Distinct(StringComparer.Ordinal).ToArray()
            : [resourceLanguageTag];
    }

    public string Tag { get; }

    public string ResourceLanguageTag { get; }

    public IReadOnlyList<string> ResourceLanguageTags { get; }

    public string DisplayNameResourceKey { get; }

    public IReadOnlyList<string> Aliases { get; }
}
