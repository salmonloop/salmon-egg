using System;
using System.IO;
using System.Threading;
using FlaUI.Core.Definitions;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.WindowsAPI;

namespace SalmonEgg.GuiTests.Windows;

public sealed class NavigationSmokeTests
{
    [SkippableFact]
    public void Launch_WithSeededData_ShowsMainNav_AndStartIsSelected()
    {
        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();

        var mainNav = session.FindByAutomationId("MainNavView");
        var startItem = session.FindByAutomationId("MainNav.Start");
        var startTitle = session.FindByAutomationId("StartView.Title");

        Assert.NotNull(mainNav);
        Assert.NotNull(startTitle);
        Assert.True(
            session.WaitUntilOnscreen("MainNav.Start", TimeSpan.FromSeconds(4)),
            $"Expected MainNav.Start to be onscreen at launch.{Environment.NewLine}{appData.ReadBootLogTail()}");

        var selectionItem = startItem.Patterns.SelectionItem.Pattern;
        var launchSnapshot = string.Join(
            "; ",
            $"StartView={session.TryFindByAutomationId("StartView.Title", TimeSpan.FromSeconds(2)) is not null}",
            $"ChatHeader={session.TryFindByAutomationId("ChatView.CurrentSessionTitle", TimeSpan.FromSeconds(2)) is not null}",
            $"StartSelected={session.TryGetIsSelected("MainNav.Start")}",
            $"Session01Selected={session.TryGetIsSelected("MainNav.Session.gui-session-01")}");
        Assert.True(
            selectionItem.IsSelected.Value,
            $"Launch state mismatch. {launchSnapshot}{Environment.NewLine}{appData.ReadBootLogTail()}");
    }

    [SkippableFact]
    public void StartComposer_ShowsAgentModeAndProjectSelectorsByDefault()
    {
        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();
        session.ResizeMainWindow(width: 1400, height: 900);

        Assert.True(
            session.WaitUntilOnscreen("StartView.AgentSelector", TimeSpan.FromSeconds(6)),
            $"Expected agent selector to be onscreen with the start composer by default.{Environment.NewLine}{appData.ReadBootLogTail()}");
        Assert.True(
            session.WaitUntilOnscreen("StartView.ModeSelector", TimeSpan.FromSeconds(6)),
            $"Expected mode selector to share the default composer row lifecycle with agent/project selectors.{Environment.NewLine}{appData.ReadBootLogTail()}");
        Assert.True(
            session.WaitUntilOnscreen("StartView.ProjectSelector", TimeSpan.FromSeconds(6)),
            $"Expected project selector to be onscreen with the start composer by default.{Environment.NewLine}{appData.ReadBootLogTail()}");

        var agentSelector = session.FindByAutomationId("StartView.AgentSelector");
        var modeSelector = session.FindByAutomationId("StartView.ModeSelector");
        var projectSelector = session.FindByAutomationId("StartView.ProjectSelector");

        Assert.False(modeSelector.IsOffscreen);
        Assert.True(
            agentSelector.BoundingRectangle.Left < modeSelector.BoundingRectangle.Left
            && modeSelector.BoundingRectangle.Left < projectSelector.BoundingRectangle.Left,
            $"Expected selector order agent -> mode -> project. agent={agentSelector.BoundingRectangle}; mode={modeSelector.BoundingRectangle}; project={projectSelector.BoundingRectangle}");
    }

    [SkippableFact]
    public void ProjectInvoke_DoesNotChangeSelectionOrContent()
    {
        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();

        session.ActivateElement(session.FindByAutomationId("MainNav.Project.project-1"));

        var startItem = session.FindByAutomationId("MainNav.Start");
        var startTitle = session.FindByAutomationId("StartView.Title");
        var selectionItem = startItem.Patterns.SelectionItem.Pattern;

        Assert.NotNull(startTitle);
        Assert.True(selectionItem.IsSelected.Value);
    }

    [SkippableFact]
    public void SelectSeededSession_UpdatesNavAndChatHeader()
    {
        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();

        var sessionItem = session.FindByAutomationId("MainNav.Session.gui-session-01");

        session.ActivateElement(sessionItem);

        var chatHeader = session.FindByAutomationId("ChatView.CurrentSessionTitle", TimeSpan.FromSeconds(10));
        var selectionItem = sessionItem.Patterns.SelectionItem.Pattern;

        Assert.NotNull(chatHeader);
        Assert.Contains("GUI Session 01", chatHeader.Name, StringComparison.Ordinal);
        Assert.True(selectionItem.IsSelected.Value);
    }

