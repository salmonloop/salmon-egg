using System;
using System.IO;
using SalmonEgg.Presentation.Models.Navigation;
using SalmonEgg.Presentation.ViewModels.Navigation;

namespace SalmonEgg.Presentation.Core.Tests;

public sealed class NavigationCoreTests
{
    [Fact]
    public void NavTimeFormatter_ToRelativeText_UsesExpectedBuckets()
    {
        var now = DateTime.UtcNow;

        Assert.Equal("刚刚", NavTimeFormatter.ToRelativeText(now - TimeSpan.FromSeconds(30)));
        Assert.Equal("2 分", NavTimeFormatter.ToRelativeText(now - TimeSpan.FromMinutes(2)));
        Assert.Equal("3 小时", NavTimeFormatter.ToRelativeText(now - TimeSpan.FromHours(3)));
        Assert.Equal("2 天", NavTimeFormatter.ToRelativeText(now - TimeSpan.FromDays(2)));
    }

    [Fact]
    public void NavTimeFormatter_NormalizePathForPrefixMatch_AppendsSeparator()
    {
        var path = Path.Combine("C:", "Temp", "Demo");
        var normalized = NavTimeFormatter.NormalizePathForPrefixMatch(path);

        Assert.EndsWith(Path.DirectorySeparatorChar.ToString(), normalized, StringComparison.Ordinal);
        Assert.Contains("Demo", normalized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NavItemTag_MoreTag_RoundTrips()
    {
        var tag = NavItemTag.More("proj-9");

        Assert.True(NavItemTag.TryParseMore(tag, out var projectId));
        Assert.Equal("proj-9", projectId);
    }

    [Fact]
    public void NavItemTag_SessionTag_RoundTrips()
    {
        var tag = NavItemTag.Session("session-42");

        Assert.True(NavItemTag.TryParseSession(tag, out var sessionId));
        Assert.Equal("session-42", sessionId);
    }

    [Fact]
    public void NavItemTag_ProjectTag_RoundTrips()
    {
        var tag = NavItemTag.Project("project-7");

        Assert.True(NavItemTag.TryParseProject(tag, out var projectId));
        Assert.Equal("project-7", projectId);
    }

    [Fact]
    public void NavItemTag_ParseRejectsInvalid()
    {
        Assert.False(NavItemTag.TryParseSession("Session:", out _));
        Assert.False(NavItemTag.TryParseSession("Other:123", out _));
        Assert.False(NavItemTag.TryParseProject("Project:", out _));
        Assert.False(NavItemTag.TryParseProject("Other:123", out _));
        Assert.False(NavItemTag.TryParseMore("More:", out _));
        Assert.False(NavItemTag.TryParseMore("Other:123", out _));
    }

    [Fact]
    public void MainPage_DoesNotMapFramePagesToShellNavigationContentDirectly()
    {
        var code = LoadFile(@"SalmonEgg\SalmonEgg\MainPage.xaml.cs");

        Assert.DoesNotContain("ShellNavigationContent.", code, StringComparison.Ordinal);
        Assert.DoesNotContain("SyncNavSelectionFromCurrentPage(", code, StringComparison.Ordinal);
    }

    [Fact]
    public void MainPage_DoesNotImperativelyProjectNavigationPaneState()
    {
        var code = LoadFile(@"SalmonEgg\SalmonEgg\MainPage.xaml.cs");

        Assert.DoesNotContain("MainNavView.PaneDisplayMode =", code, StringComparison.Ordinal);
        Assert.DoesNotContain("MainNavView.IsPaneOpen =", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ResolveNavigationViewPaneDisplayMode(", code, StringComparison.Ordinal);
    }

    [Fact]
    public void MainNavigationXaml_ExposesStableAutomationIds_ForGuiTesting()
    {
        var xaml = LoadFile(@"SalmonEgg\SalmonEgg\MainPage.xaml");

        Assert.Contains("AutomationProperties.AutomationId=\"MainNavView\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"TitleBar.ToggleSidebar\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MainNavigationAutomationIds.StartItem()", xaml, StringComparison.Ordinal);
        Assert.Contains("MainNavigationAutomationIds.SessionsLabel()", xaml, StringComparison.Ordinal);
        Assert.Contains("MainNavigationAutomationIds.AddProject()", xaml, StringComparison.Ordinal);
        Assert.Contains("MainNavigationAutomationIds.ProjectItem(ProjectId)", xaml, StringComparison.Ordinal);
        Assert.Contains("MainNavigationAutomationIds.SessionItem(SessionId)", xaml, StringComparison.Ordinal);
        Assert.Contains("MainNavigationAutomationIds.MoreItem(ProjectId)", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MainNavigationXaml_UsesNativeChildSelectionProjection_ForProjectAncestorEmphasis()
    {
        var xaml = LoadFile(@"SalmonEgg\SalmonEgg\MainPage.xaml");

        Assert.Contains("IsChildSelected=\"{x:Bind IsActiveDescendant, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void SessionsDialogXaml_ExposesStableAutomationIds_ForGuiTesting()
    {
        var xaml = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Views\Navigation\SessionsListDialog.xaml");

        Assert.Contains("AutomationProperties.AutomationId=\"SessionsDialog\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"SessionsDialog.SearchBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"SessionsDialog.List\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MainNavigationAutomationIds.SessionsDialogItem(SessionId)", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void StartViewXaml_ExposesStableAutomationIds_ForGuiTesting()
    {
        var xaml = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Views\Start\StartView.xaml");

        Assert.Contains("AutomationProperties.AutomationId=\"StartView.Title\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void StartViewXaml_ExposesSharedAgentSelector_ForNewSessionLaunch()
    {
        var xaml = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Views\Start\StartView.xaml");

        Assert.Contains("AutomationProperties.AutomationId=\"StartView.AgentSelector\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{x:Bind ViewModel.Chat.AcpProfileList, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedItem=\"{x:Bind ViewModel.Chat.SelectedAcpProfile, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void StartViewXaml_ExposesProjectSelector_ForNewSessionLaunch()
    {
        var xaml = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Views\Start\StartView.xaml");

        Assert.Contains("AutomationProperties.AutomationId=\"StartView.ProjectSelector\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{x:Bind ViewModel.StartProjectOptions, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedValue=\"{x:Bind ViewModel.SelectedStartProjectId, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedValuePath=\"ProjectId\"", xaml, StringComparison.Ordinal);
        Assert.Contains("DisplayMemberPath=\"DisplayName\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatViewXaml_ExposesStableAutomationIds_ForGuiTesting()
    {
        var xaml = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Views\Chat\ChatView.xaml");

        Assert.Contains("AutomationProperties.AutomationId=\"ChatView.ActiveRoot\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"ChatView.CurrentSessionNameButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"ChatView.CurrentSessionNameEditor\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"ChatView.CurrentAgentDisplay\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"ChatView.LoadingOverlay\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"ChatView.MessagesList\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatViewXaml_UsesAutomationCapableLoadingOverlayRoot()
    {
        var xaml = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Views\Chat\ChatView.xaml");

        Assert.Contains("AutomationProperties.AutomationId=\"ChatView.LoadingOverlay\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<ContentControl x:Name=\"LoadingOverlayPresenter\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<Grid AutomationProperties.AutomationId=\"ChatView.LoadingOverlay\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"ChatView.LoadingOverlayStatus\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("AutomationProperties.LiveSetting=\"Assertive\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatViewXaml_DoesNotContainLegacyInactiveAgentSetupPlaceholder()
    {
        var xaml = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Views\Chat\ChatView.xaml");

        Assert.DoesNotContain("AutomationProperties.AutomationId=\"ChatView.InactiveRoot\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("AutomationProperties.AutomationId=\"ChatView.GoToSettingsButton\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("准备好开始了吗？", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("去配置 Agent", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatViewXaml_DoesNotExposeAgentSwitchSelectorInHeader()
    {
        var xaml = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Views\Chat\ChatView.xaml");

        Assert.DoesNotContain("SelectedItem=\"{x:Bind ViewModel.SelectedAcpProfile, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Uid=\"ChatAcpProfileSelector\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MainNavigationXaml_UsesNativeNavigationViewItemHeaderForSessionsLabel()
    {
        var xaml = LoadFile(@"SalmonEgg\SalmonEgg\MainPage.xaml");

        // SessionsLabel should use NavigationViewItemHeader, not NavigationViewItem
        Assert.Contains("<NavigationViewItemHeader Content=\"{x:Bind Title, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MainNavigationAutomationIds.SessionsLabel()", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MainNavigationXaml_AddProjectUsesStaticAddIcon()
    {
        var xaml = LoadFile(@"SalmonEgg\SalmonEgg\MainPage.xaml");

        Assert.Contains("MainNavigationAutomationIds.AddProject()", xaml, StringComparison.Ordinal);
        Assert.Contains("<SymbolIcon Symbol=\"Add\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("NavItemTag.AddProject", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MainNavigation_SessionFlyout_DefersDialogCommandsUntilFlyoutCloses()
    {
        var xaml = LoadFile(@"SalmonEgg\SalmonEgg\MainPage.xaml");
        var code = LoadFile(@"SalmonEgg\SalmonEgg\MainPage.xaml.cs");

        Assert.Contains("x:Uid=\"SessionNavMoveItem\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnSessionMoveMenuItemClick\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Uid=\"SessionNavMoveItem\"\r\n                                        Command=\"{x:Bind MoveCommand}\"", xaml, StringComparison.Ordinal);

        Assert.Contains("x:Uid=\"SessionNavRenameItem\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnSessionRenameMenuItemClick\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Uid=\"SessionNavRenameItem\"\r\n                                        Command=\"{x:Bind RenameCommand}\"", xaml, StringComparison.Ordinal);

        Assert.Contains("private void OnSessionMoveMenuItemClick(", code, StringComparison.Ordinal);
        Assert.Contains("private void OnSessionRenameMenuItemClick(", code, StringComparison.Ordinal);
        Assert.Contains("_moveOnFlyoutClosed.TryConsume(sessionId)", code, StringComparison.Ordinal);
        Assert.Contains("_renameOnFlyoutClosed.TryConsume(sessionId)", code, StringComparison.Ordinal);
    }

    private static string LoadFile(string relativePath)
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
