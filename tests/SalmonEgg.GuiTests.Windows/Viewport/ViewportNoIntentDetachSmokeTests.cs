using System;
using System.Threading;
using Xunit;

namespace SalmonEgg.GuiTests.Windows;

public sealed class ViewportNoIntentDetachSmokeTests
{
    [SkippableFact]
    public void RemoteSlowReplay_ViewportJitterWithoutUserIntent_DoesNotDetachFromBottom()
    {
        var previousSlowLoadDelay = Environment.GetEnvironmentVariable("SALMONEGG_GUI_SLOW_SESSION_LOAD_MS");
        Environment.SetEnvironmentVariable("SALMONEGG_GUI_SLOW_SESSION_LOAD_MS", "1500");

        try
        {
            using var appData = GuiAppDataScope.CreateDeterministicSlowRemoteReplayData(
                cachedMessageCount: 1,
                replayMessageCount: 40);
            using var session = WindowsGuiAppSession.LaunchFresh();

            var sessionItem = session.FindByAutomationId("MainNav.Session.gui-remote-conversation-01", TimeSpan.FromSeconds(15));
            session.ActivateElement(sessionItem);

            Assert.True(
                session.WaitUntilHidden("ChatView.LoadingOverlay", TimeSpan.FromSeconds(30)),
                "Remote session loading overlay did not disappear after hydration should have completed.");
            Assert.True(
                WaitForViewportState(session, "bottom", TimeSpan.FromSeconds(10)),
                $"Transcript viewport did not settle to bottom before the passive jitter assertion. State='{session.TryGetElementName("ChatView.TranscriptViewportState", TimeSpan.FromMilliseconds(200)) ?? "<missing>"}'.");

            Thread.Sleep(1200);

            var state = session.TryGetElementName("ChatView.TranscriptViewportState", TimeSpan.FromMilliseconds(200));
            Assert.NotEqual("not_bottom", state);
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
}