    [SkippableFact]
    public void CtrlKShortcut_FocusesTopSearchBox()
    {
        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();

        var startItem = session.FindByAutomationId("MainNav.Start");
        session.FocusElement(startItem);

        session.PressShortcut(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_K);

        Assert.True(
            session.IsFocusWithinAutomationId("TopSearchBox"),
            $"Expected Ctrl+K to move focus into TopSearchBox.{Environment.NewLine}{appData.ReadBootLogTail()}");
    }

    [SkippableFact]
    public void SearchOverflowSession_MaterializesNativeNavSelection_AndSubsequentNavigationWorks()
    {
        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData(sessionCount: 25, withContent: true);
        using var session = WindowsGuiAppSession.LaunchFresh();

        const string targetSessionId = "MainNav.Session.gui-session-25";
        const string projectId = "MainNav.Project.project-1";
        const string startId = "MainNav.Start";

        Assert.Contains(
            "SessionVisible=False",
            session.TryGetElementName("MainNav.Automation.SelectionState", TimeSpan.FromSeconds(2)) ?? string.Empty,
            StringComparison.Ordinal);

        var startItem = session.FindByAutomationId(startId);
        session.FocusElement(startItem);
        session.PressShortcut(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_K);

        Assert.True(
            session.IsFocusWithinAutomationId("TopSearchBox"),
            $"Expected Ctrl+K to focus the native AutoSuggestBox before typing.{Environment.NewLine}{appData.ReadBootLogTail()}");

        session.TypeText("TopSearchBox", "GUI Session 25");

        Assert.True(
            WaitForSearchSuggestion(session, "SearchSuggestion.Result.gui-session-25", TimeSpan.FromSeconds(8)),
            $"Expected native search suggestions to include GUI Session 25 before submit.{Environment.NewLine}{appData.ReadBootLogTail()}");

        session.PressEnter();

        var chatHeader = session.FindByAutomationId("ChatView.CurrentSessionTitle", TimeSpan.FromSeconds(12));
        Assert.Contains("GUI Session 25", chatHeader.Name, StringComparison.Ordinal);

        Assert.True(
            session.WaitUntilOnscreen(targetSessionId, TimeSpan.FromSeconds(8)),
            $"Expected searched overflow session to be materialized into the native nav menu source.{Environment.NewLine}{DumpSelectionSnapshot(session, targetSessionId, projectId, startId)}{Environment.NewLine}{DumpProjectSnapshot(session, projectId)}{Environment.NewLine}{DumpAutomationSelectionState(session)}{Environment.NewLine}{appData.ReadBootLogTail()}");

        Assert.True(
            WaitForVisibleSelected(session, [targetSessionId, projectId, startId], TimeSpan.FromSeconds(6), out var selectedAfterSearch),
            $"Expected a visible nav selection after search activation. winner={selectedAfterSearch ?? "<null>"}{Environment.NewLine}{DumpSelectionSnapshot(session, targetSessionId, projectId, startId)}{Environment.NewLine}{DumpProjectSnapshot(session, projectId)}{Environment.NewLine}{DumpAutomationSelectionState(session)}{Environment.NewLine}{appData.ReadBootLogTail()}");
        Assert.Equal(targetSessionId, selectedAfterSearch);

        var automationState = session.TryGetElementName("MainNav.Automation.SelectionState", TimeSpan.FromMilliseconds(500)) ?? string.Empty;
        Assert.Contains("Semantic=Session:gui-session-25", automationState, StringComparison.Ordinal);
        Assert.Contains("NavSelected=Session:gui-session-25", automationState, StringComparison.Ordinal);

        session.ActivateElement(session.FindByAutomationId(startId));
        Assert.NotNull(session.FindByAutomationId("StartView.Title", TimeSpan.FromSeconds(10)));
        Assert.True(
            WaitForVisibleSelected(session, [targetSessionId, projectId, startId], TimeSpan.FromSeconds(6), out var selectedAfterStart),
            $"Expected navigation to remain ordered after leaving searched session. winner={selectedAfterStart ?? "<null>"}{Environment.NewLine}{DumpSelectionSnapshot(session, targetSessionId, projectId, startId)}{Environment.NewLine}{DumpProjectSnapshot(session, projectId)}{Environment.NewLine}{DumpAutomationSelectionState(session)}{Environment.NewLine}{appData.ReadBootLogTail()}");
        Assert.Equal(startId, selectedAfterStart);
    }

