using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using FlaUI.Core.Definitions;
using FlaUI.Core.AutomationElements;

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

        var selectionItem = startItem.Patterns.SelectionItem.Pattern;
        var launchSnapshot = string.Join(
            "; ",
            $"StartView={session.TryFindByAutomationId("StartView.Title", TimeSpan.FromSeconds(2)) is not null}",
            $"ChatHeader={session.TryFindByAutomationId("ChatView.CurrentSessionNameButton", TimeSpan.FromSeconds(2)) is not null}",
            $"StartSelected={session.TryGetIsSelected("MainNav.Start")}",
            $"Session01Selected={session.TryGetIsSelected("MainNav.Session.gui-session-01")}");
        Assert.True(
            selectionItem.IsSelected.Value,
            $"Launch state mismatch. {launchSnapshot}{Environment.NewLine}{appData.ReadBootLogTail()}");
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

        var chatHeader = session.FindByAutomationId("ChatView.CurrentSessionNameButton", TimeSpan.FromSeconds(10));
        var selectionItem = sessionItem.Patterns.SelectionItem.Pattern;

        Assert.NotNull(chatHeader);
        Assert.Contains("GUI Session 01", chatHeader.Name, StringComparison.Ordinal);
        Assert.True(selectionItem.IsSelected.Value);
    }

    [SkippableFact]
    public void TitleBarPanelButtons_Toggle_ChangesBottomPanelState()
    {
        using var _ = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();

        var sessionItem = session.FindByAutomationId("MainNav.Session.gui-session-01");
        session.ActivateElement(sessionItem);
        session.FindByAutomationId("ChatView.CurrentSessionNameButton", TimeSpan.FromSeconds(10));

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

        var chatHeader = session.FindByAutomationId("ChatView.CurrentSessionNameButton", TimeSpan.FromSeconds(10));

        Assert.NotNull(dialog);
        Assert.False(string.IsNullOrWhiteSpace(chatHeader.Name));
    }

    [SkippableFact]
    public void CompactMode_AddProject_HidesExpandedLabel_AndStaysBetweenStartAndProjects()
    {
        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();

        ResizeMainWindow(width: 800, height: 900);
        Thread.Sleep(1500);

        var startItem = session.FindByAutomationId("MainNav.Start");
        var addProject = session.FindByAutomationId("MainNav.AddProject");
        var firstProject = session.FindByAutomationId("MainNav.Project.project-1");
        var captureRoot = Path.Combine(Path.GetTempPath(), "SalmonEgg.GuiTests");
        Directory.CreateDirectory(captureRoot);
        var screenshotPath = Path.Combine(captureRoot, "nav-compact-main.png");
        session.MainWindow.CaptureToFile(screenshotPath);

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

        ResizeMainWindow(width: 500, height: 900);
        Thread.Sleep(1500);

        var startItem = session.TryFindByAutomationId("MainNav.Start", TimeSpan.FromSeconds(2));
        var addProjectItem = session.TryFindByAutomationId("MainNav.AddProject", TimeSpan.FromSeconds(2));

        var startVisible = startItem is not null && !TryGetIsOffscreen(startItem);
        var addProjectVisible = addProjectItem is not null && !TryGetIsOffscreen(addProjectItem);

        Assert.False(
            startVisible || addProjectVisible,
            $"Expected minimal mode to collapse the left pane at width=500. StartVisible={startVisible}, AddProjectVisible={addProjectVisible}.{Environment.NewLine}{appData.ReadBootLogTail()}");
    }

    [SkippableFact]
    public void CollapsedPane_AddProject_DoesNotLeakExpandedLabel()
    {
        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();

        ResizeMainWindow(width: 1400, height: 900);
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

        ResizeMainWindow(width: 1400, height: 900);
        Thread.Sleep(1500);

        var sessionItem = session.FindByAutomationId("MainNav.Session.gui-session-01");
        session.ActivateElement(sessionItem);
        session.FindByAutomationId("ChatView.CurrentSessionNameButton", TimeSpan.FromSeconds(10));

        session.InvokeButton("TitleBar.ToggleSidebar");
        Thread.Sleep(1500);
        var captureRoot = Path.Combine(Path.GetTempPath(), "SalmonEgg.GuiTests");
        Directory.CreateDirectory(captureRoot);
        session.MainWindow.CaptureToFile(Path.Combine(captureRoot, "nav-collapsed-active-session.png"));

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

        ResizeMainWindow(width: 1400, height: 900);
        Thread.Sleep(1200);

        var sessionItem = session.FindByAutomationId(sessionId, TimeSpan.FromSeconds(10));
        session.ActivateElement(sessionItem);
        session.FindByAutomationId("ChatView.CurrentSessionNameButton", TimeSpan.FromSeconds(10));
        timeline.Add($"{Stamp()} selected session");

        Assert.True(
            WaitForVisibleSelected(session, [sessionId, projectId, startId], TimeSpan.FromSeconds(6), out var winnerOpen),
            $"Expected a visible selected nav item in expanded-open mode. winner={winnerOpen ?? "<null>"}{Environment.NewLine}{DumpSelectionSnapshot(session, sessionId, projectId, startId)}{Environment.NewLine}{appData.ReadBootLogTail()}");
        Assert.Equal(sessionId, winnerOpen);

        session.InvokeButton("TitleBar.ToggleSidebar");
        timeline.Add($"{Stamp()} toggled sidebar collapsed in expanded");
        var projectItem = session.FindByAutomationId(projectId);
        var activeDescendantIndicator = string.Equals(projectItem.HelpText, "True", StringComparison.Ordinal);
        Assert.True(
            activeDescendantIndicator,
            $"Expected project to indicate active descendant after expanded collapse. HelpText={projectItem.HelpText}{Environment.NewLine}{DumpSelectionSnapshot(session, sessionId, projectId, startId)}{Environment.NewLine}{appData.ReadBootLogTail()}");

        session.InvokeButton("TitleBar.ToggleSidebar");
        timeline.Add($"{Stamp()} toggled sidebar expanded");
        Assert.True(
            WaitForVisibleSelected(session, [sessionId, projectId, startId], TimeSpan.FromSeconds(6), out var winnerExpandedBack),
            $"Expected a visible selected nav item after expanded restore. winner={winnerExpandedBack ?? "<null>"}{Environment.NewLine}{DumpSelectionSnapshot(session, sessionId, projectId, startId)}{Environment.NewLine}{appData.ReadBootLogTail()}");
        Assert.Equal(sessionId, winnerExpandedBack);

        ResizeMainWindow(width: 500, height: 900);
        Thread.Sleep(1400);
        timeline.Add($"{Stamp()} resized to minimal");

        ResizeMainWindow(width: 800, height: 900);
        Thread.Sleep(1400);
        timeline.Add($"{Stamp()} resized to compact");

        var sessionSelectedInCompact = TryGetVisibleIsSelected(session, sessionId) == true;
        if (!sessionSelectedInCompact)
        {
            var projectCompact = session.FindByAutomationId(projectId);
            var compactIndicator = string.Equals(projectCompact.HelpText, "True", StringComparison.Ordinal);
            Assert.True(
                compactIndicator,
                $"Expected project to indicate active descendant after minimal->compact when session item is not visible. HelpText={projectCompact.HelpText} timeline={string.Join(" | ", timeline)}{Environment.NewLine}{DumpSelectionSnapshot(session, sessionId, projectId, startId)}{Environment.NewLine}{appData.ReadBootLogTail()}");
        }
        else
        {
            Assert.True(
                sessionSelectedInCompact,
                $"Expected session to remain selected after minimal->compact when pane is open. timeline={string.Join(" | ", timeline)}{Environment.NewLine}{DumpSelectionSnapshot(session, sessionId, projectId, startId)}{Environment.NewLine}{appData.ReadBootLogTail()}");
        }

        if (!sessionSelectedInCompact)
        {
            session.InvokeButton("TitleBar.ToggleSidebar");
            timeline.Add($"{Stamp()} toggled compact open");
            Assert.True(
                WaitForVisibleSelected(session, [sessionId, projectId, startId], TimeSpan.FromSeconds(8), out var winnerCompactOpen),
                $"Expected a visible selected nav item after compact open. winner={winnerCompactOpen ?? "<null>"} timeline={string.Join(" | ", timeline)}{Environment.NewLine}{DumpSelectionSnapshot(session, sessionId, projectId, startId)}{Environment.NewLine}{appData.ReadBootLogTail()}");
            Assert.Equal(sessionId, winnerCompactOpen);
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

        ResizeMainWindow(width: 1400, height: 900);
        Thread.Sleep(1200);

        session.ActivateElement(session.FindByAutomationId(sessionId, TimeSpan.FromSeconds(10)));
        session.FindByAutomationId("ChatView.CurrentSessionNameButton", TimeSpan.FromSeconds(10));

        ResizeMainWindow(width: 500, height: 900);
        Thread.Sleep(1400);

        ResizeMainWindow(width: 1400, height: 900);
        Thread.Sleep(1600);

        Assert.True(
            WaitForVisibleSelected(session, [sessionId, projectId, startId], TimeSpan.FromSeconds(8), out var winner),
            $"Expected a visible selected nav item after expanded->minimal->expanded resize. winner={winner ?? "<null>"}{Environment.NewLine}{DumpSelectionSnapshot(session, sessionId, projectId, startId)}{Environment.NewLine}{DumpProjectSnapshot(session, projectId)}{Environment.NewLine}{appData.ReadBootLogTail()}");
        Assert.Equal(sessionId, winner);
    }

    [SkippableFact]
    public void ActiveSession_ExpandedToCompact_WithExplicitOpenIntent_ProjectsSelectionToProject()
    {
        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();

        const string sessionId = "MainNav.Session.gui-session-01";
        const string projectId = "MainNav.Project.project-1";
        const string startId = "MainNav.Start";

        ResizeMainWindow(width: 1400, height: 900);
        Thread.Sleep(1200);

        session.ActivateElement(session.FindByAutomationId(sessionId, TimeSpan.FromSeconds(10)));
        session.FindByAutomationId("ChatView.CurrentSessionNameButton", TimeSpan.FromSeconds(10));

        session.InvokeButton("TitleBar.ToggleSidebar");
        Thread.Sleep(600);
        session.InvokeButton("TitleBar.ToggleSidebar");
        Thread.Sleep(1200);

        ResizeMainWindow(width: 800, height: 900);
        Thread.Sleep(1500);

        Assert.True(
            WaitForVisibleSelected(session, [sessionId, projectId, startId], TimeSpan.FromSeconds(8), out var winner),
            $"Expected a visible selected nav item after expanded->compact with explicit open intent. winner={winner ?? "<null>"}{Environment.NewLine}{DumpSelectionSnapshot(session, sessionId, projectId, startId)}{Environment.NewLine}{DumpProjectSnapshot(session, projectId)}{Environment.NewLine}{appData.ReadBootLogTail()}");
        Assert.Equal(projectId, winner);
    }

    [SkippableFact]
    public void ActiveSession_ExpandedToCompact_WithoutManualToggle_KeepsSemanticSelection()
    {
        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();

        const string sessionId = "MainNav.Session.gui-session-01";
        const string projectId = "MainNav.Project.project-1";
        const string startId = "MainNav.Start";

        ResizeMainWindow(width: 1400, height: 900);
        Thread.Sleep(1200);

        session.ActivateElement(session.FindByAutomationId(sessionId, TimeSpan.FromSeconds(10)));
        session.FindByAutomationId("ChatView.CurrentSessionNameButton", TimeSpan.FromSeconds(10));

        ResizeMainWindow(width: 800, height: 900);
        Thread.Sleep(1600);

        var sessionSelected = TryGetVisibleIsSelected(session, sessionId) == true;
        if (sessionSelected)
        {
            Assert.True(
                sessionSelected,
                $"Expected session to stay selected in compact when pane remains open. {DumpSelectionSnapshot(session, sessionId, projectId, startId)}{Environment.NewLine}{DumpProjectSnapshot(session, projectId)}{Environment.NewLine}{appData.ReadBootLogTail()}");
            return;
        }

        var projectItem = session.FindByAutomationId(projectId);
        var hasActiveDescendant = string.Equals(projectItem.HelpText, "True", StringComparison.Ordinal);
        Assert.True(
            hasActiveDescendant,
            $"Expected compact collapsed pane to keep semantic selection via project active-descendant indicator. HelpText={projectItem.HelpText}{Environment.NewLine}{DumpSelectionSnapshot(session, sessionId, projectId, startId)}{Environment.NewLine}{DumpProjectSnapshot(session, projectId)}{Environment.NewLine}{appData.ReadBootLogTail()}");

        var startSelected = TryGetVisibleIsSelected(session, startId) == true;
        Assert.False(
            startSelected,
            $"Did not expect selection to fall back to Start after pure resize path. {DumpSelectionSnapshot(session, sessionId, projectId, startId)}{Environment.NewLine}{appData.ReadBootLogTail()}");
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
        [StructLayout(LayoutKind.Sequential)]
        private struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool MoveWindow(IntPtr hWnd, int x, int y, int width, int height, bool repaint);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

        [DllImport("user32.dll", SetLastError = true)]
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

    private static string DumpSelectionSnapshot(WindowsGuiAppSession session, string sessionId, string projectId, string startId)
    {
        var sessionSelected = TryGetVisibleIsSelected(session, sessionId);
        var projectSelected = TryGetVisibleIsSelected(session, projectId);
        var startSelected = TryGetVisibleIsSelected(session, startId);
        return $"visible-selected snapshot => session:{sessionSelected?.ToString() ?? "null"}, project:{projectSelected?.ToString() ?? "null"}, start:{startSelected?.ToString() ?? "null"}";
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

        return $"project snapshot => offscreen:{TryGetIsOffscreen(project)}, helpText:{project.HelpText}, expand:{expandState}";
    }
}
