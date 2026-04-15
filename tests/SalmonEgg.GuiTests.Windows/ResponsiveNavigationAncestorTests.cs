using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using FlaUI.Core.AutomationElements;
using Xunit;

namespace SalmonEgg.GuiTests.Windows;

public sealed class ResponsiveNavigationAncestorTests
{
    private const string SessionAutomationId = "MainNav.Session.gui-session-01";
    private const string ProjectAutomationId = "MainNav.Project.project-1";
    private const string StartAutomationId = "MainNav.Start";

    [SkippableFact]
    public void ActiveSession_ExpandedToCompact_WithoutManualToggle_SettlesToAncestorContext()
    {
        GuiTestGate.RequireEnabled();

        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();

        ResizeMainWindow(width: 1400, height: 900);

        var sessionItem = session.FindByAutomationId(SessionAutomationId, TimeSpan.FromSeconds(10));
        session.ActivateElement(sessionItem);
        Assert.True(
            session.WaitUntilVisible("ChatView.CurrentSessionNameButton", TimeSpan.FromSeconds(10)),
            "Chat header did not appear after selecting deterministic session gui-session-01.");

        DragMainWindowToCompact(widths: [1280, 1160, 1040, 960, 900, 860, 820, 800], height: 900);

        if (WaitForCompactSelectionContext(session, SessionAutomationId, ProjectAutomationId, StartAutomationId, TimeSpan.FromSeconds(4), out _))
        {
            return;
        }

        var screenshotPath = TryCaptureMainWindow(session);
        throw new Xunit.Sdk.XunitException(
            $"Responsive compact transition did not settle to ancestor context without manual toggle.{Environment.NewLine}" +
            $"State={DumpSelectionSnapshot(session, SessionAutomationId, ProjectAutomationId, StartAutomationId)}{Environment.NewLine}" +
            $"Project={DumpProjectSnapshot(session, ProjectAutomationId)}{Environment.NewLine}" +
            $"Automation={DumpAutomationSelectionState(session)}{Environment.NewLine}" +
            $"Focus={session.DescribeFocusedElement()}{Environment.NewLine}" +
            $"Screenshot={screenshotPath}");
    }

    [SkippableFact]
    public void ActiveSession_ExpandedToCompactThenExpanded_RestoresLeafSelectionAfterAncestorContext()
    {
        GuiTestGate.RequireEnabled();

        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();

        ResizeMainWindow(width: 1400, height: 900);

        var sessionItem = session.FindByAutomationId(SessionAutomationId, TimeSpan.FromSeconds(10));
        session.ActivateElement(sessionItem);
        Assert.True(
            session.WaitUntilVisible("ChatView.CurrentSessionNameButton", TimeSpan.FromSeconds(10)),
            "Chat header did not appear after selecting deterministic session gui-session-01.");

        DragMainWindowToCompact(widths: [1280, 1160, 1040, 960, 900, 860, 820, 800], height: 900);
        Assert.True(
            WaitForCompactSelectionContext(session, SessionAutomationId, ProjectAutomationId, StartAutomationId, TimeSpan.FromSeconds(4), out _),
            $"Expected compact resize path to settle ancestor context before restoring expanded mode. {DumpSelectionSnapshot(session, SessionAutomationId, ProjectAutomationId, StartAutomationId)}");

        Assert.True(
            WaitUntil(
                () =>
                {
                    return WaitForCompactSelectionContext(
                        session,
                        SessionAutomationId,
                        ProjectAutomationId,
                        StartAutomationId,
                        TimeSpan.FromMilliseconds(200),
                        out _);
                },
                timeout: TimeSpan.FromSeconds(2),
                pollInterval: TimeSpan.FromMilliseconds(120)),
            $"Expected compact mode to retain ancestor projection with semantic session selection. {DumpSelectionSnapshot(session, SessionAutomationId, ProjectAutomationId, StartAutomationId)}");

        ResizeMainWindow(width: 1400, height: 900);
        Assert.True(
            WaitUntil(
                () =>
                {
                    return IsElementVisible(session, SessionAutomationId)
                        && session.TryGetIsSelected(SessionAutomationId) == true
                        && session.TryFindByAutomationId("ChatView.CurrentSessionNameButton", TimeSpan.FromMilliseconds(200)) is not null;
                },
                timeout: TimeSpan.FromSeconds(4),
                pollInterval: TimeSpan.FromMilliseconds(120)),
            $"Expanded restore did not bring the leaf session selection back onscreen. {DumpSelectionSnapshot(session, SessionAutomationId, ProjectAutomationId, StartAutomationId)}");
    }