    [SkippableFact]
    public void ShortcutRecorder_UpdatesSearchBindingImmediately_AndDropsPreviousBinding()
    {
        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();

        NavigateToShortcutsSettings(session);

        var recorderButton = session.FindByAutomationId("Shortcuts.Record.search");
        session.ClickElement(recorderButton);
        session.FocusElement(recorderButton);
        Thread.Sleep(150);
        session.PressShortcut(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_L);
        Thread.Sleep(250);

        var recorderName = session.TryGetElementName("Shortcuts.Record.search", TimeSpan.FromSeconds(2));
        Assert.True(
            (recorderName ?? string.Empty).Contains("Ctrl+L", StringComparison.Ordinal),
            $"Expected shortcut recorder to display Ctrl+L after capture, but saw '{recorderName ?? "<null>"}'.{Environment.NewLine}{appData.ReadBootLogTail()}");

        var startItem = session.FindByAutomationId("MainNav.Start");
        session.FocusElement(startItem);
        session.PressShortcut(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_K);
        Thread.Sleep(150);

        Assert.False(
            session.IsFocusWithinAutomationId("TopSearchBox"),
            $"Expected previous Ctrl+K binding to stop focusing TopSearchBox after recorder update.{Environment.NewLine}{appData.ReadBootLogTail()}");

        session.FocusElement(startItem);
        session.PressShortcut(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_L);

        Assert.True(
            session.IsFocusWithinAutomationId("TopSearchBox"),
            $"Expected recorded Ctrl+L binding to focus TopSearchBox immediately.{Environment.NewLine}{appData.ReadBootLogTail()}");
    }

    [SkippableFact]
    public void TitleBarPanelButtons_Toggle_ChangesBottomPanelState()
    {
        using var _ = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();

        var sessionItem = session.FindByAutomationId("MainNav.Session.gui-session-01");
        session.ActivateElement(sessionItem);
        session.FindByAutomationId("ChatView.CurrentSessionTitle", TimeSpan.FromSeconds(10));

        var bottomPanelButton = session.FindByAutomationId("TitleBar.BottomPanel");
        Skip.IfNot(bottomPanelButton.Patterns.Toggle.IsSupported, "TitleBar.BottomPanel does not expose TogglePattern in current UIA backend.");

        var before = bottomPanelButton.Patterns.Toggle.Pattern.ToggleState.Value;
        bottomPanelButton.Patterns.Toggle.Pattern.Toggle();
        Thread.Sleep(120);

        var after = session.FindByAutomationId("TitleBar.BottomPanel").Patterns.Toggle.Pattern.ToggleState.Value;
        Assert.NotEqual(before, after);
    }

    [SkippableFact]
    public void MoreSessionsDialog_SelectsOverflowSession_AndUpdatesChatHeader()
    {
        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData(sessionCount: 21);
        using var session = WindowsGuiAppSession.LaunchFresh();

        session.ActivateElement(session.FindByAutomationId("MainNav.More.project-1"));

        var dialog = session.FindByAutomationId("SessionsDialog", TimeSpan.FromSeconds(10));
        var dialogSession = session.FindFirstDescendantByControlType(dialog, ControlType.ListItem, TimeSpan.FromSeconds(10));

        Assert.NotNull(dialogSession);
        session.ActivateElement(dialogSession);

        var chatHeader = session.FindByAutomationId("ChatView.CurrentSessionTitle", TimeSpan.FromSeconds(10));

        Assert.NotNull(dialog);
        Assert.False(string.IsNullOrWhiteSpace(chatHeader.Name));
    }

    [SkippableFact]
    public void CompactMode_AddProject_HidesExpandedLabel_AndStaysBetweenStartAndProjects()
    {
        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();

        session.ResizeMainWindow(width: 800, height: 900);
        Thread.Sleep(1500);

        Assert.True(
            WaitForCompactNavigationAffordance(session, TimeSpan.FromSeconds(4)),
            $"Expected compact navigation affordance to be onscreen after resizing to compact. {DumpCompactNavigationAffordance(session)}{Environment.NewLine}{appData.ReadBootLogTail()}");

        var startItem = session.FindByAutomationId("MainNav.Start");
        var addProject = session.FindByAutomationId("MainNav.AddProject");
        var firstProject = session.FindByAutomationId("MainNav.Project.project-1");
        var captureRoot = Path.Combine(Path.GetTempPath(), "SalmonEgg.GuiTests");
        Directory.CreateDirectory(captureRoot);
        var screenshotPath = Path.Combine(captureRoot, "nav-compact-main.png");
        session.CaptureMainWindowToFile(screenshotPath);

        var startRect = startItem.BoundingRectangle;
        var addRect = addProject.BoundingRectangle;
        var projectRect = firstProject.BoundingRectangle;
        var startCenterY = startRect.Y + (startRect.Height / 2d);
        var addCenterY = addRect.Y + (addRect.Height / 2d);
        var projectCenterY = projectRect.Y + (projectRect.Height / 2d);

        Assert.True(
            startCenterY < addCenterY && addCenterY < projectCenterY,
            $"Expected Start -> AddProject -> first project order in compact mode, but got StartY={startCenterY}, AddY={addCenterY}, ProjectY={projectCenterY}.{Environment.NewLine}{appData.ReadBootLogTail()}");

        var affordanceElements = addProject.FindAllDescendants()
            .Where(IsVisibleAffordanceElement)
            .Select(element => $"{element.ControlType}:{element.Name}")
            .ToArray();

        // Compact-mode SymbolIcon content is rendered visually but is not exposed as a stable
        // descendant Text/Image/Button peer in WinUI's UIA tree. The smoke contract we can
        // reliably enforce is that the item stays visible in order and does not leak expanded
        // text/button affordances into compact mode.
        Assert.DoesNotContain(affordanceElements, item => item.Contains("ControlType.Text", StringComparison.Ordinal));
        Assert.DoesNotContain(affordanceElements, item => item.Contains("ControlType.Button", StringComparison.Ordinal));
    }

