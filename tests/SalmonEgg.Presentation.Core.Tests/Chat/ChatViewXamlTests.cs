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
        Assert.Contains("Loaded=\"OnSessionHeaderRootLoaded\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Unloaded=\"OnSessionHeaderRootUnloaded\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SessionHeaderRoot.SizeChanged += OnSessionHeaderRootSizeChanged;", codeBehind, StringComparison.Ordinal);
        Assert.Contains("SessionHeaderRoot.SizeChanged -= OnSessionHeaderRootSizeChanged;", codeBehind, StringComparison.Ordinal);
        Assert.Contains("UpdateSessionHeaderLayoutState(SessionHeaderRoot.ActualWidth);", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("useTransitions", codeBehind, StringComparison.Ordinal);
        Assert.Contains("ApplyNarrowSessionHeaderLayout();", codeBehind, StringComparison.Ordinal);
        Assert.Contains("ApplyMediumSessionHeaderLayout();", codeBehind, StringComparison.Ordinal);
        Assert.Contains("ApplyWideSessionHeaderLayout();", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatViewSessionHeader_NarrowLayout_KeepsAgentRowRightAligned()
    {
        var codeBehind = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Views\Chat\ChatView.xaml.cs");

        Assert.Contains("Grid.SetRow(SessionHeaderAgentDisplay, 1);", codeBehind, StringComparison.Ordinal);
        Assert.Contains("Grid.SetColumnSpan(SessionHeaderAgentDisplay, 2);", codeBehind, StringComparison.Ordinal);
        Assert.Contains("SessionHeaderAgentDisplay.HorizontalAlignment = HorizontalAlignment.Right;", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("SessionHeaderAgentDisplay.HorizontalAlignment = HorizontalAlignment.Left;", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("SessionHeaderAgentDisplay.Margin = new Thickness(28, 0, 0, 0);", codeBehind, StringComparison.Ordinal);
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
    public void ChatViewXaml_BindsTerminalPanelStateIntoBottomPanelHost()
    {
        var root = FindRepoRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "SalmonEgg", "SalmonEgg", "MainPage.xaml"));

        Assert.Contains("TerminalSessions=\"{x:Bind ChatVM.TerminalSessions, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedTerminalSession=\"{x:Bind ChatVM.SelectedTerminalSession, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("LocalTerminalSession=\"{x:Bind ChatVM.ActiveLocalTerminalSession, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void BottomPanelHostXaml_UsesXtermTerminalViewForTerminalTab()
    {
        var root = FindRepoRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "SalmonEgg", "SalmonEgg", "Presentation", "Views", "Chat", "BottomPanelHost.xaml"));

        Assert.Contains("controls:XtermTerminalView", xaml, StringComparison.Ordinal);
        Assert.Contains("IsTerminalTabSelected", xaml, StringComparison.Ordinal);
        Assert.Contains("Session=\"{x:Bind EffectiveLocalTerminalSession, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ContentText=\"{x:Bind LocalTerminalContentText, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
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
        return File.ReadAllText(Path.Combine(root, relativePath));
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
}
