using SalmonEgg.Domain.Models;
using NUnit.Framework;

namespace SalmonEgg.Domain.Tests.Models;

public sealed class AppLanguageCatalogTests
{
    [TestCase(null, AppLanguageCatalog.SystemTag)]
    [TestCase("", AppLanguageCatalog.SystemTag)]
    [TestCase("System", AppLanguageCatalog.SystemTag)]
    [TestCase("en", AppLanguageCatalog.EnglishUnitedStatesTag)]
    [TestCase("en-US", AppLanguageCatalog.EnglishUnitedStatesTag)]
    [TestCase("zh", AppLanguageCatalog.SimplifiedChineseTag)]
    [TestCase("zh-CN", AppLanguageCatalog.SimplifiedChineseTag)]
    [TestCase("zh-Hans", AppLanguageCatalog.SimplifiedChineseTag)]
    [TestCase("fr-FR", AppLanguageCatalog.SystemTag)]
    public void NormalizeTag_ReturnsCanonicalSupportedTags(string? input, string expected)
    {
        Assert.That(AppLanguageCatalog.NormalizeTag(input), Is.EqualTo(expected));
    }

    [TestCase("System", "")]
    [TestCase("zh-CN", AppLanguageCatalog.SimplifiedChineseTag)]
    [TestCase("en", AppLanguageCatalog.EnglishUnitedStatesTag)]
    public void ToPlatformOverrideTag_UsesCanonicalTags(string input, string expected)
    {
        Assert.That(AppLanguageCatalog.ToPlatformOverrideTag(input), Is.EqualTo(expected));
    }

    [Test]
    public void SupportedResourceLanguageTags_DeclaresShippedCanonicalResourceCultures()
    {
        Assert.That(
            AppLanguageCatalog.SupportedResourceLanguageTags,
            Is.EqualTo(new[]
            {
                AppLanguageCatalog.EnglishNeutralTag,
                AppLanguageCatalog.EnglishUnitedStatesTag,
                AppLanguageCatalog.SimplifiedChineseTag
            }));
    }

    [Test]
    public void SupportedResourceLanguageTags_AreDerivedFromOptions()
    {
        var expected = AppLanguageCatalog.SupportedOptions
            .SelectMany(option => option.ResourceLanguageTags)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.That(AppLanguageCatalog.SupportedResourceLanguageTags, Is.EqualTo(expected));
    }

    [Test]
    public void LegacyAliasTags_AreDerivedFromOptions()
    {
        var expected = AppLanguageCatalog.SupportedOptions
            .SelectMany(option => option.Aliases)
            .Except(AppLanguageCatalog.SupportedResourceLanguageTags, StringComparer.Ordinal)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.That(AppLanguageCatalog.LegacyAliasTags, Is.EqualTo(expected));
    }
}