    [SkippableFact]
    public void MinimalMode_Resize_CollapsesLeftPane()
    {
        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();

        session.ResizeMainWindow(width: 500, height: 900);
        Thread.Sleep(1500);

        var startItem = session.TryFindByAutomationId("MainNav.Start", TimeSpan.FromSeconds(2));
        var addProjectItem = session.TryFindByAutomationId("MainNav.AddProject", TimeSpan.FromSeconds(2));

        var startVisible = startItem is not null && !TryGetIsOffscreen(startItem);
        var addProjectVisible = addProjectItem is not null && !TryGetIsOffscreen(addProjectItem);

        Assert.False(
            startVisible || addProjectVisible,
            $"Expected minimal mode to collapse the left pane at width=500. StartVisible={startVisible}, AddProjectVisible={addProjectVisible}.{Environment.NewLine}{appData.ReadBootLogTail()}");

        Assert.True(
            session.WaitUntilOnscreen("TitleBar.ToggleSidebar", TimeSpan.FromSeconds(4)),
            $"Expected title bar sidebar toggle to remain onscreen in minimal mode.{Environment.NewLine}{appData.ReadBootLogTail()}");

        session.InvokeButton("TitleBar.ToggleSidebar");

        Assert.True(
            WaitForCompactNavigationAffordance(session, TimeSpan.FromSeconds(6)),
            $"Expected navigation affordance to become onscreen after minimal toggle open.{Environment.NewLine}{DumpCompactNavigationAffordance(session)}{Environment.NewLine}{appData.ReadBootLogTail()}");
    }

    [SkippableFact]
    public void CollapsedPane_AddProject_DoesNotLeakExpandedLabel()
    {
        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();

        session.ResizeMainWindow(width: 1400, height: 900);
        Thread.Sleep(1500);
        session.InvokeButton("TitleBar.ToggleSidebar");
        Thread.Sleep(1500);

        var addProject = session.FindByAutomationId("MainNav.AddProject");
        var affordanceElements = addProject.FindAllDescendants()
            .Where(IsVisibleAffordanceElement)
            .Select(element => $"{element.ControlType}:{element.Name}")
            .ToArray();

        Assert.DoesNotContain(affordanceElements, item => item.Contains("ControlType.Text", StringComparison.Ordinal));
        Assert.DoesNotContain(affordanceElements, item => item.Contains("ControlType.Button", StringComparison.Ordinal));
    }

    [SkippableFact]
    public void ActiveSession_CollapsedPane_AddProject_DoesNotLeakExpandedLabel()
    {
        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();

        session.ResizeMainWindow(width: 1400, height: 900);
        Thread.Sleep(1500);

        var sessionItem = session.FindByAutomationId("MainNav.Session.gui-session-01");
        session.ActivateElement(sessionItem);
        session.FindByAutomationId("ChatView.CurrentSessionTitle", TimeSpan.FromSeconds(10));

        session.InvokeButton("TitleBar.ToggleSidebar");
        Thread.Sleep(1500);
        var captureRoot = Path.Combine(Path.GetTempPath(), "SalmonEgg.GuiTests");
        Directory.CreateDirectory(captureRoot);
        session.CaptureMainWindowToFile(Path.Combine(captureRoot, "nav-collapsed-active-session.png"));

        var addProject = session.FindByAutomationId("MainNav.AddProject");
        var affordanceElements = addProject.FindAllDescendants()
            .Where(IsVisibleAffordanceElement)
            .Select(element => $"{element.ControlType}:{element.Name}")
            .ToArray();

        Assert.DoesNotContain(affordanceElements, item => item.Contains("ControlType.Text", StringComparison.Ordinal));
        Assert.DoesNotContain(affordanceElements, item => item.Contains("ControlType.Button", StringComparison.Ordinal));
    }

