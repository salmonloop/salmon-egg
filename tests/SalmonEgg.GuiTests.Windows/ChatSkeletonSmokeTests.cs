using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using FlaUI.Core.AutomationElements;
using Xunit;

namespace SalmonEgg.GuiTests.Windows;

public sealed class ChatSkeletonSmokeTests
{
    [SkippableFact]
    public void SelectSessionWithContent_ShowsSkeletonLoader_ThenContent()
    {
        // Use withContent: true to ensure there are messages to be rendered,
        // which triggers the "render hold" logic in ChatView.xaml.cs.
        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData(sessionCount: 1, withContent: true);
        using var session = WindowsGuiAppSession.LaunchFresh();

        var sessionItem = session.FindByAutomationId("MainNav.Session.gui-session-01");
        session.ActivateElement(sessionItem);

        var loadingOverlay = WaitForLoadingOverlay(session, "select-session-with-content");

        // Wait for it to disappear (content rendered)
        var isHidden = session.WaitUntilHidden("ChatView.LoadingOverlay", TimeSpan.FromSeconds(10));
        Assert.True(isHidden, "Loading overlay (skeleton) did not disappear after content should have loaded.");

        // Verify content is now visible
        var chatHeader = session.FindByAutomationId("ChatView.CurrentSessionNameButton");
        Assert.NotNull(chatHeader);
        Assert.Contains("GUI Session 01", chatHeader.Name, StringComparison.Ordinal);
    }

    [SkippableFact]
    public void SelectSessionWithLongTranscript_AutoScrollsToLatestMessageAfterLoad()
    {
        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData(
            sessionCount: 1,
            withContent: true,
            messageCountPerSession: 60);
        using var session = WindowsGuiAppSession.LaunchFresh();

        var sessionItem = session.FindByAutomationId("MainNav.Session.gui-session-01");
        session.ActivateElement(sessionItem);

        if (session.TryFindByAutomationId("ChatView.LoadingOverlay", TimeSpan.FromSeconds(2)) is not null)
        {
            var isHidden = session.WaitUntilHidden("ChatView.LoadingOverlay", TimeSpan.FromSeconds(10));
            Assert.True(isHidden, "Loading overlay did not disappear after the long transcript should have loaded.");
        }

        var messagesList = session.FindByAutomationId("ChatView.MessagesList", TimeSpan.FromSeconds(10));
        var lastMessageText = "GUI Session 01 message 060";

        var lastMessageVisible = session.FindVisibleText(
            lastMessageText,
            messagesList,
            TimeSpan.FromSeconds(4));

        Assert.NotNull(lastMessageVisible);
    }

    [SkippableFact]
    public void SelectRemoteSessionWithSlowReplay_AutoScrollsToLatestMessageAfterHydration()
    {
        var previousSlowLoadDelay = Environment.GetEnvironmentVariable("SALMONEGG_GUI_SLOW_SESSION_LOAD_MS");
        Environment.SetEnvironmentVariable("SALMONEGG_GUI_SLOW_SESSION_LOAD_MS", "1500");

        try
        {
            using var appData = GuiAppDataScope.CreateDeterministicSlowRemoteReplayData(
                cachedMessageCount: 1,
                replayMessageCount: 60);
            using var session = WindowsGuiAppSession.LaunchFresh();

            var sessionItem = session.FindByAutomationId("MainNav.Session.gui-remote-conversation-01", TimeSpan.FromSeconds(15));
            session.ActivateElement(sessionItem);

            var sawOverlayStatus = session.WaitUntilVisible("ChatView.LoadingOverlayStatus", TimeSpan.FromSeconds(10));
            Assert.True(sawOverlayStatus, "Slow remote replay did not expose ChatView.LoadingOverlayStatus.");

            var overlayHidden = session.WaitUntilHidden("ChatView.LoadingOverlay", TimeSpan.FromSeconds(30));
            Assert.True(overlayHidden, "Slow remote replay overlay did not disappear after the transcript should have hydrated.");

            var messagesList = session.FindByAutomationId("ChatView.MessagesList", TimeSpan.FromSeconds(10));
            var lastMessageVisible = session.TryFindVisibleText(
                "GUI Remote Session 01 replay 060",
                messagesList,
                TimeSpan.FromSeconds(8));

            if (lastMessageVisible is null)
            {
                var captureRoot = Path.Combine(Path.GetTempPath(), "SalmonEgg.GuiTests");
                Directory.CreateDirectory(captureRoot);
                var screenshotPath = Path.Combine(
                    captureRoot,
                    $"slow-remote-replay-scroll-{DateTime.UtcNow:yyyyMMddHHmmssfff}.png");
                session.MainWindow.CaptureToFile(screenshotPath);

                var visibleTexts = session.GetVisibleTexts(messagesList);
                var bootLogTail = appData.ReadBootLogTail();
                throw new Xunit.Sdk.XunitException(
                    $"Latest replay message was not visible after slow remote hydration. Screenshot: {screenshotPath}{Environment.NewLine}Visible texts: [{string.Join(", ", visibleTexts)}]{Environment.NewLine}boot.log:{Environment.NewLine}{bootLogTail}");
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("SALMONEGG_GUI_SLOW_SESSION_LOAD_MS", previousSlowLoadDelay);
        }
    }

    private static AutomationElement WaitForLoadingOverlay(WindowsGuiAppSession session, string scenario)
    {
        var timeline = new List<string>();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);

        while (DateTime.UtcNow < deadline)
        {
            var loadingOverlay = session.TryFindByAutomationId("ChatView.LoadingOverlay", TimeSpan.FromMilliseconds(100));
            if (loadingOverlay is not null)
            {
                return loadingOverlay;
            }

            var headerVisible = session.TryFindByAutomationId("ChatView.CurrentSessionNameButton", TimeSpan.FromMilliseconds(100)) is not null;
            var messagesVisible = session.TryFindByAutomationId("ChatView.MessagesList", TimeSpan.FromMilliseconds(100)) is not null;
            var interestingIds = session.MainWindow
                .FindAllDescendants()
                .Select(TryGetAutomationId)
                .Where(automationId =>
                    !string.IsNullOrWhiteSpace(automationId) &&
                    (automationId.StartsWith("ChatView.", StringComparison.Ordinal)
                     || automationId.StartsWith("MainNav.", StringComparison.Ordinal)))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(automationId => automationId, StringComparer.Ordinal);

            timeline.Add(
                $"{DateTime.UtcNow:HH:mm:ss.fff} header={headerVisible} messages={messagesVisible} ids=[{string.Join(", ", interestingIds)}]");

            Thread.Sleep(150);
        }

        var captureRoot = Path.Combine(Path.GetTempPath(), "SalmonEgg.GuiTests");
        Directory.CreateDirectory(captureRoot);
        var screenshotPath = Path.Combine(
            captureRoot,
            $"chat-skeleton-{scenario}-{DateTime.UtcNow:yyyyMMddHHmmssfff}.png");
        session.MainWindow.CaptureToFile(screenshotPath);

        throw new Xunit.Sdk.XunitException(
            $"Loading overlay was not found for scenario '{scenario}'. Screenshot: {screenshotPath}{Environment.NewLine}{string.Join(Environment.NewLine, timeline)}");
    }

    private static string? TryGetAutomationId(AutomationElement element)
    {
        try
        {
            return element.AutomationId;
        }
        catch
        {
            return null;
        }
    }
}
