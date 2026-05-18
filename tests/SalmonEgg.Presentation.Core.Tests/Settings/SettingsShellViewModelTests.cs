using SalmonEgg.Presentation.Core.Tests.Localization;
using SalmonEgg.Presentation.Models.Settings;
using SalmonEgg.Presentation.ViewModels.Settings;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Settings;

public sealed class SettingsShellViewModelTests
{
    [Fact]
    public void Catalog_ProvidesCanonicalSectionMetadata()
    {
        Assert.Collection(
            SettingsSectionCatalog.Sections,
            section => AssertSection(section, "General", "SettingsSection_General", "SettingsNav.General"),
            section => AssertSection(section, "Appearance", "SettingsSection_Appearance", "SettingsNav.Appearance"),
            section => AssertSection(section, "AgentAcp", "SettingsSection_AgentAcp", "SettingsNav.AgentAcp"),
            section => AssertSection(section, "DataStorage", "SettingsSection_DataStorage", "SettingsNav.DataStorage"),
            section => AssertSection(section, "Shortcuts", "SettingsSection_Shortcuts", "SettingsNav.Shortcuts"),
            section => AssertSection(section, "Diagnostics", "SettingsSection_Diagnostics", "SettingsNav.Diagnostics"),
            section => AssertSection(section, "About", "SettingsSection_About", "SettingsNav.About"));
        Assert.Same(SettingsSectionCatalog.Sections[0], SettingsSectionCatalog.DefaultSection);
    }

    [Fact]
    public void Catalog_ResolvesTitlesFromCoreStrings()
    {
        var localizer = new TestCoreStringLocalizer();

        Assert.Equal("设置", SettingsSectionCatalog.ResolveRootTitle(localizer));
        Assert.Equal("诊断与日志", SettingsSectionCatalog.ResolveTitle(localizer, SettingsSectionCatalog.DiagnosticsKey));
        Assert.Equal("常规", SettingsSectionCatalog.ResolveTitle(localizer, "Missing"));
    }

    [Fact]
    public void Constructor_ProvidesStableSectionOrderAndDefaultSelection()
    {
        var viewModel = new SettingsShellViewModel(new TestCoreStringLocalizer());

        Assert.Collection(
            viewModel.Sections,
            section => Assert.Equal("General", section.Key),
            section => Assert.Equal("Appearance", section.Key),
            section => Assert.Equal("AgentAcp", section.Key),
            section => Assert.Equal("DataStorage", section.Key),
            section => Assert.Equal("Shortcuts", section.Key),
            section => Assert.Equal("Diagnostics", section.Key),
            section => Assert.Equal("About", section.Key));
        Assert.Same(viewModel.Sections[0], viewModel.SelectedSection);
    }

    [Fact]
    public void SelectSection_WhenKeyExists_SelectsSharedSectionInstance()
    {
        var viewModel = new SettingsShellViewModel(new TestCoreStringLocalizer());

        var selected = viewModel.SelectSection(SettingsSectionCatalog.DiagnosticsKey);

        Assert.Same(viewModel.Sections[5], selected);
        Assert.Same(selected, viewModel.SelectedSection);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Missing")]
    public void SelectSection_WhenKeyIsUnknown_FallsBackToGeneral(string? key)
    {
        var viewModel = new SettingsShellViewModel(new TestCoreStringLocalizer());

        var selected = viewModel.SelectSection(key);

        Assert.Same(viewModel.Sections[0], selected);
        Assert.Same(selected, viewModel.SelectedSection);
    }

    private static void AssertSection(
        SettingsSectionDefinition section,
        string key,
        string titleResourceKey,
        string automationId)
    {
        Assert.Equal(key, section.Key);
        Assert.Equal(titleResourceKey, section.TitleResourceKey);
        Assert.Equal(automationId, section.AutomationId);
    }
}
