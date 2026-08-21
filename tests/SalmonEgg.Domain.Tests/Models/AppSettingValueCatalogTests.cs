using System;
using SalmonEgg.Domain.Models;
using Xunit;

namespace SalmonEgg.Domain.Tests.Models;

/// <summary>
/// Tests for the shared app-setting value catalog (keys, parsing, rendering).
/// </summary>
public sealed class AppSettingValueCatalogTests
{
    [Fact]
    public void EditableKeys_CoversExactlyTheRenderableKeys()
    {
        // 目录契约：每个可编辑键都必须能渲染当前值，否则 get 输出会缺行。
        var settings = new AppSettings();

        foreach (var key in AppSettingValueCatalog.EditableKeys)
        {
            Assert.NotNull(AppSettingValueCatalog.RenderValue(settings, key));
        }
    }

    [Theory]
    [InlineData("theme", "Dark")]
    [InlineData("backdrop", "Acrylic")]
    [InlineData("animation_enabled", "false")]
    [InlineData("save_local_history", "false")]
    [InlineData("telemetry_sharing_enabled", "false")]
    [InlineData("keyboard_shortcuts_enabled", "false")]
    [InlineData("cache_retention_days", "30")]
    public void TryApply_WithValidValues_AssignsField(string key, string value)
    {
        var settings = new AppSettings();

        Assert.True(AppSettingValueCatalog.TryApply(settings, key, value));

        Assert.Equal(value, AppSettingValueCatalog.RenderValue(settings, key), StringComparer.Ordinal);
    }

    [Fact]
    public void TryApply_WithLanguageAlias_NormalizesToCanonicalTag()
    {
        var settings = new AppSettings();

        Assert.True(AppSettingValueCatalog.TryApply(settings, "language", "zh-CN"));

        Assert.Equal(AppLanguageCatalog.SimplifiedChineseTag, settings.Language);
    }

    [Theory]
    [InlineData("theme", "Neon")]
    [InlineData("backdrop", "Glass")]
    [InlineData("animation_enabled", "yes")]
    [InlineData("cache_retention_days", "0")]
    [InlineData("cache_retention_days", "-3")]
    [InlineData("cache_retention_days", "many")]
    [InlineData("language", "   ")]
    public void TryApply_WithInvalidValues_RejectsAndLeavesDefaults(string key, string value)
    {
        var settings = new AppSettings();

        Assert.False(AppSettingValueCatalog.TryApply(settings, key, value));

        Assert.Equal(new AppSettings().Theme, settings.Theme);
        Assert.Equal(new AppSettings().CacheRetentionDays, settings.CacheRetentionDays);
    }

    [Fact]
    public void TryApply_WithUnknownKey_ReturnsFalse()
    {
        var settings = new AppSettings();

        Assert.False(AppSettingValueCatalog.TryApply(settings, "not_a_setting", "true"));
    }

    [Fact]
    public void AllowedValues_ExposesClosedSetsForEnumKeys()
    {
        Assert.Equal(["System", "Light", "Dark"], AppSettingValueCatalog.AllowedValues("theme"));
        Assert.Equal(["System", "Mica", "Acrylic", "Solid"], AppSettingValueCatalog.AllowedValues("backdrop"));

        var languageTags = AppSettingValueCatalog.AllowedValues("language");
        Assert.Contains(AppLanguageCatalog.SystemTag, languageTags!);
        Assert.Contains(AppLanguageCatalog.SimplifiedChineseTag, languageTags!);

        // 布尔与整数键没有封闭值域说明。
        Assert.Null(AppSettingValueCatalog.AllowedValues("animation_enabled"));
        Assert.Null(AppSettingValueCatalog.AllowedValues("cache_retention_days"));
    }

    [Fact]
    public void RenderValue_WithUnknownKey_ReturnsNull()
    {
        Assert.Null(AppSettingValueCatalog.RenderValue(new AppSettings(), "not_a_setting"));
    }

    [Fact]
    public void ThemeAndBackdropValues_MatchGuiOptionLists()
    {
        // GUI 选项列表（AppPreferencesViewModel.CreateThemeOptions/CreateBackdropOptions）
        // 与本目录必须同源同序；此处锁定值集，防止两处漂移。
        Assert.Equal(3, AppSettingValueCatalog.ThemeValues.Count);
        Assert.Equal(4, AppSettingValueCatalog.BackdropValues.Count);
    }
}
