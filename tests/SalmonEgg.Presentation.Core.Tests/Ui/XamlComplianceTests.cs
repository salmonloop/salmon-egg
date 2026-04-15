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
    public void MainPage_ProjectItemsDoNotOverrideSelectsOnInvoked()
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
        Assert.False(
            string.Equals(projectNavItem!.Attribute("SelectsOnInvoked")?.Value, "False", StringComparison.OrdinalIgnoreCase),
            "ProjectNavTemplate should preserve native item selection behavior and not force SelectsOnInvoked=False.");
    }

    [Fact]
    public void MainPage_ProjectExpansionUsesNativeNavigationViewBehavior()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\MainPage.xaml");

        Assert.DoesNotContain("IsExpanded=\"{x:Bind IsExpanded, Mode=OneWay}\"", xaml);
        Assert.DoesNotContain("Expanding=\"OnMainNavItemExpanding\"", xaml);
        Assert.DoesNotContain("Collapsed=\"OnMainNavItemCollapsed\"", xaml);
    }

    [Fact]
    public void MainPage_SearchActionsUseCommands()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\MainPage.xaml");

        Assert.Contains("Command=\"{x:Bind ActivateCommand", xaml);
        Assert.Contains("Command=\"{x:Bind UseCommand", xaml);
        Assert.DoesNotContain("OnSearchResultItemClick", xaml);
        Assert.DoesNotContain("OnSearchHistoryItemClick", xaml);
    }

    [Fact]
    public void MainPage_SearchFocusIsViewModelDriven()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\MainPage.xaml");

        Assert.DoesNotContain("GotFocus=\"OnSearchBoxGotFocus\"", xaml);
        Assert.DoesNotContain("LostFocus=\"OnSearchBoxLostFocus\"", xaml);
        Assert.Contains("FocusMonitor.IsFocused", xaml);
    }

    [Fact]
    public void MainPage_SearchStringsAreLocalized()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\MainPage.xaml");

        Assert.DoesNotContain("PlaceholderText=\"搜索\"", xaml);
        Assert.DoesNotContain("AutomationProperties.Name=\"搜索\"", xaml);
        Assert.DoesNotContain("Text=\"最近搜索\"", xaml);
        Assert.DoesNotContain("Text=\"无搜索结果\"", xaml);
        Assert.Contains("x:Uid=\"TopSearchBox\"", xaml);
        Assert.Contains("x:Uid=\"SearchPanelRecentTitle\"", xaml);
        Assert.Contains("x:Uid=\"SearchPanelEmptyText\"", xaml);
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
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Chat\ChatView.xaml");

        Assert.DoesNotContain("Text=\"Agent 需要你的输入\"", xaml);
        Assert.DoesNotContain("Content=\"提交答案\"", xaml);
        Assert.DoesNotContain("AutomationProperties.Name=\"会话加载中\"", xaml);
        Assert.Contains("x:Uid=\"ChatViewAskUserTitle\"", xaml);
        Assert.Contains("x:Uid=\"ChatViewAskUserSubmitButton\"", xaml);
        Assert.Contains("x:Uid=\"ChatViewLoadingOverlay\"", xaml);
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
