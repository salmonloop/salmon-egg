using System;
using System.Diagnostics;
using System.Threading;
using FlaUI.Core.Definitions;
using FlaUI.Core.AutomationElements;

namespace SalmonEgg.GuiTests.Windows;

public sealed class ShellFocusedActivationSmokeTests
{
    [SkippableFact]
    public void DiscoverSessions_CanBeReached_AndActivated_ThroughFocusedNativeActivationPath()
    {
        GuiTestGate.RequireEnabled();

        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();

        var discoverItem = session.FindByAutomationId("MainNav.DiscoverSessions", TimeSpan.FromSeconds(10));
        session.FocusElement(discoverItem);
        Thread.Sleep(150);
        session.PressEnter();

        Assert.True(
            session.WaitUntilVisible("DiscoverSessions.Title", TimeSpan.FromSeconds(10)),
            $"Discover sessions page did not become visible through focused native activation.{Environment.NewLine}{appData.ReadBootLogTail()}");
    }

    [SkippableFact]
    public void DiscoverSessions_CanBeActivated_ThroughVirtualGamepadA()
    {
        GuiTestGate.RequireEnabled();

        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();

        var discoverItem = session.FindByAutomationId("MainNav.DiscoverSessions", TimeSpan.FromSeconds(10));
        session.FocusElement(discoverItem);
        Thread.Sleep(150);
        session.PressVirtualGamepadA();

        Assert.True(
            session.WaitUntilVisible("DiscoverSessions.Title", TimeSpan.FromSeconds(10)),
            $"Discover sessions page did not become visible through Virtual Gamepad A activation.{Environment.NewLine}{appData.ReadBootLogTail()}");
    }

