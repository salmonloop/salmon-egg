using System;
using System.Linq;
using System.Threading;
using Xunit;

namespace SalmonEgg.GuiTests.Windows;

public sealed class ViewportDetachedWarmReturnSmokeTests
{
    [SkippableFact]
    public void RemoteSession_DetachedViewport_SwitchAwayAndBack_DoesNotReclaimBottom()
    {
        var previousSlowLoadDelay = Environment.GetEnvironmentVariable("SALMONEGG_GUI_SLOW_SESSION_LOAD_MS");
        Environment.SetEnvironmentVariable("SALMONEGG_GUI_SLOW_SESSION_LOAD_MS", "1500");

        try
        {
            using var appData = GuiAppDataScope.CreateDeterministicSlowRemoteReplayData(
                cachedMessageCount: 1,
                replayMessageCount: 24,
                remoteConversationCount: 2);
            using var session = WindowsGuiAppSession.LaunchFresh();

            var firstSession = session.FindByAutomationId("MainNav.Session.gui-remote-conversation-01", TimeSpan.FromSeconds(15));
            session.ActivateElement(firstSession);

            Assert.True(
                session.WaitUntilHidden("ChatView.LoadingOverlay", TimeSpan.FromSeconds(30)),
                "Remote session A loading overlay did not disappear after hydration should have completed.");
            Assert.True(
                WaitForViewportState(session, "bottom", TimeSpan.FromSeconds(10)),
                $"Transcript viewport did not settle to bottom before manual detach. State='{session.TryGetElementName("ChatView.TranscriptViewportState", TimeSpan.FromMilliseconds(200)) ?? "<missing>"}'.");

            var messagesList = session.FindByAutomationId("ChatView.MessagesList", TimeSpan.FromSeconds(5));
            var focusAnchor = session.TryFindVisibleText("GUI Remote Session 01 replay 024", messagesList, TimeSpan.FromMilliseconds(400))
                ?? session.TryFindVisibleText("GUI Remote Session 01 replay 022", messagesList, TimeSpan.FromMilliseconds(400))
                ?? session.TryFindVisibleText("GUI Remote Session 01 replay 020", messagesList, TimeSpan.FromMilliseconds(400));
            session.BringMainWindowToFront();
            var focusPrimed = false;
            for (var attempt = 0; attempt < 6; attempt++)
            {
                session.FocusElement(messagesList);
                Thread.Sleep(150);
                if (session.IsFocusWithinAutomationId("ChatView.MessagesList"))
                {
                    focusPrimed = true;
                    break;
                }

                session.ClickElement(focusAnchor ?? messagesList);
                Thread.Sleep(150);
                if (session.IsFocusWithinAutomationId("ChatView.MessagesList"))
                {
                    focusPrimed = true;
                    break;
                }
            }

            for (var attempt = 0; focusPrimed && attempt < 6; attempt++)
            {
                session.PressPageUp();
                Thread.Sleep(150);
                if (string.Equals(session.TryGetElementName("ChatView.TranscriptViewportState", TimeSpan.FromMilliseconds(200)), "not_bottom", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
            }

            if (!string.Equals(session.TryGetElementName("ChatView.TranscriptViewportState", TimeSpan.FromMilliseconds(200)), "not_bottom", StringComparison.OrdinalIgnoreCase))
            {
                for (var attempt = 0; attempt < 4; attempt++)
                {
                    session.ClickElement(messagesList);
                    Thread.Sleep(100);
                    session.ScrollWheel(120);
                    Thread.Sleep(150);
                    if (string.Equals(session.TryGetElementName("ChatView.TranscriptViewportState", TimeSpan.FromMilliseconds(200)), "not_bottom", StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }
                }
            }

            if (!string.Equals(session.TryGetElementName("ChatView.TranscriptViewportState", TimeSpan.FromMilliseconds(200)), "not_bottom", StringComparison.OrdinalIgnoreCase)
                && messagesList.Patterns.Scroll.IsSupported)
            {
                for (var attempt = 0; attempt < 3; attempt++)
                {
                    messagesList.Patterns.Scroll.Pattern.Scroll(FlaUI.Core.Definitions.ScrollAmount.NoAmount, FlaUI.Core.Definitions.ScrollAmount.LargeDecrement);
                    Thread.Sleep(180);
                    if (string.Equals(session.TryGetElementName("ChatView.TranscriptViewportState", TimeSpan.FromMilliseconds(200)), "not_bottom", StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }
                }
            }

            Assert.True(
                WaitForViewportState(session, "not_bottom", TimeSpan.FromSeconds(3)),
                $"Transcript viewport stayed locked to bottom after user detach input. focusPrimed={focusPrimed}. State='{session.TryGetElementName("ChatView.TranscriptViewportState", TimeSpan.FromMilliseconds(200)) ?? "<missing>"}'. Debug='{session.TryGetElementName("ChatView.TranscriptViewportDebug", TimeSpan.FromMilliseconds(200)) ?? "<missing>"}'.");
            var replay018VisibleBeforeSwitch = session.TryFindVisibleText("GUI Remote Session 01 replay 018", messagesList, TimeSpan.FromMilliseconds(400)) is not null;
            var replay020VisibleBeforeSwitch = session.TryFindVisibleText("GUI Remote Session 01 replay 020", messagesList, TimeSpan.FromMilliseconds(400)) is not null;
            var replay022VisibleBeforeSwitch = session.TryFindVisibleText("GUI Remote Session 01 replay 022", messagesList, TimeSpan.FromMilliseconds(400)) is not null;
            var visibleReplayTextsBeforeSwitch = session.GetVisibleTexts(messagesList)
                .Where(static text => text.StartsWith("GUI Remote Session 01 replay ", StringComparison.Ordinal))
                .ToArray();
            var debugBeforeSwitch = session.TryGetElementName("ChatView.TranscriptViewportDebug", TimeSpan.FromMilliseconds(200)) ?? "<missing>";

            var secondSession = session.FindByAutomationId("MainNav.Session.gui-remote-conversation-02", TimeSpan.FromSeconds(10));
            session.ActivateElement(secondSession);
            Assert.True(
                session.WaitUntilHidden("ChatView.LoadingOverlay", TimeSpan.FromSeconds(30)),
                "Remote session B loading overlay did not disappear after hydration should have completed.");

            firstSession = session.FindByAutomationId("MainNav.Session.gui-remote-conversation-01", TimeSpan.FromSeconds(10));
            session.ActivateElement(firstSession);

            Assert.True(
                session.WaitUntilVisible("ChatView.CurrentSessionNameButton", TimeSpan.FromSeconds(4)),
                "Returning to detached remote session A did not restore the chat header quickly.");
            var messagesListAfterReturn = session.FindByAutomationId("ChatView.MessagesList", TimeSpan.FromSeconds(5));
            var returnTimeline = new System.Collections.Generic.List<string>();
            var returnDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (DateTime.UtcNow < returnDeadline)
            {
                var state = session.TryGetElementName("ChatView.TranscriptViewportState", TimeSpan.FromMilliseconds(100)) ?? "<missing>";
                var debug = session.TryGetElementName("ChatView.TranscriptViewportDebug", TimeSpan.FromMilliseconds(100)) ?? "<missing>";
                returnTimeline.Add($"{DateTime.UtcNow:HH:mm:ss.fff} state={state} debug={debug}");
                if (string.Equals(state, "not_bottom", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                Thread.Sleep(150);
            }
            Assert.True(
                WaitForViewportState(session, "not_bottom", TimeSpan.FromSeconds(5)),
                $"Returning to detached remote session A reclaimed bottom instead of preserving not_bottom. State='{session.TryGetElementName("ChatView.TranscriptViewportState", TimeSpan.FromMilliseconds(200)) ?? "<missing>"}'. DebugBeforeSwitch='{debugBeforeSwitch}'. DebugAfterReturn='{session.TryGetElementName("ChatView.TranscriptViewportDebug", TimeSpan.FromMilliseconds(200)) ?? "<missing>"}'. replay018VisibleBeforeSwitch={replay018VisibleBeforeSwitch}. replay020VisibleBeforeSwitch={replay020VisibleBeforeSwitch}. replay022VisibleBeforeSwitch={replay022VisibleBeforeSwitch}. replay018VisibleAfterReturn={session.TryFindVisibleText("GUI Remote Session 01 replay 018", messagesListAfterReturn, TimeSpan.FromMilliseconds(400)) is not null}. replay020VisibleAfterReturn={session.TryFindVisibleText("GUI Remote Session 01 replay 020", messagesListAfterReturn, TimeSpan.FromMilliseconds(400)) is not null}. replay022VisibleAfterReturn={session.TryFindVisibleText("GUI Remote Session 01 replay 022", messagesListAfterReturn, TimeSpan.FromMilliseconds(400)) is not null}. Timeline:{Environment.NewLine}{string.Join(Environment.NewLine, returnTimeline)}");
            var readingRegionCandidates = visibleReplayTextsBeforeSwitch
                .Take(3)
                .ToArray();
            var readingRegionRestored = readingRegionCandidates.Length > 0
                && WaitForAnyVisibleText(
                    session,
                    messagesListAfterReturn,
                    TimeSpan.FromSeconds(3),
                    readingRegionCandidates);
            var visibleReplayTextsAfterReturn = session.GetVisibleTexts(messagesListAfterReturn)
                .Where(static text => text.StartsWith("GUI Remote Session 01 replay ", StringComparison.Ordinal))
                .ToArray();
            Assert.True(
                readingRegionRestored,
                $"Detached warm return preserved the detached state probe but did not restore the prior reading region. DebugAfterReturn='{session.TryGetElementName("ChatView.TranscriptViewportDebug", TimeSpan.FromMilliseconds(200)) ?? "<missing>"}'. VisibleReplayTextsBeforeSwitch='{string.Join(" | ", visibleReplayTextsBeforeSwitch)}'. VisibleReplayTextsAfterReturn='{string.Join(" | ", visibleReplayTextsAfterReturn)}'. VisibleTextsAfterReturn='{string.Join(" | ", session.GetVisibleTexts(messagesListAfterReturn))}'. Timeline:{Environment.NewLine}{string.Join(Environment.NewLine, returnTimeline)}");
            Assert.True(
                visibleReplayTextsBeforeSwitch.Length > 0
                && visibleReplayTextsAfterReturn.Length > 0
                && string.Equals(visibleReplayTextsBeforeSwitch[0], visibleReplayTextsAfterReturn[0], StringComparison.Ordinal),
                $"Detached warm return changed the first visible replay anchor. before='{(visibleReplayTextsBeforeSwitch.FirstOrDefault() ?? "<missing>")}'. after='{(visibleReplayTextsAfterReturn.FirstOrDefault() ?? "<missing>")}'. DebugAfterReturn='{session.TryGetElementName("ChatView.TranscriptViewportDebug", TimeSpan.FromMilliseconds(200)) ?? "<missing>"}'.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("SALMONEGG_GUI_SLOW_SESSION_LOAD_MS", previousSlowLoadDelay);
        }
    }

    private static bool WaitForViewportState(
        WindowsGuiAppSession session,
        string expectedState,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var actual = session.TryGetElementName("ChatView.TranscriptViewportState", TimeSpan.FromMilliseconds(200));
            if (string.Equals(actual, expectedState, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            Thread.Sleep(150);
        }

        return false;
    }

    private static bool WaitForAnyVisibleText(
        WindowsGuiAppSession session,
        FlaUI.Core.AutomationElements.AutomationElement scope,
        TimeSpan timeout,
        params string[] candidates)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            foreach (var candidate in candidates)
            {
                if (session.TryFindVisibleText(candidate, scope, TimeSpan.FromMilliseconds(150)) is not null)
                {
                    return true;
                }
            }

            Thread.Sleep(150);
        }

        return false;
    }
}
