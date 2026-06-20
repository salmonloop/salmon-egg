using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Ui;

public sealed class XamlComplianceTests
{
    [Fact]
    public void WinUiMsixScript_RestoresAllReferenceProjectsUsedByApp()
    {
        var script = LoadText(@".tools\run-winui3-msix.ps1");

        Assert.Contains(
            "'src\\SalmonEgg.Infrastructure.Desktop\\SalmonEgg.Infrastructure.Desktop.csproj'",
            script,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SessionGuiRegressionScript_CoversInstalledMsixRightPanelAuxiliaryPanelPath()
    {
        var script = LoadText(@".tools\run-session-gui-regression.ps1");

        Assert.Contains(
            ".tools\\run-winui3-msix.ps1",
            script,
            StringComparison.Ordinal);
        Assert.DoesNotContain("-SkipInstall", script, StringComparison.Ordinal);
        Assert.Contains(
            "FullyQualifiedName~ChatSkeletonSmokeTests.AuxiliaryPanels_AfterCloseAndReopen_RetainContentInsteadOfBlankSurface",
            script,
            StringComparison.Ordinal);
    }

    [Fact]
    public void WinUiMsixScript_ClearsDebugEnvironmentOverridesBeforeLaunch()
    {
        var script = LoadText(@".tools\run-winui3-msix.ps1");

        Assert.Contains("Clear-SalmonEggDebugEnvironmentOverrides", script, StringComparison.Ordinal);
        Assert.Contains("SALMONEGG_APPDATA_ROOT", script, StringComparison.Ordinal);
        Assert.Contains("'SALMONEGG_GUI'", script, StringComparison.Ordinal);
        Assert.Contains("[EnvironmentVariableTarget]::User", script, StringComparison.Ordinal);
    }

    [Fact]
    public void MainPage_DoesNotDisableFocusOnInteraction()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\MainPage.xaml");

        Assert.DoesNotContain("AllowFocusOnInteraction=\"False\"", xaml);
    }

    [Theory]
    [InlineData("TitleBarBackButton")]
    [InlineData("TitleBarToggleLeftNavButton")]
    [InlineData("TaskOverviewPanelButton")]
    public void MainPage_IconButtonsHaveAutomationName(string elementName)
    {
        var element = FindElementByName(@"SalmonEgg\SalmonEgg\MainPage.xaml", elementName);

        Assert.True(
            HasAttributeByLocalName(element, "AutomationProperties.Name") || HasAttributeByLocalName(element, "Uid"),
            $"{elementName} must expose an accessible name via AutomationProperties.Name or x:Uid localization.");
    }

    [Fact]
    public void MainPage_SearchBoxHasAutomationName()
    {
        var element = FindElementByName(@"SalmonEgg\SalmonEgg\MainPage.xaml", "TopSearchBox");

        Assert.True(
            HasAttributeByLocalName(element, "AutomationProperties.Name") || HasXUid(element, "TopSearchBox"),
            "TopSearchBox must expose an accessible name via AutomationProperties.Name or x:Uid localization.");
    }

    [Fact]
    public void MainPage_SearchLayoutAvoidsFixedWidths()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\MainPage.xaml");

        Assert.DoesNotContain("TopSearchBox.Width", xaml);
        Assert.DoesNotContain("MinWidth\" Value=\"420\"", xaml);
        Assert.DoesNotContain("MaxWidth\" Value=\"420\"", xaml);
    }

    [Fact]
    public void MainPage_SearchUsesVirtualizedRepeaters()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\MainPage.xaml");

