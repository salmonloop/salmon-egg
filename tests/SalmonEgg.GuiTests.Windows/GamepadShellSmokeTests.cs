using System;
using System.Diagnostics;
using System.Threading;
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
        FocusAndAssert(session, sessionItem, "MainNav.Session.gui-session-01", "session navigation item");

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

    private static bool IsFocusWithinApplicationBody(WindowsGuiAppSession session)
        => session.IsFocusWithinAutomationId("MainNavView")
            || session.IsFocusWithinAutomationId("ChatView.MessagesList")
            || session.IsFocusWithinAutomationId("InputBox");

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