    [SkippableFact]
    public void ActiveSession_SelectionRemainsVisible_AcrossExpandedCollapse_AndMinimalToCompact()
    {
        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();

        const string sessionId = "MainNav.Session.gui-session-01";
        const string projectId = "MainNav.Project.project-1";
        const string startId = "MainNav.Start";

        var timeline = new System.Collections.Generic.List<string>();
        static string Stamp() => DateTime.UtcNow.ToString("HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture);

        session.ResizeMainWindow(width: 1400, height: 900);
        Thread.Sleep(1200);

        var sessionItem = session.FindByAutomationId(sessionId, TimeSpan.FromSeconds(10));
        session.ActivateElement(sessionItem);
        session.FindByAutomationId("ChatView.CurrentSessionTitle", TimeSpan.FromSeconds(10));
        timeline.Add($"{Stamp()} selected session");

        Assert.True(
            WaitForVisibleSelected(session, [sessionId, projectId, startId], TimeSpan.FromSeconds(6), out var winnerOpen),
            $"Expected a visible selected nav item in expanded-open mode. winner={winnerOpen ?? "<null>"}{Environment.NewLine}{DumpSelectionSnapshot(session, sessionId, projectId, startId)}{Environment.NewLine}{appData.ReadBootLogTail()}");
        Assert.Equal(sessionId, winnerOpen);

        session.InvokeButton("TitleBar.ToggleSidebar");
        timeline.Add($"{Stamp()} toggled sidebar collapsed in expanded");
        Assert.False(
            TryGetVisibleIsSelected(session, startId) == true,
            $"Did not expect collapsed expanded-mode pane to fall back to Start. {DumpSelectionSnapshot(session, sessionId, projectId, startId)}{Environment.NewLine}{appData.ReadBootLogTail()}");

        session.InvokeButton("TitleBar.ToggleSidebar");
        timeline.Add($"{Stamp()} toggled sidebar expanded");
        Assert.True(
            WaitForVisibleSelected(session, [sessionId, projectId, startId], TimeSpan.FromSeconds(6), out var winnerExpandedBack),
            $"Expected a visible selected nav item after expanded restore. winner={winnerExpandedBack ?? "<null>"}{Environment.NewLine}{DumpSelectionSnapshot(session, sessionId, projectId, startId)}{Environment.NewLine}{appData.ReadBootLogTail()}");
        Assert.Equal(sessionId, winnerExpandedBack);

        session.ResizeMainWindow(width: 500, height: 900);
        Thread.Sleep(1400);
        timeline.Add($"{Stamp()} resized to minimal");

        session.ResizeMainWindow(width: 800, height: 900);
        Thread.Sleep(1400);
        timeline.Add($"{Stamp()} resized to compact");

        Assert.False(
            TryGetVisibleIsSelected(session, startId) == true,
            $"Did not expect minimal->compact transition to fall back to Start. timeline={string.Join(" | ", timeline)}{Environment.NewLine}{DumpSelectionSnapshot(session, sessionId, projectId, startId)}{Environment.NewLine}{appData.ReadBootLogTail()}");

        if (IsElementVisible(session, sessionId))
        {
            Assert.True(
                TryGetVisibleIsSelected(session, sessionId) == true,
                $"Expected visible session item to remain selected after minimal->compact. timeline={string.Join(" | ", timeline)}{Environment.NewLine}{DumpSelectionSnapshot(session, sessionId, projectId, startId)}{Environment.NewLine}{appData.ReadBootLogTail()}");
        }
        else
        {
            session.InvokeButton("TitleBar.ToggleSidebar");
            timeline.Add($"{Stamp()} toggled compact open");
            Assert.True(
                session.WaitUntilOnscreen("ChatView.CurrentSessionTitle", TimeSpan.FromSeconds(8)),
                $"Expected active session content to remain visible after compact flyout open. timeline={string.Join(" | ", timeline)}{Environment.NewLine}{DumpSelectionSnapshot(session, sessionId, projectId, startId)}{Environment.NewLine}{DumpProjectSnapshot(session, projectId)}{Environment.NewLine}{appData.ReadBootLogTail()}");
        }
    }

    [SkippableFact]
    public void ActiveSession_ResizeBackToExpanded_RestoresVisibleSessionSelection()
    {
        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();

        const string sessionId = "MainNav.Session.gui-session-01";
        const string projectId = "MainNav.Project.project-1";
        const string startId = "MainNav.Start";

        session.ResizeMainWindow(width: 1400, height: 900);
        Thread.Sleep(1200);

        session.ActivateElement(session.FindByAutomationId(sessionId, TimeSpan.FromSeconds(10)));
        session.FindByAutomationId("ChatView.CurrentSessionTitle", TimeSpan.FromSeconds(10));

        session.ResizeMainWindow(width: 500, height: 900);
        Thread.Sleep(1400);

        session.ResizeMainWindow(width: 1400, height: 900);
        Thread.Sleep(1600);

        Assert.True(
            WaitForVisibleSelected(session, [sessionId, projectId, startId], TimeSpan.FromSeconds(8), out var winner),
            $"Expected a visible selected nav item after expanded->minimal->expanded resize. winner={winner ?? "<null>"}{Environment.NewLine}{DumpSelectionSnapshot(session, sessionId, projectId, startId)}{Environment.NewLine}{DumpProjectSnapshot(session, projectId)}{Environment.NewLine}{appData.ReadBootLogTail()}");
        Assert.Equal(sessionId, winner);
    }

    [SkippableFact]
    public void ActiveSession_ExpandedToCompact_WithExplicitOpenIntent_PreservesSelectionContext()
    {
        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();

        const string sessionId = "MainNav.Session.gui-session-01";
        const string projectId = "MainNav.Project.project-1";
        const string startId = "MainNav.Start";

        session.ResizeMainWindow(width: 1400, height: 900);
        Thread.Sleep(1200);

        session.ActivateElement(session.FindByAutomationId(sessionId, TimeSpan.FromSeconds(10)));
        session.FindByAutomationId("ChatView.CurrentSessionTitle", TimeSpan.FromSeconds(10));

        session.InvokeButton("TitleBar.ToggleSidebar");
        Thread.Sleep(600);
        session.InvokeButton("TitleBar.ToggleSidebar");
        Thread.Sleep(1200);

        session.ResizeMainWindow(width: 800, height: 900);
        Thread.Sleep(1500);

        AssertCompactNativeSelection(
            session,
            sessionId,
            projectId,
            startId,
            TimeSpan.FromSeconds(6),
            $"expanded->compact with explicit open intent",
            appData.ReadBootLogTail());

        if (!IsElementVisible(session, sessionId))
        {
            session.InvokeButton("TitleBar.ToggleSidebar");
            Thread.Sleep(1200);
            Assert.True(
                session.WaitUntilOnscreen("ChatView.CurrentSessionTitle", TimeSpan.FromSeconds(8)),
                $"Expected active session content to remain visible after explicit-open compact path. {DumpSelectionSnapshot(session, sessionId, projectId, startId)}{Environment.NewLine}{DumpProjectSnapshot(session, projectId)}{Environment.NewLine}{appData.ReadBootLogTail()}");
        }
    }

    [SkippableFact]
    public void ActiveSession_ExpandedToCompact_WithoutManualToggle_KeepsSemanticSelection()
    {
        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();

        const string sessionId = "MainNav.Session.gui-session-01";
        const string projectId = "MainNav.Project.project-1";
        const string startId = "MainNav.Start";

        session.ResizeMainWindow(width: 1400, height: 900);
        Thread.Sleep(1200);

        session.ActivateElement(session.FindByAutomationId(sessionId, TimeSpan.FromSeconds(10)));
        session.FindByAutomationId("ChatView.CurrentSessionTitle", TimeSpan.FromSeconds(10));

        session.ResizeMainWindow(width: 800, height: 900);
        Thread.Sleep(1600);

        AssertCompactNativeSelection(
            session,
            sessionId,
            projectId,
            startId,
            TimeSpan.FromSeconds(6),
            "pure resize path",
            appData.ReadBootLogTail());
    }

    private static bool IsVisibleAffordanceElement(AutomationElement element)
    {
        if (TryGetIsOffscreen(element))
        {
            return false;
        }

        return element.ControlType == ControlType.Text
            || element.ControlType == ControlType.Button
            || element.ControlType == ControlType.Image;
    }

    private static bool TryGetIsOffscreen(AutomationElement element)
    {
        try
        {
            return element.IsOffscreen;
        }
        catch
        {
            return false;
        }
    }

    private static bool WaitForVisibleSelected(
        WindowsGuiAppSession session,
        string[] candidates,
        TimeSpan timeout,
        out string? winner)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            foreach (var candidate in candidates)
            {
                if (TryGetVisibleIsSelected(session, candidate) == true)
                {
                    winner = candidate;
                    return true;
                }
            }

            Thread.Sleep(120);
        }