        Assert.DoesNotContain("ItemsControl ItemsSource=\"{x:Bind SearchVM.ResultGroups", xaml);
        Assert.DoesNotContain("ItemsControl ItemsSource=\"{x:Bind SearchVM.RecentSearches", xaml);
    }

    [Fact]
    public void MainPage_ProjectItemsRemainNonSelectableGroups()
    {
        var document = XDocument.Parse(LoadXaml(@"SalmonEgg\SalmonEgg\MainPage.xaml"));
        var xNamespace = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");

        var projectTemplate = document
            .Descendants()
            .FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "DataTemplate", StringComparison.Ordinal)
                && string.Equals(element.Attribute(xNamespace + "Key")?.Value, "ProjectNavTemplate", StringComparison.Ordinal));

        Assert.NotNull(projectTemplate);

        var projectNavItem = projectTemplate!
            .Descendants()
            .FirstOrDefault(element => string.Equals(element.Name.LocalName, "NavigationViewItem", StringComparison.Ordinal));

        Assert.NotNull(projectNavItem);
        Assert.True(
            string.Equals(projectNavItem!.Attribute("SelectsOnInvoked")?.Value, "False", StringComparison.OrdinalIgnoreCase),
            "ProjectNavTemplate must remain a native non-selectable grouping item.");
    }

    [Fact]
    public void MainPage_ProjectExpansionUsesNativeNavigationViewBehavior()
    {
        var document = XDocument.Parse(LoadXaml(@"SalmonEgg\SalmonEgg\MainPage.xaml"));
        var xNamespace = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");

        var mainNavView = document
            .Descendants()
            .Single(element =>
                string.Equals(element.Name.LocalName, "NavigationView", StringComparison.Ordinal)
                && string.Equals(element.Attribute(xNamespace + "Name")?.Value, "MainNavView", StringComparison.Ordinal));
        var projectTemplate = document
            .Descendants()
            .Single(element =>
                string.Equals(element.Name.LocalName, "DataTemplate", StringComparison.Ordinal)
                && string.Equals(element.Attribute(xNamespace + "Key")?.Value, "ProjectNavTemplate", StringComparison.Ordinal));
        var projectNavItem = projectTemplate
            .Descendants()
            .Single(element => string.Equals(element.Name.LocalName, "NavigationViewItem", StringComparison.Ordinal));
        var xaml = document.ToString(SaveOptions.DisableFormatting);

        Assert.Equal("{x:Bind NavVM.Items, Mode=OneWay}", mainNavView.Attribute("MenuItemsSource")?.Value);
        Assert.Equal("{x:Bind NavVM.FooterItems, Mode=OneWay}", mainNavView.Attribute("FooterMenuItemsSource")?.Value);
        Assert.Equal("{x:Bind Children, Mode=OneWay}", projectNavItem.Attribute("MenuItemsSource")?.Value);
        Assert.Equal("{x:Bind IsExpanded, Mode=TwoWay}", projectNavItem.Attribute("IsExpanded")?.Value);
        Assert.DoesNotContain("MenuItemsSource=\"{x:Bind NavVM.MenuItems, Mode=OneWay}\"", xaml);
        Assert.DoesNotContain("FooterMenuItemsSource=\"{x:Bind NavVM.FooterMenuItems, Mode=OneWay}\"", xaml);
        Assert.DoesNotContain("MenuItemsSource=\"{x:Bind ChildrenMenuItems, Mode=OneWay}\"", xaml);
        Assert.DoesNotContain("IsExpanded=\"{x:Bind IsExpanded, Mode=OneWay}\"", xaml);
        Assert.DoesNotContain("Expanding=\"OnMainNavItemExpanding\"", xaml);
        Assert.DoesNotContain("Collapsed=\"OnMainNavItemCollapsed\"", xaml);
        Assert.DoesNotContain("SelectionChanged=\"OnMainNavSelectionChanged\"", xaml);
    }

    [Fact]
    public void MainNavigationViewModel_DoesNotPublishNavigationViewMenuSnapshots()
    {
        var navVm = LoadText(@"src\SalmonEgg.Presentation.Core\ViewModels\Navigation\MainNavigationViewModel.cs");
        var navItemVm = LoadText(@"src\SalmonEgg.Presentation.Core\ViewModels\Navigation\MainNavItemViewModel.cs");

        Assert.Contains("ObservableCollection<MainNavItemViewModel> Items", navVm, StringComparison.Ordinal);
        Assert.Contains("ObservableCollection<MainNavItemViewModel> FooterItems", navVm, StringComparison.Ordinal);
        Assert.Contains("ObservableCollection<MainNavItemViewModel> Children", navItemVm, StringComparison.Ordinal);
        Assert.DoesNotContain("public IReadOnlyList<MainNavItemViewModel> MenuItems", navVm, StringComparison.Ordinal);
        Assert.DoesNotContain("public IReadOnlyList<MainNavItemViewModel> FooterMenuItems", navVm, StringComparison.Ordinal);
        Assert.DoesNotContain("ChildrenMenuItems", navItemVm, StringComparison.Ordinal);
        Assert.DoesNotContain("PublishMenuSnapshots", navVm, StringComparison.Ordinal);
        Assert.DoesNotContain("forceSelectedItemNotification", navVm, StringComparison.Ordinal);
    }

    [Fact]
    public void ToolCallPill_UsesNativeExpanderBehavior()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Controls\ToolCallPill.xaml");

        Assert.Contains("<Expander", xaml);
        Assert.Contains("IsExpanded=\"{x:Bind IsExpanded, Mode=TwoWay}\"", xaml);
        Assert.Contains("AutomationProperties.AutomationId=\"ToolCallPill.RootButton\"", xaml);
        Assert.DoesNotContain("<ToggleButton", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ToggleButtonBackgroundChecked", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MainPage_SearchUsesNativeAutoSuggestEvents()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\MainPage.xaml");
        var code = LoadText(@"SalmonEgg\SalmonEgg\MainPage.xaml.cs");

        Assert.Contains("TextChanged=\"OnSearchTextChanged\"", xaml);
        Assert.Contains("SuggestionChosen=\"OnSearchSuggestionChosen\"", xaml);
        Assert.Contains("QuerySubmitted=\"OnSearchQuerySubmitted\"", xaml);
        Assert.DoesNotContain("Command=\"{x:Bind ActivateCommand}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ItemClick=\"OnSearchSuggestionItemClick\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("OnSearchResultItemClick", code, StringComparison.Ordinal);
        Assert.DoesNotContain("OnSearchHistoryItemClick", code, StringComparison.Ordinal);
    }

    [Fact]
    public void MainPage_SearchDoesNotPatchFocusOrPopupState()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\MainPage.xaml");

        Assert.DoesNotContain("GotFocus=\"OnSearchBoxGotFocus\"", xaml);
        Assert.DoesNotContain("LostFocus=\"OnSearchBoxLostFocus\"", xaml);
        Assert.DoesNotContain("FocusMonitor.IsFocused", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("IsSuggestionListOpen=", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void App_MergesSharedTitleBarIconResources()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\App.xaml");

        Assert.Contains("Styles/TitleBarIcons.xaml", xaml);
    }

    [Fact]
    public void AppResources_DefineNativeSettingsPageLayoutStyles()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\App.xaml");

        Assert.Contains("x:Key=\"SettingsPageTitleTextStyle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"SettingsPageSummaryTextStyle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"SettingsSectionTitleTextStyle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"SettingsRowTitleTextStyle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"SettingsRowDescriptionTextStyle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"SettingsSectionContainerStyle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"SettingsRowGridStyle\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Key=\"SettingsRowControlTemplate\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void UiResources_HaveSameKeysForCanonicalLanguages()
    {
        string[] resourceFiles =
        [
            @"SalmonEgg\SalmonEgg\Strings\en\Resources.resw",
            @"SalmonEgg\SalmonEgg\Strings\en-US\Resources.resw",
            @"SalmonEgg\SalmonEgg\Strings\zh-Hans\Resources.resw"
        ];

        var resourceKeysByFile = resourceFiles.ToDictionary(
            path => path,
            path => XDocument.Parse(LoadText(path))
                .Descendants("data")
                .Select(data => (string?)data.Attribute("name"))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
                .Order(StringComparer.Ordinal)
                .ToArray(),
            StringComparer.Ordinal);
        var allKeys = resourceKeysByFile.Values
            .SelectMany(static keys => keys)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var failures = new List<string>();

        foreach (var (resourceFile, keys) in resourceKeysByFile)
        {
            var missing = allKeys.Except(keys, StringComparer.Ordinal).ToArray();
            if (missing.Length > 0)
            {
                failures.Add($"{resourceFile} missing: {string.Join(", ", missing)}");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Xaml_UserVisibleLiteralAttributesAreLocalizedWithUid()
    {
        var root = FindRepoRoot();
        var xamlFiles = Directory
            .EnumerateFiles(Path.Combine(root, "SalmonEgg", "SalmonEgg"), "*.xaml", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var failures = new List<string>();

        foreach (var xamlFile in xamlFiles)
        {
            var document = XDocument.Parse(File.ReadAllText(xamlFile));
            foreach (var element in document.Descendants())
            {
                foreach (var attribute in element.Attributes().Where(IsUserVisibleTextAttribute))
                {
                    if (!IsHardcodedUserVisibleLiteral(attribute.Value)
                        || HasAttributeByLocalName(element, "Uid")
                        || IsVisibleLiteralWhitelist(xamlFile, element, attribute))
                    {
                        continue;
                    }

                    failures.Add($"{Path.GetRelativePath(root, xamlFile)} <{element.Name.LocalName}> {attribute.Name.LocalName}=\"{attribute.Value}\"");
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void UiCode_DynamicResourceKeysExistInAllCanonicalResources()
    {
        string[] sourceFiles =
        [
            @"SalmonEgg\SalmonEgg\MainPage.xaml.cs",
            @"SalmonEgg\SalmonEgg\Controls\ToolCallPill.xaml.cs",
            @"SalmonEgg\SalmonEgg\Presentation\Converters\TaskOverviewLocalizationConverters.cs"
        ];
        string[] resourceFiles =
        [
            @"SalmonEgg\SalmonEgg\Strings\en\Resources.resw",
            @"SalmonEgg\SalmonEgg\Strings\en-US\Resources.resw",
            @"SalmonEgg\SalmonEgg\Strings\zh-Hans\Resources.resw"
        ];
        var keys = sourceFiles
            .SelectMany(path => Regex.Matches(LoadText(path), @"(?:ResolveResourceString|TaskOverviewResourceLabels\.Get)\(\s*""(?<key>[^""]+)"""))
            .Select(match => match.Groups["key"].Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var failures = new List<string>();

        foreach (var resourceFile in resourceFiles)
        {
            var resourceKeys = XDocument.Parse(LoadText(resourceFile))
                .Descendants("data")
                .Select(data => (string?)data.Attribute("name"))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToHashSet(StringComparer.Ordinal);

            foreach (var key in keys)
            {
                if (!resourceKeys.Contains(key))
                {
                    failures.Add($"{resourceFile} missing dynamic key {key}");
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void MainPage_DynamicResourceLookupSupportsXUidPropertyKeyFallback()
    {
        var code = LoadText(@"SalmonEgg\SalmonEgg\MainPage.xaml.cs");
        var resolveResourceString = ExtractSection(
            code,
            "private static string ResolveResourceString(string resourceKey, string fallback)",
            "private static bool IsChatPageType");

        Assert.Contains("ResourceLoader.GetString(resourceKey)", resolveResourceString, StringComparison.Ordinal);
        Assert.Contains("resourceKey.Replace('.', '/')", resolveResourceString, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsPages_UseSharedResponsiveContentAndFormRows()
    {
        var settingsFiles = Directory.GetFiles(
            Path.Combine(FindRepoRoot(), "SalmonEgg", "SalmonEgg", "Presentation", "Views", "Settings"),
            "*.xaml",
            SearchOption.TopDirectoryOnly);
        var generalSettings = Path.Combine(
            FindRepoRoot(),
            "SalmonEgg",
            "SalmonEgg",
            "Presentation",
            "Views",
            "GeneralSettingsPage.xaml");

        foreach (var file in settingsFiles.Append(generalSettings))
        {
            var xaml = File.ReadAllText(file);
            Assert.Contains("ResponsiveContentHost", xaml, StringComparison.Ordinal);
            Assert.DoesNotContain("ResponsiveSettingsHost", xaml, StringComparison.Ordinal);
            Assert.DoesNotContain("<ColumnDefinition Width=\"220\"", xaml, StringComparison.Ordinal);
        }

        Assert.Contains("ResponsiveFormRow", LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\AboutPage.xaml"), StringComparison.Ordinal);
        Assert.Contains("ResponsiveFormRow", LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\DiagnosticsSettingsPage.xaml"), StringComparison.Ordinal);
        Assert.Contains("ResponsiveFormRow", LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\ShortcutsSettingsPage.xaml"), StringComparison.Ordinal);

        var formRow = LoadXaml(@"SalmonEgg\SalmonEgg\Controls\ResponsiveFormRow.xaml");
        Assert.Contains("x:Class=\"SalmonEgg.Controls.ResponsiveFormRow\"", formRow, StringComparison.Ordinal);
        Assert.Contains("<AdaptiveTrigger MinWindowWidth=\"560\" />", formRow, StringComparison.Ordinal);
        Assert.DoesNotContain("MinActualWidthTrigger", formRow, StringComparison.Ordinal);
        Assert.Contains("<Setter Target=\"ValuePresenter.(Grid.Row)\" Value=\"1\" />", formRow, StringComparison.Ordinal);
        Assert.Contains("<Setter Target=\"LabelColumn.Width\" Value=\"220\" />", formRow, StringComparison.Ordinal);
    }

    [Fact]
    public void ResponsiveContentHost_UsesNativeMaxWidthInsteadOfManualSizeState()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Controls\ResponsiveContentHost.xaml");
        var code = LoadText(@"SalmonEgg\SalmonEgg\Controls\ResponsiveContentHost.xaml.cs");

        Assert.Contains("MaxWidth=\"{x:Bind MaxContentWidth, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SizeChanged=", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ColumnDefinition x:Name=", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ActualWidth", code, StringComparison.Ordinal);
        Assert.DoesNotContain("GridLength", code, StringComparison.Ordinal);
        Assert.DoesNotContain("MinGutter", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatSkeleton_DoesNotOwnStoryboardAnimationState()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Controls\ChatSkeleton.xaml");
        var code = LoadText(@"SalmonEgg\SalmonEgg\Controls\ChatSkeleton.xaml.cs");

        Assert.DoesNotContain("<Storyboard", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("DoubleAnimation", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Loaded +=", code, StringComparison.Ordinal);
        Assert.DoesNotContain(".Begin()", code, StringComparison.Ordinal);
        Assert.DoesNotContain(".Stop()", code, StringComparison.Ordinal);
    }

    [Fact]
    public void TitleBarButtons_UseSharedIconTemplates()
    {
        var mainPageXaml = LoadXaml(@"SalmonEgg\SalmonEgg\MainPage.xaml");
        var miniChatXaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\MiniWindow\MiniChatView.xaml");
        var titleBarIconsDocument = XDocument.Parse(LoadXaml(@"SalmonEgg\SalmonEgg\Styles\TitleBarIcons.xaml"));
        var titleBarButtonStylesDocument = XDocument.Parse(LoadXaml(@"SalmonEgg\SalmonEgg\Styles\TitleBarCommandButtonStyle.xaml"));
        var xNamespace = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");

        Assert.Contains("ContentTemplate=\"{StaticResource TitleBarBackIconTemplate}\"", mainPageXaml);
        Assert.Contains("ContentTemplate=\"{StaticResource TitleBarToggleLeftNavIconTemplate}\"", mainPageXaml);
        Assert.Contains("ContentTemplate=\"{StaticResource TitleBarOpenMiniWindowIconTemplate}\"", mainPageXaml);
        Assert.DoesNotContain("Glyph=\"&#xE72B;\"", mainPageXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Glyph=\"&#xE700;\"", mainPageXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Glyph=\"&#xEE49;\"", mainPageXaml, StringComparison.Ordinal);
        Assert.Contains("ContentTemplate=\"{StaticResource TitleBarReturnToMainWindowIconTemplate}\"", miniChatXaml);
        Assert.DoesNotContain("Glyph=\"&#xE73F;\"", miniChatXaml, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource MiniTitleBarAccessoryButtonStyle}\"", miniChatXaml);

        var returnIconTemplate = titleBarIconsDocument
            .Descendants()
            .FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "DataTemplate", StringComparison.Ordinal)
                && string.Equals(element.Attribute(xNamespace + "Key")?.Value, "TitleBarReturnToMainWindowIconTemplate", StringComparison.Ordinal));

        Assert.NotNull(returnIconTemplate);

        var returnIconPath = returnIconTemplate!
            .Descendants()
            .FirstOrDefault(element => string.Equals(element.Name.LocalName, "Path", StringComparison.Ordinal));

        Assert.NotNull(returnIconPath);
        Assert.Equal("16", returnIconPath!.Attribute("Width")?.Value);
        Assert.Equal("16", returnIconPath.Attribute("Height")?.Value);

        var miniAccessoryStyle = titleBarButtonStylesDocument
            .Descendants()
            .FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "Style", StringComparison.Ordinal)
                && string.Equals(element.Attribute(xNamespace + "Key")?.Value, "MiniTitleBarAccessoryButtonStyle", StringComparison.Ordinal));

        Assert.NotNull(miniAccessoryStyle);
        Assert.Contains(miniAccessoryStyle!.Descendants().Where(element => string.Equals(element.Name.LocalName, "Setter", StringComparison.Ordinal)),
            setter => string.Equals(setter.Attribute("Property")?.Value, "Width", StringComparison.Ordinal)
                && string.Equals(setter.Attribute("Value")?.Value, "40", StringComparison.Ordinal));
        Assert.Contains(miniAccessoryStyle.Descendants().Where(element => string.Equals(element.Name.LocalName, "Setter", StringComparison.Ordinal)),
            setter => string.Equals(setter.Attribute("Property")?.Value, "Height", StringComparison.Ordinal)
                && string.Equals(setter.Attribute("Value")?.Value, "40", StringComparison.Ordinal));
        Assert.DoesNotContain(miniAccessoryStyle.Descendants(), element => string.Equals(element.Name.LocalName, "Viewbox", StringComparison.Ordinal));
    }

    [Fact]
    public void TitleBarButtonStyles_DoNotReplaceNativeControlTemplates()
    {
        string[] styleFiles =
        [
            @"SalmonEgg\SalmonEgg\Styles\TitleBarCommandButtonStyle.xaml",
            @"SalmonEgg\SalmonEgg\Styles\TitleBarToggleButtonStyle.xaml"
        ];

        foreach (var styleFile in styleFiles)
        {
            var xaml = LoadXaml(styleFile);

            Assert.DoesNotContain("<ControlTemplate", xaml, StringComparison.Ordinal);
            Assert.DoesNotContain("VisualStateGroup", xaml, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TitleBarButtonStyles_KeepNativeRoundedBorderlessAppearance()
    {
        var commandStyles = XDocument.Parse(LoadXaml(@"SalmonEgg\SalmonEgg\Styles\TitleBarCommandButtonStyle.xaml"));
        var toggleStyles = XDocument.Parse(LoadXaml(@"SalmonEgg\SalmonEgg\Styles\TitleBarToggleButtonStyle.xaml"));

        AssertTitleBarStyleKeepsNativeRoundedBorderlessAppearance(commandStyles, "TitleBarCommandButtonStyle", "8");
        AssertTitleBarStyleKeepsNativeRoundedBorderlessAppearance(commandStyles, "MiniTitleBarAccessoryButtonStyle", "4");
        AssertTitleBarStyleKeepsNativeRoundedBorderlessAppearance(toggleStyles, "TitleBarToggleButtonStyle", "8");
    }

    [Fact]
    public void MiniChatView_RootSurfaceKeepsWindowBackdropVisible()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\MiniWindow\MiniChatView.xaml");

        Assert.Contains("x:Name=\"RootGrid\"", xaml);
        Assert.Contains("Background=\"Transparent\"", xaml);
        Assert.DoesNotContain("Background=\"{ThemeResource ApplicationPageBackgroundThemeBrush}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MainPage_SearchStringsAreLocalized()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\MainPage.xaml");

        Assert.DoesNotContain("PlaceholderText=\"搜索\"", xaml);
        Assert.DoesNotContain("AutomationProperties.Name=\"搜索\"", xaml);
        Assert.Contains("x:Uid=\"TopSearchBox\"", xaml);
    }

    [Fact]
    public void MainPage_MenuAndPlanStringsAreLocalized()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\MainPage.xaml");

        Assert.DoesNotContain("Text=\"新建会话\"", xaml);
        Assert.DoesNotContain("Text=\"归档…\"", xaml);
        Assert.DoesNotContain("Text=\"重命名…\"", xaml);
        Assert.DoesNotContain("Text=\"Diff 面板占位\"", xaml);
        Assert.DoesNotContain("Text=\"暂无计划\"", xaml);
        Assert.DoesNotContain("Text=\"等待 Agent 更新\"", xaml);
        Assert.Contains("x:Uid=\"ProjectNavNewSessionItem\"", xaml);
        Assert.Contains("x:Uid=\"SessionNavArchiveItem\"", xaml);
        Assert.Contains("x:Uid=\"SessionNavMoveItem\"", xaml);
        Assert.DoesNotContain("x:Uid=\"SessionNavRenameItem\"", xaml);
        Assert.DoesNotContain("x:Uid=\"DiffPanelPlaceholder\"", xaml);
        Assert.DoesNotContain("x:Uid=\"PlanEmptyTitle\"", xaml);
        Assert.DoesNotContain("x:Uid=\"PlanEmptySubtitle\"", xaml);
        Assert.Contains("x:Uid=\"TaskOverviewEmptyTitle\"", xaml);
        Assert.Contains("x:Uid=\"TaskOverviewEmptySubtitle\"", xaml);
    }

    [Fact]
    public void MainPage_RightPanelExposesAutomationAnchorsForSmoke()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\MainPage.xaml");

        Assert.Contains("x:Name=\"RightPanelSplitView\"", xaml);
        Assert.Contains("PanePlacement=\"Right\"", xaml);
        Assert.Contains("DisplayMode=\"Inline\"", xaml);
        Assert.DoesNotContain("CompactPaneLength=\"0\"", xaml);
        Assert.Contains("IsPaneOpen=\"{x:Bind LayoutVM.RightPanelVisible, Mode=OneWay}\"", xaml);
        Assert.Contains("OpenPaneLength=\"{x:Bind LayoutVM.RightPanelOpenPaneLength, Mode=OneWay}\"", xaml);
        Assert.DoesNotContain("OpenPaneLength=\"{x:Bind LayoutVM.RightPanelWidth", xaml);
        Assert.Contains("x:Name=\"RightPanelPane\"", xaml);
        Assert.Contains("AutomationProperties.AutomationId=\"RightPanel.Root\"", xaml);
        Assert.Contains("x:Name=\"RightPanelTitle\"", xaml);
        Assert.Contains("AutomationProperties.AutomationId=\"RightPanel.Title\"", xaml);
        Assert.Contains("AutomationProperties.AutomationId=\"RightPanel.TaskOverviewRoot\"", xaml);
        Assert.Contains("AutomationProperties.AutomationId=\"RightPanel.TaskOverviewSummary\"", xaml);
        Assert.Contains("AutomationProperties.AutomationId=\"RightPanel.TaskOverview.CurrentPlan\"", xaml);
        Assert.Contains("AutomationProperties.AutomationId=\"RightPanel.TaskOverview.PlanList\"", xaml);
        Assert.Contains("AutomationProperties.AutomationId=\"RightPanel.TaskOverview.EmptyTitle\"", xaml);
        Assert.Contains("AutomationProperties.AutomationId=\"RightPanel.TaskOverview.ChangesList\"", xaml);
        Assert.DoesNotContain("AutomationProperties.Name=\"\"", xaml);
        Assert.DoesNotContain("AutomationProperties.AutomationId=\"RightPanel.TodoEmptyTitle\"", xaml);
        Assert.DoesNotContain("x:Name=\"RightPanelColumn\"", xaml);
        Assert.DoesNotContain("RightPanelColumnDefinition", xaml);
    }

    [Fact]
    public void MainPage_TaskOverviewRowsUseLocalizedDynamicBindings()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\MainPage.xaml");

        Assert.Contains("Text=\"{x:Bind GetTaskOverviewSummaryText(ChatVM.TaskOverviewState), Mode=OneWay}\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"{x:Bind GetTaskOverviewSummaryAutomationName(ChatVM.TaskOverviewState), Mode=OneWay}\"", xaml);
        Assert.Contains("TaskOverviewCurrentPlanLabel", xaml);
        Assert.Contains("Text=\"{x:Bind ChatVM.TaskOverviewCurrentPlanContent, Mode=OneWay}\"", xaml);
        Assert.Contains("ItemsSource=\"{x:Bind ChatVM.TaskOverviewVisiblePlanEntries, Mode=OneWay}\"", xaml);
        Assert.Contains("ItemsSource=\"{x:Bind ChatVM.TaskOverviewVisibleChanges, Mode=OneWay}\"", xaml);
        Assert.Contains("Text=\"{x:Bind GetTaskOverviewMorePlanText(ChatVM.TaskOverviewHiddenPlanCount), Mode=OneWay}\"", xaml);
        Assert.Contains("Text=\"{x:Bind GetTaskOverviewMoreChangesText(ChatVM.TaskOverviewHiddenChangeCount), Mode=OneWay}\"", xaml);
        Assert.Contains("Fill=\"{x:Bind Status, Mode=OneWay, Converter={StaticResource PlanStatusToColorConverter}}\"", xaml);
        Assert.Contains("Text=\"{x:Bind Status, Mode=OneWay, Converter={StaticResource PlanStatusLabelConverter}}\"", xaml);
        Assert.Contains("Text=\"{x:Bind Priority, Mode=OneWay, Converter={StaticResource PlanPriorityLabelConverter}}\"", xaml);
        Assert.Contains("Text=\"{x:Bind FileName, Mode=OneWay}\"", xaml);
        Assert.Contains("Text=\"{x:Bind DirectoryPath, Mode=OneWay}\"", xaml);
        Assert.Contains("Text=\"{x:Bind Kind, Mode=OneWay, Converter={StaticResource TaskOverviewChangeKindLabelConverter}}\"", xaml);
        Assert.DoesNotContain("Text=\"{x:Bind StatusDisplayName}\"", xaml);
        Assert.DoesNotContain("Text=\"{x:Bind PriorityDisplayName}\"", xaml);
        Assert.DoesNotContain("Text=\"{x:Bind KindDisplayName}\"", xaml);
        Assert.DoesNotContain("Text=\"{x:Bind Path}\"", xaml);
    }

    [Fact]
    public void ChatInputArea_IconButtonsAccessibleAndTouchSized()
    {
        var sendButton = FindElementByName(@"SalmonEgg\SalmonEgg\Controls\ChatInputArea.xaml", "SendButton");
        var cancelButton = FindElementByName(@"SalmonEgg\SalmonEgg\Controls\ChatInputArea.xaml", "CancelButton");

        Assert.True(
            HasAttributeByLocalName(sendButton, "AutomationProperties.Name") || HasXUid(sendButton, "SendButton"),
            "SendButton must expose an accessible name via AutomationProperties.Name or x:Uid localization.");
        Assert.True(
            HasAttributeByLocalName(cancelButton, "AutomationProperties.Name") || HasXUid(cancelButton, "CancelButton"),
            "CancelButton must expose an accessible name via AutomationProperties.Name or x:Uid localization.");
        Assert.Equal("44", GetAttributeByLocalName(sendButton, "MinWidth"));
        Assert.Equal("44", GetAttributeByLocalName(sendButton, "MinHeight"));
        Assert.Equal("44", GetAttributeByLocalName(cancelButton, "MinWidth"));
        Assert.Equal("44", GetAttributeByLocalName(cancelButton, "MinHeight"));
    }

    [Fact]
    public void ChatInputArea_SendButtonUsesCommandBinding()
    {
        var sendButton = FindElementByName(@"SalmonEgg\SalmonEgg\Controls\ChatInputArea.xaml", "SendButton");
        var commandBinding = GetAttributeByLocalName(sendButton, "Command");
        var clickBinding = GetAttributeByLocalName(sendButton, "Click");

        Assert.NotEqual("OnSendClick", clickBinding);
        Assert.StartsWith("{x:Bind SubmitCommand", commandBinding, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatInputArea_AvoidsFixedModeWidth()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Controls\ChatInputArea.xaml");

        Assert.DoesNotContain("Width=\"140\"", xaml);
    }

    [Fact]
    public void ChatInputArea_UsesContainerWidthForResponsiveToolLayout()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Controls\ChatInputArea.xaml");

        Assert.Contains("x:Name=\"ComposerLayoutRoot\"", xaml);
        Assert.Contains("x:Name=\"BottomToolsStrip\"", xaml);
        Assert.Contains("x:Name=\"ToolSelectorsPanel\"", xaml);
        Assert.Contains("x:Name=\"ActionButtonsPanel\"", xaml);
        Assert.Contains("<AdaptiveTrigger MinWindowWidth=\"640\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Target=\"ToolSelectorsPanel.Orientation\" Value=\"Vertical\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Target=\"ActionButtonsPanel.(Grid.Row)\" Value=\"1\" />", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MinActualWidthTrigger", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatInputArea_TextsAreLocalized()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Controls\ChatInputArea.xaml");

        Assert.DoesNotContain("PlaceholderText=\"向 Agent 发送消息", xaml);
        Assert.DoesNotContain("PlaceholderText=\"选择模式\"", xaml);
        Assert.DoesNotContain("Content=\"娶她\"", xaml);
        Assert.DoesNotContain("ToolTipService.ToolTip=\"“娶她”功能占位\"", xaml);
        Assert.DoesNotContain("AutomationProperties.Name=\"发送\"", xaml);
        Assert.DoesNotContain("AutomationProperties.Name=\"取消发送\"", xaml);
        Assert.Contains("x:Uid=\"ChatInputBox\"", xaml);
        Assert.Contains("x:Uid=\"ChatModeSelector\"", xaml);
        Assert.Contains("x:Uid=\"SendButton\"", xaml);
        Assert.Contains("x:Uid=\"CancelButton\"", xaml);
    }

    [Fact]
    public void ChatInputArea_ExposesAgentAndProjectSlotsAsCapabilities()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Controls\ChatInputArea.xaml");
        var code = LoadText(@"SalmonEgg\SalmonEgg\Controls\ChatInputArea.xaml.cs");

        Assert.Contains("x:Name=\"AgentSelectorHost\"", xaml);
        Assert.Contains("Visibility=\"{x:Bind SelectorSlots.Agent.IsVisible, Mode=OneWay, Converter={StaticResource BoolToVisibilityConverter}}\"", xaml);
        Assert.Contains("AutomationProperties.AutomationId=\"{x:Bind AgentSelectorAutomationId, Mode=OneWay}\"", xaml);
        Assert.Contains("ItemsSource=\"{x:Bind SelectorSlots.Agent.Items, Mode=OneWay}\"", xaml);
        Assert.Contains("SelectedItem=\"{x:Bind SelectorSlots.Agent.SelectedItem, Mode=OneWay}\"", xaml);
        Assert.Contains("x:Name=\"ProjectSelectorHost\"", xaml);
        Assert.Contains("Visibility=\"{x:Bind SelectorSlots.Project.IsVisible, Mode=OneWay, Converter={StaticResource BoolToVisibilityConverter}}\"", xaml);
        Assert.Contains("AutomationProperties.AutomationId=\"{x:Bind ProjectSelectorAutomationId, Mode=OneWay}\"", xaml);
        Assert.Contains("ItemsSource=\"{x:Bind SelectorSlots.Project.Items, Mode=OneWay}\"", xaml);
        Assert.Contains("SelectedItem=\"{x:Bind SelectorSlots.Project.SelectedItem, Mode=OneWay}\"", xaml);
        Assert.Contains("Visibility=\"{x:Bind SelectorSlots.Mode.IsVisible, Mode=OneWay, Converter={StaticResource BoolToVisibilityConverter}}\"", xaml);
        Assert.DoesNotContain("ShowAgentSelector", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowModeSelector", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowProjectSelector", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatInputArea_CodeBehind_TreatsDeferredSelectorsAsOptional()
    {
        var code = LoadText(@"SalmonEgg\SalmonEgg\Controls\ChatInputArea.xaml.cs");

        Assert.Contains("FindName(selectorName) as ComboBox", code, StringComparison.Ordinal);
        Assert.DoesNotContain("AgentSelectorHost.XamlRoot", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ModeSelectorHost.XamlRoot", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectSelectorHost.XamlRoot", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatInputArea_ComposerBlockedStates_UseUnifiedViewModelProjection()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Controls\ChatInputArea.xaml");

        Assert.Contains("Visibility=\"{x:Bind ViewModel.ShouldShowSlashCommandsUi, Mode=OneWay", xaml);
        Assert.Contains("IsEnabled=\"{x:Bind ViewModel.IsTextInputEnabled, Mode=OneWay}\"", xaml);
        Assert.Contains("IsEnabled=\"{x:Bind ViewModel.AreComposerToolsEnabled, Mode=OneWay}\"", xaml);
        Assert.Contains("IsEnabled=\"{x:Bind IsSubmitButtonEnabled, Mode=OneWay}\"", xaml);
        Assert.Contains("Visibility=\"{x:Bind ViewModel.VoiceInputUiState.ShowStartButton, Mode=OneWay", xaml);
        Assert.Contains("Visibility=\"{x:Bind ViewModel.VoiceInputUiState.ShowStopButton, Mode=OneWay", xaml);
        Assert.Contains("Visibility=\"{x:Bind ViewModel.VoiceInputUiState.ShowProgressRing, Mode=OneWay", xaml);
        Assert.Contains("IsEnabled=\"{x:Bind ViewModel.CanCancelPromptUi, Mode=OneWay}\"", xaml);
        Assert.Contains("Visibility=\"{x:Bind ViewModel.ShowCancelPromptButton, Mode=OneWay", xaml);
        Assert.DoesNotContain("ViewModel.IsVoiceInputListening", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("CanSubmitUi", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatInputArea_BlockedStatusCopy_UsesLocalizedUids()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Controls\ChatInputArea.xaml");

        Assert.DoesNotContain("x:Uid=\"ChatComposerPromptInFlightStatus\"", xaml);
        Assert.DoesNotContain("x:Uid=\"ChatComposerVoiceListeningStatus\"", xaml);
    }

    [Theory]
    [InlineData(@"SalmonEgg\SalmonEgg\Presentation\Views\Chat\ChatView.xaml")]
    [InlineData(@"SalmonEgg\SalmonEgg\Presentation\Views\Start\StartView.xaml")]
    public void OverlayScrim_UsesThemeBrush(string relativePath)
    {
        var xaml = LoadXaml(relativePath);

        Assert.DoesNotContain("Background=\"#40000000\"", xaml);
    }

    [Fact]
    public void AppTheme_DoesNotUseHardcodedTintColors()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\App.xaml");

        Assert.DoesNotContain("TintColor=\"#", xaml);
        Assert.DoesNotContain("FallbackColor=\"#", xaml);
    }

    [Theory]
    [InlineData(@"SalmonEgg\SalmonEgg\Presentation\Converters\PlanStatusToColorConverter.cs")]
    [InlineData(@"SalmonEgg\SalmonEgg\Presentation\Converters\ConnectionStatusToColorConverter.cs")]
    [InlineData(@"SalmonEgg\SalmonEgg\Presentation\Converters\ResourceTypeIconConverter.cs")]
    public void SemanticColorConverters_UseThemeResources(string relativePath)
    {
        var source = LoadText(relativePath);

        Assert.Contains("ThemeBrushConverter.Resolve", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new SolidColorBrush", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ColorHelper.FromArgb", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.UI.Colors", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AgentProfileEditor_DoesNotUseValueChangedHandlers()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\AgentProfileEditorPage.xaml");

        Assert.DoesNotContain("ValueChanged=\"OnTimeoutValueChanged\"", xaml);
    }

    [Fact]
    public void ChatInputArea_DoesNotUseHardcodedWhiteForeground()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Controls\ChatInputArea.xaml");

        Assert.DoesNotContain("Foreground=\"White\"", xaml);
    }

    [Fact]
    public void ChatInputArea_DoesNotHijackGeneralFocusFlowForGamepadEntry()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Controls\ChatInputArea.xaml");
        var code = LoadText(@"SalmonEgg\SalmonEgg\Controls\ChatInputArea.xaml.cs");

        Assert.Contains("XYFocusKeyboardNavigation=\"Enabled\"", xaml);
        Assert.Contains("x:Name=\"SlashCommandsList\"", xaml);
        Assert.DoesNotContain("FocusEngaged=\"OnInputAreaFocusEngaged\"", xaml);
        Assert.DoesNotContain("FocusDisengaged=\"OnInputAreaFocusDisengaged\"", xaml);
        Assert.DoesNotContain("private void OnInputAreaFocusEngaged(", code);
        Assert.DoesNotContain("private void OnInputAreaFocusDisengaged(", code);
    }

    [Fact]
    public void ChatStyles_DoNotUseHardcodedWhiteForeground()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Styles\ChatStyles.xaml");

        Assert.DoesNotContain("Foreground=\"White\"", xaml);
        Assert.Contains("TextFillColorPrimaryBrush", xaml);
    }

    [Fact]
    public void AppXaml_DoesNotDeclareASecondUiMotionControllerInstance()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\App.xaml");

        Assert.DoesNotContain("<models:UiMotionController x:Key=", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void AppMotionPreference_DoesNotOverrideNativeControlTemplateMotion()
    {
        var appCode = LoadText(@"SalmonEgg\SalmonEgg\App.xaml.cs");
        var uiRuntimeCode = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Services\UiRuntimeService.cs");
        var motionCode = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Models\UiMotionController.cs");
        var repoRoot = FindRepoRoot();
        var reducedMotionDictionary = Path.Combine(
            repoRoot,
            "SalmonEgg",
            "SalmonEgg",
            "Styles",
            "ReducedMotion.xaml");

        Assert.False(
            File.Exists(reducedMotionDictionary),
            "Application motion settings must not override native WinUI control-template animation resources.");
        Assert.DoesNotContain("ReducedMotionDictionary", appCode, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyReducedMotion", appCode, StringComparison.Ordinal);
        Assert.DoesNotContain("FeatureConfiguration.ThemeAnimation.DefaultThemeAnimationDuration", appCode, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyReducedMotion", uiRuntimeCode, StringComparison.Ordinal);
        Assert.Contains("IsSystemAnimationEnabled", motionCode, StringComparison.Ordinal);
        Assert.Contains("IsEffectiveAnimationEnabled", motionCode, StringComparison.Ordinal);
        Assert.Contains("Timeline.AllowDependentAnimations", uiRuntimeCode, StringComparison.Ordinal);
        Assert.Contains("UISettings", uiRuntimeCode, StringComparison.Ordinal);
        Assert.Contains("AnimationsEnabledChanged", uiRuntimeCode, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductCSharp_DoesNotOverrideNativeControlTemplateMotion()
    {
        var forbiddenTokens = new[]
        {
            "ReducedMotionDictionary",
            "ApplyReducedMotion",
            "FeatureConfiguration.ThemeAnimation.DefaultThemeAnimationDuration",
            "ThemeAnimation.DefaultThemeAnimationDuration",
            "UISettingsController",
            "ControlNormalAnimationDuration",
            "ControlFastAnimationDuration",
            "ControlFastAnimationAfterDuration",
            "ControlFasterAnimationDuration",
            "ComboBoxItemScaleAnimationDuration",
            "ScrollBarColorChangeDuration",
            "ScrollBarContractDuration",
            "ScrollBarExpandDuration",
            "ScrollBarOpacityChangeDuration",
            "ScrollViewerSeparatorContractDuration",
            "ScrollViewerSeparatorExpandDuration",
            "ScrollViewScrollBarsNoTouchDuration",
            "ScrollViewScrollBarsSeparatorContractDuration",
            "ScrollViewScrollBarsSeparatorExpandDuration",
            "SplitViewPaneAnimationCloseDuration",
            "SplitViewPaneAnimationOpenDuration",
            "SplitViewPaneAnimationOpenPreDuration"
        };

        var violations = EnumerateProductCSharpFiles()
            .SelectMany(file =>
            {
                var content = File.ReadAllText(file);
                return forbiddenTokens
                    .Where(token => content.Contains(token, StringComparison.Ordinal))
                    .Select(token => $"{Path.GetRelativePath(FindRepoRoot(), file)}: {token}");
            })
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Product C# must not override native WinUI/Uno control-template motion; bind only application-owned transitions."
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void DirectoryBuildProps_DoesNotSuppressUno0001()
    {
        var props = LoadText(@"SalmonEgg\Directory.Build.props");

        Assert.DoesNotContain("UNO0001", props);
    }

    [Fact]
    public void ChatView_DoesNotUseListViewItemContainerTransitions()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Chat\ChatView.xaml");

        Assert.DoesNotContain("ItemContainerTransitions=", xaml);
    }

    [Fact]
    public void StartView_ItemsPanelTemplate_DoesNotUseXBind()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Start\StartView.xaml");

        Assert.DoesNotContain("ChildrenTransitions=\"{x:Bind", xaml);
    }

    [Fact]
    public void StartView_ComposerPreservesDirectLayoutWithoutSyntheticFocusHost()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Start\StartView.xaml");
        var code = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Views\Start\StartView.xaml.cs");

        Assert.Contains("<controls:ChatInputArea x:Name=\"ComposerShell\"", xaml);
        Assert.Contains("Grid.Row=\"1\"", xaml);
        Assert.DoesNotContain("x:Name=\"ComposerFocusHost\"", xaml);
        Assert.DoesNotContain("FocusEngaged=\"OnComposerFocusHostFocusEngaged\"", xaml);
        Assert.DoesNotContain("private void OnComposerFocusHostFocusEngaged(", code);
    }

    [Fact]
    public void StartView_DraftErrorInfoBarUsesNativeLayoutFlow()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Start\StartView.xaml");

        Assert.Contains("<InfoBar Grid.Row=\"0\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsOpen=\"{x:Bind ViewModel.HasStartSessionDraftError, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Message=\"{x:Bind ViewModel.StartSessionDraftErrorMessage, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Margin=\"24,0,24,112\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("VerticalAlignment=\"Bottom\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void StartView_ComposerUsesSharedChatInputAreaWithoutPrivateInputControls()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Start\StartView.xaml");

        Assert.Contains("<controls:ChatInputArea x:Name=\"ComposerShell\"", xaml);
        Assert.Contains("IsEnabled=\"{x:Bind ViewModel.IsInputEnabled, Mode=OneWay}\"", xaml);
        Assert.Contains("SelectorSlots=\"{x:Bind ViewModel.ComposerSelectorSlots, Mode=OneWay}\"", xaml);
        Assert.Contains("AgentSelectorAutomationId=\"StartView.AgentSelector\"", xaml);
        Assert.Contains("ModeSelectorAutomationId=\"StartView.ModeSelector\"", xaml);
        Assert.Contains("ProjectSelectorAutomationId=\"StartView.ProjectSelector\"", xaml);
        Assert.DoesNotContain("IsComposerExpanded", xaml);
        Assert.DoesNotContain("OnComposerInteractiveElementGotFocus", xaml);
        Assert.DoesNotContain("OnComposerSelectorDropDownOpened", xaml);
        Assert.DoesNotContain("x:Name=\"StartPromptBox\"", xaml);
        Assert.DoesNotContain("x:Name=\"StartAgentSelector\"", xaml);
        Assert.DoesNotContain("x:Name=\"StartModeSelector\"", xaml);
        Assert.DoesNotContain("x:Name=\"StartProjectSelector\"", xaml);
        Assert.DoesNotContain("AgentSelectorItemsSource=", xaml);
        Assert.DoesNotContain("ModeSelectorItemsSource=", xaml);
        Assert.DoesNotContain("ProjectSelectorItemsSource=", xaml);
    }

    [Fact]
    public void ChatView_UsesSharedInputAreaWithoutAgentSelectorCapability()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Chat\ChatView.xaml");

        Assert.Contains("<controls:ChatInputArea x:Name=\"ConversationInputArea\"", xaml);
        Assert.Contains("ViewModel=\"{x:Bind ViewModel, Mode=OneWay}\"", xaml);
        Assert.Contains("SelectorSlots=\"{x:Bind ViewModel.ComposerSelectorSlots, Mode=OneWay}\"", xaml);
        Assert.DoesNotContain("ShowAgentSelector=", xaml);
        Assert.DoesNotContain("ShowProjectSelector=", xaml);
        Assert.DoesNotContain("ModeSelectorItemsSource=", xaml);
        Assert.DoesNotContain("SelectedModeSelectorItem=", xaml);
        Assert.DoesNotContain("AgentSelectorAutomationId=", xaml);
        Assert.DoesNotContain("ProjectSelectorAutomationId=", xaml);
    }

    [Fact]
    public void SharedComposer_ModeSelectionUsesExplicitCommandInsteadOfTwoWaySelectedMode()
    {
        var chatInputXaml = LoadXaml(@"SalmonEgg\SalmonEgg\Controls\ChatInputArea.xaml");
        var chatViewXaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Chat\ChatView.xaml");
        var startViewXaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Start\StartView.xaml");

        Assert.Contains("ItemsSource=\"{x:Bind SelectorSlots.Mode.Items, Mode=OneWay}\"", chatInputXaml);
        Assert.Contains("SelectedItem=\"{x:Bind SelectorSlots.Mode.SelectedItem, Mode=OneWay}\"", chatInputXaml);
        Assert.Contains("SelectionChanged=\"OnModeSelectorSelectionChanged\"", chatInputXaml);
        Assert.DoesNotContain("SelectedItem=\"{x:Bind SelectedMode, Mode=TwoWay}\"", chatInputXaml, StringComparison.Ordinal);

        Assert.Contains("SelectorSlots=\"{x:Bind ViewModel.ComposerSelectorSlots, Mode=OneWay}\"", chatViewXaml);
        Assert.DoesNotContain("SelectedMode=\"{x:Bind ViewModel.SelectedMode, Mode=TwoWay}\"", chatViewXaml, StringComparison.Ordinal);

        Assert.Contains("SelectorSlots=\"{x:Bind ViewModel.ComposerSelectorSlots, Mode=OneWay}\"", startViewXaml);
        Assert.DoesNotContain("SelectedMode=\"{x:Bind ViewModel.SelectedStartMode, Mode=TwoWay}\"", startViewXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatStyles_XBindTemplate_UsesCompiledResourceDictionary()
    {
        var appXaml = LoadXaml(@"SalmonEgg\SalmonEgg\App.xaml");
        var chatStylesXaml = LoadXaml(@"SalmonEgg\SalmonEgg\Styles\ChatStyles.xaml");

        Assert.DoesNotContain("Source=\"ms-appx:///Styles/ChatStyles.xaml\"", appXaml);
        Assert.Contains("x:Class=\"SalmonEgg.Styles.ChatStyles\"", chatStylesXaml);
    }

    [Fact]
    public void ConversationProjectPickerDialog_Uids_DoNotShadowRootUidPropertyPaths()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Navigation\ConversationProjectPickerDialog.xaml");
        var zhHans = LoadText(@"SalmonEgg\SalmonEgg\Strings\zh-Hans\Resources.resw");

        Assert.Contains("x:Uid=\"SessionProjectPickerDialog\"", xaml);
        Assert.DoesNotContain("x:Uid=\"SessionProjectPickerDialog.SessionLabel\"", xaml);
        Assert.DoesNotContain("x:Uid=\"SessionProjectPickerDialog.InstructionText\"", xaml);
        Assert.DoesNotContain("x:Uid=\"SessionProjectPickerDialog.OptionsList\"", xaml);
        Assert.Contains("x:Uid=\"SessionProjectPickerDialogSessionLabel\"", xaml);
        Assert.Contains("x:Uid=\"SessionProjectPickerDialogInstructionText\"", xaml);
        Assert.Contains("x:Uid=\"SessionProjectPickerDialogOptionsList\"", xaml);

        Assert.DoesNotContain("SessionProjectPickerDialog.SessionLabel.Text", zhHans);
        Assert.DoesNotContain("SessionProjectPickerDialog.InstructionText.Text", zhHans);
        Assert.DoesNotContain("SessionProjectPickerDialog.OptionsList.[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name", zhHans);
        Assert.Contains("SessionProjectPickerDialogSessionLabel.Text", zhHans);
        Assert.Contains("SessionProjectPickerDialogInstructionText.Text", zhHans);
        Assert.Contains("SessionProjectPickerDialogOptionsList.[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name", zhHans);
    }

    [Fact]
    public void ConversationProjectPickerDialog_UsesDefaultDialogStyleAndNativeListSelection()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Navigation\ConversationProjectPickerDialog.xaml");

        Assert.Contains("Style=\"{ThemeResource DefaultContentDialogStyle}\"", xaml);
        Assert.Contains("<ListView x:Name=\"OptionsList\"", xaml);
        Assert.Contains("SelectedItem=\"{x:Bind SelectedOption, Mode=TwoWay}\"", xaml);
        Assert.DoesNotContain("<RadioButtons", xaml);
        Assert.DoesNotContain("<SymbolIcon Symbol=\"Folder\"", xaml);
    }

    [Fact]
    public void Dialogs_DoNotForceDesktopMinimumWidth()
    {
        var sessionsDialog = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Navigation\SessionsListDialog.xaml");
        var projectDialog = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Navigation\ConversationProjectPickerDialog.xaml");
        var configurationDialog = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\ConfigurationEditorDialog.xaml");

        Assert.DoesNotContain("MinWidth=\"420\"", sessionsDialog, StringComparison.Ordinal);
        Assert.DoesNotContain("MinWidth=\"400\"", projectDialog, StringComparison.Ordinal);
        Assert.DoesNotContain("MinWidth=\"400\"", configurationDialog, StringComparison.Ordinal);
        Assert.Contains("MaxWidth=\"560\"", sessionsDialog, StringComparison.Ordinal);
        Assert.Contains("MaxWidth=\"560\"", projectDialog, StringComparison.Ordinal);
        Assert.Contains("MaxWidth=\"560\"", configurationDialog, StringComparison.Ordinal);
    }

    [Fact]
    public void ConversationProjectPickerDialog_PreservesSelectionFallbackAndPrimaryButtonGuard()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Navigation\ConversationProjectPickerDialog.xaml");
        var code = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Views\Navigation\ConversationProjectPickerDialog.xaml.cs");

        Assert.Contains("IsPrimaryButtonEnabled=\"{x:Bind HasSelection, Mode=OneWay}\"", xaml);
        Assert.Contains("SelectedOption = ChooseDefaultOption(selectedProjectId);", code);
        Assert.Contains("return _options.Count > 0 ? _options[0] : null;", code);
        Assert.Contains("OnPropertyChanged(nameof(HasSelection));", code);
    }

    [Fact]
    public void ConversationProjectPickerDialog_ButtonsAreResourceDriven()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Navigation\ConversationProjectPickerDialog.xaml");

        Assert.DoesNotContain("PrimaryButtonText=\"Move\"", xaml);
        Assert.DoesNotContain("CloseButtonText=\"Cancel\"", xaml);
        Assert.Contains("x:Uid=\"SessionProjectPickerDialog\"", xaml);
    }

    [Fact]
    public void ChatView_AskUserAndLoadingOverlayTextsAreLocalized()
    {
        var chatXaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Chat\ChatView.xaml");
        var shellXaml = LoadXaml(@"SalmonEgg\SalmonEgg\MainPage.xaml");

        Assert.DoesNotContain("Text=\"Agent 需要你的输入\"", chatXaml);
        Assert.DoesNotContain("Content=\"提交答案\"", chatXaml);
        Assert.DoesNotContain("AutomationProperties.Name=\"会话加载中\"", shellXaml);
        Assert.Contains("x:Uid=\"ChatViewAskUserTitle\"", chatXaml);
        Assert.Contains("x:Uid=\"ChatViewAskUserSubmitButton\"", chatXaml);
        Assert.Contains("x:Uid=\"ChatViewLoadingOverlay\"", shellXaml);
    }

    [Fact]
    public void SessionsListDialog_TextsAreLocalized()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Navigation\SessionsListDialog.xaml");

        Assert.DoesNotContain("PlaceholderText=\"搜索会话\"", xaml);
        Assert.Contains("x:Uid=\"SessionsDialog\"", xaml);
        Assert.Contains("x:Uid=\"SessionsDialogSearchBox\"", xaml);
    }

    [Fact]
    public void ConfigurationEditorDialog_TextsAreLocalized()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\ConfigurationEditorDialog.xaml");

        Assert.DoesNotContain("Header=\"名称\"", xaml);
        Assert.DoesNotContain("PlaceholderText=\"例如：本地 Agent / 远程测试环境\"", xaml);
        Assert.DoesNotContain("Text=\"保存后会自动与“配置”卡片联动。\"", xaml);
        Assert.Contains("x:Uid=\"ConfigurationEditorDialog\"", xaml);
        Assert.Contains("x:Uid=\"ConfigurationEditorDialogName\"", xaml);
        Assert.Contains("x:Uid=\"ConfigurationEditorDialogHint\"", xaml);
    }

    [Fact]
    public void ComboBoxes_DoNotUseDisplayMemberPath_ForUnoWasm()
    {
        var chatInputXaml = LoadXaml(@"SalmonEgg\SalmonEgg\Controls\ChatInputArea.xaml");
        var dialogXaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\ConfigurationEditorDialog.xaml");
        var editorXaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\AgentProfileEditorPage.xaml");

        Assert.DoesNotContain("DisplayMemberPath=", chatInputXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("DisplayMemberPath=\"Name\"", dialogXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("DisplayMemberPath=\"Name\"", editorXaml, StringComparison.Ordinal);
        Assert.Contains("x:DataType=\"selectors:ComposerSelectorItemViewModel\"", chatInputXaml, StringComparison.Ordinal);
        Assert.Contains("x:DataType=\"vm:TransportOption\"", dialogXaml, StringComparison.Ordinal);
        Assert.Contains("x:DataType=\"vm:TransportOption\"", editorXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{x:Bind DisplayName, Mode=OneWay}\"", chatInputXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{x:Bind Name, Mode=OneWay}\"", dialogXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{x:Bind Name, Mode=OneWay}\"", editorXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void GeneralSettings_LanguageOptionsAreBoundToViewModelCatalog()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\GeneralSettingsPage.xaml");

        Assert.Contains("ItemsSource=\"{x:Bind ViewModel.Preferences.LanguageOptions, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedValue=\"{x:Bind ViewModel.Preferences.Language, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:DataType=\"settings:AppLanguageOptionViewModel\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{x:Bind DisplayNameResourceKey, Mode=OneWay, Converter={StaticResource ResourceStringConverter}}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Tag=\"zh-CN\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<ComboBoxItem x:Uid=\"General_LanguageZhCn\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MiniChatView_TextsAreLocalized()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\MiniWindow\MiniChatView.xaml");

        Assert.DoesNotContain("PlaceholderText=\"选择会话\"", xaml);
        Assert.DoesNotContain("PlaceholderText=\"输入消息\"", xaml);
        Assert.Contains("x:Uid=\"MiniChatSessionSelector\"", xaml);
        Assert.Contains("x:Uid=\"MiniChatReturnButton\"", xaml);
        Assert.Contains("x:Uid=\"MiniChatInputBox\"", xaml);
        Assert.DoesNotContain("x:Uid=\"MiniChatComposerPromptInFlightStatus\"", xaml);
        Assert.Contains("x:Uid=\"MiniChatComposerVoiceListeningStatus\"", xaml);
        Assert.Contains("x:Uid=\"MiniChatCancelButton\"", xaml);
        Assert.Contains("x:Uid=\"MiniChatSendButton\"", xaml);
    }

    [Fact]
    public void MiniChatView_ComposerBlockedStates_UseSameProjectionAsMainComposer()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\MiniWindow\MiniChatView.xaml");

        Assert.Contains("Visibility=\"{x:Bind ViewModel.ShowCancelPromptButton, Mode=OneWay", xaml);
        Assert.Contains("IsEnabled=\"{x:Bind ViewModel.CanCancelPromptUi, Mode=OneWay}\"", xaml);
        Assert.Contains("Visibility=\"{x:Bind ViewModel.VoiceInputUiState.ShowListeningStatus, Mode=OneWay", xaml);
        Assert.Contains("Visibility=\"{x:Bind ViewModel.VoiceInputUiState.ShowProgressRing, Mode=OneWay", xaml);
        Assert.Contains("IsEnabled=\"{x:Bind ViewModel.IsTextInputEnabled, Mode=OneWay}\"", xaml);
        Assert.Contains("Visibility=\"{x:Bind ViewModel.VoiceInputUiState.ShowStartButton, Mode=OneWay", xaml);
        Assert.Contains("Visibility=\"{x:Bind ViewModel.VoiceInputUiState.ShowStopButton, Mode=OneWay", xaml);
    }

    [Fact]
    public void MiniChatView_KeepsSharedTitleBarMarkupUnoCompatible()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\MiniWindow\MiniChatView.xaml");

        Assert.DoesNotContain("<TitleBar", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("LeftHeader=", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("RightHeader=", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MiniChatView_UsesCompactSessionLabelWhilePreservingFullNameTooltip()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\MiniWindow\MiniChatView.xaml");

        Assert.Contains("Text=\"{x:Bind CompactDisplayName, Mode=OneTime}\"", xaml);
        Assert.Contains("ToolTipService.ToolTip=\"{x:Bind DisplayName, Mode=OneTime}\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"{x:Bind ViewModel.CurrentSessionDisplayName, Mode=OneWay}\"", xaml);
    }

    [Fact]
    public void MiniWindowSessionSelection_ExposesStableAutomationIds()
    {
        var mainPageXaml = LoadXaml(@"SalmonEgg\SalmonEgg\MainPage.xaml");
        var miniChatXaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\MiniWindow\MiniChatView.xaml");
        var miniWindowItemVm = LoadText(@"src\SalmonEgg.Presentation.Core\ViewModels\Chat\MiniWindowConversationItemViewModel.cs");

        Assert.Contains("AutomationProperties.AutomationId=\"TitleBar.OpenMiniWindow\"", mainPageXaml);
        Assert.Contains("AutomationProperties.AutomationId=\"MiniChat.SessionSelector\"", miniChatXaml);
        Assert.Contains("AutomationProperties.AutomationId=\"{x:Bind AutomationId, Mode=OneTime}\"", miniChatXaml);
        Assert.Contains("public string AutomationId => $\"MiniChat.SessionItem.{ConversationId}\";", miniWindowItemVm);
    }

    [Fact]
    public void ChatInputArea_SelectorItems_ExposeStableAutomationIds()
    {
        var chatInputXaml = LoadXaml(@"SalmonEgg\SalmonEgg\Controls\ChatInputArea.xaml");
        var selectorItemVm = LoadText(@"src\SalmonEgg.Presentation.Core\ViewModels\Chat\Selectors\ComposerSelectorItemViewModel.cs");

        Assert.Contains(
            "AutomationProperties.AutomationId=\"{x:Bind AutomationId, Mode=OneWay}\"",
            chatInputXaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "public string AutomationId",
            selectorItemVm,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"ComposerSelectorItem.{Kind}.{ResolveAutomationSegment()}\"",
            selectorItemVm,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ChatInputArea_SelectorItems_DisableNativeComboBoxItemsFromViewModelProjection()
    {
        var chatInputXaml = LoadXaml(@"SalmonEgg\SalmonEgg\Controls\ChatInputArea.xaml");

        Assert.Contains("x:Key=\"ComposerSelectorComboBoxItemStyle\"", chatInputXaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"IsEnabled\" Value=\"{Binding IsSelectable}\"", chatInputXaml, StringComparison.Ordinal);
        Assert.Equal(3, CountOccurrences(chatInputXaml, "ItemContainerStyle=\"{StaticResource ComposerSelectorComboBoxItemStyle}\""));
    }

    [Fact]
    public void ChatView_UsesDeferredTranscriptLoadingWithoutWholePageLifecycleHack()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Chat\ChatView.xaml");
        var codeBehind = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Views\Chat\ChatView.xaml.cs");

        Assert.Contains("x:Name=\"ActiveConversationRoot\"", xaml);
        Assert.Contains("x:Load=\"{x:Bind ViewModel.ShouldLoadActiveConversationRoot, Mode=OneWay}\"", xaml);
        Assert.Contains("x:Load=\"{x:Bind ViewModel.ShouldLoadTranscriptSurface, Mode=OneWay}\"", xaml);
        Assert.Contains("Unloaded=\"OnMessagesListUnloaded\"", xaml);
        Assert.Contains("private void OnMessagesListUnloaded", codeBehind);
        Assert.DoesNotContain("PointerPressed=\"OnMessagesListPointerPressed\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("PointerWheelChanged=\"OnMessagesListPointerWheelChanged\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("KeyDown=\"OnMessagesListKeyDown\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("FindScrollViewer(", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("VisualTreeHelper.", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void MiniChatView_UsesNativeTranscriptInteractionWithoutWholePageLifecycleHack()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\MiniWindow\MiniChatView.xaml");
        var codeBehind = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Views\MiniWindow\MiniChatView.xaml.cs");

        Assert.Contains("Unloaded=\"OnMessagesListUnloaded\"", xaml);
        Assert.Contains("private void OnMessagesListUnloaded", codeBehind);
        Assert.DoesNotContain("PointerPressed=\"OnMessagesListPointerPressed\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("PointerWheelChanged=\"OnMessagesListPointerWheelChanged\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("KeyDown=\"OnMessagesListKeyDown\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("FindScrollViewer(", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("VisualTreeHelper.", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void TranscriptViewportHost_UsesNativeListViewBaseAsSingleViewportBoundary()
    {
        var host = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Transcript\ListViewTranscriptViewportHost.cs");

        Assert.Contains("ListViewBase", host, StringComparison.Ordinal);
        Assert.Contains("ListViewItem", host, StringComparison.Ordinal);
        Assert.Contains("Func<TranscriptVirtualizationRange?>", host, StringComparison.Ordinal);
        Assert.Contains("ClampRange(visibleRange, itemCount)", host, StringComparison.Ordinal);
        Assert.Contains("TryGetFirstVisibleIndexInRange(range, out index)", host, StringComparison.Ordinal);
        Assert.Contains("_listView.ScrollIntoView", host, StringComparison.Ordinal);
        Assert.Contains("_listView.ContainerFromIndex(index)", host, StringComparison.Ordinal);
        Assert.Contains("TransformToVisual(_listView).TransformPoint(default)", host, StringComparison.Ordinal);
        Assert.Contains("itemBottom <= viewportBottom + bottomGeometryTolerance", host, StringComparison.Ordinal);
        Assert.Contains("anchor.ActualHeight <= availableViewportHeight + bottomGeometryTolerance", host, StringComparison.Ordinal);
        Assert.DoesNotContain("ItemsRepeater", host, StringComparison.Ordinal);
        Assert.DoesNotContain("GetOrCreateElement", host, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateLayout()", host, StringComparison.Ordinal);
        Assert.DoesNotContain("BringIntoViewOptions", host, StringComparison.Ordinal);
        Assert.DoesNotContain("ChangeView", host, StringComparison.Ordinal);
        Assert.DoesNotContain("ScrollViewerViewportMonitor", host, StringComparison.Ordinal);
    }

    [Fact]
    public void AgentProfileEditor_InteractiveTextsExposeLocalizationUids()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\AgentProfileEditorPage.xaml");
        var zhHans = LoadText(@"SalmonEgg\SalmonEgg\Strings\zh-Hans\Resources.resw");

        Assert.Contains("x:Uid=\"AgentProfileEditorName\"", xaml);
        Assert.Contains("x:Uid=\"AgentProfileEditorAdvancedTitle\"", xaml);
        Assert.Contains("x:Uid=\"AgentProfileEditorCancelButton\"", xaml);
        Assert.Contains("AgentProfileEditorName.Header", zhHans);
        Assert.Contains("AgentProfileEditorPageTitleNew", zhHans);
        Assert.Contains("AgentProfileEditorPageTitleEdit", zhHans);
    }

    [Fact]
    public void DiscoverSessionsPage_UsesLocalizationUidsForVisibleCopy()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Discover\DiscoverSessionsPage.xaml");

        Assert.Contains("x:Uid=\"DiscoverSessionsTitle\"", xaml);
        Assert.Contains("x:Uid=\"DiscoverSessionsNoSelectionTitle\"", xaml);
        Assert.Contains("x:Uid=\"DiscoverSessionsConnectionError\"", xaml);
        Assert.Contains("x:Uid=\"DiscoverSessionsImportButton\"", xaml);
        Assert.Contains("x:Uid=\"DiscoverSessionsBackButton\"", xaml);
    }

    [Fact]
    public void DiscoverSessionsPage_UsesNativeFocusEngagementOnPrimaryLists()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Discover\DiscoverSessionsPage.xaml");

        Assert.Contains("x:Name=\"ProfilesList\"", xaml);
        Assert.Contains("x:Name=\"SessionsList\"", xaml);
        Assert.Contains("ProfilesList", xaml);
        Assert.Contains("SessionsList", xaml);
        Assert.Contains("IsFocusEngagementEnabled=\"True\"", xaml);
    }

    [Fact]
    public void MainPage_SearchUsesNativeAutoSuggestBoxWithoutFocusPatches()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\MainPage.xaml");
        var code = LoadText(@"SalmonEgg\SalmonEgg\MainPage.xaml.cs");

        Assert.Contains("<AutoSuggestBox x:Name=\"TopSearchBox\"", xaml);
        Assert.Contains("TextChanged=\"OnSearchTextChanged\"", xaml);
        Assert.Contains("SuggestionChosen=\"OnSearchSuggestionChosen\"", xaml);
        Assert.Contains("QuerySubmitted=\"OnSearchQuerySubmitted\"", xaml);
        Assert.Contains("ItemsSource=\"{x:Bind SearchVM.SuggestionEntries, Mode=OneWay}\"", xaml);
        Assert.Contains("<AutoSuggestBox.ItemTemplate>", xaml);
        Assert.DoesNotContain("FlyoutBase.AttachedFlyout", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SearchSuggestionsPresenter", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("FocusMonitor.IsFocused", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("IsSuggestionListOpen=", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("DispatcherQueue.TryEnqueue(TryFocusSearchPanelPrimaryAction)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("DispatcherQueue.TryEnqueue(() => TopSearchBox.Focus(FocusState.Programmatic))", code, StringComparison.Ordinal);
    }

    [Fact]
    public void MainPage_SearchBox_RemainsCenteredInTitleBar()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\MainPage.xaml");
        var code = LoadText(@"SalmonEgg\SalmonEgg\MainPage.xaml.cs");

        Assert.Contains("<AutoSuggestBox x:Name=\"TopSearchBox\"", xaml);
        Assert.Contains("Grid.Column=\"1\"", xaml);
        Assert.DoesNotContain("x:Name=\"TitleBarCenterSpacer\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<Border Grid.Row=\"0\"", xaml, StringComparison.Ordinal);
        Assert.Contains("TopSearchBox,", code, StringComparison.Ordinal);
        Assert.DoesNotContain("TitleBarCenterSpacer,", code, StringComparison.Ordinal);
    }

    [Fact]
    public void MainPage_SearchBox_DoesNotOverrideNativeInputChrome()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\MainPage.xaml");

        Assert.DoesNotContain("Height=\"32\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("CornerRadius=\"8\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MainPage_SearchSuggestions_StayWithinNativeAutoSuggestBox()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\MainPage.xaml");

        Assert.Contains("ItemsSource=\"{x:Bind SearchVM.SuggestionEntries, Mode=OneWay}\"", xaml);
        Assert.Contains("<AutoSuggestBox.ItemTemplate>", xaml);
        Assert.DoesNotContain("x:Name=\"SearchSuggestionsPresenter\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"SearchSuggestionsList\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Command=\"{x:Bind ActivateCommand}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("IsSuggestionListOpen=", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void DependencyInjection_RegistersGamepadInputBehindAnAbstraction()
    {
        var code = LoadText(@"SalmonEgg\SalmonEgg\DependencyInjection.cs");

        Assert.Contains("IGamepadInputService", code);
        Assert.Contains("IGamepadDiagnosticsService", code);
        Assert.Contains("SupportsGamepadInput", code);
        Assert.Contains("IsGuiAutomationEnabled()", code);
        Assert.DoesNotContain("new WindowsGamepadInputService(", code, StringComparison.Ordinal);
        Assert.DoesNotContain("new NoOpGamepadInputService(", code, StringComparison.Ordinal);
        Assert.DoesNotContain("GuiGamepadInputService", code, StringComparison.Ordinal);
        Assert.DoesNotContain("IsGuiGamepadInputEnabled", code, StringComparison.Ordinal);
        Assert.DoesNotContain("SALMONEGG_GUI_CONTROL_FILE", code, StringComparison.Ordinal);
        Assert.DoesNotContain("new WindowsGamepadDiagnosticsService(", code, StringComparison.Ordinal);
    }

    [Fact]
    public void DiagnosticsSettingsPage_ExposesGamepadDiagnosticsThroughViewModel()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\DiagnosticsSettingsPage.xaml");
        var viewModel = LoadText(@"src\SalmonEgg.Presentation.Core\ViewModels\Settings\GamepadDiagnosticsViewModel.cs");
        var windowsService = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Services\Input\WindowsGamepadDiagnosticsService.cs");
        var gamepadSection = ExtractSection(xaml, "Diagnostics_GamepadTitle", "Diagnostics_LogsTitle");

        Assert.Contains("AutomationProperties.AutomationId=\"Diagnostics.GamepadMonitorHeader\"", gamepadSection, StringComparison.Ordinal);
        Assert.DoesNotContain("<Expander", gamepadSection, StringComparison.Ordinal);
        Assert.Contains("ViewModel.GamepadDiagnostics.StatusText", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.GamepadDiagnostics.ConnectedGamepadsText", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.GamepadDiagnostics.ConnectedRawControllersText", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.GamepadDiagnostics.InputSourceText", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.GamepadDiagnostics.ActiveInputsText", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.GamepadDiagnostics.ThumbstickText", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.GamepadDiagnostics.RawControllersText", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.GamepadDiagnostics.StartMonitoringCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.GamepadDiagnostics.StopMonitoringCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.GamepadDiagnostics.RefreshSnapshotCommand", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Windows.Gaming.Input", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Windows.Gaming.Input", viewModel, StringComparison.Ordinal);
        Assert.Contains("Windows.Gaming.Input", windowsService, StringComparison.Ordinal);
    }

    [Fact]
    public void DiagnosticsSettingsPage_ExposesVoiceDiagnosticsThroughViewModel()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\DiagnosticsSettingsPage.xaml");
        var viewModel = LoadText(@"src\SalmonEgg.Presentation.Core\ViewModels\Settings\VoiceInputDiagnosticsViewModel.cs");
        var service = LoadText(@"src\SalmonEgg.Presentation.Core\Services\Input\VoiceInputDiagnosticsService.cs");
        var voiceSection = ExtractSection(xaml, "Diagnostics_VoiceTitle", "Diagnostics_GamepadTitle");

        Assert.Contains("AutomationProperties.AutomationId=\"Diagnostics.VoiceHeader\"", voiceSection, StringComparison.Ordinal);
        Assert.Contains("ViewModel.VoiceInputDiagnostics.SupportStatusText", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.VoiceInputDiagnostics.PermissionStatusText", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.VoiceInputDiagnostics.CurrentLanguageTagText", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.VoiceInputDiagnostics.InputDeviceText", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.VoiceInputDiagnostics.SessionStatusText", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.VoiceInputDiagnostics.CallbackObservationText", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.VoiceInputDiagnostics.TimelineText", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.VoiceInputDiagnostics.RecommendationText", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.VoiceInputDiagnostics.RefreshSnapshotCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.VoiceInputDiagnostics.OpenAuthorizationHelpCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.VoiceInputDiagnostics.Probe.StartProbeCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.VoiceInputDiagnostics.Probe.StopProbeCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.VoiceInputDiagnostics.Probe.ProbeStatusText", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.VoiceInputDiagnostics.Probe.ProbeTimelineText", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.VoiceInputDiagnostics.Probe.ProbeCapturedText", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.VoiceInputDiagnostics.Probe.ProbeSignalObservationText", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.VoiceInputDiagnostics.Probe.ProbeSignalTimelineText", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Windows.Media.SpeechRecognition", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Windows.Media.SpeechRecognition", viewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("Windows.Media.SpeechRecognition", LoadText(@"src\SalmonEgg.Presentation.Core\ViewModels\Settings\VoiceInputDiagnosticsProbeViewModel.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.UI.Xaml", service, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsSubPages_ExposePageTitlesAndSummaries()
    {
        string[] pages =
        [
            @"SalmonEgg\SalmonEgg\Presentation\Views\GeneralSettingsPage.xaml",
            @"SalmonEgg\SalmonEgg\Presentation\Views\Settings\AppearanceSettingsPage.xaml",
            @"SalmonEgg\SalmonEgg\Presentation\Views\Settings\AcpConnectionSettingsPage.xaml",
            @"SalmonEgg\SalmonEgg\Presentation\Views\Settings\DataStorageSettingsPage.xaml",
            @"SalmonEgg\SalmonEgg\Presentation\Views\Settings\ShortcutsSettingsPage.xaml",
            @"SalmonEgg\SalmonEgg\Presentation\Views\Settings\DiagnosticsSettingsPage.xaml",
            @"SalmonEgg\SalmonEgg\Presentation\Views\Settings\AboutPage.xaml"
        ];

        foreach (var page in pages)
        {
            var xaml = LoadXaml(page);

            Assert.Contains("Style=\"{StaticResource SettingsPageTitleTextStyle}\"", xaml, StringComparison.Ordinal);
            Assert.Contains("Style=\"{StaticResource SettingsPageSummaryTextStyle}\"", xaml, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void GeneralAndAppearanceSettingsPages_UseNativeSettingsRows()
    {
        var general = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\GeneralSettingsPage.xaml");
        var appearance = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\AppearanceSettingsPage.xaml");

        Assert.Contains("x:Uid=\"General_PageTitle\"", general, StringComparison.Ordinal);
        Assert.Contains("x:Uid=\"General_PageSummary\"", general, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource SettingsRowGridStyle}\"", general, StringComparison.Ordinal);
        Assert.Contains("<ToggleSwitch", general, StringComparison.Ordinal);
        Assert.Contains("x:Uid=\"General_AutoStartSwitch\"", general, StringComparison.Ordinal);
        Assert.Contains("x:Uid=\"General_MinimizeToTraySwitch\"", general, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Uid=\"General_AutoStart\"", general, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Uid=\"General_MinimizeToTray\"", general, StringComparison.Ordinal);
        Assert.Contains("<ComboBox", general, StringComparison.Ordinal);

        Assert.Contains("x:Uid=\"Appearance_PageTitle\"", appearance, StringComparison.Ordinal);
        Assert.Contains("x:Uid=\"Appearance_PageSummary\"", appearance, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource SettingsRowGridStyle}\"", appearance, StringComparison.Ordinal);
        Assert.Contains("<ToggleSwitch", appearance, StringComparison.Ordinal);
        Assert.Contains("<ComboBox", appearance, StringComparison.Ordinal);
    }

    [Fact]
    public void ShortcutsSettingsPage_RestoreAllRowKeepsDescriptionBesideAction()
    {
        var document = XDocument.Parse(LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\ShortcutsSettingsPage.xaml"));
        var restoreAllDescription = FindElementByUid(document, "Shortcuts_RestoreAllDescription");
        var restoreAllButton = FindElementByUid(document, "Shortcuts_RestoreAll");
        var restoreAllRow = Assert.Single(restoreAllButton.Ancestors()
            .Where(element => element.Name.LocalName == "Grid"
                && string.Equals(GetAttributeByLocalName(element, "Style"), "{StaticResource SettingsRowGridStyle}", StringComparison.Ordinal)));

        Assert.Equal("{StaticResource SettingsRowDescriptionTextStyle}", GetAttributeByLocalName(restoreAllDescription, "Style"));
        Assert.Contains(restoreAllDescription, restoreAllRow.Descendants());
        Assert.Contains(restoreAllButton, restoreAllRow.Descendants());
    }

    [Fact]
    public void Task6SettingsPages_HaveLocalizedVisibleTextResources()
    {
        string[] resourceFiles =
        [
            @"SalmonEgg\SalmonEgg\Strings\zh-Hans\Resources.resw",
            @"SalmonEgg\SalmonEgg\Strings\en\Resources.resw",
            @"SalmonEgg\SalmonEgg\Strings\en-US\Resources.resw"
        ];
        string[] requiredResources =
        [
            "Shortcuts_PageTitle.Text",
            "Shortcuts_PageSummary.Text",
            "Shortcuts_ConflictInfo.Title",
            "Shortcuts_InvalidInfo.Title",
            "Shortcuts_InvalidInfo.Message",
            "Shortcuts_CustomTitle.Text",
            "Shortcuts_AppOnlyHint.Text",
            "Shortcuts_GestureRecorder.PlaceholderText",
            "Shortcuts_GestureRecorder.RecordingText",
            "Shortcuts_RestoreSingle.Content",
            "Shortcuts_RestoreAllDescription.Text",
            "Shortcuts_RestoreAll.Content",
            "Diagnostics_PageTitle.Text",
            "Diagnostics_PageSummary.Text",
            "Diagnostics_EnvironmentTitle.Text",
            "Diagnostics_OsLabel.Text",
            "Diagnostics_FrameworkLabel.Text",
            "Diagnostics_AppVersionLabel.Text",
            "Diagnostics_ProtocolVersionLabel.Text",
            "Diagnostics_LogsTitle.Text",
            "Diagnostics_LogsFolderLabel.Text",
            "Diagnostics_LatestLogLabel.Text",
            "Diagnostics_OpenLogs.Content",
            "Diagnostics_CopyLogSnippet.Content",
            "Diagnostics_RefreshLogs.Content",
            "Diagnostics_LogActionsTitle.Text",
            "Diagnostics_LiveLogHeader.Text",
            "Diagnostics_LiveLogStart.Content",
            "Diagnostics_LiveLogPause.Content",
            "Diagnostics_LiveLogResume.Content",
            "Diagnostics_LiveLogClear.Content",
            "Diagnostics_LiveLogHint.Text",
            "Diagnostics_VoiceTitle.Text",
            "Diagnostics_VoiceHeader.Text",
            "Diagnostics_VoiceProbeHeader.Text",
            "Diagnostics_VoiceSupportLabel.Text",
            "Diagnostics_VoicePermissionLabel.Text",
            "Diagnostics_VoiceLanguageLabel.Text",
            "Diagnostics_VoiceInputDeviceLabel.Text",
            "Diagnostics_VoiceSessionStatusLabel.Text",
            "Diagnostics_VoiceCallbackStatusLabel.Text",
            "Diagnostics_VoiceTimelineLabel.Text",
            "Diagnostics_VoiceRecommendationLabel.Text",
            "Diagnostics_VoiceProbeStatusLabel.Text",
            "Diagnostics_VoiceProbeTimelineLabel.Text",
            "Diagnostics_VoiceProbeCapturedTextLabel.Text",
            "Diagnostics_VoiceProbeSignalLabel.Text",
            "Diagnostics_VoiceProbeSignalTimelineLabel.Text",
            "Diagnostics_VoiceRefresh.Content",
            "Diagnostics_VoiceOpenAuthorization.Content",
            "Diagnostics_VoiceProbeStart.Content",
            "Diagnostics_VoiceProbeStop.Content",
            "Diagnostics_GamepadTitle.Text",
            "Diagnostics_GamepadMonitorHeader.Text",
            "Diagnostics_GamepadStatusLabel.Text",
            "Diagnostics_GamepadStandardCountLabel.Text",
            "Diagnostics_GamepadRawCountLabel.Text",
            "Diagnostics_GamepadInputSourceLabel.Text",
            "Diagnostics_GamepadActiveInputsLabel.Text",
            "Diagnostics_GamepadThumbstickLabel.Text",
            "Diagnostics_GamepadRawDetailsLabel.Text",
            "Diagnostics_GamepadStart.Content",
            "Diagnostics_GamepadStop.Content",
            "Diagnostics_GamepadRefresh.Content",
            "Diagnostics_ConnectionTitle.Text",
            "Diagnostics_ConnectionStatusLabel.Text",
            "Diagnostics_AgentLabel.Text",
            "Diagnostics_SessionLabel.Text",
            "Diagnostics_BundleTitle.Text",
            "Diagnostics_BundleDescription.Text",
            "Diagnostics_CreateBundle.Content",
            "About_PageTitle.Text",
            "About_PageSummary.Text",
            "About_AppInfoTitle.Text",
            "About_AppNameLabel.Text",
            "About_VersionLabel.Text",
            "About_ProtocolLabel.Text",
            "About_SupportTitle.Text",
            "About_SupportActionsTitle.Text",
            "About_OpenAppData.Content",
            "About_ReportInappropriateAiContent.Content",
            "About_OpenReleaseNotes.Content",
            "About_OpenPrivacyPolicy.Content",
            "About_CopyVersionInfo.Content",
            "About_DocsFolderLabel.Text",
            "About_DocsHint.Text",
            "About_OpenSourceTitle.Text",
            "About_OpenSourceDescription.Text",
            "About_OpenSourcePackageHeader.Text",
            "About_OpenSourceVersionHeader.Text",
            "About_OpenSourceLicenseHeader.Text",
            "About_OpenSourceSourceHeader.Text"
        ];

        foreach (var resourceFile in resourceFiles)
        {
            var resources = XDocument.Parse(LoadText(resourceFile));

            foreach (var resourceName in requiredResources)
            {
                Assert.True(
                    resources.Descendants("data")
                        .Any(data => string.Equals((string?)data.Attribute("name"), resourceName, StringComparison.Ordinal)),
                    $"{resourceFile} must define {resourceName}.");
            }
        }
    }

    [Fact]
    public void GeneralAndAppearanceSettingsPages_HaveLocalizedVisibleTextResources()
    {
        string[] resourceFiles =
        [
            @"SalmonEgg\SalmonEgg\Strings\zh-Hans\Resources.resw",
            @"SalmonEgg\SalmonEgg\Strings\en\Resources.resw",
            @"SalmonEgg\SalmonEgg\Strings\en-US\Resources.resw"
        ];
        string[] requiredResources =
        [
            "General_PageTitle.Text",
            "General_PageSummary.Text",
            "General_AutoStartTitle.Text",
            "General_MinimizeToTrayTitle.Text",
            "Appearance_PageTitle.Text",
            "Appearance_PageSummary.Text",
            "Appearance_ThemeLabel.Text",
            "Appearance_BackdropLabel.Text",
            "Appearance_BackdropMica.Content",
            "Appearance_BackdropAcrylic.Content"
        ];

        foreach (var resourceFile in resourceFiles)
        {
            var resources = XDocument.Parse(LoadText(resourceFile));

            foreach (var resourceName in requiredResources)
            {
                Assert.True(
                    resources.Descendants("data")
                        .Any(data => string.Equals((string?)data.Attribute("name"), resourceName, StringComparison.Ordinal)),
                    $"{resourceFile} must define {resourceName}.");
            }
        }
    }

    [Fact]
    public void SettingsShell_KeepsSectionNavigationAtTheTop()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\SettingsShellPage.xaml");

        Assert.Contains("<Setter Property=\"PaneDisplayMode\" Value=\"Top\" />", xaml);
        Assert.DoesNotContain("PaneDisplayMode\" Value=\"Left", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<NavigationViewItemHeader", xaml, StringComparison.Ordinal);
        Assert.Contains("MenuItemsSource=\"{x:Bind ViewModel.Sections, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedItem=\"{x:Bind ViewModel.SelectedSection, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectionChanged=\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsShell_SelectionUsesViewModelSectionIdentity()
    {
        var code = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Views\SettingsShellPage.xaml.cs");
        var adapterCode = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Navigation\SettingsSectionNavigationAdapter.cs");

        Assert.Contains("public SettingsShellViewModel ViewModel { get; }", code, StringComparison.Ordinal);
        Assert.Contains("_sectionNavigation = new SettingsSectionNavigationAdapter(SettingsNavView)", code, StringComparison.Ordinal);
        Assert.Contains("private void AttachSectionNavigation()", code, StringComparison.Ordinal);
        Assert.Contains("private void DetachSectionNavigation()", code, StringComparison.Ordinal);
        Assert.Contains("_sectionNavigation = null;", code, StringComparison.Ordinal);
        Assert.Contains("ViewModel.SelectSection(key)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("new NavigationViewItem", code, StringComparison.Ordinal);
        Assert.DoesNotContain("SettingsNavView.MenuItems", code, StringComparison.Ordinal);
        Assert.DoesNotContain("FindNavItemByKey", code, StringComparison.Ordinal);

        Assert.Contains("SettingsShellSectionViewModel section", adapterCode, StringComparison.Ordinal);
        Assert.Contains("_navigationView.ItemInvoked += OnItemInvoked", adapterCode, StringComparison.Ordinal);
        Assert.Contains("_navigationView.ItemInvoked -= OnItemInvoked", adapterCode, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var section in sections)", adapterCode, StringComparison.Ordinal);
        Assert.DoesNotContain("_navigationView.MenuItems.Add", adapterCode, StringComparison.Ordinal);
        Assert.DoesNotContain("_navigationView.SelectedItem =", adapterCode, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectionChanged", adapterCode, StringComparison.Ordinal);
        Assert.DoesNotContain("_suppressSelectionChanged", adapterCode, StringComparison.Ordinal);
        Assert.DoesNotContain("MenuItemsSource=", adapterCode, StringComparison.Ordinal);
        Assert.DoesNotContain(".MenuItemsSource", adapterCode, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsBreadcrumbsUseCanonicalSectionCatalog()
    {
        string[] settingsPageFiles =
        [
            @"SalmonEgg\SalmonEgg\Presentation\Views\GeneralSettingsPage.xaml.cs",
            @"SalmonEgg\SalmonEgg\Presentation\Views\Settings\AppearanceSettingsPage.xaml.cs",
            @"SalmonEgg\SalmonEgg\Presentation\Views\Settings\AcpConnectionSettingsPage.xaml.cs",
            @"SalmonEgg\SalmonEgg\Presentation\Views\Settings\DataStorageSettingsPage.xaml.cs",
            @"SalmonEgg\SalmonEgg\Presentation\Views\Settings\ShortcutsSettingsPage.xaml.cs",
            @"SalmonEgg\SalmonEgg\Presentation\Views\Settings\DiagnosticsSettingsPage.xaml.cs",
            @"SalmonEgg\SalmonEgg\Presentation\Views\Settings\AboutPage.xaml.cs",
            @"SalmonEgg\SalmonEgg\Presentation\Views\Settings\AgentProfileEditorPage.xaml.cs"
        ];

        foreach (var settingsPageFile in settingsPageFiles)
        {
            var code = LoadText(settingsPageFile);

            Assert.DoesNotContain("SettingsNav_", code, StringComparison.Ordinal);
            Assert.DoesNotContain("SettingsBreadcrumbRoot", code, StringComparison.Ordinal);
            Assert.Contains("SettingsSectionCatalog.", code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void SettingsBreadcrumbBar_ActivatesThroughNavigationCoordinator()
    {
        var code = LoadText(@"SalmonEgg\SalmonEgg\Controls\SettingsBreadcrumbBar.xaml.cs");

        Assert.Contains("INavigationCoordinator", code, StringComparison.Ordinal);
        Assert.Contains("ActivateSettingsAsync", code, StringComparison.Ordinal);
        Assert.DoesNotContain("IShellNavigationService", code, StringComparison.Ordinal);
        Assert.DoesNotContain("NavigateToSettings", code, StringComparison.Ordinal);
    }

    [Fact]
    public void FrameNavigation_UsesNativeNavigationTransitionInfo()
    {
        var settingsShellCode = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Views\SettingsShellPage.xaml.cs");
        var contentNavigationCode = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Navigation\ContentFrameNavigationAdapter.cs");
        var motionCode = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Models\UiMotionController.cs");
        string[] frameNavigationFiles =
        [
            @"SalmonEgg\SalmonEgg\App.xaml.cs",
            @"SalmonEgg\SalmonEgg\Presentation\Navigation\ContentFrameNavigationAdapter.cs",
            @"SalmonEgg\SalmonEgg\Presentation\Views\SettingsShellPage.xaml.cs",
            @"SalmonEgg\SalmonEgg\Presentation\Views\Settings\AcpConnectionSettingsPage.xaml.cs",
            @"SalmonEgg\SalmonEgg\Presentation\Views\Settings\AgentProfileEditorPage.xaml.cs"
        ];

        Assert.DoesNotContain("ContentTransitions = new TransitionCollection", settingsShellCode, StringComparison.Ordinal);
        Assert.DoesNotContain("EntranceThemeTransition()", settingsShellCode, StringComparison.Ordinal);
        Assert.Contains("NavigationTransitionInfo CreateNavigationTransitionInfo()", motionCode, StringComparison.Ordinal);
        Assert.Contains("new EntranceNavigationTransitionInfo()", motionCode, StringComparison.Ordinal);
        Assert.Contains("new SuppressNavigationTransitionInfo()", motionCode, StringComparison.Ordinal);

        foreach (var frameNavigationFile in frameNavigationFiles)
        {
            var code = LoadText(frameNavigationFile);
            var navigateCalls = code.Split([".Navigate("], StringSplitOptions.None).Skip(1);

            foreach (var navigateCall in navigateCalls)
            {
                var statement = navigateCall.Split(';')[0];

                Assert.Contains(
                    "UiMotionController.Current.CreateNavigationTransitionInfo()",
                    statement,
                    StringComparison.Ordinal);
            }
        }

        Assert.Contains("UiMotionController.Current.CreateNavigationTransitionInfo()", contentNavigationCode, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedChatUi_UsesPlatformShellServiceForClipboardAndUriLaunch()
    {
        var chatStylesXaml = LoadXaml(@"SalmonEgg\SalmonEgg\Styles\ChatStyles.xaml");
        var chatStylesCode = LoadText(@"SalmonEgg\SalmonEgg\Styles\ChatStyles.xaml.cs");
        var markdownCode = LoadText(@"SalmonEgg\SalmonEgg\Controls\MarkdownTextPresenter.cs");
        var chatMessageViewModel = LoadText(@"src\SalmonEgg.Presentation.Core\ViewModels\Chat\ChatMessageViewModel.cs");
        var chatViewModel = LoadText(@"src\SalmonEgg.Presentation.Core\ViewModels\Chat\ChatViewModel.cs");

        Assert.Contains("Command=\"{x:Bind CopyTextCommand}\"", chatStylesXaml, StringComparison.Ordinal);
        Assert.Contains("IAsyncRelayCommand<string?> CopyTextCommand", chatMessageViewModel, StringComparison.Ordinal);
        Assert.Contains("IAsyncRelayCommand<string?> OpenMarkdownLinkCommand", chatMessageViewModel, StringComparison.Ordinal);
        Assert.Contains("ConfigureShellActions", chatViewModel, StringComparison.Ordinal);
        Assert.Contains("CopyToClipboardAsync", chatViewModel, StringComparison.Ordinal);
        Assert.Contains("OpenUriAsync", chatViewModel, StringComparison.Ordinal);
        Assert.Contains("LinkCommand=\"{x:Bind OpenMarkdownLinkCommand}\"", chatStylesXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("IsEnabled=\"{x:Bind HasTextContent", chatStylesXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("GetRequiredService", chatStylesCode, StringComparison.Ordinal);
        Assert.DoesNotContain("Windows.ApplicationModel.DataTransfer", chatStylesCode, StringComparison.Ordinal);
        Assert.DoesNotContain("Clipboard.SetContent", chatStylesCode, StringComparison.Ordinal);
        Assert.DoesNotContain("DataPackage", chatStylesCode, StringComparison.Ordinal);

        Assert.Contains("LinkCommandProperty", markdownCode, StringComparison.Ordinal);
        Assert.DoesNotContain("IPlatformShellService", markdownCode, StringComparison.Ordinal);
        Assert.DoesNotContain("GetRequiredService", markdownCode, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenUriAsync", markdownCode, StringComparison.Ordinal);
        Assert.DoesNotContain("using Windows.System;", markdownCode, StringComparison.Ordinal);
        Assert.DoesNotContain("Launcher.LaunchUriAsync", markdownCode, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedViews_DoNotUseUnsupportedUiElementTransitions()
    {
        string[] xamlFiles =
        [
            @"SalmonEgg\SalmonEgg\MainPage.xaml",
            @"SalmonEgg\SalmonEgg\Presentation\Views\Chat\ChatView.xaml",
            @"SalmonEgg\SalmonEgg\Presentation\Views\Start\StartView.xaml",
            @"SalmonEgg\SalmonEgg\Presentation\Views\SettingsShellPage.xaml"
        ];

        foreach (var xamlFile in xamlFiles)
        {
            var xaml = LoadXaml(xamlFile);

            Assert.DoesNotContain(" Transitions=\"", xaml, StringComparison.Ordinal);
            Assert.DoesNotContain("\n          Transitions=\"", xaml, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void MainPage_LeavesRightPanelMotionToNativeSplitView()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\MainPage.xaml");
        var mainPageCode = LoadText(@"SalmonEgg\SalmonEgg\MainPage.xaml.cs");
        var defaultPlatformCode = LoadText(@"SalmonEgg\SalmonEgg\MainPage.Default.cs");
        var windowsPlatformCode = LoadText(@"SalmonEgg\SalmonEgg\Platforms\Windows\MainPage.Windows.cs");

        Assert.Contains("x:Name=\"RightPanelSplitView\"", xaml, StringComparison.Ordinal);
        Assert.Contains("DisplayMode=\"Inline\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("DisplayMode=\"CompactInline\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("CompactPaneLength=\"0\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"RightPanelContentRoot\"", xaml, StringComparison.Ordinal);

        foreach (var code in new[] { mainPageCode, defaultPlatformCode, windowsPlatformCode })
        {
            Assert.DoesNotContain("ConfigureShellLayoutAnimations", code, StringComparison.Ordinal);
            Assert.DoesNotContain("ElementCompositionPreview.GetElementVisual", code, StringComparison.Ordinal);
            Assert.DoesNotContain("CreateImplicitAnimationCollection", code, StringComparison.Ordinal);
            Assert.DoesNotContain("ImplicitAnimations", code, StringComparison.Ordinal);
            Assert.DoesNotContain("Storyboard", code, StringComparison.Ordinal);
            Assert.DoesNotContain("DoubleAnimation", code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void GeneralSettingsPage_DoesNotDuplicateCacheMaintenance()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\GeneralSettingsPage.xaml");

        Assert.DoesNotContain("General_MaintenanceTitle", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("General_ClearCacheTitle", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("General_ClearCacheAction", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void AppearanceSettingsPage_MotionPreferenceIsActionable()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\AppearanceSettingsPage.xaml");

        Assert.Contains("IsOn=\"{x:Bind Preferences.IsAnimationEnabled, Mode=TwoWay}\"", xaml);
        Assert.DoesNotContain("未实现", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("IsEnabled=\"False\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void AppearanceSettingsPage_MotionPreferenceCopyUsesUserLanguage()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\AppearanceSettingsPage.xaml");

        Assert.DoesNotContain("全局过渡动画", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("依赖动画", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("dependent", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("动画效果", xaml, StringComparison.Ordinal);
        Assert.Contains("页面切换", xaml, StringComparison.Ordinal);
        Assert.Contains("应用内状态提示", xaml, StringComparison.Ordinal);
        Assert.Contains("系统关闭动画时也会自动关闭", xaml, StringComparison.Ordinal);

        string[] resourceFiles =
        [
            @"SalmonEgg\SalmonEgg\Strings\zh-Hans\Resources.resw",
            @"SalmonEgg\SalmonEgg\Strings\en\Resources.resw",
            @"SalmonEgg\SalmonEgg\Strings\en-US\Resources.resw"
        ];

        foreach (var resourceFile in resourceFiles)
        {
            var resources = XDocument.Parse(LoadText(resourceFile));
            var title = GetResourceValue(resources, "Appearance_MotionToggleTitle.Text");
            var description = GetResourceValue(resources, "Appearance_MotionToggleDescription.Text");
            var combinedText = $"{title} {description}";

            Assert.DoesNotContain("依赖动画", combinedText, StringComparison.Ordinal);
            Assert.DoesNotContain("dependent", combinedText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("native control", combinedText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("全局", combinedText, StringComparison.Ordinal);
            Assert.DoesNotContain("global", combinedText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(
                resourceFile.Contains("zh-Hans", StringComparison.Ordinal)
                    ? "页面"
                    : "page",
                combinedText,
                StringComparison.OrdinalIgnoreCase);
            Assert.Contains(
                resourceFile.Contains("zh-Hans", StringComparison.Ordinal)
                    ? "状态"
                    : "status",
                combinedText,
                StringComparison.OrdinalIgnoreCase);
            Assert.Contains(
                resourceFile.Contains("zh-Hans", StringComparison.Ordinal)
                    ? "系统关闭动画"
                    : "system animations are off",
                combinedText,
                StringComparison.OrdinalIgnoreCase);
            Assert.Contains(
                resourceFile.Contains("zh-Hans", StringComparison.Ordinal)
                    ? "自动关闭"
                    : "turn off automatically",
                combinedText,
                StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void DataStorageSettingsPage_SeparatesRoutineStorageAndDangerActions()
    {
        var document = XDocument.Parse(LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\DataStorageSettingsPage.xaml"));
        var xaml = document.ToString(SaveOptions.DisableFormatting);
        var resetDefaultsTitle = FindElementByUid(document, "DataStorage_ResetDefaultsTitle");
        var clearAllDataTitle = FindElementByUid(document, "DataStorage_ClearAllDataTitle");
        var resetDefaults = FindElementByUid(document, "DataStorage_ResetDefaults");
        var clearAllData = FindElementByUid(document, "DataStorage_ClearAllData");
        var dangerTitle = FindElementByUid(document, "DataStorage_DangerTitle");
        var dangerExpander = Assert.Single(dangerTitle.Ancestors().Where(element => element.Name.LocalName == "Expander"));
        var resetDefaultsRow = Assert.Single(resetDefaults.Ancestors()
            .Where(element => element.Name.LocalName == "Grid"
                && string.Equals(GetAttributeByLocalName(element, "Style"), "{StaticResource SettingsRowGridStyle}", StringComparison.Ordinal)));
        var clearAllDataRow = Assert.Single(clearAllData.Ancestors()
            .Where(element => element.Name.LocalName == "Grid"
                && string.Equals(GetAttributeByLocalName(element, "Style"), "{StaticResource SettingsRowGridStyle}", StringComparison.Ordinal)));

        Assert.Contains("x:Uid=\"DataStorage_PageTitle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Uid=\"DataStorage_PageSummary\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<Expander", xaml, StringComparison.Ordinal);
        Assert.Contains("DataStorage_DangerTitle", xaml, StringComparison.Ordinal);
        Assert.Contains("DataStorage_DangerWarning", xaml, StringComparison.Ordinal);
        Assert.Equal("{StaticResource SettingsRowTitleTextStyle}", GetAttributeByLocalName(resetDefaultsTitle, "Style"));
        Assert.Equal("{StaticResource SettingsRowTitleTextStyle}", GetAttributeByLocalName(clearAllDataTitle, "Style"));
        Assert.NotSame(resetDefaults.Parent, clearAllData.Parent);
        Assert.NotSame(resetDefaultsRow, clearAllDataRow);
        Assert.Contains(resetDefaultsRow, dangerExpander.Descendants());
        Assert.Contains(clearAllDataRow, dangerExpander.Descendants());

        string[] resourceFiles =
        [
            @"SalmonEgg\SalmonEgg\Strings\zh-Hans\Resources.resw",
            @"SalmonEgg\SalmonEgg\Strings\en\Resources.resw",
            @"SalmonEgg\SalmonEgg\Strings\en-US\Resources.resw"
        ];
        foreach (var resourceFile in resourceFiles)
        {
            var resources = LoadText(resourceFile);

            Assert.Contains("DataStorage_ResetDefaultsTitle.Text", resources, StringComparison.Ordinal);
            Assert.Contains("DataStorage_ClearAllDataTitle.Text", resources, StringComparison.Ordinal);
            Assert.DoesNotContain("DataStorage_ResetTitle.Text", resources, StringComparison.Ordinal);
            Assert.DoesNotContain("DataStorage_ResetWarning.Text", resources, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AboutPage_DisplaysGeneratedOpenSourceAcknowledgements()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\AboutPage.xaml");

        Assert.Contains("x:Uid=\"About_OpenSourceTitle\"", xaml);
        Assert.Contains("ItemsSource=\"{x:Bind ViewModel.OpenSourceAcknowledgements, Mode=OneWay}\"", xaml);
        Assert.Contains("x:DataType=\"settings:OpenSourceAcknowledgementViewModel\"", xaml);
        Assert.DoesNotContain("Binding OpenSourceAcknowledgements", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void AboutPage_SupportActionsIncludeReportInappropriateAiContent()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\AboutPage.xaml");

        Assert.Contains("x:Uid=\"About_ReportInappropriateAiContent\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"About.Support.ReportAiContent\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{x:Bind ViewModel.ReportInappropriateAiContentCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsEnabled=\"{x:Bind ViewModel.CanReportInappropriateAiContent, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void SalmonEggApp_GeneratesOpenSourceAcknowledgementsFromPackageReferences()
    {
        var project = LoadText(@"SalmonEgg\SalmonEgg\SalmonEgg.csproj");

        Assert.Contains("Target Name=\"GenerateOpenSourceAcknowledgements\"", project, StringComparison.Ordinal);
        Assert.Contains("BeforeTargets=\"CreateManifestResourceNames\"", project, StringComparison.Ordinal);
        Assert.Contains("Include=\"@(PackageReference)\"", project, StringComparison.Ordinal);
        Assert.Contains("OpenSourceAcknowledgements.tsv", project, StringComparison.Ordinal);
        Assert.Contains("EmbeddedResource Include=\"$(OpenSourceAcknowledgementsGeneratedFile)\"", project, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenSourceAcknowledgements.g.cs", project, StringComparison.Ordinal);
        Assert.DoesNotContain("<Compile Include=\"$(OpenSourceAcknowledgementsGeneratedFile)\"", project, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowsGamepadInputService_DelegatesRepeatAndDeadzonePolicyToCoreProcessor()
    {
        var code = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Services\Input\WindowsGamepadInputService.cs");

        Assert.Contains("GamepadIntentProcessor", code);
        Assert.DoesNotContain("InitialRepeatDelay", code, StringComparison.Ordinal);
        Assert.DoesNotContain("RepeatInterval", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ThumbstickDeadzone", code, StringComparison.Ordinal);
        Assert.DoesNotContain("PressState", code, StringComparison.Ordinal);
    }

    [Fact]
    public void MainPage_GamepadNavigation_UsesServiceAndDoesNotMaintainSyntheticSelectionState()
    {
        var code = LoadText(@"SalmonEgg\SalmonEgg\MainPage.xaml.cs");

        Assert.Contains("IGamepadInputService", code);
        Assert.DoesNotContain("currentGamepadIndex", code, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("selectedByGamepad", code, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MainShellGamepadNavigationDispatcher_DoesNotSynthesizeNativeControlFocusOrActivation()
    {
        var code = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Services\Input\MainShellGamepadNavigationDispatcher.cs");

        Assert.Contains("IShellBackNavigationService", code);
        Assert.Contains("IGamepadNativeInputBridge", code);
        Assert.Contains("TryConsumeNavigationIntent", code);
        Assert.Contains("GamepadNavigationIntent.Back", code);
        Assert.Contains("_nativeInputBridge.TryDispatch(intent)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("SendInput", code, StringComparison.Ordinal);
        Assert.DoesNotContain("XamlFocusManager.TryMoveFocus", code, StringComparison.Ordinal);
        Assert.DoesNotContain("FindNextElementOptions", code, StringComparison.Ordinal);
        Assert.DoesNotContain("SearchRoot = searchRoot", code, StringComparison.Ordinal);
        Assert.DoesNotContain("FrameworkElementAutomationPeer", code, StringComparison.Ordinal);
        Assert.DoesNotContain("IInvokeProvider", code);
        Assert.DoesNotContain("IToggleProvider", code);
        Assert.DoesNotContain("IExpandCollapseProvider", code);
        Assert.DoesNotContain("ISelectionItemProvider", code);
        Assert.DoesNotContain(".Select()", code, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedItem =", code, StringComparison.Ordinal);
        Assert.DoesNotContain(".IsOpen = false", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ContentFrame", code, StringComparison.Ordinal);
        Assert.DoesNotContain("TitleBarBackButton", code, StringComparison.Ordinal);
        Assert.DoesNotContain("GetOpenPopupsForXamlRoot", code, StringComparison.Ordinal);
        Assert.DoesNotContain(".GoBack(", code, StringComparison.Ordinal);
        Assert.DoesNotContain(".Hide()", code, StringComparison.Ordinal);
    }

    [Fact]
    public void MainPage_GamepadNavigation_DoesNotInterceptNavigationViewActivation()
    {
        var mainPage = LoadText(@"SalmonEgg\SalmonEgg\MainPage.xaml.cs");
        var adapter = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Navigation\MainNavigationViewAdapter.cs");

        Assert.Contains("HandleItemInvokedAsync", adapter, StringComparison.Ordinal);
        Assert.Contains("HandleActivatableTagAsync(navItem, tag)", adapter, StringComparison.Ordinal);
        Assert.Contains("MainPage : Page, INavigationIntentConsumer", mainPage, StringComparison.Ordinal);
        Assert.Contains("public bool TryConsumeNavigationIntent(GamepadNavigationIntent intent)", mainPage, StringComparison.Ordinal);
        Assert.Contains("intent != GamepadNavigationIntent.MoveRight", mainPage, StringComparison.Ordinal);
        Assert.Contains("IsFocusWithinMainNavigation()", mainPage, StringComparison.Ordinal);
        Assert.Contains("TryMoveFocusFromMainNavigationIntoCurrentContent()", mainPage, StringComparison.Ordinal);
        Assert.DoesNotContain("TryHandleFocusedMainNavigationActivationAsync", mainPage, StringComparison.Ordinal);
        Assert.DoesNotContain("ResolveFocusedMainNavigationItem", mainPage, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateFocusedItemActivationTask", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("HandleFocusedItemActivationAsync", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("_mainNavigationViewAdapter.CreateFocusedItemActivationTask", mainPage, StringComparison.Ordinal);
        Assert.DoesNotContain("MainNav.Start", mainPage, StringComparison.Ordinal);
        Assert.DoesNotContain("MainNav.DiscoverSessions", mainPage, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectionChanged", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedItem =", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedItem =", mainPage, StringComparison.Ordinal);
        Assert.DoesNotContain("GamepadNavigationIntent.Activate", mainPage, StringComparison.Ordinal);
    }

    [Fact]
    public void MainPage_GamepadMainNavFocus_AllowsProjectChildrenToReceiveNativeFocus()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\MainPage.xaml");

        var navigationViewSection = ExtractSection(
            xaml,
            "<NavigationView x:Name=\"MainNavView\"",
            "<NavigationView.Content>");
        var projectTemplateSection = ExtractSection(
            xaml,
            "<DataTemplate x:Key=\"ProjectNavTemplate\"",
            "<DataTemplate x:Key=\"SessionNavTemplate\"");
        var sessionTemplateSection = ExtractSection(
            xaml,
            "<DataTemplate x:Key=\"SessionNavTemplate\"",
            "<DataTemplate x:Key=\"MoreNavTemplate\"");

        Assert.DoesNotContain("IsFocusEngagementEnabled=\"True\"", navigationViewSection, StringComparison.Ordinal);
        Assert.Contains("XYFocusKeyboardNavigation=\"Enabled\"", navigationViewSection, StringComparison.Ordinal);
        Assert.Contains("XYFocusRight=\"{x:Bind ContentFrame, Mode=OneWay}\"", navigationViewSection, StringComparison.Ordinal);
        Assert.Contains("XYFocusUp=\"{x:Bind TitleBarToggleLeftNavButton, Mode=OneWay}\"", navigationViewSection, StringComparison.Ordinal);
        Assert.DoesNotContain("IsFocusEngagementEnabled=\"True\"", projectTemplateSection, StringComparison.Ordinal);
        Assert.DoesNotContain("XYFocusKeyboardNavigation=\"Enabled\"", projectTemplateSection, StringComparison.Ordinal);
        Assert.DoesNotContain("Loaded=\"OnMainNavItemLoaded\"", projectTemplateSection, StringComparison.Ordinal);
        Assert.DoesNotContain("XYFocusKeyboardNavigation=\"Enabled\"", sessionTemplateSection, StringComparison.Ordinal);
        Assert.DoesNotContain("Loaded=\"OnMainNavItemLoaded\"", sessionTemplateSection, StringComparison.Ordinal);
        var mainPageCode = LoadText(@"SalmonEgg\SalmonEgg\MainPage.xaml.cs");
        Assert.DoesNotContain("UpdateMainNavHierarchicalFocusRoutes", mainPageCode, StringComparison.Ordinal);
        Assert.DoesNotContain("OnMainNavItemLoaded", mainPageCode, StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshNavGamepadFocusRoutes", mainPageCode, StringComparison.Ordinal);
        Assert.DoesNotContain("EnumerateNavigationViewItems", mainPageCode, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedItem =", LoadText(@"SalmonEgg\SalmonEgg\MainPage.xaml.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void MainPage_TitleBarCommands_DoNotTrapGamepadDirectionalNavigation()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\MainPage.xaml");
        var leftCommandsSection = ExtractSection(
            xaml,
            "<StackPanel x:Name=\"TitleBarLeftButtons\"",
            "</StackPanel>");
        var rightCommandsSection = ExtractSection(
            xaml,
            "<StackPanel x:Name=\"TitleBarRightButtons\"",
            "</StackPanel>");

        Assert.DoesNotContain("XYFocusKeyboardNavigation", leftCommandsSection, StringComparison.Ordinal);
        Assert.DoesNotContain("XYFocusKeyboardNavigation", rightCommandsSection, StringComparison.Ordinal);
        AssertTitleBarCommandTargetsMainNavigationOnGamepadDown(xaml, "TitleBarBackButton");
        AssertTitleBarCommandTargetsMainNavigationOnGamepadDown(xaml, "TitleBarToggleLeftNavButton");
        AssertTitleBarCommandTargetsMainNavigationOnGamepadDown(xaml, "TitleBarMiniWindowButton");
        AssertTitleBarCommandTargetsMainNavigationOnGamepadDown(xaml, "BottomPanelButton");
        AssertTitleBarCommandTargetsMainNavigationOnGamepadDown(xaml, "TaskOverviewPanelButton");
    }

    [Fact]
    public void WindowsGuiAppSession_ActivatesThroughInvokeOrPointerWithoutManualSelection()
    {
        var code = LoadText(@"tests\SalmonEgg.GuiTests.Windows\WindowsGuiAppSession.cs");
        var activateElement = ExtractSection(
            code,
            "public void ActivateElement",
            "public void ClickElement");

        var invokeIndex = activateElement.IndexOf("Patterns.Invoke.IsSupported", StringComparison.Ordinal);
        var pointerIndex = activateElement.IndexOf("GetClickablePoint()", StringComparison.Ordinal);

        Assert.True(invokeIndex >= 0, "Activation helper must prefer the native Invoke pattern.");
        Assert.True(pointerIndex >= 0, "Activation helper must fall back to a real pointer click.");
        Assert.DoesNotContain("Patterns.SelectionItem.IsSupported", activateElement, StringComparison.Ordinal);
        Assert.DoesNotContain(".Select()", activateElement, StringComparison.Ordinal);
    }

    [Fact]
    public void ShellBackNavigationService_UsesCurrentShellBackOwner()
    {
        var service = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Services\ShellBackNavigationService.cs");
        var mainPage = LoadText(@"SalmonEgg\SalmonEgg\MainPage.xaml.cs");

        Assert.Contains("IShellBackNavigationService", service, StringComparison.Ordinal);
        Assert.Contains("public sealed class ShellBackNavigationService : IShellBackNavigationService", service, StringComparison.Ordinal);
        Assert.DoesNotContain("IShellBackNavigationService", mainPage, StringComparison.Ordinal);
        Assert.Contains("rootFrame.Content as MainPage", service, StringComparison.Ordinal);
        Assert.Contains("TryHandleGamepadBack()", service, StringComparison.Ordinal);
        Assert.Contains("public bool TryHandleGamepadBack()", mainPage, StringComparison.Ordinal);
        Assert.Contains("public bool TryGoBack()", mainPage, StringComparison.Ordinal);
        Assert.Contains("_titleBarAdapter.TryGoBack()", mainPage, StringComparison.Ordinal);
        Assert.DoesNotContain("TitleBarBackButton", service, StringComparison.Ordinal);
        Assert.DoesNotContain("ContentFrame", service, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowsRawGameControllerMapper_UsesTypedGameControllerButtonLabels()
    {
        var code = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Services\Input\WindowsRawGameControllerMapper.cs");

        Assert.Contains("GameControllerButtonLabel", code);
        Assert.DoesNotContain("ToString()", code, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowsRawGameControllerMapper_DelegatesAxisNormalizationToCorePolicy()
    {
        var code = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Services\Input\WindowsRawGameControllerMapper.cs");

        Assert.Contains("!RawGameControllerAxisNormalizer.IsAllAxesZero(axes)", code, StringComparison.Ordinal);
        Assert.Contains("RawGameControllerAxisNormalizer.NormalizeHorizontal(axes[0])", code, StringComparison.Ordinal);
        Assert.Contains("RawGameControllerAxisNormalizer.NormalizeVertical(axes[1])", code, StringComparison.Ordinal);
        Assert.DoesNotContain("axes[0] - 0.5", code, StringComparison.Ordinal);
        Assert.DoesNotContain("0.5 - axes[1]", code, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowsRawGameControllerMapper_DelegatesEightWaySwitchMappingToCorePolicy()
    {
        var code = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Services\Input\WindowsRawGameControllerMapper.cs");

        Assert.Contains("GamepadDirectionalSwitchMapper.Apply", code, StringComparison.Ordinal);
        Assert.DoesNotContain("GameControllerSwitchPosition.Up) == GameControllerSwitchPosition.Up", code, StringComparison.Ordinal);
        Assert.DoesNotContain("GameControllerSwitchPosition.Down) == GameControllerSwitchPosition.Down", code, StringComparison.Ordinal);
        Assert.DoesNotContain("GameControllerSwitchPosition.Left) == GameControllerSwitchPosition.Left", code, StringComparison.Ordinal);
        Assert.DoesNotContain("GameControllerSwitchPosition.Right) == GameControllerSwitchPosition.Right", code, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowsGamepadDiagnosticsService_DoesNotHideRawFallbackBehindInactiveStandardGamepad()
    {
        var code = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Services\Input\WindowsGamepadDiagnosticsService.cs");

        Assert.Contains("foreach (var gamepad in", code);
        Assert.Contains("foreach (var controller in", code);
        Assert.DoesNotContain("HasMatchingGamepad", code, StringComparison.Ordinal);
        Assert.DoesNotContain("RawGameController.FromGameController", code, StringComparison.Ordinal);
    }

    [Fact]
    public void GamepadDiagnosticsSnapshot_UsesTypedInputSourceContract()
    {
        var snapshot = LoadText(@"src\SalmonEgg.Presentation.Core\Services\Input\GamepadDiagnosticsSnapshot.cs");
        var viewModel = LoadText(@"src\SalmonEgg.Presentation.Core\ViewModels\Settings\GamepadDiagnosticsViewModel.cs");
        var windowsService = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Services\Input\WindowsGamepadDiagnosticsService.cs");

        Assert.Contains("GamepadDiagnosticsInputSource InputSource", snapshot, StringComparison.Ordinal);
        Assert.Contains("GamepadDiagnosticsInputSource.Gamepad", windowsService, StringComparison.Ordinal);
        Assert.Contains("GamepadDiagnosticsInputSource.RawGameController", windowsService, StringComparison.Ordinal);
        Assert.Contains("FormatInputSource(GamepadDiagnosticsInputSource inputSource)", viewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("string InputSource", snapshot, StringComparison.Ordinal);
        Assert.DoesNotContain("FormatInputSource(string", viewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("InputSource: \"", windowsService, StringComparison.Ordinal);
    }

    [Fact]
    public void MainShellGamepadNavigationDispatcher_BridgesPolledDirectionsThroughNativeGamepadKeys()
    {
        var code = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Services\Input\MainShellGamepadNavigationDispatcher.cs");
        var mainPage = LoadText(@"SalmonEgg\SalmonEgg\MainPage.xaml.cs");

        Assert.Contains("TryConsumeNavigationIntent", code);
        Assert.Contains("_nativeInputBridge.TryDispatch(intent)", code, StringComparison.Ordinal);
        Assert.Contains("ShouldSuppressPolledGamepadIntent(intent)", mainPage, StringComparison.Ordinal);
        Assert.Contains("_gamepadNavigationDispatcher.TryDispatch(intent)", mainPage, StringComparison.Ordinal);
        Assert.DoesNotContain("GamepadNavigationIntent.MoveDown => TryMoveFocus", code);
        Assert.DoesNotContain("XamlFocusManager.TryMoveFocus", code);
        Assert.DoesNotContain("GetNavigationSearchRoot()", code);
    }

    [Fact]
    public void GamepadNativeInputBridge_IsReplaceableAndPlatformBounded()
    {
        var contract = LoadText(@"src\SalmonEgg.Presentation.Core\Services\Input\IGamepadNativeInputBridge.cs");
        var noOp = LoadText(@"src\SalmonEgg.Presentation.Core\Services\Input\NoOpGamepadNativeInputBridge.cs");
        var windowsBridge = LoadText(@"SalmonEgg\SalmonEgg\Platforms\Windows\WindowsGamepadNativeInputBridge.cs");
        var dependencyInjection = LoadText(@"SalmonEgg\SalmonEgg\DependencyInjection.cs");
        var projectFile = LoadText(@"SalmonEgg\SalmonEgg\SalmonEgg.csproj");

        Assert.Contains("interface IGamepadNativeInputBridge", contract, StringComparison.Ordinal);
        Assert.Contains("bool TryDispatch(GamepadNavigationIntent intent)", contract, StringComparison.Ordinal);
        Assert.Contains("sealed class NoOpGamepadNativeInputBridge : IGamepadNativeInputBridge", noOp, StringComparison.Ordinal);
        Assert.Contains("return false;", noOp, StringComparison.Ordinal);

        Assert.Contains("sealed class WindowsGamepadNativeInputBridge : IGamepadNativeInputBridge", windowsBridge, StringComparison.Ordinal);
        Assert.DoesNotContain("VK_LEFT", windowsBridge, StringComparison.Ordinal);
        Assert.DoesNotContain("VK_UP", windowsBridge, StringComparison.Ordinal);
        Assert.DoesNotContain("VK_RIGHT", windowsBridge, StringComparison.Ordinal);
        Assert.DoesNotContain("VK_DOWN", windowsBridge, StringComparison.Ordinal);
        Assert.DoesNotContain("NavigationView", windowsBridge, StringComparison.Ordinal);
        Assert.DoesNotContain("FocusManager", windowsBridge, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedItem", windowsBridge, StringComparison.Ordinal);
        Assert.DoesNotContain("AutomationPeer", windowsBridge, StringComparison.Ordinal);
        Assert.DoesNotContain("MainPage", windowsBridge, StringComparison.Ordinal);

        Assert.Contains("services.AddSingleton<IGamepadNativeInputBridge, WindowsGamepadNativeInputBridge>();", dependencyInjection, StringComparison.Ordinal);
        Assert.Contains("services.AddSingleton<IGamepadNativeInputBridge, NoOpGamepadNativeInputBridge>();", dependencyInjection, StringComparison.Ordinal);
        Assert.Contains(@"<Compile Remove=""Platforms/Windows/**/*.cs"" />", projectFile, StringComparison.Ordinal);
    }

    [Fact]
    public void NavigationIntentConsumer_Contract_Exists_AndDispatcherRemainsControlAgnostic()
    {
        var contract = LoadText(@"src\SalmonEgg.Presentation.Core\Services\Input\INavigationIntentConsumer.cs");
        var dispatcher = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Services\Input\MainShellGamepadNavigationDispatcher.cs");

        Assert.Contains("interface INavigationIntentConsumer", contract);
        Assert.Contains("TryConsumeNavigationIntent", contract);
        Assert.Contains("INavigationIntentConsumer", dispatcher);
        Assert.DoesNotContain("ChatInputArea", dispatcher, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatInputArea_NavigationIntentSupport_PreservesKeyboardAndSlashHandlers()
    {
        var code = LoadText(@"SalmonEgg\SalmonEgg\Controls\ChatInputArea.xaml.cs");
        var policy = LoadText(@"src\SalmonEgg.Presentation.Core\Services\Input\ChatInputNavigationPolicy.cs");

        Assert.Contains("INavigationIntentConsumer", code);
        Assert.Contains("MoveUpEscapeHandler", code);
        Assert.Contains("UIElement.KeyDownEvent", code, StringComparison.Ordinal);
        Assert.Contains("_inputBoxHandledKeyDownHandler", code, StringComparison.Ordinal);
        Assert.DoesNotContain("GamepadNavigationIntent.MoveUp when focusContext == ChatInputFocusContext.ModeSelector", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("InputBox.IsEnabled && ViewModel.IsInputEnabled", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatInputArea_CodeBehind_UsesGamepadShortcutConsumer_WithoutExpandingNavigationEnum()
    {
        var code = LoadText(@"SalmonEgg\SalmonEgg\Controls\ChatInputArea.xaml.cs");
        var navigationEnum = LoadText(@"src\SalmonEgg.Presentation.Core\Services\Input\GamepadNavigationIntent.cs");

        Assert.Contains("IGamepadShortcutConsumer", code, StringComparison.Ordinal);
        Assert.Contains("TryConsumeShortcutIntent", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ToggleVoiceInput", navigationEnum, StringComparison.Ordinal);
    }

    [Fact]
    public void MiniChatView_UsesFocusedShortcutConsumerForVoiceInput()
    {
        var code = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Views\MiniWindow\MiniChatView.xaml.cs");
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\MiniWindow\MiniChatView.xaml");

        Assert.Contains("IGamepadShortcutConsumer", code, StringComparison.Ordinal);
        Assert.Contains("TryConsumeShortcutIntent", code, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"MiniChatInputBox\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatInputArea_ComposerDirectionalNavigation_UsesNativeBoundaryAnchorsForActions()
    {
        var code = LoadText(@"SalmonEgg\SalmonEgg\Controls\ChatInputArea.xaml.cs");

        Assert.Contains("trailingSelector.XYFocusRight = leadingActionButton;", code, StringComparison.Ordinal);
        Assert.Contains("leadingActionButton.XYFocusLeft = trailingSelector;", code, StringComparison.Ordinal);
        Assert.Contains("RegisterPropertyChangedCallback(UIElement.VisibilityProperty", code, StringComparison.Ordinal);
        Assert.DoesNotContain("RegisterPropertyChangedCallback(Control.IsEnabledProperty", code, StringComparison.Ordinal);
        Assert.DoesNotContain("FocusManager.TryMoveFocus", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ValueSelectors_UseNativeFocusEngagementForGamepadTraversal()
    {
        var root = Path.Combine(FindRepoRoot(), "SalmonEgg", "SalmonEgg");
        var failures = new List<string>();

        foreach (var xamlFile in Directory.EnumerateFiles(root, "*.xaml", SearchOption.AllDirectories))
        {
            if (xamlFile.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                || xamlFile.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var document = XDocument.Parse(File.ReadAllText(xamlFile));
            foreach (var control in document.Descendants().Where(IsValueSelectorRequiringFocusEngagement))
            {
                if (string.Equals(control.Attribute("IsFocusEngagementEnabled")?.Value, "True", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var id = control.Attribute(XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml") + "Name")?.Value
                         ?? control.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "AutomationProperties.AutomationId")?.Value
                         ?? control.Attribute(XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml") + "Uid")?.Value
                         ?? "<unnamed>";
                failures.Add($"{Path.GetRelativePath(FindRepoRoot(), xamlFile)} {control.Name.LocalName} {id}");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static bool IsValueSelectorRequiringFocusEngagement(XElement element)
    {
        return element.Name.LocalName is "ComboBox" or "NumberBox";
    }

    [Fact]
    public void NumberBoxes_UseSystemFocusVisuals_ForGamepadFocusVisibility()
    {
        var root = Path.Combine(FindRepoRoot(), "SalmonEgg", "SalmonEgg");
        var failures = new List<string>();

        foreach (var xamlFile in Directory.EnumerateFiles(root, "*.xaml", SearchOption.AllDirectories))
        {
            if (xamlFile.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                || xamlFile.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var document = XDocument.Parse(File.ReadAllText(xamlFile));
            foreach (var control in document.Descendants().Where(element => element.Name.LocalName == "NumberBox"))
            {
                if (string.Equals(control.Attribute("UseSystemFocusVisuals")?.Value, "True", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var id = control.Attribute(XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml") + "Name")?.Value
                         ?? control.Attribute(XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml") + "Uid")?.Value
                         ?? "<unnamed>";
                failures.Add($"{Path.GetRelativePath(FindRepoRoot(), xamlFile)} NumberBox {id}");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void SettingsPages_DoNotUseMenuFlyoutSeparator_AsSectionDividers()
    {
        var xamlFiles = new[]
        {
            @"SalmonEgg\SalmonEgg\Presentation\Views\GeneralSettingsPage.xaml",
            @"SalmonEgg\SalmonEgg\Presentation\Views\Settings\DataStorageSettingsPage.xaml",
            @"SalmonEgg\SalmonEgg\Presentation\Views\Settings\McpSettingsPage.xaml"
        };

        foreach (var relativePath in xamlFiles)
        {
            var xaml = LoadXaml(relativePath);
            Assert.DoesNotContain("MenuFlyoutSeparator", xaml, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ShortcutRecorder_TracksModifiersWithoutPlatformKeyStateFallback()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Controls\ShortcutRecorder.xaml");
        var code = LoadText(@"SalmonEgg\SalmonEgg\Controls\ShortcutRecorder.xaml.cs");

        Assert.Contains("PreviewKeyDown=\"OnRecorderButtonPreviewKeyDown\"", xaml, StringComparison.Ordinal);
        Assert.Contains("KeyUp=\"OnRecorderButtonKeyUp\"", xaml, StringComparison.Ordinal);
        Assert.Contains("_pressedModifiers", code, StringComparison.Ordinal);
        Assert.Contains("UpdatePressedModifier(e.Key, isDown: true)", code, StringComparison.Ordinal);
        Assert.Contains("UpdatePressedModifier(e.Key, isDown: false)", code, StringComparison.Ordinal);
        Assert.Contains("_pressedModifiers = AppShortcutModifiers.None;", code, StringComparison.Ordinal);
        Assert.Contains("partial void AttachSystemKeyCapture()", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.UI.Input", code, StringComparison.Ordinal);
        Assert.DoesNotContain("InputKeyboardSource", code, StringComparison.Ordinal);
        Assert.DoesNotContain("CoreVirtualKeyStates", code, StringComparison.Ordinal);

        var modifierLookup = ExtractSection(code, "private AppShortcutModifiers GetCurrentModifiers()", "private void UpdatePressedModifier");
        Assert.Contains("=> _pressedModifiers;", modifierLookup, StringComparison.Ordinal);
    }

    [Fact]
    public void MainPage_KeepsWindowsTrayImplementationInPlatformPartial()
    {
        var sharedPage = LoadText(@"SalmonEgg\SalmonEgg\MainPage.xaml.cs");
        var windowsPage = LoadText(@"SalmonEgg\SalmonEgg\Platforms\Windows\MainPage.Windows.cs");

        Assert.Contains("partial void InitializeTray();", sharedPage, StringComparison.Ordinal);
        Assert.Contains("partial void DisposePlatformTray();", sharedPage, StringComparison.Ordinal);
        Assert.DoesNotContain("TrayIconManager", sharedPage, StringComparison.Ordinal);
        Assert.DoesNotContain("AppWindowClosingEventArgs", sharedPage, StringComparison.Ordinal);
        Assert.Contains("TrayIconManager", windowsPage, StringComparison.Ordinal);
        Assert.Contains("AppWindowClosingEventArgs", windowsPage, StringComparison.Ordinal);
    }

    [Fact]
    public void MainPage_WindowsDebugKeyboardProbe_UsesKeyDownInsteadOfSystemKeyDown()
    {
        var windowsPage = LoadText(@"SalmonEgg\SalmonEgg\Platforms\Windows\MainPage.Windows.cs");

        Assert.Contains("InputKeyboardSource.GetForIsland", windowsPage, StringComparison.Ordinal);
        Assert.Contains("_debugKeyboardSource.KeyDown -= OnDebugKeyDown;", windowsPage, StringComparison.Ordinal);
        Assert.Contains("_debugKeyboardSource.KeyDown += OnDebugKeyDown;", windowsPage, StringComparison.Ordinal);
        Assert.Contains("private static void OnDebugKeyDown", windowsPage, StringComparison.Ordinal);
        Assert.Contains("partial void AttachPlatformGamepadDirectionalBridge()", windowsPage, StringComparison.Ordinal);
        Assert.Contains("partial void DetachPlatformGamepadDirectionalBridge()", windowsPage, StringComparison.Ordinal);
        Assert.Contains("OnPlatformGamepadDirectionalBridgeKeyDown", windowsPage, StringComparison.Ordinal);
        Assert.Contains("Windows.System.VirtualKey.GamepadDPadRight", windowsPage, StringComparison.Ordinal);
        Assert.Contains("TryMoveFocusFromMainNavigationIntoCurrentContent()", windowsPage, StringComparison.Ordinal);
        Assert.DoesNotContain("SystemKeyDown", windowsPage, StringComparison.Ordinal);
    }

    [Fact]
    public void MainPage_GamepadDirectionalBridge_RemainsPlatformBounded()
    {
        var sharedPage = LoadText(@"SalmonEgg\SalmonEgg\MainPage.xaml.cs");
        var windowsPage = LoadText(@"SalmonEgg\SalmonEgg\Platforms\Windows\MainPage.Windows.cs");

        Assert.Contains("partial void AttachPlatformGamepadDirectionalBridge();", sharedPage, StringComparison.Ordinal);
        Assert.Contains("partial void DetachPlatformGamepadDirectionalBridge();", sharedPage, StringComparison.Ordinal);
        Assert.DoesNotContain("InputKeyboardSource", sharedPage, StringComparison.Ordinal);
        Assert.DoesNotContain("Windows.System.VirtualKey.GamepadDPadRight", sharedPage, StringComparison.Ordinal);
        Assert.Contains("InputKeyboardSource", windowsPage, StringComparison.Ordinal);
        Assert.Contains("Windows.System.VirtualKey.GamepadDPadRight", windowsPage, StringComparison.Ordinal);
    }

    [Fact]
    public void MainPage_WindowsPlatformBridge_DelegatesToFocusedConsumerBeforeShellFallbacks()
    {
        var windowsPage = LoadText(@"SalmonEgg\SalmonEgg\Platforms\Windows\MainPage.Windows.cs");
        var dispatcher = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Services\Input\MainShellGamepadNavigationDispatcher.cs");
        var contract = LoadText(@"src\SalmonEgg.Presentation.Core\Services\Input\IGamepadNavigationDispatcher.cs");
        var keyDownHandler = ExtractSection(
            windowsPage,
            "private void OnPlatformGamepadDirectionalBridgeKeyDown",
            "private bool ShouldSuppressPolledGamepadIntentForWindows");

        Assert.Contains("bool TryDispatchWithoutNativeFallback(GamepadNavigationIntent intent);", contract, StringComparison.Ordinal);
        Assert.Contains("public bool TryDispatchWithoutNativeFallback(GamepadNavigationIntent intent)", dispatcher, StringComparison.Ordinal);
        Assert.Contains("TryDispatchCore(intent, allowNativeFallback: false)", dispatcher, StringComparison.Ordinal);
        Assert.Contains("if (args.Handled", keyDownHandler, StringComparison.Ordinal);
        Assert.Contains("case Windows.System.VirtualKey.GamepadDPadRight:", windowsPage, StringComparison.Ordinal);
        Assert.Contains("IsFocusWithinMainNavigation() && TryMoveFocusFromMainNavigationIntoCurrentContent()", windowsPage, StringComparison.Ordinal);
        Assert.Contains("TryMoveFocusFromMainNavigationIntoCurrentContent()", windowsPage, StringComparison.Ordinal);
        Assert.Contains("case Windows.System.VirtualKey.GamepadDPadUp:", windowsPage, StringComparison.Ordinal);
        Assert.Contains("TryDispatchWithoutNativeFallback(GamepadNavigationIntent.MoveUp)", windowsPage, StringComparison.Ordinal);
        Assert.Contains("case Windows.System.VirtualKey.GamepadDPadDown:", windowsPage, StringComparison.Ordinal);
        Assert.Contains("TryDispatchWithoutNativeFallback(GamepadNavigationIntent.MoveDown)", windowsPage, StringComparison.Ordinal);
        Assert.Contains("case Windows.System.VirtualKey.GamepadB:", windowsPage, StringComparison.Ordinal);
        Assert.Contains("_virtualGamepadNavigationDispatcher?.TryDispatchWithoutNativeFallback(GamepadNavigationIntent.Back)", windowsPage, StringComparison.Ordinal);
        Assert.DoesNotContain("case Windows.System.VirtualKey.GamepadDPadLeft:", keyDownHandler, StringComparison.Ordinal);
        Assert.DoesNotContain("case Windows.System.VirtualKey.GamepadA:", keyDownHandler, StringComparison.Ordinal);
        Assert.DoesNotContain("XamlFocusManager.TryMoveFocus", windowsPage, StringComparison.Ordinal);
        Assert.DoesNotContain("AutomationPeer", windowsPage, StringComparison.Ordinal);
        Assert.DoesNotContain("TryConsumeFocusedNavigationIntent(intent.Value)", windowsPage, StringComparison.Ordinal);
    }

    [Fact]
    public void MainPage_ContentEntryFocus_UsesSharedPrimaryContentTargetContract()
    {
        var sharedPage = LoadText(@"SalmonEgg\SalmonEgg\MainPage.xaml.cs");
        var contract = LoadText(@"src\SalmonEgg.Presentation.Core\Services\Input\IPrimaryContentFocusTarget.cs");
        var chatView = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Views\Chat\ChatView.xaml.cs");
        var titleBarAdapter = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Navigation\MainWindowTitleBarAdapter.cs");

        Assert.Contains("interface IPrimaryContentFocusTarget", contract, StringComparison.Ordinal);
        Assert.Contains("ContentFrame.Content is IPrimaryContentFocusTarget focusTarget", sharedPage, StringComparison.Ordinal);
        Assert.DoesNotContain("ContentFrame.Content is SalmonEgg.Presentation.Views.Chat.ChatView chatView", sharedPage, StringComparison.Ordinal);
        Assert.Contains("public sealed partial class ChatView : Page, INavigationIntentConsumer, IGamepadContextIntentConsumer, IPrimaryContentFocusTarget", chatView, StringComparison.Ordinal);
        Assert.Contains("IsDescendantOf(current, ContentFrame)", sharedPage, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(current, MainNavView)", sharedPage, StringComparison.Ordinal);
        Assert.DoesNotContain("SyncShellSelectionFromCurrentContent", sharedPage, StringComparison.Ordinal);
        Assert.Contains("consumer.TryConsumeNavigationIntent(GamepadNavigationIntent.Back)", titleBarAdapter, StringComparison.Ordinal);
        Assert.Contains("_ = _navigationCoordinator.ActivateStartAsync();", titleBarAdapter, StringComparison.Ordinal);
    }

    [Fact]
    public void StartView_HeroSuggestions_UseStableButtonIdsInsteadOfListSelectionState()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Start\StartView.xaml");
        var code = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Views\Start\StartView.xaml.cs");
        var suggestionVm = LoadText(@"src\SalmonEgg.Presentation.Core\ViewModels\Start\QuickSuggestionViewModel.cs");
        var startVm = LoadText(@"src\SalmonEgg.Presentation.Core\ViewModels\Start\StartViewModel.cs");

        Assert.Contains("ItemsControl x:Name=\"HeroSuggestionsHost\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<ListView x:Name=\"HeroSuggestionsList\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"{x:Bind AutomationId, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IPrimaryContentFocusTarget", code, StringComparison.Ordinal);
        Assert.Contains("FindSuggestionButton(ViewModel.Suggestions[0].AutomationId)", code, StringComparison.Ordinal);
        Assert.Contains("promptBox.XYFocusUp = firstSuggestion;", code, StringComparison.Ordinal);
        Assert.Contains("button.XYFocusDown = promptFocusTarget;", code, StringComparison.Ordinal);
        Assert.Contains("button.ClearValue(Control.XYFocusDownProperty);", code, StringComparison.Ordinal);
        Assert.DoesNotContain("TryConsumeNavigationIntent", code, StringComparison.Ordinal);
        Assert.DoesNotContain("TryMoveFocusedSuggestion", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ResolveFocusedSuggestionIndex", code, StringComparison.Ordinal);
        Assert.DoesNotContain("TryActivateSelectedHeroSuggestion", code, StringComparison.Ordinal);
        Assert.DoesNotContain("HeroSuggestionsList.SelectedIndex", code, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateSlug(", suggestionVm, StringComparison.Ordinal);
        Assert.Contains("StartView.Suggestion.AnalyzeCodebase", startVm, StringComparison.Ordinal);
        Assert.Contains("StartView.Suggestion.RecommendTasks", startVm, StringComparison.Ordinal);
        Assert.Contains("StartView.Suggestion.ResolveErrors", startVm, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowMetricsProvider_DoesNotExposeAppWindowTitleBar()
    {
        var provider = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Services\WindowMetricsProvider.cs");
        var titleBarAdapter = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Navigation\MainWindowTitleBarAdapter.cs");

        Assert.Contains("ITitleBarInsetProvider", provider, StringComparison.Ordinal);
        Assert.DoesNotContain("AppWindowTitleBar", provider, StringComparison.Ordinal);
        Assert.DoesNotContain("AppWindowTitleBar =>", titleBarAdapter, StringComparison.Ordinal);
        Assert.Contains("ITitleBarInsetProvider", titleBarAdapter, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsShellPage_UsesNativeXyFocusWithoutPageLevelGamepadTraversal()
    {
        var code = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Views\SettingsShellPage.xaml.cs");
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\SettingsShellPage.xaml");
        var document = XDocument.Parse(xaml);
        var pageBase = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Views\SettingsPageBase.cs");
        var acpPage = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\AcpConnectionSettingsPage.xaml.cs");
        var diagnosticsPage = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\DiagnosticsSettingsPage.xaml.cs");
        var shellGrid = document
            .Descendants()
            .Single(element => element.Name.LocalName == "Grid"
                && string.Equals(element.Attribute("Padding")?.Value, "40,24", StringComparison.Ordinal));

        Assert.Contains("SettingsShellPage : Page, IPrimaryContentFocusTarget", code, StringComparison.Ordinal);
        Assert.Contains("public bool TryFocusPrimaryContentTarget()", code, StringComparison.Ordinal);
        Assert.Contains("=> TryFocusCurrentSectionNavigationItem();", code, StringComparison.Ordinal);
        Assert.Contains("SettingsNavView.ContainerFromMenuItem(ViewModel.SelectedSection)", code, StringComparison.Ordinal);
        Assert.Equal("Enabled", shellGrid.Attribute("XYFocusKeyboardNavigation")?.Value);
        Assert.Contains("XYFocusKeyboardNavigation=\"Enabled\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"SettingsNavView\"", xaml, StringComparison.Ordinal);
        Assert.Contains("navItem.XYFocusDown = sectionEntryTarget;", code, StringComparison.Ordinal);
        Assert.Contains("returnTarget.XYFocusUp = navItem;", code, StringComparison.Ordinal);
        Assert.Contains("protected virtual Control? GetSectionEntryFocusTarget()", pageBase, StringComparison.Ordinal);
        Assert.Contains("protected virtual IEnumerable<Control?> GetSectionFocusReturnTargets()", pageBase, StringComparison.Ordinal);
        Assert.Contains("if (!TryRefreshCurrentSectionFocusTargets())", code, StringComparison.Ordinal);
        Assert.Contains("settingsPage.Loaded += OnDeferredFocusTargetRefreshLoaded;", code, StringComparison.Ordinal);
        Assert.Contains("DetachDeferredFocusTargetRefresh(settingsPage);", code, StringComparison.Ordinal);
        Assert.DoesNotContain("LayoutUpdated", code, StringComparison.Ordinal);
        Assert.DoesNotContain("TryConsumeNavigationIntent", code, StringComparison.Ordinal);
        Assert.DoesNotContain("TryMoveFocusWithinSettingsContent", code, StringComparison.Ordinal);
        Assert.DoesNotContain("IsFocusOnFirstSettingsContentControl", code, StringComparison.Ordinal);
        Assert.DoesNotContain("GetInteractiveControlsInTraversalOrder", code, StringComparison.Ordinal);
        Assert.DoesNotContain("FindDescendants<Control>", code, StringComparison.Ordinal);
        Assert.DoesNotContain("control is ComboBox or NumberBox or ToggleSwitch or TextBox or Button or Expander", code, StringComparison.Ordinal);
        Assert.DoesNotContain("selectedItem.Focus(FocusState.Keyboard)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("SettingsNavView.Focus(", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Focus(FocusState.Programmatic)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("INavigationIntentConsumer", acpPage, StringComparison.Ordinal);
        Assert.DoesNotContain("INavigationIntentConsumer", diagnosticsPage, StringComparison.Ordinal);
        Assert.DoesNotContain("_lastFocusedGamepadActionButton", diagnosticsPage, StringComparison.Ordinal);
        Assert.DoesNotContain("ViewModel.SelectedSection.Key == SettingsSectionCatalog.AgentAcpKey", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ViewModel.SelectedSection.Key == SettingsSectionCatalog.McpKey", code, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsPages_KeepSectionTraversalOnNativeDirectionalNavigation()
    {
        var diagnosticsXaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\DiagnosticsSettingsPage.xaml");
        var appearanceXaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\AppearanceSettingsPage.xaml");
        var pageBase = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Views\SettingsPageBase.cs");
        var diagnosticsCode = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\DiagnosticsSettingsPage.xaml.cs");
        var mcpCode = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\McpSettingsPage.xaml.cs");
        var aboutCode = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\AboutPage.xaml.cs");

        Assert.Contains("protected Control? FirstAvailableSectionEntryTarget", pageBase, StringComparison.Ordinal);
        Assert.Contains("GetSectionFocusReturnTargets", pageBase, StringComparison.Ordinal);

        Assert.DoesNotContain("XYFocusUp=", diagnosticsXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("XYFocusDown=", diagnosticsXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("XYFocusUp=", appearanceXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("XYFocusDown=", appearanceXaml, StringComparison.Ordinal);

        Assert.Contains("FirstAvailableSectionEntryTarget(", diagnosticsCode, StringComparison.Ordinal);
        Assert.Contains("FirstAvailableSectionEntryTarget(", mcpCode, StringComparison.Ordinal);
        Assert.Contains("GetSectionFocusReturnTargets()", mcpCode, StringComparison.Ordinal);
        Assert.Contains("FirstAvailableSectionEntryTarget(", aboutCode, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowsDpapiSecureStorage_DoesNotOverwriteUndecryptableCiphertextAsLegacyText()
    {
        var code = LoadText(@"SalmonEgg\SalmonEgg\Platforms\Windows\WindowsDpapiSecureStorage.cs");

        Assert.Contains("TryDecodeLegacyPlainText", code, StringComparison.Ordinal);
        Assert.Contains("throwOnInvalidBytes: true", code, StringComparison.Ordinal);
        Assert.Contains("IsPlausibleLegacySecret", code, StringComparison.Ordinal);
        Assert.DoesNotContain("var legacyValue = Encoding.UTF8.GetString(bytes);", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ResizeGrip_KeepsPlatformCursorImplementationOutOfSharedControl()
    {
        var sharedControl = LoadText(@"SalmonEgg\SalmonEgg\Controls\ResizeGrip.cs");
        var windowsImplementation = LoadText(@"SalmonEgg\SalmonEgg\Platforms\Windows\ResizeGrip.Windows.cs");

        Assert.Contains("partial void ApplyPlatformCursor()", sharedControl, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.UI.Input", sharedControl, StringComparison.Ordinal);
        Assert.DoesNotContain("InputSystemCursor", sharedControl, StringComparison.Ordinal);
        Assert.Contains("InputSystemCursor.Create", windowsImplementation, StringComparison.Ordinal);
    }


    private static string LoadXaml(string relativePath)
    {
        return LoadText(relativePath);
    }

    private static string LoadText(string relativePath)
    {
        var root = FindRepoRoot();
        var fullPath = Path.Combine(root, NormalizeRelativePath(relativePath));
        return File.ReadAllText(fullPath);
    }

    private static string[] EnumerateProductCSharpFiles()
    {
        var root = FindRepoRoot();
        var sourceRoots = new[]
        {
            Path.Combine(root, "src"),
            Path.Combine(root, "SalmonEgg", "SalmonEgg")
        };

        return sourceRoots
            .Where(Directory.Exists)
            .SelectMany(sourceRoot => Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private static string ExtractSection(string content, string startMarker, string? endMarker = null)
    {
        var start = content.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Unable to locate marker '{startMarker}'.");

        var end = endMarker is null
            ? content.Length
            : content.IndexOf(endMarker, start, StringComparison.Ordinal);
        if (end < 0)
        {
            end = content.Length;
        }

        return content.Substring(start, end - start);
    }

    private static void AssertTitleBarCommandTargetsMainNavigationOnGamepadDown(string xaml, string controlName)
    {
        var controlSection = ExtractSection(
            xaml,
            $"x:Name=\"{controlName}\"",
            ">");

        Assert.Contains("XYFocusDown=\"{x:Bind MainNavView, Mode=OneWay}\"", controlSection, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SalmonEgg.sln")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root (SalmonEgg.sln) not found.");
    }

    private static XElement FindElementByName(string relativePath, string elementName)
    {
        var document = XDocument.Parse(LoadXaml(relativePath));
        var element = document.Descendants().FirstOrDefault(candidate =>
            candidate.Attributes().Any(attribute =>
                string.Equals(attribute.Name.LocalName, "Name", StringComparison.Ordinal)
                && string.Equals(attribute.Value, elementName, StringComparison.Ordinal)));
        if (element is null)
        {
            throw new InvalidOperationException($"Element '{elementName}' not found in XAML '{relativePath}'.");
        }

        return element;
    }

    private static XElement FindElementByUid(XDocument document, string uid)
    {
        var element = document.Descendants().FirstOrDefault(candidate =>
            candidate.Attributes().Any(attribute =>
                string.Equals(attribute.Name.LocalName, "Uid", StringComparison.Ordinal)
                && string.Equals(attribute.Value, uid, StringComparison.Ordinal)));
        if (element is null)
        {
            throw new InvalidOperationException($"Element with x:Uid '{uid}' not found.");
        }

        return element;
    }

    private static void AssertTitleBarStyleKeepsNativeRoundedBorderlessAppearance(
        XDocument document,
        string styleKey,
        string expectedCornerRadius)
    {
        var xNamespace = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var style = document.Descendants()
            .FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "Style", StringComparison.Ordinal)
                && string.Equals(element.Attribute(xNamespace + "Key")?.Value, styleKey, StringComparison.Ordinal));

        Assert.NotNull(style);
        Assert.Contains(style!.Descendants().Where(element => string.Equals(element.Name.LocalName, "Setter", StringComparison.Ordinal)),
            setter => string.Equals(setter.Attribute("Property")?.Value, "CornerRadius", StringComparison.Ordinal)
                && string.Equals(setter.Attribute("Value")?.Value, expectedCornerRadius, StringComparison.Ordinal));
        Assert.Contains(style.Descendants().Where(element => string.Equals(element.Name.LocalName, "Setter", StringComparison.Ordinal)),
            setter => string.Equals(setter.Attribute("Property")?.Value, "BorderBrush", StringComparison.Ordinal)
                && string.Equals(setter.Attribute("Value")?.Value, "Transparent", StringComparison.Ordinal));
        Assert.Contains(style.Descendants().Where(element => string.Equals(element.Name.LocalName, "Setter", StringComparison.Ordinal)),
            setter => string.Equals(setter.Attribute("Property")?.Value, "BorderThickness", StringComparison.Ordinal)
                && string.Equals(setter.Attribute("Value")?.Value, "0", StringComparison.Ordinal));
    }

    private static string NormalizeRelativePath(string relativePath)
        => relativePath.Replace('\\', Path.DirectorySeparatorChar);

    private static int CountOccurrences(string value, string fragment)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(fragment, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += fragment.Length;
        }

        return count;
    }

    private static bool HasAttributeByLocalName(XElement element, string localName)
        => element.Attributes().Any(attribute => string.Equals(attribute.Name.LocalName, localName, StringComparison.Ordinal));

    private static bool IsUserVisibleTextAttribute(XAttribute attribute)
    {
        if (attribute.Name.LocalName is not ("Text" or "Content" or "Header" or "PlaceholderText" or "ToolTip" or "Name"))
        {
            return false;
        }

        return attribute.Name.LocalName != "Name"
            || attribute.Name.NamespaceName.EndsWith("/automation", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHardcodedUserVisibleLiteral(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith('{')
            || trimmed.StartsWith("&#x", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("ms-appx://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("/", StringComparison.Ordinal)
            || trimmed.All(char.IsDigit))
        {
            return false;
        }

        return true;
    }

    private static bool IsVisibleLiteralWhitelist(string xamlFile, XElement element, XAttribute attribute)
    {
        var fileName = Path.GetFileName(xamlFile);
        var elementName = element.Name.LocalName;
        var value = attribute.Value;

        return elementName is "FontIcon" or "SymbolIcon"
            || string.Equals(value, "Icon", StringComparison.Ordinal)
            || string.Equals(value, "boot", StringComparison.Ordinal)
            || string.Equals(value, "inactive", StringComparison.Ordinal)
            || string.Equals(fileName, "ChatInputArea.xaml", StringComparison.OrdinalIgnoreCase)
                && attribute.Name.LocalName == "Content"
                && value.Length <= 2;
    }

    private static string GetResourceValue(XDocument resources, string name)
    {
        var value = resources.Descendants("data")
            .FirstOrDefault(data => string.Equals((string?)data.Attribute("name"), name, StringComparison.Ordinal))
            ?.Element("value")
            ?.Value;

        Assert.False(string.IsNullOrWhiteSpace(value), $"Resource '{name}' must define a non-empty value.");
        return value!;
    }

    private static string? GetAttributeByLocalName(XElement element, string localName)
        => element.Attributes()
            .FirstOrDefault(attribute => string.Equals(attribute.Name.LocalName, localName, StringComparison.Ordinal))
            ?.Value;

    private static bool HasXUid(XElement element, string expectedValue)
        => string.Equals(GetAttributeByLocalName(element, "Uid"), expectedValue, StringComparison.Ordinal);
}
