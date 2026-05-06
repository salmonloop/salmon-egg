using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Ui;

public sealed class XamlComplianceTests
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
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\MainPage.xaml");

        Assert.Contains("IsExpanded=\"{x:Bind IsExpanded, Mode=TwoWay}\"", xaml);
        Assert.DoesNotContain("IsExpanded=\"{x:Bind IsExpanded, Mode=OneWay}\"", xaml);
        Assert.DoesNotContain("Expanding=\"OnMainNavItemExpanding\"", xaml);
        Assert.DoesNotContain("Collapsed=\"OnMainNavItemCollapsed\"", xaml);
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
    public void MiniChatView_RootSurfaceKeepsWindowBackdropVisible()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\MiniWindow\MiniChatView.xaml");

        Assert.Contains("x:Name=\"RootGrid\"", xaml);
        Assert.Contains("Background=\"Transparent\"", xaml);
        Assert.DoesNotContain("Background=\"{ThemeResource ApplicationPageBackgroundThemeBrush}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MiniChatView_UsesCoordinatorBasedViewportPolicy()
    {
        var code = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Views\MiniWindow\MiniChatView.xaml.cs");

        Assert.Contains("TranscriptViewportCoordinator", code, StringComparison.Ordinal);
        Assert.DoesNotContain("_userScrolledUp = !IsListViewportAtBottom()", code, StringComparison.Ordinal);
    }

    [Fact]
    public void MiniChatView_DoesNotTreatPointerPressedAsViewportDetachIntent()
    {
        var code = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Views\MiniWindow\MiniChatView.xaml.cs");

        Assert.Contains("private void OnMessagesListPointerPressed", code, StringComparison.Ordinal);
        Assert.DoesNotContain("OnMessagesListPointerPressed(object sender, PointerRoutedEventArgs e)\r\n    {\r\n        RegisterUserViewportIntent();", code, StringComparison.Ordinal);
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
        Assert.Contains("x:Uid=\"SessionNavRenameItem\"", xaml);
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
        Assert.Contains("x:Uid=\"MarryHerButton\"", xaml);
        Assert.Contains("x:Uid=\"SendButton\"", xaml);
        Assert.Contains("x:Uid=\"CancelButton\"", xaml);
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

    [Fact]
    public void AgentProfileEditor_DoesNotUseValueChangedHandlers()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\AgentProfileEditorPage.xaml");

        Assert.DoesNotContain("ValueChanged=\"OnHeartbeatValueChanged\"", xaml);
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
        Assert.Contains("TextOnAccentFillColorPrimaryBrush", xaml);
    }

    [Fact]
    public void AppXaml_DoesNotDeclareASecondUiMotionInstance()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\App.xaml");

        Assert.DoesNotContain("<models:UiMotion x:Key=\"UiMotion\"", xaml);
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

        Assert.Contains("<Border x:Name=\"ComposerShell\"", xaml);
        Assert.Contains("Grid.Row=\"1\"", xaml);
        Assert.DoesNotContain("x:Name=\"ComposerFocusHost\"", xaml);
        Assert.DoesNotContain("FocusEngaged=\"OnComposerFocusHostFocusEngaged\"", xaml);
        Assert.DoesNotContain("private void OnComposerFocusHostFocusEngaged(", code);
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
    public void MiniChatView_TextsAreLocalized()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\MiniWindow\MiniChatView.xaml");

        Assert.DoesNotContain("PlaceholderText=\"选择会话\"", xaml);
        Assert.DoesNotContain("PlaceholderText=\"输入消息\"", xaml);
        Assert.Contains("x:Uid=\"MiniChatSessionSelector\"", xaml);
        Assert.Contains("x:Uid=\"MiniChatReturnButton\"", xaml);
        Assert.Contains("x:Uid=\"MiniChatInputBox\"", xaml);
        Assert.Contains("x:Uid=\"MiniChatSendButton\"", xaml);
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
        Assert.Contains("PointerPressed=\"OnMessagesListPointerPressed\"", xaml);
        Assert.Contains("PointerWheelChanged=\"OnMessagesListPointerWheelChanged\"", xaml);
        Assert.Contains("KeyDown=\"OnMessagesListKeyDown\"", xaml);
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
        Assert.Contains("PointerPressed=\"OnMessagesListPointerPressed\"", xaml);
        Assert.Contains("PointerWheelChanged=\"OnMessagesListPointerWheelChanged\"", xaml);
        Assert.Contains("KeyDown=\"OnMessagesListKeyDown\"", xaml);
        Assert.DoesNotContain("FindScrollViewer(", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("VisualTreeHelper.", codeBehind, StringComparison.Ordinal);
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
        Assert.Contains("TryMoveSlashSelection", code);
        Assert.Contains("TryAcceptSelectedSlashCommand", code);
        Assert.Contains("_isImeComposing", code);
        Assert.Contains("InputBox.IsEnabled", code);
        Assert.Contains("TryAcceptSelectedSlashCommandAndMoveCaretToEnd", code);
    }


    private static string LoadXaml(string relativePath)
    {
        return LoadText(relativePath);
    }

    private static string LoadText(string relativePath)
    {
        var root = FindRepoRoot();
        var fullPath = Path.Combine(root, relativePath);
        return File.ReadAllText(fullPath);
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

    private static bool HasAttributeByLocalName(XElement element, string localName)
        => element.Attributes().Any(attribute => string.Equals(attribute.Name.LocalName, localName, StringComparison.Ordinal));

    private static string? GetAttributeByLocalName(XElement element, string localName)
        => element.Attributes()
            .FirstOrDefault(attribute => string.Equals(attribute.Name.LocalName, localName, StringComparison.Ordinal))
            ?.Value;

    private static bool HasXUid(XElement element, string expectedValue)
        => string.Equals(GetAttributeByLocalName(element, "Uid"), expectedValue, StringComparison.Ordinal);
}
