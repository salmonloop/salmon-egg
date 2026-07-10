using SalmonEgg.Domain.Models;
using Xunit;

namespace SalmonEgg.Domain.Tests.Models;

public sealed class AppLanguageCatalogTests
{
    [Theory]
    [InlineData(null, AppLanguageCatalog.SystemTag)]
    [InlineData("", AppLanguageCatalog.SystemTag)]
    [InlineData("System", AppLanguageCatalog.SystemTag)]
    [InlineData("en", AppLanguageCatalog.EnglishUnitedStatesTag)]
    [InlineData("en-US", AppLanguageCatalog.EnglishUnitedStatesTag)]
    [InlineData("zh", AppLanguageCatalog.SimplifiedChineseTag)]
    [InlineData("zh-CN", AppLanguageCatalog.SimplifiedChineseTag)]
    [InlineData("zh-Hans", AppLanguageCatalog.SimplifiedChineseTag)]
    [InlineData("fr-FR", AppLanguageCatalog.SystemTag)]
    public void NormalizeTag_ReturnsCanonicalSupportedTags(string? input, string expected)
    {
        Assert.Equal(expected, AppLanguageCatalog.NormalizeTag(input));
    }

    [Theory]
    [InlineData("System", "")]
    [InlineData("zh-CN", AppLanguageCatalog.SimplifiedChineseTag)]
    [InlineData("en", AppLanguageCatalog.EnglishUnitedStatesTag)]
    public void ToPlatformOverrideTag_UsesCanonicalTags(string input, string expected)
    {
        Assert.Equal(expected, AppLanguageCatalog.ToPlatformOverrideTag(input));
    }

    [Fact]
    public void SupportedResourceLanguageTags_DeclaresShippedCanonicalResourceCultures()
    {
        Assert.Equal(new[] { AppLanguageCatalog.EnglishNeutralTag, AppLanguageCatalog.EnglishUnitedStatesTag, AppLanguageCatalog.SimplifiedChineseTag }, AppLanguageCatalog.SupportedResourceLanguageTags);
    }

    [Fact]
    public void SupportedResourceLanguageTags_AreDerivedFromOptions()
    {
        var expected = AppLanguageCatalog.SupportedOptions
            .SelectMany(option => option.ResourceLanguageTags)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, AppLanguageCatalog.SupportedResourceLanguageTags);
    }

    [Fact]
    public void LegacyAliasTags_AreDerivedFromOptions()
    {
        var expected = AppLanguageCatalog.SupportedOptions
            .SelectMany(option => option.Aliases)
            .Except(AppLanguageCatalog.SupportedResourceLanguageTags, StringComparer.Ordinal)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, AppLanguageCatalog.LegacyAliasTags);
    }
}