    [SkippableFact]
    public void ActiveSession_SlowResizeToCompact_KeepsAncestorContextStable()
    {
        GuiTestGate.RequireEnabled();

        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();

        ResizeMainWindow(width: 1400, height: 900);
        var sessionItem = session.FindByAutomationId(SessionAutomationId, TimeSpan.FromSeconds(10));
        session.ActivateElement(sessionItem);
        Assert.True(
            session.WaitUntilVisible("ChatView.CurrentSessionNameButton", TimeSpan.FromSeconds(10)),
            "Chat header did not appear after selecting deterministic session gui-session-01.");

        DragMainWindowToCompact(
            widths: [1280, 1220, 1160, 1100, 1040, 980, 920, 860, 820, 800],
            height: 900,
            delayMs: 260);

        Assert.True(
            WaitForCompactSelectionContext(session, SessionAutomationId, ProjectAutomationId, StartAutomationId, TimeSpan.FromSeconds(4), out _),
            $"Expected slow compact resize path to settle ancestor context. {DumpSelectionSnapshot(session, SessionAutomationId, ProjectAutomationId, StartAutomationId)}");

        var stableDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(1.2);
        while (DateTime.UtcNow < stableDeadline)
        {
            if (!WaitForCompactSelectionContext(
                    session,
                    SessionAutomationId,
                    ProjectAutomationId,
                    StartAutomationId,
                    TimeSpan.FromMilliseconds(200),
                    out _))
            {
                throw new Xunit.Sdk.XunitException(
                    $"Ancestor context regressed during slow compact stabilization. {DumpSelectionSnapshot(session, SessionAutomationId, ProjectAutomationId, StartAutomationId)}");
            }

            Thread.Sleep(110);
        }
    }

    [SkippableFact]
    public void ActiveSession_BoundaryJitterBeforeCompact_KeepsAncestorContextStable()
    {
        GuiTestGate.RequireEnabled();

        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();

        ResizeMainWindow(width: 1400, height: 900);
        var sessionItem = session.FindByAutomationId(SessionAutomationId, TimeSpan.FromSeconds(10));
        session.ActivateElement(sessionItem);
        Assert.True(
            session.WaitUntilVisible("ChatView.CurrentSessionNameButton", TimeSpan.FromSeconds(10)),
            "Chat header did not appear after selecting deterministic session gui-session-01.");

        // Simulate manual slow dragging around the Expanded/Compact threshold before settling compact.
        DragMainWindowToCompact(
            widths: [1120, 1060, 1020, 1008, 1002, 999, 1001, 998, 996, 980, 940, 900, 860, 820, 800],
            height: 900,
            delayMs: 260);

        Assert.True(
            WaitForCompactSelectionContext(session, SessionAutomationId, ProjectAutomationId, StartAutomationId, TimeSpan.FromSeconds(5), out _),
            $"Expected jittered compact path to settle ancestor context. {DumpSelectionSnapshot(session, SessionAutomationId, ProjectAutomationId, StartAutomationId)}");

        var stableDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(1.6);
        while (DateTime.UtcNow < stableDeadline)
        {
            if (!WaitForCompactSelectionContext(
                    session,
                    SessionAutomationId,
                    ProjectAutomationId,
                    StartAutomationId,
                    TimeSpan.FromMilliseconds(200),
                    out _))
            {
                throw new Xunit.Sdk.XunitException(
                    $"Ancestor context regressed during threshold-jitter compact stabilization. {DumpSelectionSnapshot(session, SessionAutomationId, ProjectAutomationId, StartAutomationId)}");
            }

            Thread.Sleep(110);
        }
    }

