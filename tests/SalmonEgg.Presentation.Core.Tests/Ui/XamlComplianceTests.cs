using System;
using System.IO;
using System.Linq;
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
    public void MainPage_DoesNotDisableFocusOnInteraction()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\MainPage.xaml");

        Assert.DoesNotContain("AllowFocusOnInteraction=\"False\"", xaml);
    }

    [Theory]
    [InlineData("TitleBarBackButton")]
    [InlineData("TitleBarToggleLeftNavButton")]
    [InlineData("DiffPanelButton")]
    [InlineData("TodoPanelButton")]
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

        Assert.Equal("{x:Bind NavVM.MenuItems, Mode=OneWay}", mainNavView.Attribute("MenuItemsSource")?.Value);
        Assert.Equal("{x:Bind NavVM.FooterMenuItems, Mode=OneWay}", mainNavView.Attribute("FooterMenuItemsSource")?.Value);
        Assert.Equal("{x:Bind ChildrenMenuItems, Mode=OneWay}", projectNavItem.Attribute("MenuItemsSource")?.Value);
        Assert.Equal("{x:Bind IsExpanded, Mode=TwoWay}", projectNavItem.Attribute("IsExpanded")?.Value);
        Assert.DoesNotContain("MenuItemsSource=\"{x:Bind NavVM.Items, Mode=OneWay}\"", xaml);
        Assert.DoesNotContain("FooterMenuItemsSource=\"{x:Bind NavVM.FooterItems, Mode=OneWay}\"", xaml);
        Assert.DoesNotContain("MenuItemsSource=\"{x:Bind Children, Mode=OneWay}\"", xaml);
        Assert.DoesNotContain("IsExpanded=\"{x:Bind IsExpanded, Mode=OneWay}\"", xaml);
        Assert.DoesNotContain("Expanding=\"OnMainNavItemExpanding\"", xaml);
        Assert.DoesNotContain("Collapsed=\"OnMainNavItemCollapsed\"", xaml);
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
        Assert.Contains("x:Uid=\"DiffPanelPlaceholder\"", xaml);
        Assert.Contains("x:Uid=\"PlanEmptyTitle\"", xaml);
        Assert.Contains("x:Uid=\"PlanEmptySubtitle\"", xaml);
    }

    [Fact]
    public void MainPage_RightPanelExposesAutomationAnchorsForSmoke()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\MainPage.xaml");

        Assert.Contains("x:Name=\"RightPanelColumn\"", xaml);
        Assert.Contains("AutomationProperties.AutomationId=\"RightPanel.Root\"", xaml);
        Assert.Contains("x:Name=\"RightPanelTitle\"", xaml);
        Assert.Contains("AutomationProperties.AutomationId=\"RightPanel.Title\"", xaml);
        Assert.Contains("AutomationProperties.AutomationId=\"RightPanel.TodoEmptyTitle\"", xaml);
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

        Assert.Contains("x:Name=\"AgentSelectorHost\"", xaml);
        Assert.Contains("x:Load=\"{x:Bind ShowAgentSelector", xaml);
        Assert.Contains("AutomationProperties.AutomationId=\"{x:Bind AgentSelectorAutomationId, Mode=OneWay}\"", xaml);
        Assert.Contains("x:Name=\"ProjectSelectorHost\"", xaml);
        Assert.Contains("x:Load=\"{x:Bind ShowProjectSelector", xaml);
        Assert.Contains("AutomationProperties.AutomationId=\"{x:Bind ProjectSelectorAutomationId, Mode=OneWay}\"", xaml);
    }

    [Fact]
    public void ChatInputArea_ComposerBlockedStates_UseUnifiedViewModelProjection()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Controls\ChatInputArea.xaml");

        Assert.Contains("Visibility=\"{x:Bind ViewModel.ShouldShowSlashCommandsUi, Mode=OneWay", xaml);
        Assert.Contains("IsEnabled=\"{x:Bind ViewModel.IsTextInputEnabled, Mode=OneWay}\"", xaml);
        Assert.Contains("IsEnabled=\"{x:Bind ViewModel.AreComposerToolsEnabled, Mode=OneWay}\"", xaml);
        Assert.Contains("IsEnabled=\"{x:Bind IsSubmitButtonEnabled, Mode=OneWay}\"", xaml);
        Assert.Contains("IsEnabled=\"{x:Bind ViewModel.CanStartVoiceInput, Mode=OneWay}\"", xaml);
        Assert.Contains("IsEnabled=\"{x:Bind ViewModel.CanStopVoiceInput, Mode=OneWay}\"", xaml);
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
        Assert.Contains("ShowAgentSelector=\"True\"", xaml);
        Assert.Contains("ShowModeSelector=\"True\"", xaml);
        Assert.Contains("ShowProjectSelector=\"True\"", xaml);
        Assert.Contains("ModeItemsSource=\"{x:Bind ViewModel.StartModeOptions, Mode=OneWay}\"", xaml);
        Assert.DoesNotContain("IsComposerExpanded", xaml);
        Assert.DoesNotContain("OnComposerInteractiveElementGotFocus", xaml);
        Assert.DoesNotContain("OnComposerSelectorDropDownOpened", xaml);
        Assert.DoesNotContain("x:Name=\"StartPromptBox\"", xaml);
        Assert.DoesNotContain("x:Name=\"StartAgentSelector\"", xaml);
        Assert.DoesNotContain("x:Name=\"StartModeSelector\"", xaml);
        Assert.DoesNotContain("x:Name=\"StartProjectSelector\"", xaml);
    }

    [Fact]
    public void ChatView_UsesSharedInputAreaWithoutAgentSelectorCapability()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Chat\ChatView.xaml");

        Assert.Contains("<controls:ChatInputArea ViewModel=\"{x:Bind ViewModel, Mode=OneWay}\"", xaml);
        Assert.Contains("ModeItemsSource=\"{x:Bind ViewModel.AvailableModes, Mode=OneWay}\"", xaml);
        Assert.DoesNotContain("ShowAgentSelector=\"True\"", xaml);
    }

    [Fact]
    public void SharedComposer_ModeSelectionUsesExplicitCommandInsteadOfTwoWaySelectedMode()
    {
        var chatInputXaml = LoadXaml(@"SalmonEgg\SalmonEgg\Controls\ChatInputArea.xaml");
        var chatViewXaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Chat\ChatView.xaml");
        var startViewXaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Start\StartView.xaml");

        Assert.Contains("SelectedItem=\"{x:Bind SelectedMode, Mode=OneWay}\"", chatInputXaml);
        Assert.Contains("SelectionChanged=\"OnModeSelectorSelectionChanged\"", chatInputXaml);
        Assert.DoesNotContain("SelectedItem=\"{x:Bind SelectedMode, Mode=TwoWay}\"", chatInputXaml, StringComparison.Ordinal);

        Assert.Contains("SelectedMode=\"{x:Bind ViewModel.SelectedMode, Mode=OneWay}\"", chatViewXaml);
        Assert.Contains("ModeSelectionCommand=\"{x:Bind ViewModel.SetModeCommand}\"", chatViewXaml);
        Assert.DoesNotContain("SelectedMode=\"{x:Bind ViewModel.SelectedMode, Mode=TwoWay}\"", chatViewXaml, StringComparison.Ordinal);

        Assert.Contains("SelectedMode=\"{x:Bind ViewModel.SelectedStartMode, Mode=OneWay}\"", startViewXaml);
        Assert.Contains("ModeSelectionCommand=\"{x:Bind ViewModel.SelectStartModeCommand}\"", startViewXaml);
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
        Assert.Contains("x:DataType=\"startVm:StartProjectOptionViewModel\"", chatInputXaml, StringComparison.Ordinal);
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
        Assert.Contains("Visibility=\"{x:Bind ViewModel.ComposerState.ShowVoiceListeningStatus, Mode=OneWay", xaml);
        Assert.Contains("IsEnabled=\"{x:Bind ViewModel.IsTextInputEnabled, Mode=OneWay}\"", xaml);
        Assert.Contains("IsEnabled=\"{x:Bind ViewModel.CanStartVoiceInput, Mode=OneWay}\"", xaml);
        Assert.Contains("IsEnabled=\"{x:Bind ViewModel.CanStopVoiceInput, Mode=OneWay}\"", xaml);
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
        Assert.DoesNotContain("new WindowsGamepadInputService(", code, StringComparison.Ordinal);
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
    public void WindowsGamepadInputService_UsesRawFallbackAsASecondaryPath()
    {
        var code = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Services\Input\WindowsGamepadInputService.cs");

        Assert.Contains("RawGameController", code);
        Assert.Contains("Gamepad.Gamepads", code);
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
    public void MainShellGamepadNavigationDispatcher_UsesNativeToggleAndExpandCollapsePatterns()
    {
        var code = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Services\Input\MainShellGamepadNavigationDispatcher.cs");

        Assert.Contains("IToggleProvider", code);
        Assert.Contains("IExpandCollapseProvider", code);
        Assert.Contains("ISelectionItemProvider", code);
        Assert.DoesNotContain("SelectedItem =", code, StringComparison.Ordinal);
        Assert.DoesNotContain(".IsOpen = false", code, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowsRawGameControllerMapper_UsesTypedGameControllerButtonLabels()
    {
        var code = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Services\Input\WindowsRawGameControllerMapper.cs");

        Assert.Contains("GameControllerButtonLabel", code);
        Assert.DoesNotContain("ToString()", code, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowsGamepadInputService_ConsidersActiveRawControllersEvenWhenStandardGamepadsAreConnected()
    {
        var code = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Services\Input\WindowsGamepadInputService.cs");

        Assert.Contains("foreach (var gamepad in", code);
        Assert.Contains("foreach (var controller in", code);
        Assert.DoesNotContain("var gamepad = GetPrimaryGamepad()", code, StringComparison.Ordinal);
    }

    [Fact]
    public void MainShellGamepadNavigationDispatcher_BoundsDirectionalNavigationToTheFocusedVisualTree()
    {
        var code = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Services\Input\MainShellGamepadNavigationDispatcher.cs");

        Assert.Contains("GetNavigationSearchRoot()", code);
        Assert.Contains("VisualTreeHelper.GetParent", code);
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

        Assert.Contains("INavigationIntentConsumer", code);
        Assert.Contains("OnInputKeyDown", code);
        Assert.Contains("OnSendAcceleratorInvoked", code);
        Assert.Contains("OnNewLineAcceleratorInvoked", code);
        Assert.Contains("_isImeComposing", code);
        Assert.Contains("IsPromptEditingAvailable()", code);
        Assert.DoesNotContain("InputBox.IsEnabled && ViewModel.IsInputEnabled", code, StringComparison.Ordinal);
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

    private static string NormalizeRelativePath(string relativePath)
        => relativePath.Replace('\\', Path.DirectorySeparatorChar);

    private static bool HasAttributeByLocalName(XElement element, string localName)
        => element.Attributes().Any(attribute => string.Equals(attribute.Name.LocalName, localName, StringComparison.Ordinal));

    private static string? GetAttributeByLocalName(XElement element, string localName)
        => element.Attributes()
            .FirstOrDefault(attribute => string.Equals(attribute.Name.LocalName, localName, StringComparison.Ordinal))
            ?.Value;

    private static bool HasXUid(XElement element, string expectedValue)
        => string.Equals(GetAttributeByLocalName(element, "Uid"), expectedValue, StringComparison.Ordinal);
}
