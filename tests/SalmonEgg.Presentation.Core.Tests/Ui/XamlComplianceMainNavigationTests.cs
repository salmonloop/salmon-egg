using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

using SalmonEgg.Presentation.Core.Services.Input;

namespace SalmonEgg.Presentation.Core.Tests.Ui;

using static SalmonEgg.Presentation.Core.Tests.Ui.XamlComplianceTestHelpers;

public sealed class XamlComplianceMainNavigationTests
{

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
    public void MainPage_ProjectContextFlyout_IsScopedToContentLeaf_NotHierarchicalItem()
    {
        // Architecture lock: hierarchical NavigationViewItem owns MenuItemsHost for sessions.
        // Project ContextFlyout must live on the content leaf so session right-click cannot
        // also open the project menu (Uno 6.5.x Skia ContextRequested bubbling defect
        // unoplatform/uno#23440, plus correct WinUI ownership either way).
        var document = XDocument.Parse(LoadXaml(@"SalmonEgg\SalmonEgg\MainPage.xaml"));
        var xNamespace = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");

        var projectTemplate = document
            .Descendants()
            .Single(element =>
                string.Equals(element.Name.LocalName, "DataTemplate", StringComparison.Ordinal)
                && string.Equals(element.Attribute(xNamespace + "Key")?.Value, "ProjectNavTemplate", StringComparison.Ordinal));
        var sessionTemplate = document
            .Descendants()
            .Single(element =>
                string.Equals(element.Name.LocalName, "DataTemplate", StringComparison.Ordinal)
                && string.Equals(element.Attribute(xNamespace + "Key")?.Value, "SessionNavTemplate", StringComparison.Ordinal));

        var projectNavItem = projectTemplate
            .Descendants()
            .Single(element => string.Equals(element.Name.LocalName, "NavigationViewItem", StringComparison.Ordinal));
        var sessionNavItem = sessionTemplate
            .Descendants()
            .Single(element => string.Equals(element.Name.LocalName, "NavigationViewItem", StringComparison.Ordinal));

        // Property-element ContextFlyout under NVI itself is forbidden for hierarchical hosts.
        // XAML property elements are named "NavigationViewItem.ContextFlyout" / "Grid.ContextFlyout".
        Assert.DoesNotContain(
            projectNavItem.Elements(),
            element => element.Name.LocalName.EndsWith(".ContextFlyout", StringComparison.Ordinal)
                || string.Equals(element.Name.LocalName, "ContextFlyout", StringComparison.Ordinal));

        var contentProperty = projectNavItem
            .Elements()
            .SingleOrDefault(element =>
                string.Equals(element.Name.LocalName, "NavigationViewItem.Content", StringComparison.Ordinal)
                || string.Equals(element.Name.LocalName, "Content", StringComparison.Ordinal));
        Assert.NotNull(contentProperty);

        var contentGrid = contentProperty!
            .Elements()
            .FirstOrDefault(element => string.Equals(element.Name.LocalName, "Grid", StringComparison.Ordinal));
        Assert.NotNull(contentGrid);
        Assert.Contains(
            contentGrid!.Elements(),
            element => element.Name.LocalName.EndsWith(".ContextFlyout", StringComparison.Ordinal)
                || string.Equals(element.Name.LocalName, "ContextFlyout", StringComparison.Ordinal));
        Assert.Contains(
            "ProjectNavNewSessionItem",
            contentGrid.ToString(SaveOptions.DisableFormatting),
            StringComparison.Ordinal);

        // Session leaf may keep ContextFlyout on the NavigationViewItem itself — it is not a
        // hierarchical menu host for further ContextFlyout parents in our tree.
        Assert.Contains(
            sessionNavItem.Elements(),
            element => element.Name.LocalName.EndsWith(".ContextFlyout", StringComparison.Ordinal)
                || string.Equals(element.Name.LocalName, "ContextFlyout", StringComparison.Ordinal));
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
        Assert.DoesNotContain("x:Uid=\"SessionNavMoveItem\"", xaml);
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
    public void DiscoverSessionsPage_ProfileTransportChipsUseFluentAccentThemePattern()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Discover\DiscoverSessionsPage.xaml");

        // Profiles list and session rows share the Fluent soft-accent chip used on
        // AcpConnectionSettingsPage (AccentFill + low Opacity + AccentBrush icon).
        Assert.Contains("Background=\"{ThemeResource AccentFillColorDefaultBrush}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Foreground=\"{ThemeResource AccentBrush}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SystemControlBackgroundAccentBrush", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("TextOnAccentFillColorPrimaryBrush", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Opacity=\"0.8\"", xaml, StringComparison.Ordinal);
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

        // FlyoutBase.AttachedFlyout is legitimately used elsewhere (the add-project entry's
        // source-chooser menu), so that one negative assertion is scoped to the search box.
        // The remaining markers are search-only hacks with no legitimate use anywhere in the
        // page, so they stay whole-file scans to also catch any sibling-element placement.
        var searchBox = ExtractSection(xaml, "<AutoSuggestBox x:Name=\"TopSearchBox\"", "</AutoSuggestBox>");
        Assert.DoesNotContain("FlyoutBase.AttachedFlyout", searchBox, StringComparison.Ordinal);
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
        Assert.DoesNotContain("AttachPlatformGamepadDirectionalBridge", windowsPage, StringComparison.Ordinal);
        Assert.DoesNotContain("OnPlatformGamepadDirectionalBridgeKeyDown", windowsPage, StringComparison.Ordinal);
        Assert.DoesNotContain("Windows.System.VirtualKey.GamepadDPadRight", windowsPage, StringComparison.Ordinal);
        Assert.DoesNotContain("SystemKeyDown", windowsPage, StringComparison.Ordinal);
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
        Assert.Contains("_ = _navigationViewModel.ActivateStartAsync();", titleBarAdapter, StringComparison.Ordinal);
    }
}
