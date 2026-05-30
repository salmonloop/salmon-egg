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
        var gamepad = session.CreateGamepadInput();

        Assert.True(
            session.WaitUntilOnscreen("MainNav.Session.gui-session-01", TimeSpan.FromSeconds(15)),
            $"Session nav child did not become visible before keyboard focus navigation.{Environment.NewLine}{appData.ReadBootLogTail()}");

        var projectItem = session.FindByAutomationId("MainNav.Project.project-1", TimeSpan.FromSeconds(10));
        FocusAndAssert(session, projectItem, "MainNav.Project.project-1", "project navigation item");

        var reachedSessionChild = MoveFocusUntil(
            session,
            gamepad.PressDown,
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
    public void MainNavigationSessionItem_VirtualGamepadDPadRight_CanReachChatInputBox()
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

        var reachedChatInputBox = MoveFocusUntil(
            session,
            session.PressVirtualGamepadDPadRight,
            () => session.IsFocusWithinAutomationId("InputBox"),
            attempts: 6);

        Assert.True(
            reachedChatInputBox,
            $"Virtual gamepad D-pad focus did not leave MainNav for the chat input box."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");
    }

    [SkippableFact]
    public void StartHeroSuggestions_VirtualGamepad_CanTraverseCards_AndActivateSelection()
    {
        GuiTestGate.RequireEnabled();

        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();
        EnsureMainWindowWide(session);

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
    public void StartChatInputSelectors_VirtualGamepadDPadUp_CanReturnToInputBox()
    {
        GuiTestGate.RequireEnabled();

        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();
        EnsureMainWindowWide(session);

        Assert.True(
            session.WaitUntilVisible("StartView.PromptBox", TimeSpan.FromSeconds(10)),
            $"Start prompt box did not become visible before selector return validation.{Environment.NewLine}{appData.ReadBootLogTail()}");

        var promptBox = session.FindByAutomationId("StartView.PromptBox", TimeSpan.FromSeconds(10));
        FocusAndAssert(session, promptBox, "StartView.PromptBox", "start prompt box");

        session.PressVirtualGamepadDPadDown();

        Assert.True(
            WaitUntil(
                () => session.IsFocusWithinAutomationId("StartView.AgentSelector")
                    || session.IsFocusWithinAutomationId("StartView.ModeSelector")
                    || session.IsFocusWithinAutomationId("StartView.ProjectSelector"),
                TimeSpan.FromSeconds(3)),
            $"Virtual gamepad D-pad Down did not reach any start composer selector before return validation."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");

        session.PressVirtualGamepadDPadUp();

        Assert.True(
            WaitUntil(
                () => session.IsFocusWithinAutomationId("StartView.PromptBox"),
                TimeSpan.FromSeconds(3)),
            $"Virtual gamepad D-pad Up did not return from the start selector row to the prompt box."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");
    }

    [SkippableFact]
    public void StartPromptBox_VirtualGamepadDPadUp_CanReturnToFirstHeroSuggestion()
    {
        GuiTestGate.RequireEnabled();

        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();
        EnsureMainWindowWide(session);

        var promptBox = session.FindByAutomationId("StartView.PromptBox", TimeSpan.FromSeconds(10));
        FocusAndAssert(session, promptBox, "StartView.PromptBox", "start prompt box");

        session.PressVirtualGamepadDPadUp();

        Assert.True(
            WaitUntil(
                () => session.IsFocusWithinAutomationId("StartView.Suggestion.AnalyzeCodebase"),
                TimeSpan.FromSeconds(3)),
            $"Virtual gamepad D-pad Up did not return from the start prompt box to the first hero suggestion."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");
    }

    [SkippableFact]
    public void StartPromptBox_VirtualGamepadDPadDown_CanReachFirstSelector()
    {
        GuiTestGate.RequireEnabled();

        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();
        EnsureMainWindowWide(session);

        var promptBox = session.FindByAutomationId("StartView.PromptBox", TimeSpan.FromSeconds(10));
        FocusAndAssert(session, promptBox, "StartView.PromptBox", "start prompt box");

        session.PressVirtualGamepadDPadDown();

        Assert.True(
            WaitUntil(
                () => session.IsFocusWithinAutomationId("StartView.AgentSelector")
                    || session.IsFocusWithinAutomationId("StartView.ModeSelector")
                    || session.IsFocusWithinAutomationId("StartView.ProjectSelector"),
                TimeSpan.FromSeconds(3)),
            $"Virtual gamepad D-pad Down did not leave the start prompt box for the composer selectors."
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
    public void ChatInputArea_ModeSelector_VirtualGamepadDPadUp_CanReturnToInputBox()
    {
        GuiTestGate.RequireEnabled();

        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData(withContent: true);
        using var session = WindowsGuiAppSession.LaunchFresh();

        Assert.True(
            session.WaitUntilOnscreen("MainNav.Session.gui-session-01", TimeSpan.FromSeconds(15)),
            $"Session nav item did not become onscreen before chat selector return validation.{Environment.NewLine}{appData.ReadBootLogTail()}");

        var sessionItem = session.FindByAutomationId("MainNav.Session.gui-session-01", TimeSpan.FromSeconds(10));
        session.ClickElement(sessionItem);
        Assert.True(
            session.WaitUntilVisible("ChatView.CurrentSessionTitle", TimeSpan.FromSeconds(10)),
            $"Chat view did not become visible before selector return validation.{Environment.NewLine}{appData.ReadBootLogTail()}");
        Assert.True(
            session.WaitUntilOnscreen("ChatInputArea.ModeSelector", TimeSpan.FromSeconds(6)),
            $"Chat mode selector did not become visible before selector return validation.{Environment.NewLine}{appData.ReadBootLogTail()}");

        var modeSelector = session.FindByAutomationId("ChatInputArea.ModeSelector", TimeSpan.FromSeconds(10));
        FocusAndAssert(session, modeSelector, "ChatInputArea.ModeSelector", "chat mode selector");

        session.PressVirtualGamepadDPadUp();

        Assert.True(
            WaitUntil(
                () => session.IsFocusWithinAutomationId("InputBox"),
                TimeSpan.FromSeconds(3)),
            $"Virtual gamepad D-pad Up did not return from the chat mode selector to the input box."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");
    }

    [SkippableFact]
    public void ChatInputArea_ComposerHorizontalTraversal_CanReachActionButtonAndReturnToTrailingVisibleSelector()
    {
        GuiTestGate.RequireEnabled();

        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData(withContent: true);
        using var session = WindowsGuiAppSession.LaunchFresh();
        var focusTimeline = new List<string>();

        Assert.True(
            session.WaitUntilOnscreen("MainNav.Session.gui-session-01", TimeSpan.FromSeconds(15)),
            $"Session nav item did not become onscreen before chat selector horizontal traversal validation.{Environment.NewLine}{appData.ReadBootLogTail()}");

        var sessionItem = session.FindByAutomationId("MainNav.Session.gui-session-01", TimeSpan.FromSeconds(10));
        session.ClickElement(sessionItem);
        Assert.True(
            session.WaitUntilVisible("ChatView.CurrentSessionTitle", TimeSpan.FromSeconds(10)),
            $"Chat view did not become visible before selector horizontal traversal validation.{Environment.NewLine}{appData.ReadBootLogTail()}");
        Assert.True(
            session.WaitUntilOnscreen("ChatInputArea.ModeSelector", TimeSpan.FromSeconds(6)),
            $"Chat mode selector did not become visible before selector horizontal traversal validation.{Environment.NewLine}{appData.ReadBootLogTail()}");

        var modeSelector = session.FindByAutomationId("ChatInputArea.ModeSelector", TimeSpan.FromSeconds(10));
        FocusAndAssert(session, modeSelector, "ChatInputArea.ModeSelector", "chat mode selector");
        focusTimeline.Add($"start: {session.DescribeFocusedElementDetailed()}");
        var expectedReturnSelectorAutomationId =
            session.WaitUntilOnscreen("ChatInputArea.ProjectSelector", TimeSpan.FromMilliseconds(250)) ? "ChatInputArea.ProjectSelector" :
            session.WaitUntilOnscreen("ChatInputArea.ModeSelector", TimeSpan.FromMilliseconds(250)) ? "ChatInputArea.ModeSelector" :
            "ChatInputArea.AgentSelector";

        Assert.True(
            MoveFocusUntil(
                session,
                () =>
                {
                    session.PressVirtualGamepadDPadRight();
                    focusTimeline.Add($"after right: {session.DescribeFocusedElementDetailed()}");
                },
                () => session.IsFocusWithinAutomationId("VoiceInputStartButton")
                    || session.IsFocusWithinAutomationId("VoiceInputStopButton")
                    || session.IsFocusWithinAutomationId("SendButton")
                    || session.IsFocusWithinAutomationId("CancelButton"),
                attempts: 6),
            $"Virtual gamepad D-pad Right did not leave the chat mode selector for any composer action button."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}Timeline={string.Join(Environment.NewLine, focusTimeline)}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");

        Assert.True(
            MoveFocusUntil(
                session,
                () =>
                {
                    session.PressVirtualGamepadDPadLeft();
                    focusTimeline.Add($"after left: {session.DescribeFocusedElementDetailed()}");
                },
                () => session.IsFocusWithinAutomationId(expectedReturnSelectorAutomationId),
                attempts: 6),
            $"Virtual gamepad D-pad Left did not return from the composer action buttons to the trailing visible selector."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}ExpectedReturn={expectedReturnSelectorAutomationId}"
            + $"{Environment.NewLine}Timeline={string.Join(Environment.NewLine, focusTimeline)}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");
    }

    [SkippableFact]
    public void ChatInputArea_ComposerHorizontalTraversal_KeyboardCanReturnToTrailingVisibleSelector()
    {
        GuiTestGate.RequireEnabled();

        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData(withContent: true);
        using var session = WindowsGuiAppSession.LaunchFresh();
        var focusTimeline = new List<string>();

        Assert.True(
            session.WaitUntilOnscreen("MainNav.Session.gui-session-01", TimeSpan.FromSeconds(15)),
            $"Session nav item did not become onscreen before chat keyboard horizontal traversal validation.{Environment.NewLine}{appData.ReadBootLogTail()}");

        var sessionItem = session.FindByAutomationId("MainNav.Session.gui-session-01", TimeSpan.FromSeconds(10));
        session.ClickElement(sessionItem);
        Assert.True(
            session.WaitUntilVisible("ChatView.CurrentSessionTitle", TimeSpan.FromSeconds(10)),
            $"Chat view did not become visible before keyboard horizontal traversal validation.{Environment.NewLine}{appData.ReadBootLogTail()}");
        Assert.True(
            session.WaitUntilOnscreen("ChatInputArea.ModeSelector", TimeSpan.FromSeconds(6)),
            $"Chat mode selector did not become visible before keyboard horizontal traversal validation.{Environment.NewLine}{appData.ReadBootLogTail()}");

        var modeSelector = session.FindByAutomationId("ChatInputArea.ModeSelector", TimeSpan.FromSeconds(10));
        FocusAndAssert(session, modeSelector, "ChatInputArea.ModeSelector", "chat mode selector");
        focusTimeline.Add($"start: {session.DescribeFocusedElementDetailed()}");
        var expectedReturnSelectorAutomationId =
            session.WaitUntilOnscreen("ChatInputArea.ProjectSelector", TimeSpan.FromMilliseconds(250)) ? "ChatInputArea.ProjectSelector" :
            session.WaitUntilOnscreen("ChatInputArea.ModeSelector", TimeSpan.FromMilliseconds(250)) ? "ChatInputArea.ModeSelector" :
            "ChatInputArea.AgentSelector";

        Assert.True(
            MoveFocusUntil(
                session,
                () =>
                {
                    session.PressRight();
                    focusTimeline.Add($"after right: {session.DescribeFocusedElementDetailed()}");
                },
                () => session.IsFocusWithinAutomationId("VoiceInputStartButton")
                    || session.IsFocusWithinAutomationId("VoiceInputStopButton")
                    || session.IsFocusWithinAutomationId("SendButton")
                    || session.IsFocusWithinAutomationId("CancelButton"),
                attempts: 6),
            $"Keyboard Right did not leave the chat mode selector for any composer action button."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}Timeline={string.Join(Environment.NewLine, focusTimeline)}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");

        Assert.True(
            MoveFocusUntil(
                session,
                () =>
                {
                    session.PressLeft();
                    focusTimeline.Add($"after left: {session.DescribeFocusedElementDetailed()}");
                },
                () => session.IsFocusWithinAutomationId(expectedReturnSelectorAutomationId),
                attempts: 6),
            $"Keyboard Left did not return from the composer action buttons to the trailing visible selector."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}ExpectedReturn={expectedReturnSelectorAutomationId}"
            + $"{Environment.NewLine}Timeline={string.Join(Environment.NewLine, focusTimeline)}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");
    }

    [SkippableFact]
    public void SettingsCenter_VirtualGamepadDPadRight_CanReachSectionNavigation()
    {
        GuiTestGate.RequireEnabled();

        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();

        var settingsItem = OpenSettingsAndWaitForSectionNavigation(session, appData, "section-navigation validation");

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
    public void SettingsContent_VirtualGamepadDPadUp_CanReturnToSectionNavigation()
    {
        GuiTestGate.RequireEnabled();

        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();
        EnsureMainWindowWide(session);

        _ = OpenSettingsAndWaitForSectionNavigation(session, appData, "section return validation");

        var appearanceItem = session.FindByAutomationId("SettingsNav.Appearance", TimeSpan.FromSeconds(10));
        FocusAndAssert(session, appearanceItem, "SettingsNav.Appearance", "appearance settings navigation item");
        session.PressEnter();
        Assert.True(
            session.WaitUntilOnscreen("Appearance.Theme", TimeSpan.FromSeconds(10)),
            $"Appearance settings page did not become visible before section return validation.{Environment.NewLine}{appData.ReadBootLogTail()}");

        var firstControl = FindAndScrollIntoView(session, "Appearance.Theme", TimeSpan.FromSeconds(10));
        FocusAndAssert(session, firstControl, "Appearance.Theme", "appearance first control");

        Assert.True(
            MoveFocusUntil(
                session,
                session.PressVirtualGamepadDPadDown,
                () => session.IsFocusWithinAutomationId("Appearance.Animation")
                    || session.IsFocusWithinAutomationId("Appearance.Theme"),
                attempts: 4),
            $"Virtual gamepad D-pad Down did not move from appearance section navigation into appearance content."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");

        Assert.True(
            MoveFocusUntil(
                session,
                session.PressVirtualGamepadDPadUp,
                () => session.IsFocusWithinAutomationId("SettingsNav.Appearance")
                    || session.IsFocusWithinAutomationId("SettingsNav.General")
                    || session.IsFocusWithinAutomationId("SettingsNav.AgentAcp"),
                attempts: 4),
            $"Virtual gamepad D-pad Up did not eventually return from settings content to section navigation."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");
    }

    [SkippableFact]
    public void SettingsAboutContent_VirtualGamepadDPadUp_CanReturnToSectionNavigation()
    {
        GuiTestGate.RequireEnabled();

        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();
        var gamepad = session.CreateGamepadInput();
        EnsureMainWindowWide(session);

        _ = OpenSettingsAndWaitForSectionNavigation(session, appData, "about section return validation");

        var aboutItem = session.FindByAutomationId("SettingsNav.About", TimeSpan.FromSeconds(10));
        FocusAndAssert(session, aboutItem, "SettingsNav.About", "about settings navigation item");
        session.PressEnter();
        Assert.True(
            MoveFocusUntil(
                session,
                gamepad.PressDown,
                () => session.IsFocusWithinAutomationId("About.Support.OpenAppData")
                    || session.IsFocusWithinAutomationId("About.Support.OpenReleaseNotes")
                    || session.IsFocusWithinAutomationId("About.Support.OpenPrivacyPolicy")
                    || session.IsFocusWithinAutomationId("About.Support.CopyVersionInfo"),
                attempts: 6),
            $"Virtual gamepad D-pad Down did not enter the About support actions from the section navigation item."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");

        gamepad.PressUp();

        Assert.True(
            WaitUntil(
                () => session.IsFocusWithinAutomationId("SettingsNav.About"),
                TimeSpan.FromSeconds(3)),
            $"Virtual gamepad D-pad Up did not return from the about support action to the About section navigation item."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");
    }

    [SkippableFact]
    public void SettingsMcpTopRowAction_VirtualGamepadDPadUp_CanReturnToSectionNavigation()
    {
        GuiTestGate.RequireEnabled();

        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();
        EnsureMainWindowWide(session);

        _ = OpenSettingsAndWaitForSectionNavigation(session, appData, "MCP section return validation");

        var mcpItem = session.FindByAutomationId("SettingsNav.Mcp", TimeSpan.FromSeconds(10));
        FocusAndAssert(session, mcpItem, "SettingsNav.Mcp", "MCP settings navigation item");
        session.PressEnter();
        Assert.True(
            session.WaitUntilOnscreen("Mcp.AddServer", TimeSpan.FromSeconds(10)),
            $"MCP settings page did not become visible before section return validation.{Environment.NewLine}{appData.ReadBootLogTail()}");

        var addServerButton = session.FindByAutomationId("Mcp.AddServer", TimeSpan.FromSeconds(10));
        FocusAndAssert(session, addServerButton, "Mcp.AddServer", "MCP add server action");

        session.PressVirtualGamepadDPadUp();

        Assert.True(
            WaitUntil(
                () => session.IsFocusWithinAutomationId("SettingsNav.Mcp"),
                TimeSpan.FromSeconds(3)),
            $"Virtual gamepad D-pad Up did not return from the MCP top-row action to the MCP section navigation item."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");
    }

    [SkippableFact]
    public void SettingsShortcutsContent_VirtualGamepadDPadUp_CanReturnToSectionNavigation()
    {
        GuiTestGate.RequireEnabled();

        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();
        var gamepad = session.CreateGamepadInput();
        EnsureMainWindowWide(session);

        _ = OpenSettingsAndWaitForSectionNavigation(session, appData, "shortcuts section return validation");

        var shortcutsItem = session.FindByAutomationId("SettingsNav.Shortcuts", TimeSpan.FromSeconds(10));
        FocusAndAssert(session, shortcutsItem, "SettingsNav.Shortcuts", "shortcuts settings navigation item");
        session.PressEnter();

        Assert.True(
            MoveFocusUntil(
                session,
                gamepad.PressDown,
                () =>
                {
                    var recorder = session.FindFirstByAutomationIdPrefix("Shortcuts.Record.", TimeSpan.FromMilliseconds(100));
                    return recorder is not null && session.IsFocusedElement(recorder);
                },
                attempts: 6),
            $"Virtual gamepad D-pad Down did not enter the Shortcuts content from the section navigation item."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");

        gamepad.PressUp();

        Assert.True(
            WaitUntil(
                () => session.IsFocusWithinAutomationId("SettingsNav.Shortcuts"),
                TimeSpan.FromSeconds(3)),
            $"Virtual gamepad D-pad Up did not return from the shortcuts content to the Shortcuts section navigation item."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");
    }

    [SkippableFact]
    public void SettingsShortcutsRestoreAll_VirtualGamepadDPadUp_CanReturnToTrailingShortcutAction()
    {
        GuiTestGate.RequireEnabled();

        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();
        var gamepad = session.CreateGamepadInput();
        EnsureMainWindowWide(session);

        _ = OpenSettingsAndWaitForSectionNavigation(session, appData, "shortcuts restore-all return validation");

        var shortcutsItem = session.FindByAutomationId("SettingsNav.Shortcuts", TimeSpan.FromSeconds(10));
        FocusAndAssert(session, shortcutsItem, "SettingsNav.Shortcuts", "shortcuts settings navigation item");
        session.PressEnter();

        var restoreAllButton = FindAndScrollIntoView(session, "Shortcuts.RestoreAll", TimeSpan.FromSeconds(10));
        FocusAndAssert(session, restoreAllButton, "Shortcuts.RestoreAll", "shortcuts restore-all action");

        gamepad.PressUp();

        Assert.True(
            WaitUntil(
                () => session.IsFocusWithinAutomationId("Shortcuts.Restore.search")
                    || session.IsFocusWithinAutomationId("Shortcuts.Record.search"),
                TimeSpan.FromSeconds(3)),
            $"Virtual gamepad D-pad Up did not return from the shortcuts restore-all action to the trailing shortcut control."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}DetailedFocus={session.DescribeFocusedElementDetailed()}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");
    }

    [SkippableFact]
    public void SettingsShortcutsRestoreAll_KeyboardUp_CanReturnToTrailingShortcutAction()
    {
        GuiTestGate.RequireEnabled();

        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();
        EnsureMainWindowWide(session);

        _ = OpenSettingsAndWaitForSectionNavigation(session, appData, "shortcuts restore-all keyboard return validation");

        var shortcutsItem = session.FindByAutomationId("SettingsNav.Shortcuts", TimeSpan.FromSeconds(10));
        FocusAndAssert(session, shortcutsItem, "SettingsNav.Shortcuts", "shortcuts settings navigation item");
        session.PressEnter();

        var restoreAllButton = FindAndScrollIntoView(session, "Shortcuts.RestoreAll", TimeSpan.FromSeconds(10));
        FocusAndAssert(session, restoreAllButton, "Shortcuts.RestoreAll", "shortcuts restore-all action");

        session.PressUp();

        Assert.True(
            WaitUntil(
                () => session.IsFocusWithinAutomationId("Shortcuts.Restore.search")
                    || session.IsFocusWithinAutomationId("Shortcuts.Record.search"),
                TimeSpan.FromSeconds(3)),
            $"Keyboard Up did not return from the shortcuts restore-all action to the trailing shortcut control."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}DetailedFocus={session.DescribeFocusedElementDetailed()}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");
    }

    [SkippableFact]
    public void SettingsDiagnosticsContent_VirtualGamepadDPadUp_CanReturnToSectionNavigation()
    {
        GuiTestGate.RequireEnabled();

        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();
        var gamepad = session.CreateGamepadInput();
        EnsureMainWindowWide(session);

        _ = OpenSettingsAndWaitForSectionNavigation(session, appData, "diagnostics section return validation");

        var diagnosticsItem = session.FindByAutomationId("SettingsNav.Diagnostics", TimeSpan.FromSeconds(10));
        FocusAndAssert(session, diagnosticsItem, "SettingsNav.Diagnostics", "diagnostics settings navigation item");
        session.PressEnter();

        Assert.True(
            MoveFocusUntil(
                session,
                gamepad.PressDown,
                () => session.IsFocusWithinAutomationId("Diagnostics.GamepadStart")
                    || session.IsFocusWithinAutomationId("Diagnostics.GamepadStop")
                    || session.IsFocusWithinAutomationId("Diagnostics.GamepadRefresh"),
                attempts: 6),
            $"Virtual gamepad D-pad Down did not enter the diagnostics actions from the section navigation item."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");

        gamepad.PressUp();

        Assert.True(
            WaitUntil(
                () => session.IsFocusWithinAutomationId("SettingsNav.Diagnostics"),
                TimeSpan.FromSeconds(3)),
            $"Virtual gamepad D-pad Up did not return from the diagnostics content to the Diagnostics section navigation item."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}DetailedFocus={session.DescribeFocusedElementDetailed()}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");
    }

    [SkippableFact]
    public void SettingsDiagnosticsContent_KeyboardUp_CanReturnToSectionNavigation()
    {
        GuiTestGate.RequireEnabled();

        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();
        EnsureMainWindowWide(session);

        _ = OpenSettingsAndWaitForSectionNavigation(session, appData, "diagnostics keyboard section return validation");

        var diagnosticsItem = session.FindByAutomationId("SettingsNav.Diagnostics", TimeSpan.FromSeconds(10));
        FocusAndAssert(session, diagnosticsItem, "SettingsNav.Diagnostics", "diagnostics settings navigation item");
        session.PressEnter();

        var startButton = session.FindByAutomationId("Diagnostics.GamepadStart", TimeSpan.FromSeconds(10));
        FocusAndAssert(session, startButton, "Diagnostics.GamepadStart", "diagnostics gamepad start action");

        session.PressUp();

        Assert.True(
            WaitUntil(
                () => session.IsFocusWithinAutomationId("SettingsNav.Diagnostics"),
                TimeSpan.FromSeconds(3)),
            $"Keyboard Up did not return from the diagnostics content to the Diagnostics section navigation item."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}DetailedFocus={session.DescribeFocusedElementDetailed()}"
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

        var sessionsList = session.FindByAutomationId("DiscoverSessions.SessionsList", TimeSpan.FromSeconds(10));
        var sessionItems = sessionsList
            .FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem))
            .ToArray();
        Assert.True(sessionItems.Length >= 1, $"Discover sessions list did not expose any session items before GamepadB validation.{Environment.NewLine}{appData.ReadBootLogTail()}");
        ClickAndAssertListItemFocus(session, sessionItems[0], "discover session list item in details");
        Thread.Sleep(200);

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
    public void GamepadB_FromDiscoverProfiles_ReturnsFocusToMainNavigation()
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

        var reachedProfiles = MoveFocusUntil(
            session,
            session.PressVirtualGamepadDPadRight,
            () => session.IsFocusWithinAutomationId("DiscoverSessions.ProfilesList"),
            attempts: 6);

        Assert.True(
            reachedProfiles,
            $"Virtual gamepad D-pad focus did not leave Discover nav item for the profiles list before back validation."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");

        session.PressVirtualGamepadB();

        Assert.True(
            WaitUntil(
                () => session.IsFocusWithinAutomationId("MainNav.DiscoverSessions"),
                TimeSpan.FromSeconds(3)),
            $"GamepadB did not return Discover focus back to the main navigation item."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");
    }

    [SkippableFact]
    public void GamepadB_FromSettingsSectionNavigation_ReturnsFocusToMainNavigation()
    {
        GuiTestGate.RequireEnabled();

        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();

        _ = OpenSettingsAndWaitForSectionNavigation(session, appData, "page-level GamepadB validation");

        session.PressVirtualGamepadB();

        Assert.True(
            WaitUntil(
                () => session.IsFocusWithinAutomationId("SettingsItem"),
                TimeSpan.FromSeconds(3)),
            $"GamepadB did not return Settings content focus back to the main navigation item."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");
    }

    [SkippableFact]
    public void GamepadB_FromChatBody_ReturnsFocusToMainNavigation()
    {
        GuiTestGate.RequireEnabled();

        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData(withContent: true);
        using var session = WindowsGuiAppSession.LaunchFresh();

        Assert.True(
            session.WaitUntilOnscreen("MainNav.Session.gui-session-01", TimeSpan.FromSeconds(15)),
            $"Session nav item did not become onscreen before chat back validation.{Environment.NewLine}{appData.ReadBootLogTail()}");

        var sessionItem = session.FindByAutomationId("MainNav.Session.gui-session-01", TimeSpan.FromSeconds(10));
        session.ClickElement(sessionItem);
        Assert.True(
            session.WaitUntilVisible("ChatView.CurrentSessionTitle", TimeSpan.FromSeconds(10)),
            $"Chat view did not become visible before GamepadB validation.{Environment.NewLine}{appData.ReadBootLogTail()}");

        var reachedChatBody = MoveFocusUntil(
            session,
            session.PressVirtualGamepadDPadRight,
            () => session.IsFocusWithinAutomationId("ChatView.MessagesList"),
            attempts: 6);

        Assert.True(
            reachedChatBody,
            $"Virtual gamepad D-pad focus did not leave MainNav for the chat transcript body before back validation."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");

        session.PressVirtualGamepadB();

        Assert.True(
            WaitUntil(
                () => session.IsFocusWithinAutomationId("MainNav.Session.gui-session-01"),
                TimeSpan.FromSeconds(3)),
            $"GamepadB did not return Chat content focus back to the selected main navigation item."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");
    }

    [SkippableFact]
    public void GamepadB_FromStartSuggestions_ReturnsFocusToMainNavigation()
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
            $"Virtual gamepad D-pad focus did not leave MainNav for the start suggestion strip before back validation."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");

        session.PressVirtualGamepadB();

        Assert.True(
            WaitUntil(
                () => session.IsFocusWithinAutomationId("MainNav.Start"),
                TimeSpan.FromSeconds(3)),
            $"GamepadB did not return Start content focus back to the main navigation item."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");
    }

    [SkippableFact]
    public void SettingsContent_VirtualGamepadDPadDown_CanReachSubsequentDiagnosticsActions()
    {
        GuiTestGate.RequireEnabled();

        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();
        EnsureMainWindowWide(session);

        var settingsItem = session.FindByAutomationId("SettingsItem", TimeSpan.FromSeconds(10));
        session.ClickElement(settingsItem);
        Assert.True(
            session.WaitUntilOnscreen("SettingsNav.Diagnostics", TimeSpan.FromSeconds(10)),
            $"Settings navigation did not become visible before diagnostics traversal validation.{Environment.NewLine}{appData.ReadBootLogTail()}");

        var diagnosticsItem = session.FindByAutomationId("SettingsNav.Diagnostics", TimeSpan.FromSeconds(10));
        session.ActivateElement(diagnosticsItem);
        Assert.True(
            session.WaitUntilOnscreen("Diagnostics.GamepadMonitorHeader", TimeSpan.FromSeconds(10)),
            $"Diagnostics settings page did not become visible before diagnostics traversal validation.{Environment.NewLine}{appData.ReadBootLogTail()}");
        FocusAndAssert(session, diagnosticsItem, "SettingsNav.Diagnostics", "diagnostics settings navigation item");

        var startButton = FindAndScrollIntoView(session, "Diagnostics.GamepadStart", TimeSpan.FromSeconds(10));
        FocusElementAndWait(session, startButton, "diagnostics start action");
        Assert.True(
            session.IsFocusWithinAutomationId("Diagnostics.GamepadStart"),
            $"Unable to establish diagnostics start action focus before traversal validation."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");

        Assert.True(
            MoveFocusUntil(
                session,
                session.PressVirtualGamepadDPadDown,
                () => session.IsFocusWithinAutomationId("Diagnostics.GamepadStop")
                    || session.IsFocusWithinAutomationId("Diagnostics.GamepadRefresh"),
                attempts: 6),
            $"Virtual gamepad D-pad Down did not advance beyond the first diagnostics action."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");
    }

    [SkippableFact]
    public void DiagnosticsGamepadActions_VirtualGamepadDPadDown_CanAdvanceWhenStopIsDisabled()
    {
        GuiTestGate.RequireEnabled();

        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();
        EnsureMainWindowWide(session);

        _ = OpenSettingsAndWaitForSectionNavigation(session, appData, "diagnostics action traversal validation");

        var diagnosticsItem = session.FindByAutomationId("SettingsNav.Diagnostics", TimeSpan.FromSeconds(10));
        FocusAndAssert(session, diagnosticsItem, "SettingsNav.Diagnostics", "diagnostics settings navigation item");
        session.PressEnter();
        Assert.True(
            session.WaitUntilOnscreen("Diagnostics.GamepadMonitorHeader", TimeSpan.FromSeconds(10)),
            $"Diagnostics settings page did not become visible before disabled-stop traversal validation.{Environment.NewLine}{appData.ReadBootLogTail()}");

        var startButton = FindAndScrollIntoView(session, "Diagnostics.GamepadStart", TimeSpan.FromSeconds(10));
        var stopButton = FindAndScrollIntoView(session, "Diagnostics.GamepadStop", TimeSpan.FromSeconds(10));
        Assert.False(
            stopButton.IsEnabled,
            $"Diagnostics stop action should be disabled before monitoring starts.{Environment.NewLine}{appData.ReadBootLogTail()}");

        FocusElementAndWait(session, startButton, "diagnostics start action");
        session.PressVirtualGamepadDPadDown();

        Assert.True(
            WaitUntil(
                () => session.IsFocusWithinAutomationId("Diagnostics.GamepadStop")
                    || session.IsFocusWithinAutomationId("Diagnostics.GamepadRefresh"),
                TimeSpan.FromSeconds(3)),
            $"Virtual gamepad D-pad Down did not advance beyond the first diagnostics action while stop was disabled."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");
    }

    [SkippableFact]
    public void DiagnosticsGamepadActions_VirtualGamepadDPadDown_SkipsDisabledStopButton()
    {
        GuiTestGate.RequireEnabled();

        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();
        EnsureMainWindowWide(session);

        _ = OpenSettingsAndWaitForSectionNavigation(session, appData, "diagnostics disabled-stop skip validation");

        var diagnosticsItem = session.FindByAutomationId("SettingsNav.Diagnostics", TimeSpan.FromSeconds(10));
        FocusAndAssert(session, diagnosticsItem, "SettingsNav.Diagnostics", "diagnostics settings navigation item");
        session.PressEnter();
        Assert.True(
            session.WaitUntilOnscreen("Diagnostics.GamepadMonitorHeader", TimeSpan.FromSeconds(10)),
            $"Diagnostics settings page did not become visible before disabled-stop skip validation.{Environment.NewLine}{appData.ReadBootLogTail()}");

        var startButton = FindAndScrollIntoView(session, "Diagnostics.GamepadStart", TimeSpan.FromSeconds(10));
        var stopButton = FindAndScrollIntoView(session, "Diagnostics.GamepadStop", TimeSpan.FromSeconds(10));
        Assert.False(
            stopButton.IsEnabled,
            $"Diagnostics stop action should be disabled before monitoring starts.{Environment.NewLine}{appData.ReadBootLogTail()}");

        FocusAndAssert(session, startButton, "Diagnostics.GamepadStart", "diagnostics start action");
        session.PressVirtualGamepadDPadDown();

        Assert.True(
            WaitUntil(
                () => session.IsFocusWithinAutomationId("Diagnostics.GamepadRefresh"),
                TimeSpan.FromSeconds(2)),
            $"Virtual gamepad D-pad Down should skip the disabled diagnostics stop action and land on refresh."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}DetailedFocus={session.DescribeFocusedElementDetailed()}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");
    }

    [SkippableFact]
    public void SettingsGeneralContent_VirtualGamepadDPadDown_CanReachSubsequentControls()
    {
        GuiTestGate.RequireEnabled();

        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();
        EnsureMainWindowWide(session);

        var settingsItem = session.FindByAutomationId("SettingsItem", TimeSpan.FromSeconds(10));
        session.ClickElement(settingsItem);
        Assert.True(
            session.WaitUntilOnscreen("SettingsNav.General", TimeSpan.FromSeconds(10)),
            $"Settings navigation did not become visible before general-content traversal validation.{Environment.NewLine}{appData.ReadBootLogTail()}");

        var generalItem = session.FindByAutomationId("SettingsNav.General", TimeSpan.FromSeconds(10));
        session.ClickElement(generalItem);

        var firstInteractive = session.FindFirstByAutomationIdPrefix("GeneralSettings.", TimeSpan.FromSeconds(10))
            ?? session.FindVisibleText("默认启动页", timeout: TimeSpan.FromSeconds(10))
            ?? throw new InvalidOperationException("Unable to locate a first interactive control in General settings.");
        FocusElementAndWait(session, firstInteractive, "general settings first interactive control");

        var focusedBefore = session.DescribeFocusedElement();
        session.PressVirtualGamepadDPadDown();

        Assert.True(
            WaitUntil(
                () => !string.Equals(session.DescribeFocusedElement(), focusedBefore, StringComparison.Ordinal),
                TimeSpan.FromSeconds(3)),
            $"Virtual gamepad D-pad Down did not move beyond the first general-settings control."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");
    }

    [SkippableFact]
    public void SettingsAppearanceContent_VirtualGamepadDPadDown_CanReachSubsequentControls()
    {
        GuiTestGate.RequireEnabled();

        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();
        EnsureMainWindowWide(session);

        var settingsItem = session.FindByAutomationId("SettingsItem", TimeSpan.FromSeconds(10));
        session.ClickElement(settingsItem);
        Assert.True(
            session.WaitUntilOnscreen("SettingsNav.Appearance", TimeSpan.FromSeconds(10)),
            $"Settings navigation did not become visible before appearance-content traversal validation.{Environment.NewLine}{appData.ReadBootLogTail()}");

        var appearanceItem = session.FindByAutomationId("SettingsNav.Appearance", TimeSpan.FromSeconds(10));
        session.ClickElement(appearanceItem);
        Assert.True(
            session.WaitUntilOnscreen("Appearance.Theme", TimeSpan.FromSeconds(10)),
            $"Appearance settings page did not become visible before in-page traversal validation.{Environment.NewLine}{appData.ReadBootLogTail()}");

        var firstInteractive = session.FindByAutomationId("Appearance.Theme", TimeSpan.FromSeconds(10));
        FocusAndAssert(session, firstInteractive, "Appearance.Theme", "appearance settings first interactive control");

        var focusedBefore = session.DescribeFocusedElement();
        session.PressVirtualGamepadDPadDown();

        Assert.True(
            WaitUntil(
                () => !string.Equals(session.DescribeFocusedElement(), focusedBefore, StringComparison.Ordinal),
                TimeSpan.FromSeconds(3)),
            $"Virtual gamepad D-pad Down did not move beyond the first appearance-settings control."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");
    }

    [SkippableFact]
    public void SettingsAcpContent_VirtualGamepadDPadDown_CanReachSubsequentControls()
    {
        GuiTestGate.RequireEnabled();

        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();
        EnsureMainWindowWide(session);

        OpenSettingsSectionAndFocusFirstControl(
            session,
            appData,
            sectionAutomationId: "SettingsNav.AgentAcp",
            expectedFirstControlAutomationId: "Acp.Global.Enabled",
            firstControlAutomationId: "Acp.Global.Enabled",
            sectionDescription: "ACP");

        var focusedBefore = session.DescribeFocusedElement();
        session.PressVirtualGamepadDPadDown();

        Assert.True(
            WaitUntil(
                () => !string.Equals(session.DescribeFocusedElement(), focusedBefore, StringComparison.Ordinal),
                TimeSpan.FromSeconds(3)),
            $"Virtual gamepad D-pad Down did not move beyond the first ACP settings control."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");
    }

    [SkippableFact]
    public void SettingsMcpContent_VirtualGamepadDPadDown_CanReachSubsequentControls()
    {
        GuiTestGate.RequireEnabled();

        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();
        EnsureMainWindowWide(session);

        OpenSettingsSectionAndFocusFirstControl(
            session,
            appData,
            sectionAutomationId: "SettingsNav.Mcp",
            expectedFirstControlAutomationId: "Mcp.AddServer",
            firstControlAutomationId: "Mcp.AddServer",
            sectionDescription: "MCP");

        var focusedBefore = session.DescribeFocusedElement();
        session.PressVirtualGamepadDPadDown();

        Assert.True(
            WaitUntil(
                () => !string.Equals(session.DescribeFocusedElement(), focusedBefore, StringComparison.Ordinal),
                TimeSpan.FromSeconds(3)),
            $"Virtual gamepad D-pad Down did not move beyond the first MCP settings control."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");
    }

    [SkippableFact]
    public void SettingsMcpReload_VirtualGamepadDPadDown_ReachesFirstServerToggleWithoutHiddenStops()
    {
        GuiTestGate.RequireEnabled();

        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        appData.WriteMcpYaml(
            """
            schema_version: 1
            servers:
            - transport: http
              name: search
              enabled: false
              url: https://example.com/mcp
            """);
        using var session = WindowsGuiAppSession.LaunchFresh();
        EnsureMainWindowWide(session);

        _ = OpenSettingsAndWaitForSectionNavigation(session, appData, "MCP reload downward traversal validation");

        var mcpItem = session.FindByAutomationId("SettingsNav.Mcp", TimeSpan.FromSeconds(10));
        FocusAndAssert(session, mcpItem, "SettingsNav.Mcp", "MCP settings navigation item");
        session.PressEnter();
        Assert.True(
            session.WaitUntilOnscreen("Mcp.AddServer", TimeSpan.FromSeconds(10))
            && session.WaitUntilOnscreen("Mcp.Server.Enabled", TimeSpan.FromSeconds(10)),
            $"MCP settings page did not become visible before reload traversal validation.{Environment.NewLine}{appData.ReadBootLogTail()}");

        var addServerButton = session.FindByAutomationId("Mcp.AddServer", TimeSpan.FromSeconds(10));
        FocusAndAssert(session, addServerButton, "Mcp.AddServer", "MCP add server action");
        session.PressVirtualGamepadDPadLeft();
        Assert.True(
            WaitUntil(
                () => session.IsFocusWithinAutomationId("Mcp.Reload"),
                TimeSpan.FromSeconds(2)),
            $"Virtual gamepad D-pad Left did not move from the MCP add-server action to the reload action."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}DetailedFocus={session.DescribeFocusedElementDetailed()}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");

        session.PressVirtualGamepadDPadDown();

        Assert.True(
            WaitUntil(
                () => session.IsFocusWithinAutomationId("Mcp.Server.Enabled"),
                TimeSpan.FromSeconds(2)),
            $"Virtual gamepad D-pad Down from the MCP reload action should land on the first server toggle."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}DetailedFocus={session.DescribeFocusedElementDetailed()}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");
    }

    [SkippableFact]
    public void SettingsMcpReload_KeyboardDown_ReachesFirstServerToggleWithoutHiddenStops()
    {
        GuiTestGate.RequireEnabled();

        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        appData.WriteMcpYaml(
            """
            schema_version: 1
            servers:
            - transport: http
              name: search
              enabled: false
              url: https://example.com/mcp
            """);
        using var session = WindowsGuiAppSession.LaunchFresh();
        EnsureMainWindowWide(session);

        _ = OpenSettingsAndWaitForSectionNavigation(session, appData, "MCP keyboard-down traversal validation");

        var mcpItem = session.FindByAutomationId("SettingsNav.Mcp", TimeSpan.FromSeconds(10));
        FocusAndAssert(session, mcpItem, "SettingsNav.Mcp", "MCP settings navigation item");
        session.PressEnter();
        Assert.True(
            session.WaitUntilOnscreen("Mcp.AddServer", TimeSpan.FromSeconds(10))
            && session.WaitUntilOnscreen("Mcp.Server.Enabled", TimeSpan.FromSeconds(10)),
            $"MCP settings page did not become visible before keyboard-down traversal validation.{Environment.NewLine}{appData.ReadBootLogTail()}");

        var addServerButton = session.FindByAutomationId("Mcp.AddServer", TimeSpan.FromSeconds(10));
        FocusAndAssert(session, addServerButton, "Mcp.AddServer", "MCP add server action");
        session.PressLeft();
        Assert.True(
            WaitUntil(
                () => session.IsFocusWithinAutomationId("Mcp.Reload"),
                TimeSpan.FromSeconds(2)),
            $"Keyboard Left did not move from the MCP add-server action to the reload action."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}DetailedFocus={session.DescribeFocusedElementDetailed()}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");

        session.PressDown();

        Assert.True(
            WaitUntil(
                () => session.IsFocusWithinAutomationId("Mcp.Server.Enabled"),
                TimeSpan.FromSeconds(2)),
            $"Keyboard Down from the MCP reload action should land on the first server toggle."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}DetailedFocus={session.DescribeFocusedElementDetailed()}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");
    }

    [SkippableFact]
    public void SettingsShortcutsContent_VirtualGamepadDPadDown_CanReachSubsequentControls()
    {
        GuiTestGate.RequireEnabled();

        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();
        EnsureMainWindowWide(session);

        var settingsItem = session.FindByAutomationId("SettingsItem", TimeSpan.FromSeconds(10));
        session.ClickElement(settingsItem);
        Assert.True(
            session.WaitUntilOnscreen("SettingsNav.Shortcuts", TimeSpan.FromSeconds(10)),
            $"Settings navigation did not become visible before shortcuts traversal validation.{Environment.NewLine}{appData.ReadBootLogTail()}");

        var shortcutsItem = session.FindByAutomationId("SettingsNav.Shortcuts", TimeSpan.FromSeconds(10));
        session.ClickElement(shortcutsItem);

        var firstInteractive = session.FindFirstByAutomationIdPrefix("Shortcuts.Record.", TimeSpan.FromSeconds(10))
            ?? throw new InvalidOperationException("Unable to locate the first shortcut recorder in Shortcuts settings.");
        FocusElementAndWait(session, firstInteractive, "shortcuts settings first interactive control");

        var focusedBefore = session.DescribeFocusedElement();
        session.PressVirtualGamepadDPadDown();

        Assert.True(
            WaitUntil(
                () => !string.Equals(session.DescribeFocusedElement(), focusedBefore, StringComparison.Ordinal),
                TimeSpan.FromSeconds(3)),
            $"Virtual gamepad D-pad Down did not move beyond the first shortcuts settings control."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");
    }

    [SkippableFact]
    public void SettingsAboutContent_VirtualGamepadDPadDown_CanReachSubsequentControls()
    {
        GuiTestGate.RequireEnabled();

        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();
        EnsureMainWindowWide(session);

        var settingsItem = session.FindByAutomationId("SettingsItem", TimeSpan.FromSeconds(10));
        session.ClickElement(settingsItem);
        Assert.True(
            session.WaitUntilOnscreen("SettingsNav.About", TimeSpan.FromSeconds(10)),
            $"Settings navigation did not become visible before about traversal validation.{Environment.NewLine}{appData.ReadBootLogTail()}");

        var aboutItem = session.FindByAutomationId("SettingsNav.About", TimeSpan.FromSeconds(10));
        session.ClickElement(aboutItem);

        var firstInteractive = session.FindVisibleElementByNameAnywhere("复制版本信息", TimeSpan.FromSeconds(10))
            ?? throw new InvalidOperationException("Unable to locate the Copy Version Info action in About settings.");
        FocusElementAndWait(session, firstInteractive, "about settings first interactive control");

        var focusedBefore = session.DescribeFocusedElement();
        session.PressVirtualGamepadDPadDown();

        Assert.True(
            WaitUntil(
                () => !string.Equals(session.DescribeFocusedElement(), focusedBefore, StringComparison.Ordinal),
                TimeSpan.FromSeconds(3)),
            $"Virtual gamepad D-pad Down did not move beyond the first about settings control."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");
    }

    [SkippableFact]
    public void SettingsAppearanceSection_VirtualGamepadDPadDown_CanReachFirstControl()
    {
        GuiTestGate.RequireEnabled();

        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();
        EnsureMainWindowWide(session);

        var settingsItem = session.FindByAutomationId("SettingsItem", TimeSpan.FromSeconds(10));
        session.ClickElement(settingsItem);
        Assert.True(
            session.WaitUntilOnscreen("SettingsNav.Appearance", TimeSpan.FromSeconds(10)),
            $"Settings navigation did not become visible before appearance entry validation.{Environment.NewLine}{appData.ReadBootLogTail()}");

        var appearanceItem = session.FindByAutomationId("SettingsNav.Appearance", TimeSpan.FromSeconds(10));
        session.ClickElement(appearanceItem);

        Assert.True(
            MoveFocusUntil(
                session,
                session.PressVirtualGamepadDPadDown,
                () => session.IsFocusWithinAutomationId("Appearance.Theme"),
                attempts: 6),
            $"Virtual gamepad D-pad Down did not move from the appearance section into the first control."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");
    }

    [SkippableFact]
    public void SettingsSectionNavigation_VirtualGamepadActivationThenDPadDown_EntersActivatedSectionContent()
    {
        GuiTestGate.RequireEnabled();

        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();
        var gamepad = session.CreateGamepadInput();
        EnsureMainWindowWide(session);

        var settingsItem = OpenSettingsAndWaitForSectionNavigation(session, appData, "section activation traversal validation");
        FocusAndAssert(session, settingsItem, "SettingsItem", "settings navigation item");

        Assert.True(
            MoveFocusUntil(
                session,
                gamepad.PressRight,
                () => session.IsFocusWithinAutomationId("SettingsNav.Appearance"),
                attempts: 8),
            $"Virtual gamepad D-pad Right did not move focus to the Appearance settings section navigation item."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");

        gamepad.PressActivate();
        Assert.True(
            session.WaitUntilOnscreen("Appearance.Theme", TimeSpan.FromSeconds(10)),
            $"Appearance settings content did not become visible after virtual gamepad activation."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");
        var focusAfterActivation = session.DescribeFocusedElement();
        Thread.Sleep(500);
        var focusAfterActivationSettled = session.DescribeFocusedElement();

        gamepad.PressDown();
        Thread.Sleep(150);
        var focusAfterDown = session.DescribeFocusedElement();
        Assert.True(
            WaitUntil(
                () => session.IsFocusWithinAutomationId("Appearance.Theme"),
                TimeSpan.FromSeconds(3)),
            $"Virtual gamepad D-pad Down did not enter the activated Appearance settings content."
            + $"{Environment.NewLine}FocusAfterActivation={focusAfterActivation}"
            + $"{Environment.NewLine}FocusAfterActivationSettled={focusAfterActivationSettled}"
            + $"{Environment.NewLine}FocusAfterDown={focusAfterDown}"
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");
    }

    [SkippableFact]
    public void SettingsDataStorageContent_VirtualGamepadDPadDown_CanReachSubsequentControls()
    {
        GuiTestGate.RequireEnabled();

        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();
        EnsureMainWindowWide(session);

        var settingsItem = session.FindByAutomationId("SettingsItem", TimeSpan.FromSeconds(10));
        session.ClickElement(settingsItem);
        Assert.True(
            session.WaitUntilOnscreen("SettingsNav.DataStorage", TimeSpan.FromSeconds(10)),
            $"Settings navigation did not become visible before data-storage traversal validation.{Environment.NewLine}{appData.ReadBootLogTail()}");

        var dataStorageItem = session.FindByAutomationId("SettingsNav.DataStorage", TimeSpan.FromSeconds(10));
        session.ClickElement(dataStorageItem);

        var firstInteractive = session.FindByAutomationId("DataStorage.SaveLocalHistory", TimeSpan.FromSeconds(10));
        FocusElementAndWait(session, firstInteractive, "data storage first interactive control");

        var focusedBefore = session.DescribeFocusedElement();
        session.PressVirtualGamepadDPadDown();

        Assert.True(
            WaitUntil(
                () => !string.Equals(session.DescribeFocusedElement(), focusedBefore, StringComparison.Ordinal),
                TimeSpan.FromSeconds(3)),
            $"Virtual gamepad D-pad Down did not move beyond the first data-storage control."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");
    }

    [SkippableFact]
    public void SettingsDataStorageNumberBox_VirtualGamepadDPadDown_MovesFocusWithoutChangingValue()
    {
        GuiTestGate.RequireEnabled();

        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();
        var gamepad = session.CreateGamepadInput();
        EnsureMainWindowWide(session);

        var settingsItem = session.FindByAutomationId("SettingsItem", TimeSpan.FromSeconds(10));
        session.ClickElement(settingsItem);
        Assert.True(
            session.WaitUntilOnscreen("SettingsNav.DataStorage", TimeSpan.FromSeconds(10)),
            $"Settings navigation did not become visible before data-storage NumberBox validation.{Environment.NewLine}{appData.ReadBootLogTail()}");

        var dataStorageItem = session.FindByAutomationId("SettingsNav.DataStorage", TimeSpan.FromSeconds(10));
        session.ClickElement(dataStorageItem);

        var firstInteractive = session.FindByAutomationId("DataStorage.SaveLocalHistory", TimeSpan.FromSeconds(10));
        var numberBox = session.FindByAutomationId("DataStorage.CacheRetention", TimeSpan.FromSeconds(10));
        var valueBefore = session.TryGetValue(numberBox);
        Assert.False(
            string.IsNullOrWhiteSpace(valueBefore),
            $"Unable to read the data-storage NumberBox value before gamepad traversal.{Environment.NewLine}Focus={session.DescribeFocusedElement()}");

        FocusElementAndWait(session, firstInteractive, "data storage first interactive control");
        Assert.True(
            MoveFocusUntil(
                session,
                gamepad.PressDown,
                () => session.IsFocusWithinAutomationId("DataStorage.CacheRetention"),
                attempts: 6),
            $"Virtual gamepad D-pad Down did not naturally reach the data-storage NumberBox from the preceding control."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");
        Assert.Equal(valueBefore, session.TryGetValue(numberBox));

        var focusOnNumberBox = session.DescribeFocusedElement();
        gamepad.PressDown();
        Assert.True(
            WaitUntil(
                () => !session.IsFocusWithinAutomationId("DataStorage.CacheRetention"),
                TimeSpan.FromSeconds(3)),
            $"Virtual gamepad D-pad Down did not leave the data-storage NumberBox before engagement."
            + $"{Environment.NewLine}FocusBefore={focusOnNumberBox}"
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");

        Assert.True(
            WaitUntil(
                () => string.Equals(valueBefore, session.TryGetValue(numberBox), StringComparison.Ordinal),
                TimeSpan.FromSeconds(1)),
            $"Virtual gamepad D-pad Down changed the data-storage NumberBox value before engagement."
            + $"{Environment.NewLine}ValueBefore={valueBefore}"
            + $"{Environment.NewLine}ValueAfter={session.TryGetValue(numberBox)}"
            + $"{Environment.NewLine}FocusBefore={focusOnNumberBox}"
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");
    }

    [SkippableFact]
    public void SettingsDataStorageFirstToggle_VirtualGamepadDPadDown_ReachesNumberBoxInSingleStep()
    {
        GuiTestGate.RequireEnabled();

        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();
        EnsureMainWindowWide(session);

        var settingsItem = session.FindByAutomationId("SettingsItem", TimeSpan.FromSeconds(10));
        session.ClickElement(settingsItem);
        Assert.True(
            session.WaitUntilOnscreen("SettingsNav.DataStorage", TimeSpan.FromSeconds(10)),
            $"Settings navigation did not become visible before data-storage single-step validation.{Environment.NewLine}{appData.ReadBootLogTail()}");

        var dataStorageItem = session.FindByAutomationId("SettingsNav.DataStorage", TimeSpan.FromSeconds(10));
        session.ClickElement(dataStorageItem);

        var firstInteractive = session.FindByAutomationId("DataStorage.SaveLocalHistory", TimeSpan.FromSeconds(10));
        FocusAndAssert(session, firstInteractive, "DataStorage.SaveLocalHistory", "data storage first interactive control");

        session.PressVirtualGamepadDPadDown();

        Assert.True(
            WaitUntil(
                () => session.IsFocusWithinAutomationId("DataStorage.CacheRetention"),
                TimeSpan.FromSeconds(2)),
            $"Virtual gamepad D-pad Down from the first data-storage toggle should land on the cache-retention NumberBox in a single step."
            + $"{Environment.NewLine}Focus={session.DescribeFocusedElement()}"
            + $"{Environment.NewLine}DetailedFocus={session.DescribeFocusedElementDetailed()}"
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

    private static bool TryFocus(
        WindowsGuiAppSession session,
        AutomationElement element,
        string automationId)
    {
        session.BringMainWindowToFront();
        for (var attempt = 0; attempt < 4; attempt++)
        {
            session.FocusElement(element);
            if (WaitUntil(
                    () => session.IsFocusWithinAutomationId(automationId),
                    TimeSpan.FromMilliseconds(250)))
            {
                return true;
            }
        }

        return false;
    }

    private static void OpenSettingsSectionAndFocusFirstControl(
        WindowsGuiAppSession session,
        GuiAppDataScope appData,
        string sectionAutomationId,
        string expectedFirstControlAutomationId,
        string firstControlAutomationId,
        string sectionDescription)
    {
        _ = OpenSettingsAndWaitForSectionNavigation(session, appData, $"{sectionDescription} traversal validation");

        var sectionItem = session.FindByAutomationId(sectionAutomationId, TimeSpan.FromSeconds(10));
        FocusAndAssert(session, sectionItem, sectionAutomationId, $"{sectionDescription} settings navigation item");
        session.PressEnter();
        Assert.True(
            session.WaitUntilOnscreen(firstControlAutomationId, TimeSpan.FromSeconds(10)),
            $"The {sectionDescription} settings content did not become visible after activating its section navigation item.{Environment.NewLine}{appData.ReadBootLogTail()}");
        var firstControl = session.FindByAutomationId(firstControlAutomationId, TimeSpan.FromSeconds(10));
        FocusAndAssert(session, firstControl, expectedFirstControlAutomationId, $"{sectionDescription} first interactive control");
    }

    private static AutomationElement OpenSettingsAndWaitForSectionNavigation(
        WindowsGuiAppSession session,
        GuiAppDataScope appData,
        string validationDescription)
    {
        var settingsItem = session.FindByAutomationId("SettingsItem", TimeSpan.FromSeconds(10));
        FocusAndAssert(session, settingsItem, "SettingsItem", "settings navigation item");
        session.PressEnter();
        Assert.True(
            session.WaitUntilOnscreen("SettingsNav.General", TimeSpan.FromSeconds(10)),
            $"Settings navigation did not become visible before {validationDescription}.{Environment.NewLine}{appData.ReadBootLogTail()}");
        return settingsItem;
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

        ResizeMainWindow(width: 1800, height: 1000);
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