        winner = null;
        return false;
    }

    private static bool WaitForSearchSuggestion(WindowsGuiAppSession session, string automationId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (session.TryFindByAutomationIdAnywhere(automationId, TimeSpan.FromMilliseconds(200)) is not null)
            {
                return true;
            }

            Thread.Sleep(120);
        }

        return false;
    }

    private static bool? TryGetVisibleIsSelected(WindowsGuiAppSession session, string automationId)
    {
        var element = session.TryFindByAutomationId(automationId, TimeSpan.FromMilliseconds(200));
        if (element is null || TryGetIsOffscreen(element))
        {
            return null;
        }

        try
        {
            if (!element.Patterns.SelectionItem.IsSupported)
            {
                return null;
            }

            return element.Patterns.SelectionItem.Pattern.IsSelected.Value;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsElementVisible(WindowsGuiAppSession session, string automationId)
    {
        var element = session.TryFindByAutomationId(automationId, TimeSpan.FromMilliseconds(200));
        return element is not null && !TryGetIsOffscreen(element);
    }

    private static bool WaitForCompactNavigationAffordance(WindowsGuiAppSession session, TimeSpan timeout)
    {
        return session.WaitUntilOnscreen("MainNav.Start", timeout)
            || session.WaitUntilOnscreen("MainNav.AddProject", timeout)
            || session.WaitUntilOnscreen("MainNav.Project.project-1", timeout);
    }

    private static void NavigateToShortcutsSettings(WindowsGuiAppSession session)
    {
        var settingsItem = session.FindByAutomationId("SettingsItem", TimeSpan.FromSeconds(10));
        session.ActivateElement(settingsItem);

        var shortcutsSettingsItem = session.TryFindByAutomationId("SettingsNav.Shortcuts", TimeSpan.FromSeconds(10))
            ?? session.TryFindVisibleElementByNameAnywhere("Shortcuts", TimeSpan.FromSeconds(10))
            ?? session.TryFindVisibleElementByNameAnywhere("快捷键", TimeSpan.FromSeconds(10));

        Assert.NotNull(shortcutsSettingsItem);
        session.ActivateElement(shortcutsSettingsItem!);

        Assert.True(
            session.WaitUntilOnscreen("Shortcuts.Record.search", TimeSpan.FromSeconds(10)),
            "Shortcut recorder for search did not become visible.");
    }

    private static string DumpCompactNavigationAffordance(WindowsGuiAppSession session)
    {
        var startVisible = session.WaitUntilOnscreen("MainNav.Start", TimeSpan.FromMilliseconds(200));
        var addProjectVisible = session.WaitUntilOnscreen("MainNav.AddProject", TimeSpan.FromMilliseconds(200));
        var firstProjectVisible = session.WaitUntilOnscreen("MainNav.Project.project-1", TimeSpan.FromMilliseconds(200));
        return $"compact affordance => start:{startVisible}, addProject:{addProjectVisible}, firstProject:{firstProjectVisible}";
    }

    private static void AssertCompactNativeSelection(
        WindowsGuiAppSession session,
        string sessionId,
        string projectId,
        string startId,
        TimeSpan timeout,
        string scenario,
        string bootLogTail)
    {
        Assert.True(
            WaitForCompactNavigationAffordance(session, timeout),
            $"Expected compact navigation affordance to be onscreen for {scenario}. {DumpCompactNavigationAffordance(session)}{Environment.NewLine}{DumpSelectionSnapshot(session, sessionId, projectId, startId)}{Environment.NewLine}{DumpProjectSnapshot(session, projectId)}{Environment.NewLine}{DumpAutomationSelectionState(session)}{Environment.NewLine}{bootLogTail}");

        Assert.True(
            WaitForCompactSelectionContext(session, sessionId, projectId, startId, timeout, out var winner),
            $"Expected compact selection context to settle for {scenario}, but it did not. winner={winner ?? "<null>"}{Environment.NewLine}{DumpSelectionSnapshot(session, sessionId, projectId, startId)}{Environment.NewLine}{DumpProjectSnapshot(session, projectId)}{Environment.NewLine}{DumpAutomationSelectionState(session)}{Environment.NewLine}{bootLogTail}");

        Assert.NotEqual(
            startId,
            winner);

        var sessionVisible = IsElementVisible(session, sessionId);
        if (sessionVisible)
        {
            Assert.Equal(sessionId, winner);
            return;
        }

        if (winner == projectId)
        {
            return;
        }

        Assert.Fail($"Expected visible selection to fall back to project, but winner was {winner ?? "<null>"}. {DumpSelectionSnapshot(session, sessionId, projectId, startId)}");
    }

    private static bool WaitForCompactSelectionContext(
        WindowsGuiAppSession session,
        string sessionId,
        string projectId,
        string startId,
        TimeSpan timeout,
        out string? winner)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var sessionVisible = IsElementVisible(session, sessionId);
            var sessionSelected = TryGetVisibleIsSelected(session, sessionId) == true;
            var projectSelected = TryGetVisibleIsSelected(session, projectId) == true;
            var startSelected = TryGetVisibleIsSelected(session, startId) == true;
            var projectElement = session.TryFindByAutomationId(projectId, TimeSpan.FromMilliseconds(200));
            var projectHasSelectedDescendant = TryGetHasSelectedDescendant(projectElement);
            var chatHeaderVisible = session.WaitUntilOnscreen("ChatView.CurrentSessionTitle", TimeSpan.FromMilliseconds(200));

            if (sessionVisible && sessionSelected)
            {
                winner = sessionId;
                return true;
            }

            if (!sessionVisible && (projectSelected || projectHasSelectedDescendant))
            {
                winner = projectId;
                return true;
            }

            if (startSelected)
            {
                winner = startId;
                return true;
            }

            var automationContext = TryGetAutomationSelectionContext(session);
            if (string.Equals(automationContext, "Session", StringComparison.Ordinal)
                || string.Equals(automationContext, "Ancestor", StringComparison.Ordinal))
            {
                // Automation probe is diagnostic-only fallback; require pane/content affordance
                // to be onscreen to avoid accepting stale context snapshots.
                if (WaitForCompactNavigationAffordance(session, TimeSpan.FromMilliseconds(200)) || chatHeaderVisible)
                {
                    winner = string.Equals(automationContext, "Session", StringComparison.Ordinal)
                        ? sessionId
                        : projectId;
                    return true;
                }
            }

            Thread.Sleep(120);
        }

        winner = null;
        return false;
    }

    private static string DumpSelectionSnapshot(WindowsGuiAppSession session, string sessionId, string projectId, string startId)
    {
        var sessionSelected = TryGetVisibleIsSelected(session, sessionId);
        var projectSelected = TryGetVisibleIsSelected(session, projectId);
        var startSelected = TryGetVisibleIsSelected(session, startId);
        var sessionVisible = IsElementVisible(session, sessionId);
        var projectVisible = IsElementVisible(session, projectId);
        var startVisible = IsElementVisible(session, startId);
        return $"visible-selected snapshot => session:{sessionSelected?.ToString() ?? "null"}(visible={sessionVisible}), project:{projectSelected?.ToString() ?? "null"}(visible={projectVisible}), start:{startSelected?.ToString() ?? "null"}(visible={startVisible})";
    }

    private static string DumpAutomationSelectionState(WindowsGuiAppSession session)
        => $"automation selection state => {session.TryGetElementName("MainNav.Automation.SelectionState", TimeSpan.FromMilliseconds(200)) ?? "<missing>"}";

    private static string? TryGetAutomationSelectionContext(WindowsGuiAppSession session)
    {
        var state = session.TryGetElementName("MainNav.Automation.SelectionState", TimeSpan.FromMilliseconds(200));
        if (string.IsNullOrWhiteSpace(state))
        {
            return null;
        }

        foreach (var segment in state.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (segment.StartsWith("Context=", StringComparison.Ordinal))
            {
                return segment["Context=".Length..];
            }
        }

        return null;
    }

    private static string DumpProjectSnapshot(WindowsGuiAppSession session, string projectId)
    {
        var project = session.TryFindByAutomationId(projectId, TimeSpan.FromMilliseconds(500));
        if (project is null)
        {
            return "project snapshot => <missing>";
        }

        string expandState;
        try
        {
            expandState = project.Patterns.ExpandCollapse.IsSupported
                ? project.Patterns.ExpandCollapse.Pattern.ExpandCollapseState.Value.ToString()
                : "<unsupported>";
        }
        catch
        {
            expandState = "<error>";
        }

        return $"project snapshot => offscreen:{TryGetIsOffscreen(project)}, visibleSelected:{TryGetVisibleIsSelected(session, projectId)?.ToString() ?? "null"}, expand:{expandState}";
    }

    private static bool TryGetHasSelectedDescendant(AutomationElement? element)
    {
        if (element is null)
        {
            return false;
        }

        try
        {
            return string.Equals(element.ItemStatus, "Selected", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
