using System;
using System.Linq;
using System.Threading;
using Xunit;

namespace SalmonEgg.GuiTests.Windows;

public sealed class ViewportProjectionOwnedRestoreSmokeTests
{
    [SkippableFact]
    public void RemoteSession_WheelOnVisibleMessageContent_DetachesViewportFromBottom()
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
                "Remote session loading overlay did not disappear after hydration should have completed.");
            Assert.True(
                WaitForViewportState(session, "bottom", TimeSpan.FromSeconds(10)),
                $"Transcript viewport did not settle to bottom before wheel detach. State='{session.TryGetElementName("ChatView.TranscriptViewportState", TimeSpan.FromMilliseconds(200)) ?? "<missing>"}'.");

            var messagesList = session.FindByAutomationId("ChatView.MessagesList", TimeSpan.FromSeconds(5));
            var visibleContent = session.TryFindVisibleText("GUI Remote Session 01 replay 024", messagesList, TimeSpan.FromMilliseconds(400))
                ?? session.TryFindVisibleText("GUI Remote Session 01 replay 022", messagesList, TimeSpan.FromMilliseconds(400))
                ?? session.TryFindVisibleText("GUI Remote Session 01 replay 020", messagesList, TimeSpan.FromMilliseconds(400));

            Assert.NotNull(visibleContent);

            session.BringMainWindowToFront();
            session.ClickElement(visibleContent!);

            for (var attempt = 0; attempt < 6; attempt++)
            {
                Thread.Sleep(100);
                session.ScrollWheel(120);
                Thread.Sleep(180);
                if (string.Equals(session.TryGetElementName("ChatView.TranscriptViewportState", TimeSpan.FromMilliseconds(200)), "not_bottom", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
            }

            Assert.True(
                WaitForViewportState(session, "not_bottom", TimeSpan.FromSeconds(3)),
                $"Transcript viewport stayed locked to bottom after wheel input on visible message content. State='{session.TryGetElementName("ChatView.TranscriptViewportState", TimeSpan.FromMilliseconds(200)) ?? "<missing>"}'. Debug='{session.TryGetElementName("ChatView.TranscriptViewportDebug", TimeSpan.FromMilliseconds(200)) ?? "<missing>"}'.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("SALMONEGG_GUI_SLOW_SESSION_LOAD_MS", previousSlowLoadDelay);
        }
    }

    [SkippableFact]
    public void RemoteSession_DetachedWarmReturn_RestoresFirstVisibleReplayAnchorWithoutBottomReclaim()
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
            DetachViewport(session, messagesList);

            Assert.True(
                WaitForViewportState(session, "not_bottom", TimeSpan.FromSeconds(3)),
                $"Transcript viewport stayed locked to bottom after user detach input. State='{session.TryGetElementName("ChatView.TranscriptViewportState", TimeSpan.FromMilliseconds(200)) ?? "<missing>"}'. Debug='{session.TryGetElementName("ChatView.TranscriptViewportDebug", TimeSpan.FromMilliseconds(200)) ?? "<missing>"}'.");

            var visibleReplayTextsBeforeSwitch = session.GetVisibleTexts(messagesList)
                .Where(static text => text.StartsWith("GUI Remote Session 01 replay ", StringComparison.Ordinal))
                .ToArray();
            var firstVisibleReplayBeforeSwitch = visibleReplayTextsBeforeSwitch.FirstOrDefault();
            Assert.False(string.IsNullOrWhiteSpace(firstVisibleReplayBeforeSwitch), "Detached viewport did not expose a replay anchor before switching sessions.");

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
            Assert.True(
                WaitForViewportState(session, "not_bottom", TimeSpan.FromSeconds(5)),
                $"Returning to detached remote session A reclaimed bottom instead of preserving not_bottom. State='{session.TryGetElementName("ChatView.TranscriptViewportState", TimeSpan.FromMilliseconds(200)) ?? "<missing>"}'. Debug='{session.TryGetElementName("ChatView.TranscriptViewportDebug", TimeSpan.FromMilliseconds(200)) ?? "<missing>"}'.");

            var messagesListAfterReturn = session.FindByAutomationId("ChatView.MessagesList", TimeSpan.FromSeconds(5));
            var visibleReplayTextsAfterReturn = session.GetVisibleTexts(messagesListAfterReturn)
                .Where(static text => text.StartsWith("GUI Remote Session 01 replay ", StringComparison.Ordinal))
                .ToArray();
            var firstVisibleReplayAfterReturn = visibleReplayTextsAfterReturn.FirstOrDefault();

            Assert.Equal(
                firstVisibleReplayBeforeSwitch,
                firstVisibleReplayAfterReturn);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SALMONEGG_GUI_SLOW_SESSION_LOAD_MS", previousSlowLoadDelay);
        }
    }

    private static void DetachViewport(
        WindowsGuiAppSession session,
        FlaUI.Core.AutomationElements.AutomationElement messagesList)
    {
        var focusAnchor = session.TryFindVisibleText("GUI Remote Session 01 replay 024", messagesList, TimeSpan.FromMilliseconds(400))
            ?? session.TryFindVisibleText("GUI Remote Session 01 replay 022", messagesList, TimeSpan.FromMilliseconds(400))
            ?? session.TryFindVisibleText("GUI Remote Session 01 replay 020", messagesList, TimeSpan.FromMilliseconds(400));

        session.BringMainWindowToFront();
        for (var attempt = 0; attempt < 6; attempt++)
        {
            session.FocusElement(messagesList);
            Thread.Sleep(150);
            if (session.IsFocusWithinAutomationId("ChatView.MessagesList"))
            {
                break;
            }

            session.ClickElement(focusAnchor ?? messagesList);
            Thread.Sleep(150);
            if (session.IsFocusWithinAutomationId("ChatView.MessagesList"))
            {
                break;
            }
        }

        for (var attempt = 0; attempt < 6; attempt++)
        {
            session.PressPageUp();
            Thread.Sleep(150);
            if (string.Equals(session.TryGetElementName("ChatView.TranscriptViewportState", TimeSpan.FromMilliseconds(200)), "not_bottom", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        for (var attempt = 0; attempt < 4; attempt++)
        {
            session.ClickElement(messagesList);
            Thread.Sleep(100);
            session.ScrollWheel(120);
            Thread.Sleep(150);
            if (string.Equals(session.TryGetElementName("ChatView.TranscriptViewportState", TimeSpan.FromMilliseconds(200)), "not_bottom", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        if (!messagesList.Patterns.Scroll.IsSupported)
        {
            return;
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            messagesList.Patterns.Scroll.Pattern.Scroll(
                FlaUI.Core.Definitions.ScrollAmount.NoAmount,
                FlaUI.Core.Definitions.ScrollAmount.LargeDecrement);
            Thread.Sleep(180);
            if (string.Equals(session.TryGetElementName("ChatView.TranscriptViewportState", TimeSpan.FromMilliseconds(200)), "not_bottom", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
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
}