    private static bool WaitUntil(Func<bool> condition, TimeSpan timeout, TimeSpan pollInterval)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(pollInterval);
        }

        return condition();
    }

    private static string TryCaptureMainWindow(WindowsGuiAppSession session)
    {
        var captureRoot = Path.Combine(Path.GetTempPath(), "SalmonEgg.GuiTests");
        Directory.CreateDirectory(captureRoot);
        var screenshotPath = Path.Combine(
            captureRoot,
            $"responsive-nav-ancestor-{DateTime.UtcNow:yyyyMMddHHmmssfff}.png");

        try
        {
            session.MainWindow.CaptureToFile(screenshotPath);
            return screenshotPath;
        }
        catch (Exception ex)
        {
            return $"<capture failed: {ex.Message}>";
        }
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

        throw new InvalidOperationException("Failed to resize the SalmonEgg window.");
    }

    private static void DragMainWindowToCompact(int[] widths, int height, int delayMs = 90)
    {
        foreach (var width in widths)
        {
            ResizeMainWindow(width, height);
            Thread.Sleep(delayMs);
        }
    }

    private static bool WaitForCompactSelectionContext(
        WindowsGuiAppSession session,
        string sessionId,
        string projectId,
        string startId,
        TimeSpan timeout,
        out string? winner)
    {
        // Use the automation selection state text exposed by MainPage code-behind.
        // It reads NavigationViewItem.IsChildSelected directly from the control,
        // which is the ground truth for ancestor visual state.
        //
        // Context values:
        //   "Session"  — leaf session container is visible and selected
        //   "Ancestor" — project container is visible with IsChildSelected=true
        //   "Start"    — start item is selected (selection drifted — bug)
        //   "None"     — no selection visual at all (ancestor visual lost — bug)
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var context = TryGetAutomationContext(session);

            if (string.Equals(context, "Session", StringComparison.Ordinal))
            {
                winner = sessionId;
                return true;
            }

            if (string.Equals(context, "Ancestor", StringComparison.Ordinal))
            {
                winner = projectId;
                return true;
            }

            // "Start" and "None" are both failures — do not accept them.
            Thread.Sleep(120);
        }

        winner = null;
        return false;
    }

    private static string? TryGetAutomationContext(WindowsGuiAppSession session)
    {
        var raw = session.TryGetElementName("MainNav.Automation.SelectionState", TimeSpan.FromMilliseconds(200));
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        // Parse "Context=Ancestor;Semantic=...;..." → "Ancestor"
        foreach (var segment in raw.Split(';'))
        {
            var trimmed = segment.Trim();
            if (trimmed.StartsWith("Context=", StringComparison.Ordinal))
            {
                return trimmed.Substring("Context=".Length);
            }
        }

        return null;
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

    private static string DumpSelectionSnapshot(WindowsGuiAppSession session, string sessionId, string projectId, string startId)
    {
        var sessionSelected = TryGetVisibleIsSelected(session, sessionId);
        var projectSelected = TryGetVisibleIsSelected(session, projectId);
        var startSelected = TryGetVisibleIsSelected(session, startId);
        var sessionVisible = IsElementVisible(session, sessionId);
        var projectVisible = IsElementVisible(session, projectId);
        var startVisible = IsElementVisible(session, startId);
        var context = TryGetAutomationContext(session) ?? "<null>";
        return $"Context={context}; visible-selected snapshot => session:{sessionSelected?.ToString() ?? "null"}(visible={sessionVisible}), project:{projectSelected?.ToString() ?? "null"}(visible={projectVisible}), start:{startSelected?.ToString() ?? "null"}(visible={startVisible})";
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

    private static string DumpAutomationSelectionState(WindowsGuiAppSession session)
        => $"automation selection state => {session.TryGetElementName("MainNav.Automation.SelectionState", TimeSpan.FromMilliseconds(200)) ?? "<missing>"}";

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

    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool MoveWindow(IntPtr hWnd, int x, int y, int width, int height, bool repaint);
    }
}