    [SkippableFact]
    public void TitleBarCommand_VirtualGamepadDPadDown_CanReachMainNavigation()
    {
        GuiTestGate.RequireEnabled();

        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();

        var titleBarToggle = session.FindByAutomationId("TitleBar.ToggleSidebar", TimeSpan.FromSeconds(10));
        FocusAndAssert(session, titleBarToggle, "TitleBar.ToggleSidebar", "title bar command");

        var reachedMainNavigation = MoveFocusUntil(
            session,
            session.PressVirtualGamepadDPadDown,
            () => session.IsFocusWithinAutomationId("MainNavView"));

        Assert.True(
            reachedMainNavigation,
            $"Gamepad D-pad focus did not leave the title bar command group for main navigation."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");
    }

    [SkippableFact]
    public void MainNavigationStartItem_VirtualGamepadDPadDown_CanReachAnotherNavigationItem()
    {
        GuiTestGate.RequireEnabled();

        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();

        var startItem = session.FindByAutomationId("MainNav.Start", TimeSpan.FromSeconds(10));
        FocusAndAssert(session, startItem, "MainNav.Start", "start navigation item");

        var reachedNavigationItem = MoveFocusUntil(
            session,
            session.PressVirtualGamepadDPadDown,
            () => IsFocusOnConcreteMainNavigationItemOtherThan(session, "MainNav.Start"),
            attempts: 10);

        Assert.True(
            reachedNavigationItem,
            $"Virtual gamepad D-pad focus did not continue past the Start navigation item."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");
    }

    [SkippableFact]
    public void MainNavigationProjectItem_VirtualGamepadDPadDown_CanReachSessionChild()
    {
        GuiTestGate.RequireEnabled();

        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData(withContent: true);
        using var session = WindowsGuiAppSession.LaunchFresh();

        Assert.True(
            session.WaitUntilOnscreen("MainNav.Session.gui-session-01", TimeSpan.FromSeconds(15)),
            $"Session nav child did not become visible before keyboard focus navigation.{Environment.NewLine}{appData.ReadBootLogTail()}");

        var projectItem = session.FindByAutomationId("MainNav.Project.project-1", TimeSpan.FromSeconds(10));
        FocusAndAssert(session, projectItem, "MainNav.Project.project-1", "project navigation item");

        var reachedSessionChild = MoveFocusUntil(
            session,
            session.PressVirtualGamepadDPadDown,
            () => session.IsFocusWithinAutomationId("MainNav.Session.gui-session-01"),
            attempts: 8);

        Assert.True(
            reachedSessionChild,
            $"Virtual gamepad D-pad focus did not move from a project item into its session child."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");
    }

    [SkippableFact]
    public void MainNavigationSessionItem_VirtualGamepadDPadUp_CanReachParentProject()
    {
        GuiTestGate.RequireEnabled();

        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData(withContent: true);
        using var session = WindowsGuiAppSession.LaunchFresh();

        Assert.True(
            session.WaitUntilOnscreen("MainNav.Session.gui-session-01", TimeSpan.FromSeconds(15)),
            $"Session nav child did not become visible before gamepad upward navigation.{Environment.NewLine}{appData.ReadBootLogTail()}");

        var sessionItem = session.FindByAutomationId("MainNav.Session.gui-session-01", TimeSpan.FromSeconds(10));
        FocusAndAssert(session, sessionItem, "MainNav.Session.gui-session-01", "session navigation item");

        var reachedParentProject = MoveFocusUntil(
            session,
            session.PressVirtualGamepadDPadUp,
            () => session.IsFocusWithinAutomationId("MainNav.Project.project-1"),
            attempts: 8);

        Assert.True(
            reachedParentProject,
            $"Virtual gamepad D-pad focus did not move from a session child back to its parent project."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");
    }

    [SkippableFact]
    public void MainNavigationLastSession_VirtualGamepadDPadDown_CanReachNextProject()
    {
        GuiTestGate.RequireEnabled();

        using var appData = GuiAppDataScope.CreateDeterministicMultiProjectLeftNavData(
            projectCount: 2,
            sessionsPerProject: 1,
            withContent: true);
        using var session = WindowsGuiAppSession.LaunchFresh();

        Assert.True(
            session.WaitUntilOnscreen("MainNav.Session.gui-session-01", TimeSpan.FromSeconds(15)),
            $"First project session item did not become visible before cross-project gamepad navigation.{Environment.NewLine}{appData.ReadBootLogTail()}");
        Assert.True(
            session.WaitUntilOnscreen("MainNav.Project.project-2", TimeSpan.FromSeconds(15)),
            $"Second project item did not become visible before cross-project gamepad navigation.{Environment.NewLine}{appData.ReadBootLogTail()}");

        var sessionItem = session.FindByAutomationId("MainNav.Session.gui-session-01", TimeSpan.FromSeconds(10));
        FocusAndAssert(session, sessionItem, "MainNav.Session.gui-session-01", "last session item in first project");

        var reachedNextProject = MoveFocusUntil(
            session,
            session.PressVirtualGamepadDPadDown,
            () => session.IsFocusWithinAutomationId("MainNav.Project.project-2"),
            attempts: 8);

        Assert.True(
            reachedNextProject,
            $"Virtual gamepad D-pad focus did not leave the first project's last session for the next project."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");
    }

    [SkippableFact]
    public void RightTitleBarCommand_VirtualGamepadDPadDown_CanReachApplicationBody()
    {
        GuiTestGate.RequireEnabled();

        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();

        Assert.True(
            session.WaitUntilOnscreen("MainNav.Session.gui-session-01", TimeSpan.FromSeconds(15)),
            $"Session nav item did not become onscreen before title bar directional navigation.{Environment.NewLine}{appData.ReadBootLogTail()}");
        var sessionItem = session.FindByAutomationId("MainNav.Session.gui-session-01", TimeSpan.FromSeconds(2));
        session.FocusElement(sessionItem);
        session.PressEnter();
        Assert.True(
            session.WaitUntilVisible("ChatView.CurrentSessionTitle", TimeSpan.FromSeconds(10)),
            $"Chat view did not become visible before title bar directional navigation.{Environment.NewLine}{appData.ReadBootLogTail()}");

        var bottomPanelButton = session.FindByAutomationId("TitleBar.BottomPanel", TimeSpan.FromSeconds(10));
        FocusAndAssert(session, bottomPanelButton, "TitleBar.BottomPanel", "right title bar command");

        var reachedApplicationBody = MoveFocusUntil(
            session,
            session.PressVirtualGamepadDPadDown,
            () => IsFocusWithinApplicationBody(session));

        Assert.True(
            reachedApplicationBody,
            $"Gamepad D-pad focus did not leave the right title bar command group for the application body."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");
    }

    [SkippableFact]
    public void MainNavigationTopItem_VirtualGamepadDPadUp_CanReachTitleBar()
    {
        GuiTestGate.RequireEnabled();

        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();

        var startItem = session.FindByAutomationId("MainNav.Start", TimeSpan.FromSeconds(10));
        FocusAndAssert(session, startItem, "MainNav.Start", "top navigation item");

        var reachedTitleBar = MoveFocusUntil(
            session,
            session.PressVirtualGamepadDPadUp,
            () => session.IsFocusWithinAutomationId("TitleBar.ToggleSidebar")
                || session.IsFocusWithinAutomationId("TitleBar.BottomPanel")
                || session.IsFocusWithinAutomationId("TopSearchBox"),
            attempts: 6);

        Assert.True(
            reachedTitleBar,
            $"Virtual gamepad D-pad focus did not leave the top MainNav item for the title bar."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");
    }

    [SkippableFact]
    public void MainNavigationSessionItem_VirtualGamepadDPadRight_CanReachChatBody()
    {
        GuiTestGate.RequireEnabled();

        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();

        Assert.True(
            session.WaitUntilOnscreen("MainNav.Session.gui-session-01", TimeSpan.FromSeconds(15)),
            $"Session nav item did not become onscreen before rightward gamepad navigation.{Environment.NewLine}{appData.ReadBootLogTail()}");

        var sessionItem = session.FindByAutomationId("MainNav.Session.gui-session-01", TimeSpan.FromSeconds(10));
        session.FocusElement(sessionItem);
        session.PressEnter();
        Assert.True(
            session.WaitUntilVisible("ChatView.CurrentSessionTitle", TimeSpan.FromSeconds(10)),
            $"Chat view did not become visible before testing MainNav -> chat directional navigation.{Environment.NewLine}{appData.ReadBootLogTail()}");

        ClickAndAssertFocus(session, sessionItem, "MainNav.Session.gui-session-01", "session navigation item");

        var reachedChatBody = MoveFocusUntil(
            session,
            session.PressVirtualGamepadDPadRight,
            () => session.IsFocusWithinAutomationId("ChatView.MessagesList")
                || session.IsFocusWithinAutomationId("InputBox"),
            attempts: 6);

        Assert.True(
            reachedChatBody,
            $"Virtual gamepad D-pad focus did not leave MainNav for the chat body."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");
    }

    [SkippableFact]
    public void StartHeroSuggestions_VirtualGamepad_CanTraverseCards_AndActivateSelection()
    {
        GuiTestGate.RequireEnabled();

        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();

        const string firstId = "StartView.Suggestion.AnalyzeCodebase";
        const string secondId = "StartView.Suggestion.RecommendTasks";
        const string thirdId = "StartView.Suggestion.ResolveErrors";

        var firstCard = session.TryFindByAutomationId(firstId, TimeSpan.FromSeconds(10));
        var secondCard = session.TryFindByAutomationId(secondId, TimeSpan.FromSeconds(10));
        var thirdCard = session.TryFindByAutomationId(thirdId, TimeSpan.FromSeconds(10));

        Assert.True(
            firstCard is not null && secondCard is not null && thirdCard is not null,
            $"Expected all three start suggestion automation ids to be exposed. Buttons=[{string.Join(" | ", session.GetVisibleButtons())}]{Environment.NewLine}{appData.ReadBootLogTail()}");

        var startItem = session.FindByAutomationId("MainNav.Start", TimeSpan.FromSeconds(10));
        ClickAndAssertFocus(session, startItem, "MainNav.Start", "start navigation item");

        Assert.True(
            MoveFocusUntil(
                session,
                session.PressVirtualGamepadDPadRight,
                () => session.IsFocusWithinAutomationId(firstId)
                    || session.IsFocusWithinAutomationId(secondId)
                    || session.IsFocusWithinAutomationId(thirdId),
                attempts: 4),
            $"Virtual gamepad D-pad focus did not leave MainNav for the start suggestion strip.{Environment.NewLine}Focus={session.DescribeFocusedElement()}{Environment.NewLine}{appData.ReadBootLogTail()}");

        var timeline = new System.Collections.Generic.List<string>
        {
            $"initial focus={session.DescribeFocusedElement()}",
            DescribeSuggestionState(session, firstId, secondId, thirdId)
        };

        var reachedSecond = false;
        var reachedThird = false;
        for (var attempt = 0; attempt < 6; attempt++)
        {
            session.PressRight();
            Thread.Sleep(180);

            reachedSecond |= session.IsFocusWithinAutomationId(secondId) || session.TryGetIsSelected(secondId) == true;
            reachedThird |= session.IsFocusWithinAutomationId(thirdId) || session.TryGetIsSelected(thirdId) == true;
            timeline.Add($"after right {attempt + 1}: focus={session.DescribeFocusedElement()} ; {DescribeSuggestionState(session, firstId, secondId, thirdId)}");

            if (reachedSecond && reachedThird)
            {
                break;
            }
        }

        Assert.True(
            reachedSecond,
            $"Virtual gamepad focus never covered the second start suggestion.{Environment.NewLine}{string.Join(Environment.NewLine, timeline)}{Environment.NewLine}{appData.ReadBootLogTail()}");
        Assert.True(
            reachedThird,
            $"Virtual gamepad focus never covered the third start suggestion.{Environment.NewLine}{string.Join(Environment.NewLine, timeline)}{Environment.NewLine}{appData.ReadBootLogTail()}");

        session.PressVirtualGamepadA();

        var promptBox = session.FindByAutomationId("StartView.PromptBox", TimeSpan.FromSeconds(5)).AsTextBox();
        var promptText = promptBox.Text ?? string.Empty;
        Assert.Contains("我刚才遇到了一些报错", promptText, StringComparison.Ordinal);
    }

    [SkippableFact]
    public void StartPrompt_VirtualGamepadDPadUp_CanReturnToSuggestionCards()
    {
        GuiTestGate.RequireEnabled();

        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();

        var startItem = session.FindByAutomationId("MainNav.Start", TimeSpan.FromSeconds(10));
        ClickAndAssertFocus(session, startItem, "MainNav.Start", "start navigation item");

        Assert.True(
            MoveFocusUntil(
                session,
                session.PressVirtualGamepadDPadRight,
                () => session.IsFocusWithinAutomationId("StartView.Suggestion.AnalyzeCodebase")
                    || session.IsFocusWithinAutomationId("StartView.Suggestion.RecommendTasks")
                    || session.IsFocusWithinAutomationId("StartView.Suggestion.ResolveErrors"),
                attempts: 4),
            $"Virtual gamepad D-pad focus did not leave MainNav for the start suggestion strip before prompt return validation."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");

        Assert.True(
            MoveFocusUntil(
                session,
                session.PressVirtualGamepadDPadDown,
                () => session.IsFocusWithinAutomationId("StartView.PromptBox"),
                attempts: 4),
            $"Virtual gamepad D-pad focus did not enter the Start prompt box before return validation."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");

        session.PressVirtualGamepadDPadUp();

        Assert.True(
            WaitUntil(
                () => session.IsFocusWithinAutomationId("StartView.Suggestion.AnalyzeCodebase")
                    || session.IsFocusWithinAutomationId("StartView.Suggestion.RecommendTasks")
                    || session.IsFocusWithinAutomationId("StartView.Suggestion.ResolveErrors"),
                TimeSpan.FromSeconds(3)),
            $"Virtual gamepad D-pad Up did not return from Start prompt to suggestion cards."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");
    }

    [SkippableFact]
    public void ChatInputArea_VirtualGamepadDPadDown_CanReachInputBox()
    {
        GuiTestGate.RequireEnabled();

        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();

        Assert.True(
            session.WaitUntilOnscreen("MainNav.Session.gui-session-01", TimeSpan.FromSeconds(15)),
            $"Session nav item did not become onscreen before chat input gamepad navigation.{Environment.NewLine}{appData.ReadBootLogTail()}");

        var sessionItem = session.FindByAutomationId("MainNav.Session.gui-session-01", TimeSpan.FromSeconds(10));
        session.ClickElement(sessionItem);
        Assert.True(
            session.WaitUntilVisible("ChatView.CurrentSessionTitle", TimeSpan.FromSeconds(10)),
            $"Chat view did not become visible before chat input gamepad navigation.{Environment.NewLine}{appData.ReadBootLogTail()}");

        var reachedChatBody = MoveFocusUntil(
            session,
            session.PressVirtualGamepadDPadRight,
            () => session.IsFocusWithinAutomationId("ChatView.MessagesList")
                || session.IsFocusWithinAutomationId("InputBox"),
            attempts: 6);

        Assert.True(
            reachedChatBody,
            $"Virtual gamepad D-pad focus did not leave MainNav for the chat body before input-box validation."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");

        if (session.IsFocusWithinAutomationId("InputBox"))
        {
            return;
        }

        var reachedInput = MoveFocusUntil(
            session,
            session.PressVirtualGamepadDPadDown,
            () => session.IsFocusWithinAutomationId("InputBox"),
            attempts: 8);

        Assert.True(
            reachedInput,
            $"Virtual gamepad D-pad focus did not reach the chat input box."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");
    }

    [SkippableFact]
    public void SettingsCenter_VirtualGamepadDPadRight_CanReachSectionNavigation()
    {
        GuiTestGate.RequireEnabled();

        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();

        var settingsItem = session.FindByAutomationId("SettingsItem", TimeSpan.FromSeconds(10));
        ClickAndAssertFocus(session, settingsItem, "SettingsItem", "settings navigation item");

        var reachedSettingsSection = MoveFocusUntil(
            session,
            session.PressVirtualGamepadDPadRight,
            () => session.IsFocusWithinAutomationId("SettingsNav.General")
                || session.IsFocusWithinAutomationId("SettingsNav.Appearance")
                || session.IsFocusWithinAutomationId("SettingsNav.AgentAcp"),
            attempts: 6);

        Assert.True(
            reachedSettingsSection,
            $"Virtual gamepad D-pad focus did not leave Settings nav item for the settings section navigation."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");
    }

    [SkippableFact]
    public void SettingsSectionNavigation_VirtualGamepadDPadDown_CanReachDiagnosticsActions()
    {
        GuiTestGate.RequireEnabled();

        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();

        var settingsItem = session.FindByAutomationId("SettingsItem", TimeSpan.FromSeconds(10));
        session.ClickElement(settingsItem);
        Assert.True(
            session.WaitUntilOnscreen("SettingsNav.Diagnostics", TimeSpan.FromSeconds(10)),
            $"Settings navigation did not become visible before diagnostics gamepad validation.{Environment.NewLine}{appData.ReadBootLogTail()}");

        var diagnosticsItem = session.FindByAutomationId("SettingsNav.Diagnostics", TimeSpan.FromSeconds(10));
        ClickAndAssertFocus(session, diagnosticsItem, "SettingsNav.Diagnostics", "diagnostics settings navigation item");
        Assert.True(
            session.WaitUntilOnscreen("Diagnostics.GamepadMonitorHeader", TimeSpan.FromSeconds(10)),
            $"Diagnostics settings page did not become visible before diagnostics action validation.{Environment.NewLine}{appData.ReadBootLogTail()}");

        var reachedDiagnosticsAction = MoveFocusUntil(
            session,
            session.PressVirtualGamepadDPadDown,
            () => session.IsFocusWithinAutomationId("Diagnostics.GamepadStart")
                || session.IsFocusWithinAutomationId("Diagnostics.GamepadRefresh")
                || session.IsFocusWithinAutomationId("Diagnostics.GamepadStop"),
            attempts: 10);

        Assert.True(
            reachedDiagnosticsAction,
            $"Virtual gamepad D-pad focus did not move from settings section navigation into diagnostics action buttons."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");
    }

    [SkippableFact]
    public void SettingsContent_VirtualGamepadDPadUp_CanReturnToSectionNavigation()
    {
        GuiTestGate.RequireEnabled();

        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();
        EnsureMainWindowWide(session);

        var settingsItem = session.FindByAutomationId("SettingsItem", TimeSpan.FromSeconds(10));
        session.ClickElement(settingsItem);
        Assert.True(
            session.WaitUntilOnscreen("SettingsNav.Diagnostics", TimeSpan.FromSeconds(10)),
            $"Settings navigation did not become visible before section return validation.{Environment.NewLine}{appData.ReadBootLogTail()}");

        var diagnosticsItem = session.FindByAutomationId("SettingsNav.Diagnostics", TimeSpan.FromSeconds(10));
        ClickAndAssertFocus(session, diagnosticsItem, "SettingsNav.Diagnostics", "diagnostics settings navigation item");
        Assert.True(
            session.WaitUntilOnscreen("Diagnostics.GamepadMonitorHeader", TimeSpan.FromSeconds(10)),
            $"Diagnostics settings page did not become visible before section return validation.{Environment.NewLine}{appData.ReadBootLogTail()}");

        var startButton = FindAndScrollIntoView(session, "Diagnostics.GamepadStart", TimeSpan.FromSeconds(10));
        session.ClickElement(startButton);
        Assert.True(
            WaitUntil(
                () => session.IsFocusWithinAutomationId("Diagnostics.GamepadStart")
                    || session.IsFocusWithinAutomationId("Diagnostics.GamepadRefresh")
                    || session.IsFocusWithinAutomationId("Diagnostics.GamepadStop"),
                TimeSpan.FromSeconds(2)),
            $"Unable to establish diagnostics action focus before directional navigation."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");

        session.PressVirtualGamepadDPadUp();

        Assert.True(
            WaitUntil(
                () => session.IsFocusWithinAutomationId("SettingsNav.Diagnostics")
                    || session.IsFocusWithinAutomationId("SettingsNav.General")
                    || session.IsFocusWithinAutomationId("SettingsNav.Appearance"),
                TimeSpan.FromSeconds(3)),
            $"Virtual gamepad D-pad Up did not return from settings content to section navigation."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");
    }

    [SkippableFact]
    public void DiscoverSessions_VirtualGamepadDPadRight_CanReachProfilesList()
    {
        GuiTestGate.RequireEnabled();

        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();

        var discoverItem = session.FindByAutomationId("MainNav.DiscoverSessions", TimeSpan.FromSeconds(10));
        ClickAndAssertFocus(session, discoverItem, "MainNav.DiscoverSessions", "discover navigation item");

        var reachedProfiles = MoveFocusUntil(
            session,
            session.PressVirtualGamepadDPadRight,
            () => session.IsFocusWithinAutomationId("DiscoverSessions.ProfilesList"),
            attempts: 6);

        Assert.True(
            reachedProfiles,
            $"Virtual gamepad D-pad focus did not leave Discover nav item for the profiles list."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");
    }

    [SkippableFact]
    public void DiscoverProfilesSelection_VirtualGamepadDPadRight_CanReachImportAction()
    {
        GuiTestGate.RequireEnabled();

        using var appData = GuiAppDataScope.CreateDeterministicSlowRemoteReplayData(
            cachedMessageCount: 1,
            replayMessageCount: 12,
            includeLocalConversation: false,
            remoteConversationCount: 1);
        using var session = WindowsGuiAppSession.LaunchFresh();
        ResizeMainWindow(width: 800, height: 900);

        OpenDiscoverSessions(session);
        Assert.True(
            session.WaitUntilVisible("DiscoverSessions.Title", TimeSpan.FromSeconds(10)),
            $"Discover sessions page did not become visible before import-action validation.{Environment.NewLine}{appData.ReadBootLogTail()}");

        var profilesList = session.FindByAutomationId("DiscoverSessions.ProfilesList", TimeSpan.FromSeconds(10));
        var profileItems = profilesList
            .FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem))
            .ToArray();
        Assert.True(profileItems.Length >= 1, $"Discover profiles list did not expose any child items.{Environment.NewLine}{appData.ReadBootLogTail()}");
        session.ClickElement(profileItems[0]);

        Assert.True(
            session.WaitUntilOnscreen("DiscoverSessions.SessionsList", TimeSpan.FromSeconds(10)),
            $"Discover sessions list did not become visible before import-action validation.{Environment.NewLine}{appData.ReadBootLogTail()}");

        var sessionsList = session.FindByAutomationId("DiscoverSessions.SessionsList", TimeSpan.FromSeconds(10));
        var sessionItems = sessionsList
            .FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem))
            .ToArray();
        Assert.True(sessionItems.Length >= 1, $"Discover sessions list did not expose any session items.{Environment.NewLine}{appData.ReadBootLogTail()}");
        ClickAndAssertListItemFocus(session, sessionItems[0], "discover session list item");

        var reachedImport = MoveFocusUntil(
            session,
            session.PressVirtualGamepadDPadRight,
            () => session.IsFocusWithinAutomationId("DiscoverSessions.ImportButton"),
            attempts: 10);

        Assert.True(
            reachedImport,
            $"Virtual gamepad D-pad focus did not reach the Discover import action."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");
    }

    [SkippableFact]
    public void GamepadB_FromDiscoverDetails_ReturnsToProfilesPane()
    {
        GuiTestGate.RequireEnabled();

        using var appData = GuiAppDataScope.CreateDeterministicSlowRemoteReplayData(
            cachedMessageCount: 1,
            replayMessageCount: 12,
            includeLocalConversation: false,
            remoteConversationCount: 1);
        using var session = WindowsGuiAppSession.LaunchFresh();
        OpenDiscoverSessions(session);
        Assert.True(
            session.WaitUntilVisible("DiscoverSessions.Title", TimeSpan.FromSeconds(10)),
            $"Discover sessions page did not become visible before back-semantics validation.{Environment.NewLine}{appData.ReadBootLogTail()}");
        ResizeMainWindow(width: 700, height: 900);
        Thread.Sleep(250);

        var profilesList = session.FindByAutomationId("DiscoverSessions.ProfilesList", TimeSpan.FromSeconds(10));
        var profileItems = profilesList
            .FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem))
            .ToArray();
        Assert.True(profileItems.Length >= 1, $"Discover profiles list did not expose any child items.{Environment.NewLine}{appData.ReadBootLogTail()}");
        session.ClickElement(profileItems[0]);

        Assert.True(
            session.WaitUntilOnscreen("DiscoverSessions.SessionsList", TimeSpan.FromSeconds(10)),
            $"Discover sessions list did not become visible before GamepadB validation.{Environment.NewLine}{appData.ReadBootLogTail()}");

        session.PressVirtualGamepadB();

        Assert.True(
            WaitUntil(
                () => session.IsFocusWithinAutomationId("DiscoverSessions.ProfilesList")
                    || session.TryFindByAutomationId("DiscoverSessions.SessionsList", TimeSpan.FromMilliseconds(100)) is null,
                TimeSpan.FromSeconds(5)),
            $"GamepadB did not return Discover from details to the profiles pane."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");
        AssertMainNavSemanticSelection(session, "DiscoverSessions");
    }

    [SkippableFact]
    public void GamepadB_FromDiscoverPage_ReturnsToStartPage()
    {
        GuiTestGate.RequireEnabled();

        using var appData = GuiAppDataScope.CreateDeterministicSlowRemoteReplayData(
            cachedMessageCount: 1,
            replayMessageCount: 12,
            includeLocalConversation: false,
            remoteConversationCount: 1);
        using var session = WindowsGuiAppSession.LaunchFresh();

        var discoverItem = session.FindByAutomationId("MainNav.DiscoverSessions", TimeSpan.FromSeconds(10));
        session.ClickElement(discoverItem);
        Assert.True(
            session.WaitUntilVisible("DiscoverSessions.Title", TimeSpan.FromSeconds(10)),
            $"Discover sessions page did not become visible before page-level GamepadB validation.{Environment.NewLine}{appData.ReadBootLogTail()}");

        session.PressVirtualGamepadB();

        Assert.True(
            session.WaitUntilVisible("StartView.Title", TimeSpan.FromSeconds(10)),
            $"GamepadB did not return Discover page back to Start."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");
        AssertMainNavSemanticSelection(session, "Start");
    }

    [SkippableFact]
    public void GamepadB_FromSettingsPage_ReturnsToStartPage()
    {
        GuiTestGate.RequireEnabled();

        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();

        var settingsItem = session.FindByAutomationId("SettingsItem", TimeSpan.FromSeconds(10));
        session.ClickElement(settingsItem);
        Assert.True(
            session.WaitUntilOnscreen("SettingsNav.General", TimeSpan.FromSeconds(10)),
            $"Settings page did not become visible before page-level GamepadB validation.{Environment.NewLine}{appData.ReadBootLogTail()}");

        session.PressVirtualGamepadB();

        Assert.True(
            session.WaitUntilVisible("StartView.Title", TimeSpan.FromSeconds(10)),
            $"GamepadB did not return Settings page back to Start."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");
        AssertMainNavSemanticSelection(session, "Start");
    }

    [SkippableFact]
    public void GamepadB_FromChatPage_ReturnsToStartPage()
    {
        GuiTestGate.RequireEnabled();

        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();

        Assert.True(
            session.WaitUntilOnscreen("MainNav.Session.gui-session-01", TimeSpan.FromSeconds(15)),
            $"Session nav item did not become onscreen before chat back validation.{Environment.NewLine}{appData.ReadBootLogTail()}");

        var sessionItem = session.FindByAutomationId("MainNav.Session.gui-session-01", TimeSpan.FromSeconds(10));
        session.ClickElement(sessionItem);
        Assert.True(
            session.WaitUntilVisible("ChatView.CurrentSessionTitle", TimeSpan.FromSeconds(10)),
            $"Chat view did not become visible before GamepadB validation.{Environment.NewLine}{appData.ReadBootLogTail()}");

        session.PressVirtualGamepadB();

        Assert.True(
            session.WaitUntilVisible("StartView.Title", TimeSpan.FromSeconds(10)),
            $"GamepadB did not return Chat page back to Start."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");
        AssertMainNavSemanticSelection(session, "Start");
    }

    private static bool MoveFocusUntil(
        WindowsGuiAppSession session,
        Action move,
        Func<bool> condition,
        int attempts = 6)
    {
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            move();
            if (WaitUntil(condition, TimeSpan.FromMilliseconds(150)))
            {
                return true;
            }
        }

        return false;
    }

    private static void FocusAndAssert(
        WindowsGuiAppSession session,
        AutomationElement element,
        string automationId,
        string description)
    {
        session.BringMainWindowToFront();
        for (var attempt = 0; attempt < 4; attempt++)
        {
            session.FocusElement(element);
            if (WaitUntil(
                    () => session.IsFocusWithinAutomationId(automationId),
                    TimeSpan.FromMilliseconds(250)))
            {
                return;
            }
        }

        Assert.Fail(
            $"Unable to establish {description} focus before directional navigation."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}");
    }

    private static void FocusElementAndWait(
        WindowsGuiAppSession session,
        AutomationElement element,
        string description)
    {
        session.BringMainWindowToFront();
        for (var attempt = 0; attempt < 4; attempt++)
        {
            session.FocusElement(element);
            if (WaitUntil(() => session.IsFocusedElement(element), TimeSpan.FromMilliseconds(250)))
            {
                return;
            }
        }

        Assert.Fail(
            $"Unable to establish {description} focus before directional navigation."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}");
    }

    private static void ClickAndAssertFocus(
        WindowsGuiAppSession session,
        AutomationElement element,
        string automationId,
        string description)
    {
        session.BringMainWindowToFront();
        for (var attempt = 0; attempt < 4; attempt++)
        {
            session.ClickElement(element);
            if (WaitUntil(
                    () => session.IsFocusWithinAutomationId(automationId),
                    TimeSpan.FromMilliseconds(300)))
            {
                return;
            }
        }

        Assert.Fail(
            $"Unable to establish {description} focus before directional navigation."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}");
    }

    private static void ClickAndAssertListItemFocus(
        WindowsGuiAppSession session,
        AutomationElement element,
        string description)
    {
        session.BringMainWindowToFront();
        for (var attempt = 0; attempt < 4; attempt++)
        {
            session.ClickElement(element);
            if (WaitUntil(() => session.IsFocusedElement(element), TimeSpan.FromMilliseconds(300)))
            {
                return;
            }
        }

        Assert.Fail(
            $"Unable to establish {description} focus before directional navigation."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}");
    }

    private static void OpenDiscoverSessions(WindowsGuiAppSession session)
    {
        var discoverItem = session.FindByAutomationId("MainNav.DiscoverSessions", TimeSpan.FromSeconds(10));
        session.BringMainWindowToFront();

        for (var attempt = 0; attempt < 3; attempt++)
        {
            session.ActivateElement(discoverItem);
            if (WaitUntil(() => session.WaitUntilVisible("DiscoverSessions.Title", TimeSpan.FromMilliseconds(250)), TimeSpan.FromMilliseconds(500)))
            {
                return;
            }
        }

        Assert.Fail(
            $"Unable to activate Discover sessions through the native nav item."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}");
    }

    private static AutomationElement FindAndScrollIntoView(
        WindowsGuiAppSession session,
        string automationId,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        AutomationElement? element = null;
        while (DateTime.UtcNow < deadline)
        {
            element = session.TryFindByAutomationId(automationId, TimeSpan.FromMilliseconds(250));
            if (element is not null)
            {
                break;
            }

            session.ScrollWheel(-120);
            Thread.Sleep(120);
        }

        element ??= session.FindByAutomationId(automationId, TimeSpan.FromMilliseconds(250));
        if (element.Patterns.ScrollItem.IsSupported)
        {
            element.Patterns.ScrollItem.Pattern.ScrollIntoView();
            Thread.Sleep(150);
        }

        return element;
    }

    private static void EnsureMainWindowWide(WindowsGuiAppSession session)
    {
        try
        {
            if (session.MainWindow.Patterns.Window.IsSupported)
            {
                session.MainWindow.Patterns.Window.Pattern.SetWindowVisualState(WindowVisualState.Normal);
            }
        }
        catch
        {
        }

        ResizeMainWindow(width: 1400, height: 900);
    }

    private static void ResizeMainWindow(int width, int height)
    {
        var process = Process.GetProcessesByName("SalmonEgg")
            .OrderByDescending(candidate => candidate.StartTime)
            .First();

        if (NativeMethods.MoveWindow(process.MainWindowHandle, 80, 80, width, height, true))
        {
            return;
        }

        if (NativeMethods.SetWindowPos(process.MainWindowHandle, IntPtr.Zero, 80, 80, width, height, 0))
        {
            return;
        }

        if (NativeMethods.TryGetWindowSize(process.MainWindowHandle, out var currentWidth, out var currentHeight)
            && Math.Abs(currentWidth - width) <= 2
            && Math.Abs(currentHeight - height) <= 2)
        {
            return;
        }

        throw new InvalidOperationException("Failed to resize the SalmonEgg window.");
    }

    private static class NativeMethods
    {
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        internal static extern bool MoveWindow(IntPtr hWnd, int x, int y, int width, int height, bool repaint);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        internal static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);

        internal static bool TryGetWindowSize(IntPtr hWnd, out int width, out int height)
        {
            if (GetWindowRect(hWnd, out var rect))
            {
                width = rect.Right - rect.Left;
                height = rect.Bottom - rect.Top;
                return true;
            }

            width = 0;
            height = 0;
            return false;
        }
    }

    private static bool IsFocusWithinApplicationBody(WindowsGuiAppSession session)
        => session.IsFocusWithinAutomationId("MainNavView")
            || session.IsFocusWithinAutomationId("ChatView.MessagesList")
            || session.IsFocusWithinAutomationId("InputBox");

    private static string DescribeSuggestionState(
        WindowsGuiAppSession session,
        string firstId,
        string secondId,
        string thirdId)
        => string.Join(
            "; ",
            $"{firstId}=selected:{session.TryGetIsSelected(firstId)?.ToString() ?? "<null>"} focus:{session.IsFocusWithinAutomationId(firstId)}",
            $"{secondId}=selected:{session.TryGetIsSelected(secondId)?.ToString() ?? "<null>"} focus:{session.IsFocusWithinAutomationId(secondId)}",
            $"{thirdId}=selected:{session.TryGetIsSelected(thirdId)?.ToString() ?? "<null>"} focus:{session.IsFocusWithinAutomationId(thirdId)}");

    private static bool IsFocusOnConcreteMainNavigationItemOtherThan(
        WindowsGuiAppSession session,
        string excludedAutomationId)
    {
        var focusPath = session.GetFocusedAutomationIdPath();
        return focusPath.Count > 1
            && focusPath.Contains("MainNavView", StringComparer.Ordinal)
            && !string.Equals(focusPath[0], "MainNavView", StringComparison.Ordinal)
            && !string.Equals(focusPath[0], excludedAutomationId, StringComparison.Ordinal);
    }

    private static void AssertMainNavSemanticSelection(WindowsGuiAppSession session, string expectedSemantic)
    {
        var state = session.TryGetElementName("MainNav.Automation.SelectionState", TimeSpan.FromSeconds(2)) ?? string.Empty;
        Assert.Contains($"Semantic={expectedSemantic}", state, StringComparison.Ordinal);
    }

    private static bool WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(50);
        }

        return condition();
    }
}
