using System;
using System.IO;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public sealed class ChatViewXamlTests
{
    [Fact]
    public void ChatViewXaml_DoesNotExposeProjectAffinityCorrectionPanelInHeader()
    {
        var xaml = LoadChatViewXaml();

        Assert.DoesNotContain("AutomationProperties.AutomationId=\"ChatView.ProjectAffinityCorrectionPanel\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("AutomationProperties.AutomationId=\"ChatView.ProjectAffinityCorrectionProjectSelector\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("AutomationProperties.AutomationId=\"ChatView.ProjectAffinityCorrectionApplyButton\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("AutomationProperties.AutomationId=\"ChatView.ProjectAffinityCorrectionClearButton\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatViewXaml_DoesNotBindProjectAffinityCorrectionControls()
    {
        var xaml = LoadChatViewXaml();

        Assert.DoesNotContain("Visibility=\"{x:Bind ViewModel.IsProjectAffinityCorrectionVisible, Mode=OneWay", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ItemsSource=\"{x:Bind ViewModel.ProjectAffinityOverrideOptions, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedValue=\"{x:Bind ViewModel.SelectedProjectAffinityOverrideProjectId, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Command=\"{x:Bind ViewModel.ApplyProjectAffinityOverrideCommand}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Command=\"{x:Bind ViewModel.ClearProjectAffinityOverrideCommand}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatViewXaml_ProjectAffinityCorrection_DoesNotReintroduceAgentSelector()
    {
        var xaml = LoadChatViewXaml();

        Assert.DoesNotContain("SelectedItem=\"{x:Bind ViewModel.SelectedAcpProfile, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatViewXaml_SessionHeader_PreservesFullSessionNameMetadata()
    {
        var xaml = LoadChatViewXaml();

        Assert.Contains("AutomationProperties.Name=\"{x:Bind ViewModel.PresentedSessionHeaderDisplayName, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ToolTipService.ToolTip=\"{x:Bind ViewModel.PresentedSessionHeaderDisplayName, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{x:Bind ViewModel.PresentedSessionHeaderDisplayName, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("TextTrimming=\"CharacterEllipsis\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MaxLines=\"1\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatViewCodeBehind_SessionHeaderTitleChangeRefreshesGeneratedXBind()
    {
        var codeBehind = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Views\Chat\ChatView.xaml.cs");

        Assert.Contains("e.PropertyName == nameof(ChatViewModel.PresentedSessionHeaderDisplayName)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("Bindings.Update();", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("AutomationProperties.SetName(CurrentSessionNameButton", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatViewsCodeBehind_SessionDrivenAutoScrollUsesAttachedOnlyHelper()
    {
        var chatViewCodeBehind = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Views\Chat\ChatView.xaml.cs");
        var miniChatViewCodeBehind = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Views\MiniWindow\MiniChatView.xaml.cs");

        Assert.Contains("TryIssueTranscriptScrollRequestIfAttached();", chatViewCodeBehind, StringComparison.Ordinal);
        Assert.Contains("TryIssueTranscriptScrollRequestIfAttached();", miniChatViewCodeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("if (!IsViewportDetachedByUser())\n                {\n                    TryIssueTranscriptScrollRequest();\n                }\n                TryIssueTranscriptScrollRequest();", chatViewCodeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("if (!IsViewportDetachedByUser())\n            {\n                TryIssueTranscriptScrollRequest();\n            }\n            TryIssueTranscriptScrollRequest();", miniChatViewCodeBehind, StringComparison.Ordinal);
        Assert.Contains("ActivateViewportForCurrentSession(TranscriptViewportActivationKind.OverlayResume);", chatViewCodeBehind, StringComparison.Ordinal);
        Assert.Contains("ActivateViewportForCurrentSession(TranscriptViewportActivationKind.WarmReturn);", chatViewCodeBehind, StringComparison.Ordinal);
        Assert.Contains("ActivateViewportForCurrentSession(TranscriptViewportActivationKind.OverlayResume);", miniChatViewCodeBehind, StringComparison.Ordinal);
        Assert.Contains("ActivateViewportForCurrentSession(TranscriptViewportActivationKind.WarmReturn);", miniChatViewCodeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatViewXaml_SessionHeader_UsesNativeButtonAndSharedWidthContractForReadOnlyAndEditStates()
    {
        var xaml = LoadChatViewXaml();

        Assert.DoesNotContain("Style x:Key=\"InlineHeaderButtonStyle\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Style=\"{StaticResource InlineHeaderButtonStyle}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"ChatView.CurrentSessionNameButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("HorizontalAlignment=\"Stretch\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinWidth=\"0\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MaxWidth=\"560\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MinWidth=\"120\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatViewXaml_SessionHeader_UsesNamedResponsiveLayoutParts()
    {
        var xaml = LoadChatViewXaml();

        Assert.Contains("x:Name=\"SessionHeaderMetaGrid\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SessionHeaderAgentRow\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SessionHeaderAgentColumn\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SessionHeaderAgentDisplay\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatViewSessionHeader_UsesActualHeaderWidthInsteadOfWindowAdaptiveTriggers()
    {
        var xaml = LoadChatViewXaml();
        var codeBehind = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Views\Chat\ChatView.xaml.cs");

        Assert.DoesNotContain("<AdaptiveTrigger MinWindowWidth=\"720\" />", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<AdaptiveTrigger MinWindowWidth=\"1080\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("<utils:MinActualWidthTrigger TargetElement=\"{x:Bind SessionHeaderRoot}\" MinWidth=\"720\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("<utils:MinActualWidthTrigger TargetElement=\"{x:Bind SessionHeaderRoot}\" MinWidth=\"1080\" />", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("OnSessionHeaderRootLoaded", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("OnSessionHeaderRootUnloaded", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SizeChanged += OnSessionHeaderRootSizeChanged;", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("SizeChanged -= OnSessionHeaderRootSizeChanged;", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyNarrowSessionHeaderLayout", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyMediumSessionHeaderLayout", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyWideSessionHeaderLayout", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatViewSessionHeader_NarrowLayout_KeepsAgentRowRightAligned()
    {
        var xaml = LoadChatViewXaml();

        Assert.Contains("<Setter Target=\"SessionHeaderAgentDisplay.(Grid.Row)\" Value=\"1\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Target=\"SessionHeaderAgentDisplay.(Grid.ColumnSpan)\" Value=\"2\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Target=\"SessionHeaderAgentDisplay.HorizontalAlignment\" Value=\"Right\" />", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("HorizontalAlignment\" Value=\"Left\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Value=\"28,0,0,0\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatTranscriptRestore_DoesNotUseManualRenderedTranscriptWindowExpansion()
    {
        var chatViewCode = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Views\Chat\ChatView.xaml.cs");
        var miniChatViewCode = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Views\MiniWindow\MiniChatView.xaml.cs");
        var chatViewXaml = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Views\Chat\ChatView.xaml");
        var miniChatViewXaml = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Views\MiniWindow\MiniChatView.xaml");

        Assert.Contains("TryApplyPendingProjectionRestore();", chatViewCode, StringComparison.Ordinal);
        Assert.Contains("TryApplyPendingProjectionRestore();", miniChatViewCode, StringComparison.Ordinal);
        Assert.Contains("private void OnMessagesListViewportChanged", chatViewCode, StringComparison.Ordinal);
        Assert.Contains("private void OnMessagesListViewportChanged", miniChatViewCode, StringComparison.Ordinal);
        Assert.Contains("TranscriptProjectionRestoreController", chatViewCode, StringComparison.Ordinal);
        Assert.Contains("TranscriptProjectionRestoreController", miniChatViewCode, StringComparison.Ordinal);
        Assert.DoesNotContain("private void SchedulePendingProjectionRestoreRetry()", chatViewCode, StringComparison.Ordinal);
        Assert.DoesNotContain("private void SchedulePendingProjectionRestoreRetry()", miniChatViewCode, StringComparison.Ordinal);
        Assert.DoesNotContain("TryExpandOlderTranscriptWindowAtTop()", chatViewCode, StringComparison.Ordinal);
        Assert.DoesNotContain("TryExpandOlderTranscriptWindowAtTop()", miniChatViewCode, StringComparison.Ordinal);
        Assert.DoesNotContain("TryExpandOlderRenderedTranscriptWindow", chatViewCode, StringComparison.Ordinal);
        Assert.DoesNotContain("TryExpandOlderRenderedTranscriptWindow", miniChatViewCode, StringComparison.Ordinal);
        Assert.DoesNotContain("TryMaterializeRenderedTranscriptProjectionItem", chatViewCode, StringComparison.Ordinal);
        Assert.DoesNotContain("TryMaterializeRenderedTranscriptProjectionItem", miniChatViewCode, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{x:Bind MessagesItemsSource, Mode=OneWay}\"", chatViewXaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{x:Bind MessagesItemsSource, Mode=OneWay}\"", miniChatViewXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("OnMessagesListLayoutUpdated(object? sender, object e)\r\n        {\r\n            var lastItemContainerGenerated = HasLastItemContainerGenerated(ViewModel.MessageHistory.Count);\r\n            TryApplyPendingProjectionRestore();", chatViewCode, StringComparison.Ordinal);
        Assert.DoesNotContain("OnMessagesListLayoutUpdated(object? sender, object e)\r\n    {\r\n        var lastItemContainerGenerated = HasLastItemContainerGenerated(ViewModel.MessageHistory.Count);\r\n        TryApplyPendingProjectionRestore();", miniChatViewCode, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatTranscriptViewport_UsesHostDrivenViewportEventsWithoutPageLayoutUpdatedHooks()
    {
        var chatViewCode = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Views\Chat\ChatView.xaml.cs");
        var miniChatViewCode = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Views\MiniWindow\MiniChatView.xaml.cs");
        var chatViewXaml = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Views\Chat\ChatView.xaml");
        var miniChatViewXaml = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Views\MiniWindow\MiniChatView.xaml");
        var hostCode = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Transcript\ListViewTranscriptViewportHost.cs");

        Assert.DoesNotContain("LayoutUpdated=\"OnMessagesListLayoutUpdated\"", chatViewXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("LayoutUpdated=\"OnMessagesListLayoutUpdated\"", miniChatViewXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("private void OnMessagesListLayoutUpdated", chatViewCode, StringComparison.Ordinal);
        Assert.DoesNotContain("private void OnMessagesListLayoutUpdated", miniChatViewCode, StringComparison.Ordinal);
        Assert.Contains("ScrollViewer", hostCode, StringComparison.Ordinal);
        Assert.Contains("ViewChanged", hostCode, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatTranscriptViewport_ViewCodeUsesSharedControllerInsteadOfOwningOrchestrator()
    {
        var chatViewCode = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Views\Chat\ChatView.xaml.cs");
        var miniChatViewCode = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Views\MiniWindow\MiniChatView.xaml.cs");
        var controllerCode = LoadText(@"src\SalmonEgg.Presentation.Core\Utilities\TranscriptViewportController.cs");

        Assert.Contains("TranscriptViewportController", chatViewCode, StringComparison.Ordinal);
        Assert.Contains("TranscriptViewportController", miniChatViewCode, StringComparison.Ordinal);
        Assert.DoesNotContain("TranscriptViewportOrchestrator", chatViewCode, StringComparison.Ordinal);
        Assert.DoesNotContain("TranscriptViewportOrchestrator", miniChatViewCode, StringComparison.Ordinal);
        Assert.Contains("TranscriptViewportOrchestrator", controllerCode, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.UI.Xaml", controllerCode, StringComparison.Ordinal);
        Assert.DoesNotContain("ScrollViewer", controllerCode, StringComparison.Ordinal);
        Assert.DoesNotContain("ListView", controllerCode, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Suite", "Smoke")]
    public void ChatTranscriptVirtualization_UsesNativeListViewBindingWithoutUiTypesInCore()
    {
        var coreCollectionCode = LoadText(@"src\SalmonEgg.Presentation.Core\ViewModels\Chat\Transcript\ChatTranscriptVirtualizedMessageCollection.cs");
        var chatViewXaml = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Views\Chat\ChatView.xaml");
        var miniChatViewXaml = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Views\MiniWindow\MiniChatView.xaml");
        Assert.Contains("<ListView x:Name=\"MessagesList\"", chatViewXaml, StringComparison.Ordinal);
        Assert.Contains("<ListView x:Name=\"MessagesList\"", miniChatViewXaml, StringComparison.Ordinal);
        Assert.Contains("<ItemsPanelTemplate>", chatViewXaml, StringComparison.Ordinal);
        Assert.Contains("<ItemsPanelTemplate>", miniChatViewXaml, StringComparison.Ordinal);
        Assert.Contains("<ItemsStackPanel", chatViewXaml, StringComparison.Ordinal);
        Assert.Contains("<ItemsStackPanel", miniChatViewXaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"ChatView.MessagesList\"", chatViewXaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{x:Bind MessagesItemsSource, Mode=OneWay}\"", chatViewXaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{x:Bind MessagesItemsSource, Mode=OneWay}\"", miniChatViewXaml, StringComparison.Ordinal);
        Assert.Contains("ListViewTranscriptItemsSource", LoadText(@"SalmonEgg\SalmonEgg\Presentation\Transcript\ListViewTranscriptItemsSource.cs"), StringComparison.Ordinal);
        Assert.Contains("IItemsRangeInfo", LoadText(@"SalmonEgg\SalmonEgg\Presentation\Transcript\ListViewTranscriptItemsSource.cs"), StringComparison.Ordinal);
        Assert.Contains("MessagesItemsSource.Attach(ViewModel.MessageHistory);", LoadText(@"SalmonEgg\SalmonEgg\Presentation\Views\Chat\ChatView.xaml.cs"), StringComparison.Ordinal);
        Assert.Contains("MessagesItemsSource.Attach(ViewModel.MessageHistory);", LoadText(@"SalmonEgg\SalmonEgg\Presentation\Views\MiniWindow\MiniChatView.xaml.cs"), StringComparison.Ordinal);
        Assert.Contains("MessagesItemsSource.Detach();", LoadText(@"SalmonEgg\SalmonEgg\Presentation\Views\Chat\ChatView.xaml.cs"), StringComparison.Ordinal);
        Assert.Contains("MessagesItemsSource.Detach();", LoadText(@"SalmonEgg\SalmonEgg\Presentation\Views\MiniWindow\MiniChatView.xaml.cs"), StringComparison.Ordinal);
        Assert.Contains("ItemTemplateSelector=\"{StaticResource MessageTemplateSelector}\"", chatViewXaml, StringComparison.Ordinal);
        Assert.Contains("ItemTemplateSelector=\"{StaticResource MessageTemplateSelector}\"", miniChatViewXaml, StringComparison.Ordinal);
        Assert.Contains("SelectionMode=\"None\"", chatViewXaml, StringComparison.Ordinal);
        Assert.Contains("SelectionMode=\"None\"", miniChatViewXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<ScrollViewer x:Name=\"MessagesScrollViewer\"", chatViewXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<ScrollViewer x:Name=\"MessagesScrollViewer\"", miniChatViewXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<muxc:ItemsRepeater", chatViewXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<muxc:ItemsRepeater", miniChatViewXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<muxc:StackLayout", chatViewXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<muxc:StackLayout", miniChatViewXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowsScrollingPlaceholders", chatViewXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowsScrollingPlaceholders", miniChatViewXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ScrollViewerViewportMonitor", chatViewXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ScrollViewerViewportMonitor", miniChatViewXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("public void ApplyRequiredRanges", coreCollectionCode, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.UI.Xaml", coreCollectionCode, StringComparison.Ordinal);
        Assert.DoesNotContain("ItemIndexRange", coreCollectionCode, StringComparison.Ordinal);
        Assert.DoesNotContain("IItemsRangeInfo", coreCollectionCode, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowsGuiAppSession_AnywhereLookupStaysWithinApplicationWindows()
    {
        var code = LoadText(@"tests\SalmonEgg.GuiTests.Windows\WindowsGuiAppSession.cs");

        Assert.Contains("_application.GetAllTopLevelWindows(_automation)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("_automation.GetDesktop().FindFirstDescendant", code, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowsGuiAppSession_VisibleAnywhereLookupsStayWithinApplicationWindows()
    {
        var code = LoadText(@"tests\SalmonEgg.GuiTests.Windows\WindowsGuiAppSession.cs");

        Assert.DoesNotContain("_automation.GetDesktop()", code, StringComparison.Ordinal);
        Assert.Contains("GetApplicationTopLevelWindows()", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ConversationActivationPreview_IsNotExposedAsPublicShellFacingServiceSurface()
    {
        var serviceCode = LoadText(@"src\SalmonEgg.Presentation.Core\Services\Chat\IConversationSessionSwitcher.cs");
        var viewModelCode = LoadText(@"src\SalmonEgg.Presentation.Core\ViewModels\Chat\ChatViewModel.cs");

        Assert.DoesNotContain("public interface IConversationActivationPreview", serviceCode, StringComparison.Ordinal);
        Assert.DoesNotContain("IConversationActivationPreview", viewModelCode, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatViewXaml_BindsLocalTerminalSessionIntoBottomPanelHost()
    {
        var root = FindRepoRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "SalmonEgg", "SalmonEgg", "MainPage.xaml"));

        Assert.DoesNotContain("TabsSource=", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedTab=", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("TerminalSessions=", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedTerminalSession=", xaml, StringComparison.Ordinal);
        Assert.Contains("LocalTerminalSession=\"{x:Bind ChatVM.ActiveLocalTerminalSession, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void BottomPanelHostXaml_UsesXtermTerminalViewForTerminalTab()
    {
        var root = FindRepoRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "SalmonEgg", "SalmonEgg", "Presentation", "Views", "Chat", "BottomPanelHost.xaml"));

        Assert.Contains("<TabView", xaml, StringComparison.Ordinal);
        Assert.Contains("HorizontalAlignment=\"Stretch\"", xaml, StringComparison.Ordinal);
        Assert.Contains("VerticalAlignment=\"Stretch\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedIndex=\"0\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<TabView.TabItems>", xaml, StringComparison.Ordinal);
        Assert.Contains("<TabViewItem", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"{x:Bind TerminalTabTitle, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsAddTabButtonVisible=\"False\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CanReorderTabs=\"False\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<TabViewItem.Content>", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<ListView", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Converter={StaticResource BoolToVisibilityConverter}", xaml, StringComparison.Ordinal);
        Assert.Contains("controls:XtermTerminalView", xaml, StringComparison.Ordinal);
        Assert.Contains("Session=\"{x:Bind EffectiveLocalTerminalSession, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ContentText=\"{x:Bind LocalTerminalContentText, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("BottomPanelOutputSourceLabel", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void XtermTerminalViewXaml_FallbackBindsTerminalContent()
    {
        var root = FindRepoRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "SalmonEgg", "SalmonEgg", "Controls", "XtermTerminalView.xaml"));

        Assert.Contains("Text=\"{x:Bind ContentText, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"{x:Bind ContentText, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void XtermHost_UsesOfficialParserHooksAndWindowsPtyCompatibility()
    {
        var root = FindRepoRoot();
        var script = File.ReadAllText(Path.Combine(root, "SalmonEgg", "SalmonEgg", "Assets", "Terminal", "xterm-host.js"));

        Assert.Contains("terminal.parser.registerCsiHandler", script, StringComparison.Ordinal);
        Assert.Contains("windowsPty", script, StringComparison.Ordinal);
        Assert.DoesNotContain("replaceAll('\\u001b[?9001h'", script, StringComparison.Ordinal);
        Assert.DoesNotContain("replaceAll('\\u001b[?9001l'", script, StringComparison.Ordinal);
    }

    private static string LoadChatViewXaml()
    {
        var root = FindRepoRoot();
        return File.ReadAllText(Path.Combine(root, "SalmonEgg", "SalmonEgg", "Presentation", "Views", "Chat", "ChatView.xaml"));
    }

    private static string LoadText(string relativePath)
    {
        var root = FindRepoRoot();
        return File.ReadAllText(Path.Combine(root, NormalizeRelativePath(relativePath)));
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

    private static string NormalizeRelativePath(string relativePath)
        => relativePath.Replace('\\', Path.DirectorySeparatorChar);
}
