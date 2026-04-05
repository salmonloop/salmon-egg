using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using FlaUI.Core.Definitions;
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
            var protocolStageStatus = WaitForOverlayStatus(
                session,
                appData,
                IsUserFriendlyLoadingStatus,
                TimeSpan.FromSeconds(20),
                "slow-remote-replay-protocol-stage-pill");
            Assert.True(IsUserFriendlyLoadingStatus(protocolStageStatus));

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
                string screenshotDescriptor;
                try
                {
                    session.MainWindow.CaptureToFile(screenshotPath);
                    screenshotDescriptor = screenshotPath;
                }
                catch (Exception ex) when (ex is COMException or Win32Exception or InvalidOperationException)
                {
                    screenshotDescriptor = $"<capture failed: {ex.Message}>";
                }

                var visibleTexts = session.GetVisibleTexts(messagesList);
                var bootLogTail = appData.ReadBootLogTail();
                throw new Xunit.Sdk.XunitException(
                    $"Latest replay message was not visible after slow remote hydration. Screenshot: {screenshotDescriptor}{Environment.NewLine}Visible texts: [{string.Join(", ", visibleTexts)}]{Environment.NewLine}boot.log:{Environment.NewLine}{bootLogTail}");
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("SALMONEGG_GUI_SLOW_SESSION_LOAD_MS", previousSlowLoadDelay);
        }
    }

    [SkippableFact]
    public void SelectRemoteSessionFromStart_ShowsLoadingOverlayBeforeChatShell()
    {
        var previousSlowLoadDelay = Environment.GetEnvironmentVariable("SALMONEGG_GUI_SLOW_SESSION_LOAD_MS");
        Environment.SetEnvironmentVariable("SALMONEGG_GUI_SLOW_SESSION_LOAD_MS", "1800");

        try
        {
            using var appData = GuiAppDataScope.CreateDeterministicSlowRemoteReplayData(
                cachedMessageCount: 1,
                replayMessageCount: 24);
            using var session = WindowsGuiAppSession.LaunchFresh();

            var sessionItem = session.FindByAutomationId("MainNav.Session.gui-remote-conversation-01", TimeSpan.FromSeconds(15));
            session.ActivateElement(sessionItem);

            WaitForLoadingOverlayBeforeChatShell(
                session,
                appData,
                expectedHeaderText: "GUI Remote Session 01",
                scenario: "start-to-remote-overlay-before-shell");

            var overlayHidden = session.WaitUntilHidden("ChatView.LoadingOverlay", TimeSpan.FromSeconds(30));
            Assert.True(overlayHidden, "Remote session loading overlay did not disappear after hydration should have completed.");

            var messagesList = session.FindByAutomationId("ChatView.MessagesList", TimeSpan.FromSeconds(10));
            var lastMessageVisible = session.TryFindVisibleText(
                "GUI Remote Session 01 replay 024",
                messagesList,
                TimeSpan.FromSeconds(8));

            if (lastMessageVisible is null)
            {
                ThrowWithScreenshot(
                    session,
                    appData,
                    "start-to-remote-overlay-before-shell-scroll",
                    $"Latest replay message was not visible after start-to-remote hydration. Visible texts: [{string.Join(", ", session.GetVisibleTexts(messagesList))}]");
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("SALMONEGG_GUI_SLOW_SESSION_LOAD_MS", previousSlowLoadDelay);
        }
    }

    [SkippableFact]
    public void SelectRemoteSession_RepeatedClicksWithLocalDetour_DoesNotHangAndHydratesLatestSelection()
    {
        var previousSlowLoadDelay = Environment.GetEnvironmentVariable("SALMONEGG_GUI_SLOW_SESSION_LOAD_MS");
        Environment.SetEnvironmentVariable("SALMONEGG_GUI_SLOW_SESSION_LOAD_MS", "2000");

        try
        {
            using var appData = GuiAppDataScope.CreateDeterministicSlowRemoteReplayData(
                cachedMessageCount: 1,
                replayMessageCount: 24,
                includeLocalConversation: true,
                localMessageCount: 4);
            using var session = WindowsGuiAppSession.LaunchFresh();

            var remoteItem = session.FindByAutomationId("MainNav.Session.gui-remote-conversation-01", TimeSpan.FromSeconds(15));
            var localItem = session.FindByAutomationId("MainNav.Session.gui-local-conversation-01", TimeSpan.FromSeconds(15));

            session.ActivateElement(remoteItem);

            var sawInitialRemoteStatus = session.WaitUntilVisible("ChatView.LoadingOverlayStatus", TimeSpan.FromSeconds(10));
            Assert.True(sawInitialRemoteStatus, "Initial remote selection did not expose ChatView.LoadingOverlayStatus.");

            session.ActivateElement(remoteItem);
            session.ActivateElement(localItem);

            var localHeader = WaitForSessionHeader(
                session,
                expectedTitle: "GUI Local Session 01",
                scenario: "repeated-remote-clicks-local-detour-local",
                appData);
            Assert.Contains("GUI Local Session 01", localHeader.Name, StringComparison.Ordinal);

            session.ActivateElement(remoteItem);
            session.ActivateElement(remoteItem);

            var sawFinalRemoteStatus = session.WaitUntilVisible("ChatView.LoadingOverlayStatus", TimeSpan.FromSeconds(10));
            Assert.True(sawFinalRemoteStatus, "Final remote reselection did not expose ChatView.LoadingOverlayStatus.");

            var overlayHidden = session.WaitUntilHidden("ChatView.LoadingOverlay", TimeSpan.FromSeconds(40));
            if (!overlayHidden)
            {
                ThrowWithScreenshot(
                    session,
                    appData,
                    "repeated-remote-clicks-local-detour-overlay-stuck",
                    $"Repeated remote reselection stayed stuck behind the loading overlay. Visible texts: [{string.Join(", ", session.GetVisibleTexts())}]");
            }

            var remoteHeader = WaitForSessionHeader(
                session,
                expectedTitle: "GUI Remote Session 01",
                scenario: "repeated-remote-clicks-local-detour-remote",
                appData);
            Assert.Contains("GUI Remote Session 01", remoteHeader.Name, StringComparison.Ordinal);

            var messagesList = session.FindByAutomationId("ChatView.MessagesList", TimeSpan.FromSeconds(10));
            var lastMessageVisible = session.TryFindVisibleText(
                "GUI Remote Session 01 replay 024",
                messagesList,
                TimeSpan.FromSeconds(8));

            if (lastMessageVisible is null)
            {
                ThrowWithScreenshot(
                    session,
                    appData,
                    "repeated-remote-clicks-local-detour-scroll",
                    $"Latest remote replay message was not visible after repeated remote clicks with a local detour. Visible texts: [{string.Join(", ", session.GetVisibleTexts(messagesList))}]");
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("SALMONEGG_GUI_SLOW_SESSION_LOAD_MS", previousSlowLoadDelay);
        }
    }

    [SkippableFact]
    public void RandomSwitchWithOneSecondCadence_FinalSelectionAlwaysDrivesRightPane()
    {
        var previousSlowLoadDelay = Environment.GetEnvironmentVariable("SALMONEGG_GUI_SLOW_SESSION_LOAD_MS");
        Environment.SetEnvironmentVariable("SALMONEGG_GUI_SLOW_SESSION_LOAD_MS", "2200");

        try
        {
            using var appData = GuiAppDataScope.CreateDeterministicSlowRemoteReplayData(
                cachedMessageCount: 1,
                replayMessageCount: 24,
                includeLocalConversation: true,
                localMessageCount: 4);
            using var session = WindowsGuiAppSession.LaunchFresh();

            const string remoteId = "MainNav.Session.gui-remote-conversation-01";
            const string localId = "MainNav.Session.gui-local-conversation-01";
            const string startId = "MainNav.Start";

            session.FindByAutomationId(remoteId, TimeSpan.FromSeconds(15));
            session.FindByAutomationId(localId, TimeSpan.FromSeconds(15));
            session.FindByAutomationId(startId, TimeSpan.FromSeconds(15));

            var random = new Random(20260402);
            var targets = new[] { remoteId, localId, startId };
            for (var index = 0; index < 12; index++)
            {
                var target = targets[random.Next(targets.Length)];
                ActivateNavItem(session, appData, target, $"one-second-random-switch-step-{index:00}");
                Thread.Sleep(1000);
            }

            ActivateNavItem(session, appData, startId, "one-second-random-switch-final-start");
            Thread.Sleep(600);
            var startSelected = session.TryGetIsSelected(startId) == true;
            var startVisible = session.TryFindByAutomationId("StartView.Title", TimeSpan.FromSeconds(4)) is not null;
            if (!startSelected || !startVisible)
            {
                ThrowWithScreenshot(
                    session,
                    appData,
                    "one-second-random-switch-start-not-visible",
                    $"Start view did not become interactive after 1s cadence random switching. startSelected={startSelected} startVisible={startVisible}");
            }

            ActivateNavItem(session, appData, remoteId, "one-second-random-switch-final-remote-1");
            ActivateNavItem(session, appData, remoteId, "one-second-random-switch-final-remote-2");

            var remoteHeader = WaitForSessionHeader(
                session,
                expectedTitle: "GUI Remote Session 01",
                scenario: "one-second-random-switch-remote-header",
                appData);
            Assert.Contains("GUI Remote Session 01", remoteHeader.Name, StringComparison.Ordinal);

            var overlayHidden = session.WaitUntilHidden("ChatView.LoadingOverlay", TimeSpan.FromSeconds(40));
            if (!overlayHidden)
            {
                ThrowWithScreenshot(
                    session,
                    appData,
                    "one-second-random-switch-overlay-stuck",
                    "Loading overlay stayed visible after final remote selection in 1s cadence random switching.");
            }

            var messagesList = session.FindByAutomationId("ChatView.MessagesList", TimeSpan.FromSeconds(10));
            var latestRemoteVisible = session.TryFindVisibleText(
                "GUI Remote Session 01 replay 024",
                messagesList,
                TimeSpan.FromSeconds(8));
            if (latestRemoteVisible is null)
            {
                ThrowWithScreenshot(
                    session,
                    appData,
                    "one-second-random-switch-remote-scroll",
                    $"Remote latest replay message was not visible after final remote selection. Visible texts: [{string.Join(", ", session.GetVisibleTexts(messagesList))}]");
            }

            ActivateNavItem(session, appData, localId, "one-second-random-switch-final-local-1");
            ActivateNavItem(session, appData, localId, "one-second-random-switch-final-local-2");

            var localHeader = WaitForSessionHeader(
                session,
                expectedTitle: "GUI Local Session 01",
                scenario: "one-second-random-switch-local-header",
                appData);
            Assert.Contains("GUI Local Session 01", localHeader.Name, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SALMONEGG_GUI_SLOW_SESSION_LOAD_MS", previousSlowLoadDelay);
        }
    }

    [SkippableFact]
    public void SelectRemoteSession_ClickStormWithLocalInterruption_DoesNotFreezeAndHydratesFinalSelection()
    {
        var previousSlowLoadDelay = Environment.GetEnvironmentVariable("SALMONEGG_GUI_SLOW_SESSION_LOAD_MS");
        Environment.SetEnvironmentVariable("SALMONEGG_GUI_SLOW_SESSION_LOAD_MS", "2600");

        try
        {
            using var appData = GuiAppDataScope.CreateDeterministicSlowRemoteReplayData(
                cachedMessageCount: 1,
                replayMessageCount: 80,
                includeLocalConversation: true,
                localMessageCount: 6);
            using var session = WindowsGuiAppSession.LaunchFresh();

            var remoteItem = session.FindByAutomationId("MainNav.Session.gui-remote-conversation-01", TimeSpan.FromSeconds(15));
            var localItem = session.FindByAutomationId("MainNav.Session.gui-local-conversation-01", TimeSpan.FromSeconds(15));

            session.ActivateElement(remoteItem);
            Assert.True(
                session.WaitUntilVisible("ChatView.LoadingOverlayStatus", TimeSpan.FromSeconds(10)),
                "Initial remote selection did not expose ChatView.LoadingOverlayStatus before click storm.");

            for (var index = 0; index < 48; index++)
            {
                if (index % 5 == 0)
                {
                    session.ActivateElement(localItem);
                    session.ActivateElement(remoteItem);
                }
                else if (index % 2 == 0)
                {
                    session.ActivateElement(remoteItem);
                }
                else
                {
                    session.ActivateElement(localItem);
                }

                Thread.Sleep(30);
            }

            // Final intent: settle on remote conversation after the click storm.
            session.ActivateElement(remoteItem);
            session.ActivateElement(remoteItem);

            var remoteHeader = WaitForSessionHeader(
                session,
                expectedTitle: "GUI Remote Session 01",
                scenario: "click-storm-local-interruption-remote",
                appData);
            Assert.Contains("GUI Remote Session 01", remoteHeader.Name, StringComparison.Ordinal);

            var overlayHidden = session.WaitUntilHidden("ChatView.LoadingOverlay", TimeSpan.FromSeconds(50));
            if (!overlayHidden)
            {
                ThrowWithScreenshot(
                    session,
                    appData,
                    "click-storm-local-interruption-overlay-stuck",
                    $"Click storm scenario stayed stuck behind loading overlay. Visible texts: [{string.Join(", ", session.GetVisibleTexts())}]");
            }

            // If the UI pump is still alive, we should be able to resolve navigation
            // and latest replay text after hydration settles.
            var messagesList = session.FindByAutomationId("ChatView.MessagesList", TimeSpan.FromSeconds(10));
            var lastMessageVisible = session.TryFindVisibleText(
                "GUI Remote Session 01 replay 080",
                messagesList,
                TimeSpan.FromSeconds(12));

            if (lastMessageVisible is null)
            {
                ThrowWithScreenshot(
                    session,
                    appData,
                    "click-storm-local-interruption-scroll",
                    $"Latest remote replay message was not visible after click storm stabilization. Visible texts: [{string.Join(", ", session.GetVisibleTexts(messagesList))}]");
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("SALMONEGG_GUI_SLOW_SESSION_LOAD_MS", previousSlowLoadDelay);
        }
    }

    [SkippableFact]
    public void SelectBetweenTwoRemoteSessions_ClickStorm_DoesNotFreezeAndSettlesOnLatestRemote()
    {
        var previousSlowLoadDelay = Environment.GetEnvironmentVariable("SALMONEGG_GUI_SLOW_SESSION_LOAD_MS");
        Environment.SetEnvironmentVariable("SALMONEGG_GUI_SLOW_SESSION_LOAD_MS", "2400");

        try
        {
            using var appData = GuiAppDataScope.CreateDeterministicSlowRemoteReplayData(
                cachedMessageCount: 1,
                replayMessageCount: 40,
                includeLocalConversation: true,
                localMessageCount: 5,
                remoteConversationCount: 2);
            var seededConversations = appData.ReadConversationsJson();
            Assert.Contains("gui-remote-conversation-02", seededConversations, StringComparison.Ordinal);
            using var session = WindowsGuiAppSession.LaunchFresh();

            const string remoteOneId = "MainNav.Session.gui-remote-conversation-01";
            const string remoteTwoId = "MainNav.Session.gui-remote-conversation-02";
            const string localId = "MainNav.Session.gui-local-conversation-01";

            session.FindByAutomationId(remoteOneId, TimeSpan.FromSeconds(15));
            session.FindByAutomationId(remoteTwoId, TimeSpan.FromSeconds(15));
            session.FindByAutomationId(localId, TimeSpan.FromSeconds(15));

            ActivateNavItem(session, appData, remoteOneId, "dual-remote-click-storm-prime");
            Assert.True(
                session.WaitUntilVisible("ChatView.LoadingOverlayStatus", TimeSpan.FromSeconds(10)),
                "Initial remote session selection did not expose ChatView.LoadingOverlayStatus before dual-remote click storm.");

            for (var index = 0; index < 64; index++)
            {
                if (index % 7 == 0)
                {
                    ActivateNavItem(session, appData, localId, $"dual-remote-click-storm-loop-{index}-local");
                }
                else if (index % 2 == 0)
                {
                    ActivateNavItem(session, appData, remoteTwoId, $"dual-remote-click-storm-loop-{index}-remote-two");
                }
                else
                {
                    ActivateNavItem(session, appData, remoteOneId, $"dual-remote-click-storm-loop-{index}-remote-one");
                }

                Thread.Sleep(22);
            }

            // Final intent: latest selection should win and settle on remote #2.
            ActivateNavItem(session, appData, remoteTwoId, "dual-remote-click-storm-final-1");
            ActivateNavItem(session, appData, remoteTwoId, "dual-remote-click-storm-final-2");

            var remoteHeader = WaitForSessionHeader(
                session,
                expectedTitle: "GUI Remote Session 02",
                scenario: "dual-remote-click-storm-latest-selection",
                appData);
            Assert.Contains("GUI Remote Session 02", remoteHeader.Name, StringComparison.Ordinal);

            var overlayHidden = session.WaitUntilHidden("ChatView.LoadingOverlay", TimeSpan.FromSeconds(60));
            if (!overlayHidden)
            {
                ThrowWithScreenshot(
                    session,
                    appData,
                    "dual-remote-click-storm-overlay-stuck",
                    $"Dual-remote click storm stayed stuck behind loading overlay. Visible texts: [{string.Join(", ", session.GetVisibleTexts())}]");
            }

            var messagesList = FindElementOrThrowWithScreenshot(
                session,
                appData,
                "ChatView.MessagesList",
                TimeSpan.FromSeconds(10),
                "dual-remote-click-storm-messages-list");
            var lastMessageVisible = session.TryFindVisibleText(
                "GUI Remote Session 02 replay 040",
                messagesList,
                TimeSpan.FromSeconds(12));

            if (lastMessageVisible is null)
            {
                ThrowWithScreenshot(
                    session,
                    appData,
                    "dual-remote-click-storm-scroll",
                    $"Latest remote #2 replay message was not visible after click storm stabilization. Visible texts: [{string.Join(", ", session.GetVisibleTexts(messagesList))}]");
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("SALMONEGG_GUI_SLOW_SESSION_LOAD_MS", previousSlowLoadDelay);
        }
    }

    [SkippableFact]
    public void SelectBetweenTwoRemoteSessions_DoubleTapPattern_DoesNotFreezeAndSettlesOnLatestIntent()
    {
        var previousSlowLoadDelay = Environment.GetEnvironmentVariable("SALMONEGG_GUI_SLOW_SESSION_LOAD_MS");
        Environment.SetEnvironmentVariable("SALMONEGG_GUI_SLOW_SESSION_LOAD_MS", "2800");

        try
        {
            using var appData = GuiAppDataScope.CreateDeterministicSlowRemoteReplayData(
                cachedMessageCount: 1,
                replayMessageCount: 50,
                includeLocalConversation: false,
                remoteConversationCount: 2);
            var seededConversations = appData.ReadConversationsJson();
            Assert.Contains("gui-remote-conversation-01", seededConversations, StringComparison.Ordinal);
            Assert.Contains("gui-remote-conversation-02", seededConversations, StringComparison.Ordinal);

            using var session = WindowsGuiAppSession.LaunchFresh();

            const string remoteOneId = "MainNav.Session.gui-remote-conversation-01";
            const string remoteTwoId = "MainNav.Session.gui-remote-conversation-02";

            session.FindByAutomationId(remoteOneId, TimeSpan.FromSeconds(15));
            session.FindByAutomationId(remoteTwoId, TimeSpan.FromSeconds(15));

            // User-reported manual pattern: A, A, B, A, A, B ...
            for (var index = 0; index < 48; index++)
            {
                var inBlock = index % 3;
                if (inBlock == 0 || inBlock == 1)
                {
                    ActivateNavItem(session, appData, remoteOneId, $"double-tap-pattern-loop-{index}-remote-one");
                }
                else
                {
                    ActivateNavItem(session, appData, remoteTwoId, $"double-tap-pattern-loop-{index}-remote-two");
                }

                Thread.Sleep(26);
            }

            // Final intent: latest user click should be remote #1.
            ActivateNavItem(session, appData, remoteOneId, "double-tap-pattern-final-1");
            ActivateNavItem(session, appData, remoteOneId, "double-tap-pattern-final-2");

            var remoteHeader = WaitForSessionHeader(
                session,
                expectedTitle: "GUI Remote Session 01",
                scenario: "double-tap-pattern-header",
                appData);
            Assert.Contains("GUI Remote Session 01", remoteHeader.Name, StringComparison.Ordinal);

            var overlayHidden = session.WaitUntilHidden("ChatView.LoadingOverlay", TimeSpan.FromSeconds(70));
            if (!overlayHidden)
            {
                ThrowWithScreenshot(
                    session,
                    appData,
                    "double-tap-pattern-overlay-stuck",
                    $"Double-tap remote pattern stayed stuck behind loading overlay. Visible texts: [{string.Join(", ", session.GetVisibleTexts())}]");
            }

            var messagesList = FindElementOrThrowWithScreenshot(
                session,
                appData,
                "ChatView.MessagesList",
                TimeSpan.FromSeconds(10),
                "double-tap-pattern-messages-list");
            var lastMessageVisible = session.TryFindVisibleText(
                "GUI Remote Session 01 replay 050",
                messagesList,
                TimeSpan.FromSeconds(12));

            if (lastMessageVisible is null)
            {
                ThrowWithScreenshot(
                    session,
                    appData,
                    "double-tap-pattern-scroll",
                    $"Latest remote #1 replay message was not visible after double-tap pattern stabilization. Visible texts: [{string.Join(", ", session.GetVisibleTexts(messagesList))}]");
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("SALMONEGG_GUI_SLOW_SESSION_LOAD_MS", previousSlowLoadDelay);
        }
    }

    [SkippableFact]
    public void SelectMixedRemoteAndLocal_RandomOrderBurst_FinalLocalIntent_DoesNotFreeze()
    {
        var previousSlowLoadDelay = Environment.GetEnvironmentVariable("SALMONEGG_GUI_SLOW_SESSION_LOAD_MS");
        Environment.SetEnvironmentVariable("SALMONEGG_GUI_SLOW_SESSION_LOAD_MS", "3200");

        try
        {
            using var appData = GuiAppDataScope.CreateDeterministicSlowRemoteReplayData(
                cachedMessageCount: 1,
                replayMessageCount: 60,
                includeLocalConversation: true,
                localMessageCount: 8,
                remoteConversationCount: 2);
            using var session = WindowsGuiAppSession.LaunchFresh();

            const string remoteOneId = "MainNav.Session.gui-remote-conversation-01";
            const string remoteTwoId = "MainNav.Session.gui-remote-conversation-02";
            const string localId = "MainNav.Session.gui-local-conversation-01";

            session.FindByAutomationId(remoteOneId, TimeSpan.FromSeconds(15));
            session.FindByAutomationId(remoteTwoId, TimeSpan.FromSeconds(15));
            session.FindByAutomationId(localId, TimeSpan.FromSeconds(15));

            ActivateNavItem(session, appData, remoteOneId, "mixed-random-burst-prime");
            Assert.True(
                session.WaitUntilVisible("ChatView.LoadingOverlayStatus", TimeSpan.FromSeconds(10)),
                "Initial remote selection did not expose ChatView.LoadingOverlayStatus before mixed random burst.");

            var random = new Random(20260330);
            var candidates = new[] { remoteOneId, remoteOneId, remoteTwoId, localId, localId, remoteTwoId };
            for (var index = 0; index < 120; index++)
            {
                var target = candidates[random.Next(candidates.Length)];
                ActivateNavItem(session, appData, target, $"mixed-random-burst-{index}");
                if (index % 11 == 0)
                {
                    ActivateNavItem(session, appData, target, $"mixed-random-burst-double-{index}");
                }

                Thread.Sleep(index % 3 == 0 ? 12 : 24);
            }

            // Final intent: local conversation should win and stay responsive.
            ActivateNavItem(session, appData, localId, "mixed-random-burst-final-local-1");
            ActivateNavItem(session, appData, localId, "mixed-random-burst-final-local-2");

            var localHeader = WaitForSessionHeader(
                session,
                expectedTitle: "GUI Local Session 01",
                scenario: "mixed-random-burst-final-local-header",
                appData);
            Assert.Contains("GUI Local Session 01", localHeader.Name, StringComparison.Ordinal);

            var statusDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(8);
            var statusStillVisible = false;
            while (DateTime.UtcNow < statusDeadline)
            {
                statusStillVisible = session.TryFindByAutomationId("ChatView.LoadingOverlayStatus", TimeSpan.FromMilliseconds(120)) is not null;
                if (!statusStillVisible)
                {
                    break;
                }

                Thread.Sleep(120);
            }

            if (statusStillVisible)
            {
                ThrowWithScreenshot(
                    session,
                    appData,
                    "mixed-random-burst-final-local-status-leak",
                    "Remote loading status remained visible after the final local selection settled.");
            }

            var messagesList = FindElementOrThrowWithScreenshot(
                session,
                appData,
                "ChatView.MessagesList",
                TimeSpan.FromSeconds(10),
                "mixed-random-burst-final-local-messages-list");
            var localLatestVisible = session.TryFindVisibleText(
                "GUI Local Session 01 message 008",
                messagesList,
                TimeSpan.FromSeconds(8));

            if (localLatestVisible is null)
            {
                ThrowWithScreenshot(
                    session,
                    appData,
                    "mixed-random-burst-final-local-scroll",
                    $"Final local transcript did not surface expected latest message after mixed random burst. Visible texts: [{string.Join(", ", session.GetVisibleTexts(messagesList))}]");
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("SALMONEGG_GUI_SLOW_SESSION_LOAD_MS", previousSlowLoadDelay);
        }
    }

    [SkippableFact]
    public void SelectMixedRemoteAndLocal_DeterministicFailurePrefix_FinalLocalIntent_DoesNotFreeze()
    {
        var previousSlowLoadDelay = Environment.GetEnvironmentVariable("SALMONEGG_GUI_SLOW_SESSION_LOAD_MS");
        Environment.SetEnvironmentVariable("SALMONEGG_GUI_SLOW_SESSION_LOAD_MS", "3200");

        try
        {
            using var appData = GuiAppDataScope.CreateDeterministicSlowRemoteReplayData(
                cachedMessageCount: 1,
                replayMessageCount: 60,
                includeLocalConversation: true,
                localMessageCount: 8,
                remoteConversationCount: 2);
            using var session = WindowsGuiAppSession.LaunchFresh();

            const string remoteOneId = "MainNav.Session.gui-remote-conversation-01";
            const string remoteTwoId = "MainNav.Session.gui-remote-conversation-02";
            const string localId = "MainNav.Session.gui-local-conversation-01";

            session.FindByAutomationId(remoteOneId, TimeSpan.FromSeconds(15));
            session.FindByAutomationId(remoteTwoId, TimeSpan.FromSeconds(15));
            session.FindByAutomationId(localId, TimeSpan.FromSeconds(15));

            ActivateNavItem(session, appData, remoteOneId, "mixed-deterministic-prime");
            Assert.True(
                session.WaitUntilVisible("ChatView.LoadingOverlayStatus", TimeSpan.FromSeconds(10)),
                "Initial remote selection did not expose ChatView.LoadingOverlayStatus before deterministic mixed burst.");

            var deterministicSequence = new[]
            {
                remoteTwoId,
                remoteTwoId,
                remoteOneId,
                localId,
                remoteOneId,
                localId,
                remoteTwoId,
                localId
            };

            for (var index = 0; index < deterministicSequence.Length; index++)
            {
                ActivateNavItem(session, appData, deterministicSequence[index], $"mixed-deterministic-{index}");
                Thread.Sleep(index % 2 == 0 ? 18 : 32);
            }

            ActivateNavItem(session, appData, localId, "mixed-deterministic-final-local-1");
            ActivateNavItem(session, appData, localId, "mixed-deterministic-final-local-2");

            var localHeader = WaitForSessionHeader(
                session,
                expectedTitle: "GUI Local Session 01",
                scenario: "mixed-deterministic-final-local-header",
                appData);
            Assert.Contains("GUI Local Session 01", localHeader.Name, StringComparison.Ordinal);

            var statusDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(8);
            var statusStillVisible = false;
            while (DateTime.UtcNow < statusDeadline)
            {
                statusStillVisible = session.TryFindByAutomationId("ChatView.LoadingOverlayStatus", TimeSpan.FromMilliseconds(120)) is not null;
                if (!statusStillVisible)
                {
                    break;
                }

                Thread.Sleep(120);
            }

            if (statusStillVisible)
            {
                ThrowWithScreenshot(
                    session,
                    appData,
                    "mixed-deterministic-final-local-status-leak",
                    "Remote loading status remained visible after the deterministic final local selection settled.");
            }

            var messagesList = FindElementOrThrowWithScreenshot(
                session,
                appData,
                "ChatView.MessagesList",
                TimeSpan.FromSeconds(10),
                "mixed-deterministic-final-local-messages-list");
            var localLatestVisible = session.TryFindVisibleText(
                "GUI Local Session 01 message 008",
                messagesList,
                TimeSpan.FromSeconds(8));

            if (localLatestVisible is null)
            {
                ThrowWithScreenshot(
                    session,
                    appData,
                    "mixed-deterministic-final-local-scroll",
                    $"Final local transcript did not surface expected latest message after deterministic mixed burst. Visible texts: [{string.Join(", ", session.GetVisibleTexts(messagesList))}]");
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("SALMONEGG_GUI_SLOW_SESSION_LOAD_MS", previousSlowLoadDelay);
        }
    }

    [SkippableFact]
    public void SelectRemoteSessionWithSlowReplay_ViewportStateReportsBottomAfterHydration()
    {
        var previousSlowLoadDelay = Environment.GetEnvironmentVariable("SALMONEGG_GUI_SLOW_SESSION_LOAD_MS");
        Environment.SetEnvironmentVariable("SALMONEGG_GUI_SLOW_SESSION_LOAD_MS", "1500");

        try
        {
            using var appData = GuiAppDataScope.CreateDeterministicSlowRemoteReplayData(
                cachedMessageCount: 1,
                replayMessageCount: 24);
            using var session = WindowsGuiAppSession.LaunchFresh();

            var sessionItem = session.FindByAutomationId("MainNav.Session.gui-remote-conversation-01", TimeSpan.FromSeconds(15));
            session.ActivateElement(sessionItem);

            var overlayHidden = session.WaitUntilHidden("ChatView.LoadingOverlay", TimeSpan.FromSeconds(30));
            Assert.True(overlayHidden, "Remote session loading overlay did not disappear after hydration should have completed.");

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            string? viewportState = null;
            while (DateTime.UtcNow < deadline)
            {
                viewportState = session.TryGetElementName("ChatView.TranscriptViewportState", TimeSpan.FromMilliseconds(200));
                if (string.Equals(viewportState, "bottom", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                Thread.Sleep(150);
            }

            if (!string.Equals(viewportState, "bottom", StringComparison.OrdinalIgnoreCase))
            {
                ThrowWithScreenshot(
                    session,
                    appData,
                    "remote-hydration-viewport-state-not-bottom",
                    $"Transcript viewport state did not settle to bottom after hydration. State='{viewportState ?? "<missing>"}'.");
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("SALMONEGG_GUI_SLOW_SESSION_LOAD_MS", previousSlowLoadDelay);
        }
    }

    [SkippableFact]
    public void HydratedRemoteSession_NavigateToDiscoverAndBack_ReturnsHotWithoutRemoteReload()
    {
        var previousSlowLoadDelay = Environment.GetEnvironmentVariable("SALMONEGG_GUI_SLOW_SESSION_LOAD_MS");
        Environment.SetEnvironmentVariable("SALMONEGG_GUI_SLOW_SESSION_LOAD_MS", "1500");

        try
        {
            using var appData = GuiAppDataScope.CreateDeterministicSlowRemoteReplayData(
                cachedMessageCount: 1,
                replayMessageCount: 24);
            using var session = WindowsGuiAppSession.LaunchFresh();

            var remoteItem = session.FindByAutomationId("MainNav.Session.gui-remote-conversation-01", TimeSpan.FromSeconds(15));
            session.ActivateElement(remoteItem);

            var overlayHidden = session.WaitUntilHidden("ChatView.LoadingOverlay", TimeSpan.FromSeconds(30));
            Assert.True(overlayHidden, "Initial remote hydration did not complete before discover navigation.");

            var discoverItem = session.FindByAutomationId("MainNav.DiscoverSessions", TimeSpan.FromSeconds(10));
            session.ActivateElement(discoverItem);

            var discoverVisible = session.WaitUntilVisible("DiscoverSessions.Title", TimeSpan.FromSeconds(10));
            Assert.True(discoverVisible, "Discover sessions page did not become visible.");

            remoteItem = session.FindByAutomationId("MainNav.Session.gui-remote-conversation-01", TimeSpan.FromSeconds(10));
            session.ActivateElement(remoteItem);

            var headerVisible = session.WaitUntilVisible("ChatView.CurrentSessionNameButton", TimeSpan.FromSeconds(5));
            Assert.True(headerVisible, "Returning from discover to a hydrated remote chat did not restore the chat header quickly.");

            var returnedOverlayHidden = session.WaitUntilHidden("ChatView.LoadingOverlay", TimeSpan.FromSeconds(5));
            Assert.True(returnedOverlayHidden, "Returning from discover to a hydrated remote chat stayed behind the loading overlay for too long.");

            var messagesList = session.FindByAutomationId("ChatView.MessagesList", TimeSpan.FromSeconds(5));
            Assert.NotNull(messagesList);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SALMONEGG_GUI_SLOW_SESSION_LOAD_MS", previousSlowLoadDelay);
        }
    }

    [SkippableFact]
    public void SelectLargeCachedRemoteSession_FirstOpen_HeaderVisibleWithinBudget()
    {
        // Performance guardrail: first-open of a large cached session should not regress into
        // multi-second UI stalls before the chat header is visible.
        using var appData = GuiAppDataScope.CreateDeterministicSlowRemoteReplayData(
            cachedMessageCount: 900,
            replayMessageCount: 24);
        using var session = WindowsGuiAppSession.LaunchFresh();

        var sessionItem = session.FindByAutomationId("MainNav.Session.gui-remote-conversation-01", TimeSpan.FromSeconds(15));
        var stopwatch = Stopwatch.StartNew();
        session.ActivateElement(sessionItem);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        AutomationElement? header = null;
        while (DateTime.UtcNow < deadline)
        {
            header = session.TryFindByAutomationId("ChatView.CurrentSessionNameButton", TimeSpan.FromMilliseconds(120));
            if (header is not null)
            {
                break;
            }

            Thread.Sleep(35);
        }

        stopwatch.Stop();
        if (header is null)
        {
            ThrowWithScreenshot(
                session,
                appData,
                "large-cached-remote-first-open-header-timeout",
                "Header did not become visible within the expected timeout on first open.");
        }

        const int headerVisibleBudgetMs = 1800;
        if (stopwatch.ElapsedMilliseconds > headerVisibleBudgetMs)
        {
            ThrowWithScreenshot(
                session,
                appData,
                "large-cached-remote-first-open-header-over-budget",
                $"Header visibility exceeded budget. elapsedMs={stopwatch.ElapsedMilliseconds} budgetMs={headerVisibleBudgetMs}");
        }
    }

    [SkippableFact]
    public void SelectHugeCachedRemoteSession_FirstOpen_LoadingPillAppearsWithinBudget()
    {
        using var appData = GuiAppDataScope.CreateDeterministicSlowRemoteReplayData(
            cachedMessageCount: 5000,
            replayMessageCount: 24);
        using var session = WindowsGuiAppSession.LaunchFresh();

        var sessionItem = session.FindByAutomationId("MainNav.Session.gui-remote-conversation-01", TimeSpan.FromSeconds(20));

        var clickStopwatch = Stopwatch.StartNew();
        session.ActivateElement(sessionItem);
        clickStopwatch.Stop();

        const int clickInvokeBudgetMs = 1200;
        if (clickStopwatch.ElapsedMilliseconds > clickInvokeBudgetMs)
        {
            ThrowWithScreenshot(
                session,
                appData,
                "huge-cached-remote-first-open-click-over-budget",
                $"Session click invoke exceeded budget. elapsedMs={clickStopwatch.ElapsedMilliseconds} budgetMs={clickInvokeBudgetMs}");
        }

        var statusDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(8);
        var statusStopwatch = Stopwatch.StartNew();
        while (DateTime.UtcNow < statusDeadline)
        {
            if (session.TryFindByAutomationId("ChatView.LoadingOverlayStatus", TimeSpan.FromMilliseconds(120)) is not null)
            {
                break;
            }

            Thread.Sleep(40);
        }

        const int statusVisibleBudgetMs = 1800;
        if (statusStopwatch.ElapsedMilliseconds > statusVisibleBudgetMs)
        {
            ThrowWithScreenshot(
                session,
                appData,
                "huge-cached-remote-first-open-status-over-budget",
                $"Loading status pill exceeded budget. elapsedMs={statusStopwatch.ElapsedMilliseconds} budgetMs={statusVisibleBudgetMs}");
        }
    }

    [SkippableFact]
    public void RemoteFirstOpen_ImmediateSwitchToLocal_CompletesWithinResponsivenessBudget()
    {
        var previousSlowLoadDelay = Environment.GetEnvironmentVariable("SALMONEGG_GUI_SLOW_SESSION_LOAD_MS");
        Environment.SetEnvironmentVariable("SALMONEGG_GUI_SLOW_SESSION_LOAD_MS", "2600");

        try
        {
            using var appData = GuiAppDataScope.CreateDeterministicSlowRemoteReplayData(
                cachedMessageCount: 900,
                replayMessageCount: 24,
                includeLocalConversation: true,
                localMessageCount: 6);
            using var session = WindowsGuiAppSession.LaunchFresh();

            var remoteItem = session.FindByAutomationId("MainNav.Session.gui-remote-conversation-01", TimeSpan.FromSeconds(15));
            var localItem = session.FindByAutomationId("MainNav.Session.gui-local-conversation-01", TimeSpan.FromSeconds(15));

            session.ActivateElement(remoteItem);
            Thread.Sleep(120);

            var localSwitchStopwatch = Stopwatch.StartNew();
            session.ActivateElement(localItem);
            var localDeadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(1500);
            AutomationElement? localHeader = null;
            while (DateTime.UtcNow < localDeadline)
            {
                var header = session.TryFindByAutomationId("ChatView.CurrentSessionNameButton", TimeSpan.FromMilliseconds(120));
                if (header is not null
                    && header.Name.Contains("GUI Local Session 01", StringComparison.Ordinal))
                {
                    localHeader = header;
                    break;
                }

                Thread.Sleep(40);
            }
            localSwitchStopwatch.Stop();

            if (localHeader is null)
            {
                ThrowWithScreenshot(
                    session,
                    appData,
                    "remote-first-open-immediate-local-switch-not-responsive",
                    $"Local switch did not settle quickly while remote hydration was in flight. elapsedMs={localSwitchStopwatch.ElapsedMilliseconds}");
            }

            const int localSwitchBudgetMs = 1000;
            if (localSwitchStopwatch.ElapsedMilliseconds > localSwitchBudgetMs)
            {
                ThrowWithScreenshot(
                    session,
                    appData,
                    "remote-first-open-immediate-local-switch-over-budget",
                    $"Local switch responsiveness exceeded budget. elapsedMs={localSwitchStopwatch.ElapsedMilliseconds} budgetMs={localSwitchBudgetMs}");
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("SALMONEGG_GUI_SLOW_SESSION_LOAD_MS", previousSlowLoadDelay);
        }
    }

    [SkippableFact]
    public void SelectRemoteSessionWithSlowReplay_PersistsLoadedModeAfterHydration()
    {
        var previousSlowLoadDelay = Environment.GetEnvironmentVariable("SALMONEGG_GUI_SLOW_SESSION_LOAD_MS");
        Environment.SetEnvironmentVariable("SALMONEGG_GUI_SLOW_SESSION_LOAD_MS", "1500");

        try
        {
            using var appData = GuiAppDataScope.CreateDeterministicSlowRemoteReplayData(
                cachedMessageCount: 1,
                replayMessageCount: 18);
            using var session = WindowsGuiAppSession.LaunchFresh();
            if (session.MainWindow.Patterns.Window.IsSupported)
            {
                session.MainWindow.Patterns.Window.Pattern.SetWindowVisualState(WindowVisualState.Maximized);
            }

            var sessionItem = session.FindByAutomationId("MainNav.Session.gui-remote-conversation-01", TimeSpan.FromSeconds(15));
            session.ActivateElement(sessionItem);

            var overlayHidden = session.WaitUntilHidden("ChatView.LoadingOverlay", TimeSpan.FromSeconds(30));
            Assert.True(overlayHidden, "Remote session loading overlay did not disappear after hydration should have completed.");

            var remoteHeader = WaitForSessionHeader(
                session,
                expectedTitle: "GUI Remote Session 01",
                scenario: "remote-hydration-load-mode-visible",
                appData);
            Assert.Contains("GUI Remote Session 01", remoteHeader.Name, StringComparison.Ordinal);

            var messagesList = session.FindByAutomationId("ChatView.MessagesList", TimeSpan.FromSeconds(10));
            var lastMessageVisible = session.TryFindVisibleText(
                "GUI Remote Session 01 replay 018",
                messagesList,
                TimeSpan.FromSeconds(8));

            if (lastMessageVisible is null)
            {
                ThrowWithScreenshot(
                    session,
                    appData,
                    "remote-hydration-load-mode-visible-missing-message",
                    $"Latest remote replay message was not visible after hydration. Visible texts: [{string.Join(", ", session.GetVisibleTexts(messagesList))}]");
            }

            var persistenceDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            var conversationsJson = appData.ReadConversationsJson();
            while (DateTime.UtcNow < persistenceDeadline)
            {
                if (conversationsJson.Contains("\"selectedModeId\":\"planner\"", StringComparison.Ordinal)
                    && conversationsJson.Contains("\"selectedValue\":\"planner\"", StringComparison.Ordinal))
                {
                    break;
                }

                Thread.Sleep(250);
                conversationsJson = appData.ReadConversationsJson();
            }

            if (!conversationsJson.Contains("\"selectedModeId\":\"planner\"", StringComparison.Ordinal)
                || !conversationsJson.Contains("\"selectedValue\":\"planner\"", StringComparison.Ordinal))
            {
                ThrowWithScreenshot(
                    session,
                    appData,
                    "remote-hydration-load-mode-persistence-missing",
                    $"Hydrated remote session did not persist loaded mode/config state. conversations.json: {conversationsJson}");
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("SALMONEGG_GUI_SLOW_SESSION_LOAD_MS", previousSlowLoadDelay);
        }
    }

    [SkippableFact]
    public void SelectAcrossProfilesAndLocal_LongRandomSwitch_RemainsInteractive()
    {
        var previousSlowLoadDelay = Environment.GetEnvironmentVariable("SALMONEGG_GUI_SLOW_SESSION_LOAD_MS");
        Environment.SetEnvironmentVariable("SALMONEGG_GUI_SLOW_SESSION_LOAD_MS", "2800");

        try
        {
            using var appData = GuiAppDataScope.CreateDeterministicCrossProfileRemoteReplayData(
                cachedMessageCount: 1,
                replayMessageCount: 40,
                localMessageCount: 6);
            using var session = WindowsGuiAppSession.LaunchFresh();

            const string remoteOneId = "MainNav.Session.gui-remote-conversation-01";
            const string remoteTwoId = "MainNav.Session.gui-remote-conversation-02";
            const string localId = "MainNav.Session.gui-local-conversation-01";
            const string startId = "MainNav.Start";

            session.FindByAutomationId(remoteOneId, TimeSpan.FromSeconds(15));
            session.FindByAutomationId(remoteTwoId, TimeSpan.FromSeconds(15));
            session.FindByAutomationId(localId, TimeSpan.FromSeconds(15));

            ActivateNavItem(session, appData, remoteOneId, "cross-profile-long-random-prime");
            Assert.True(
                session.WaitUntilVisible("ChatView.LoadingOverlayStatus", TimeSpan.FromSeconds(10)),
                "Initial cross-profile remote selection did not expose ChatView.LoadingOverlayStatus before random switching.");

            var random = new Random(20260402);
            var targets = new[] { remoteOneId, remoteTwoId, localId, remoteTwoId, remoteOneId, localId };
            for (var index = 0; index < 90; index++)
            {
                var target = targets[random.Next(targets.Length)];
                ActivateNavItem(session, appData, target, $"cross-profile-long-random-{index}");
                if (index % 10 == 0)
                {
                    ActivateNavItem(session, appData, target, $"cross-profile-long-random-double-{index}");
                }

                Thread.Sleep(index % 3 == 0 ? 22 : 34);
            }

            // Final intent: settle on remote #2 first (cross-profile target), then verify we can still navigate to Start and Local.
            ActivateNavItem(session, appData, remoteTwoId, "cross-profile-long-random-final-remote-1");
            ActivateNavItem(session, appData, remoteTwoId, "cross-profile-long-random-final-remote-2");

            var remoteHeader = WaitForSessionHeader(
                session,
                expectedTitle: "GUI Remote Session 02",
                scenario: "cross-profile-long-random-final-remote-header",
                appData);
            Assert.Contains("GUI Remote Session 02", remoteHeader.Name, StringComparison.Ordinal);

            var overlayHidden = session.WaitUntilHidden("ChatView.LoadingOverlay", TimeSpan.FromSeconds(60));
            if (!overlayHidden)
            {
                ThrowWithScreenshot(
                    session,
                    appData,
                    "cross-profile-long-random-overlay-stuck",
                    $"Cross-profile long random switching stayed stuck behind loading overlay. Visible texts: [{string.Join(", ", session.GetVisibleTexts())}]");
            }

            var remoteMessages = FindElementOrThrowWithScreenshot(
                session,
                appData,
                "ChatView.MessagesList",
                TimeSpan.FromSeconds(10),
                "cross-profile-long-random-remote-messages-list");
            var remoteLatestVisible = session.TryFindVisibleText(
                "GUI Remote Session 02 replay 040",
                remoteMessages,
                TimeSpan.FromSeconds(10));
            if (remoteLatestVisible is null)
            {
                ThrowWithScreenshot(
                    session,
                    appData,
                    "cross-profile-long-random-remote-scroll",
                    $"Cross-profile final remote transcript did not surface expected latest replay message. Visible texts: [{string.Join(", ", session.GetVisibleTexts(remoteMessages))}]");
            }

            var startItem = FindElementOrThrowWithScreenshot(
                session,
                appData,
                startId,
                TimeSpan.FromSeconds(8),
                "cross-profile-long-random-start-item");
            session.ActivateElement(startItem);
            Thread.Sleep(600);

            var startSelected = session.TryGetIsSelected(startId) == true;
            var startVisible = session.TryFindByAutomationId("StartView.Title", TimeSpan.FromSeconds(4)) is not null;
            if (!startSelected || !startVisible)
            {
                ThrowWithScreenshot(
                    session,
                    appData,
                    "cross-profile-long-random-start-not-interactive",
                    $"Start view was not interactive after cross-profile long random switching. startSelected={startSelected} startVisible={startVisible}");
            }

            ActivateNavItem(session, appData, localId, "cross-profile-long-random-final-local-1");
            ActivateNavItem(session, appData, localId, "cross-profile-long-random-final-local-2");

            var localHeader = WaitForSessionHeader(
                session,
                expectedTitle: "GUI Local Session 01",
                scenario: "cross-profile-long-random-final-local-header",
                appData);
            Assert.Contains("GUI Local Session 01", localHeader.Name, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SALMONEGG_GUI_SLOW_SESSION_LOAD_MS", previousSlowLoadDelay);
        }
    }

    [SkippableFact]
    public void SelectAcrossProfilesAndLocal_OneSecondCadence_FinalIntentAlwaysWins()
    {
        var previousSlowLoadDelay = Environment.GetEnvironmentVariable("SALMONEGG_GUI_SLOW_SESSION_LOAD_MS");
        Environment.SetEnvironmentVariable("SALMONEGG_GUI_SLOW_SESSION_LOAD_MS", "2400");

        try
        {
            using var appData = GuiAppDataScope.CreateDeterministicCrossProfileRemoteReplayData(
                cachedMessageCount: 1,
                replayMessageCount: 30,
                localMessageCount: 6);
            using var session = WindowsGuiAppSession.LaunchFresh();

            const string remoteOneId = "MainNav.Session.gui-remote-conversation-01";
            const string remoteTwoId = "MainNav.Session.gui-remote-conversation-02";
            const string localId = "MainNav.Session.gui-local-conversation-01";
            const string startId = "MainNav.Start";

            session.FindByAutomationId(remoteOneId, TimeSpan.FromSeconds(15));
            session.FindByAutomationId(remoteTwoId, TimeSpan.FromSeconds(15));
            session.FindByAutomationId(localId, TimeSpan.FromSeconds(15));
            session.FindByAutomationId(startId, TimeSpan.FromSeconds(15));

            ActivateNavItem(session, appData, remoteOneId, "cross-profile-one-second-prime");
            Assert.True(
                session.WaitUntilVisible("ChatView.LoadingOverlayStatus", TimeSpan.FromSeconds(10)),
                "Initial cross-profile remote selection did not expose ChatView.LoadingOverlayStatus before 1s cadence switching.");

            var random = new Random(20260402);
            var targets = new[] { remoteOneId, remoteTwoId, localId, startId };
            for (var index = 0; index < 14; index++)
            {
                var target = targets[random.Next(targets.Length)];
                ActivateNavItem(session, appData, target, $"cross-profile-one-second-step-{index:00}");
                Thread.Sleep(1000);
            }

            ActivateNavItem(session, appData, remoteTwoId, "cross-profile-one-second-final-remote-two-1");
            ActivateNavItem(session, appData, remoteTwoId, "cross-profile-one-second-final-remote-two-2");

            var remoteTwoHeader = WaitForSessionHeader(
                session,
                expectedTitle: "GUI Remote Session 02",
                scenario: "cross-profile-one-second-final-remote-two-header",
                appData);
            Assert.Contains("GUI Remote Session 02", remoteTwoHeader.Name, StringComparison.Ordinal);

            var overlayHidden = session.WaitUntilHidden("ChatView.LoadingOverlay", TimeSpan.FromSeconds(45));
            if (!overlayHidden)
            {
                ThrowWithScreenshot(
                    session,
                    appData,
                    "cross-profile-one-second-final-remote-two-overlay-stuck",
                    "Loading overlay stayed visible after final remote #2 selection in cross-profile 1s cadence switching.");
            }

            var remoteMessages = FindElementOrThrowWithScreenshot(
                session,
                appData,
                "ChatView.MessagesList",
                TimeSpan.FromSeconds(10),
                "cross-profile-one-second-final-remote-two-messages");
            var remoteLatestVisible = session.TryFindVisibleText(
                "GUI Remote Session 02 replay 030",
                remoteMessages,
                TimeSpan.FromSeconds(10));
            if (remoteLatestVisible is null)
            {
                ThrowWithScreenshot(
                    session,
                    appData,
                    "cross-profile-one-second-final-remote-two-scroll",
                    $"Expected latest replay text for remote #2 was not visible after final intent. Visible texts: [{string.Join(", ", session.GetVisibleTexts(remoteMessages))}]");
            }

            ActivateNavItem(session, appData, startId, "cross-profile-one-second-final-start");
            Thread.Sleep(700);
            var startSelected = session.TryGetIsSelected(startId) == true;
            var startVisible = session.TryFindByAutomationId("StartView.Title", TimeSpan.FromSeconds(4)) is not null;
            if (!startSelected || !startVisible)
            {
                ThrowWithScreenshot(
                    session,
                    appData,
                    "cross-profile-one-second-final-start-not-visible",
                    $"Start view did not become visible after remote #2 finalization. startSelected={startSelected} startVisible={startVisible}");
            }

            ActivateNavItem(session, appData, localId, "cross-profile-one-second-final-local-1");
            ActivateNavItem(session, appData, localId, "cross-profile-one-second-final-local-2");

            var localHeader = WaitForSessionHeader(
                session,
                expectedTitle: "GUI Local Session 01",
                scenario: "cross-profile-one-second-final-local-header",
                appData);
            Assert.Contains("GUI Local Session 01", localHeader.Name, StringComparison.Ordinal);
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

    private static void WaitForLoadingOverlayBeforeChatShell(
        WindowsGuiAppSession session,
        GuiAppDataScope appData,
        string expectedHeaderText,
        string scenario)
    {
        var timeline = new List<string>();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(6);

        while (DateTime.UtcNow < deadline)
        {
            var overlayVisible = session.TryFindByAutomationId("ChatView.LoadingOverlay", TimeSpan.FromMilliseconds(50)) is not null;
            var header = session.TryFindByAutomationId("ChatView.CurrentSessionNameButton", TimeSpan.FromMilliseconds(50));
            var messagesVisible = session.TryFindByAutomationId("ChatView.MessagesList", TimeSpan.FromMilliseconds(50)) is not null;
            var headerName = header?.Name ?? "<missing>";

            timeline.Add(
                $"{DateTime.UtcNow:HH:mm:ss.fff} overlay={overlayVisible} header={headerName} messages={messagesVisible}");

            if (overlayVisible)
            {
                return;
            }

            if ((header is not null && headerName.Contains(expectedHeaderText, StringComparison.Ordinal)) || messagesVisible)
            {
                ThrowWithScreenshot(
                    session,
                    appData,
                    scenario,
                    $"Chat shell became visible before the loading overlay. Timeline:{Environment.NewLine}{string.Join(Environment.NewLine, timeline)}");
            }

            Thread.Sleep(40);
        }

        ThrowWithScreenshot(
            session,
            appData,
            scenario,
            $"Loading overlay did not become visible before timeout.{Environment.NewLine}{string.Join(Environment.NewLine, timeline)}");
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

    private static AutomationElement WaitForSessionHeader(
        WindowsGuiAppSession session,
        string expectedTitle,
        string scenario,
        GuiAppDataScope appData)
    {
        var timeline = new List<string>();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(12);

        while (DateTime.UtcNow < deadline)
        {
            var header = session.TryFindByAutomationId("ChatView.CurrentSessionNameButton", TimeSpan.FromMilliseconds(100));
            var headerName = header?.Name ?? "<missing>";
            var overlayVisible = session.TryFindByAutomationId("ChatView.LoadingOverlay", TimeSpan.FromMilliseconds(100)) is not null;
            var statusVisible = session.TryFindByAutomationId("ChatView.LoadingOverlayStatus", TimeSpan.FromMilliseconds(100)) is not null;

            timeline.Add(
                $"{DateTime.UtcNow:HH:mm:ss.fff} header={headerName} overlay={overlayVisible} status={statusVisible}");

            if (header is not null
                && headerName.Contains(expectedTitle, StringComparison.Ordinal))
            {
                return header;
            }

            Thread.Sleep(150);
        }

        ThrowWithScreenshot(
            session,
            appData,
            scenario,
            $"Expected header containing '{expectedTitle}' was not observed.{Environment.NewLine}{string.Join(Environment.NewLine, timeline)}");
        throw new Xunit.Sdk.XunitException("Unreachable");
    }

    private static void ThrowWithScreenshot(
        WindowsGuiAppSession session,
        GuiAppDataScope appData,
        string scenario,
        string message)
    {
        var captureRoot = Path.Combine(Path.GetTempPath(), "SalmonEgg.GuiTests");
        Directory.CreateDirectory(captureRoot);
        var screenshotPath = Path.Combine(
            captureRoot,
            $"{scenario}-{DateTime.UtcNow:yyyyMMddHHmmssfff}.png");
        string screenshotDescriptor;
        try
        {
            session.MainWindow.CaptureToFile(screenshotPath);
            screenshotDescriptor = screenshotPath;
        }
        catch (Exception ex) when (ex is COMException or Win32Exception or InvalidOperationException)
        {
            screenshotDescriptor = $"<capture failed: {ex.Message}>";
        }

        throw new Xunit.Sdk.XunitException(
            $"{message}{Environment.NewLine}Screenshot: {screenshotDescriptor}{Environment.NewLine}boot.log:{Environment.NewLine}{appData.ReadBootLogTail()}");
    }

    private static void ActivateNavItem(
        WindowsGuiAppSession session,
        GuiAppDataScope appData,
        string automationId,
        string scenario)
    {
        Exception? lastException = null;
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            try
            {
                var element = session.FindByAutomationId(automationId, TimeSpan.FromSeconds(4));
                session.ActivateElement(element);
                return;
            }
            catch (Exception ex) when (ex is TimeoutException or Win32Exception or COMException or InvalidOperationException)
            {
                lastException = ex;
                Thread.Sleep(40 * attempt);
            }
        }

        var observedSessionIds = "<unavailable>";
        try
        {
            observedSessionIds = string.Join(
                ", ",
                session.MainWindow
                    .FindAllDescendants()
                    .Select(TryGetAutomationId)
                    .Where(id => !string.IsNullOrWhiteSpace(id) && id.StartsWith("MainNav.Session.", StringComparison.Ordinal))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(id => id, StringComparer.Ordinal));
        }
        catch (Exception ex) when (ex is TimeoutException or COMException or Win32Exception or InvalidOperationException)
        {
            observedSessionIds = $"<failed to enumerate nav session ids: {ex.Message}>";
        }

        ThrowWithScreenshot(
            session,
            appData,
            $"{scenario}-activate-{automationId.Replace('.', '-')}",
            $"Scenario '{scenario}' failed to activate navigation item '{automationId}' after retries. Last error: {lastException?.Message ?? "<none>"}{Environment.NewLine}Observed session ids: [{observedSessionIds}]");
    }

    private static AutomationElement FindElementOrThrowWithScreenshot(
        WindowsGuiAppSession session,
        GuiAppDataScope appData,
        string automationId,
        TimeSpan timeout,
        string scenario)
    {
        try
        {
            return session.FindByAutomationId(automationId, timeout);
        }
        catch (TimeoutException ex)
        {
            ThrowWithScreenshot(
                session,
                appData,
                scenario,
                $"Expected element '{automationId}' was not found. Error: {ex.Message}");
            throw;
        }
    }

    private static string WaitForOverlayStatus(
        WindowsGuiAppSession session,
        GuiAppDataScope appData,
        Func<string, bool> predicate,
        TimeSpan timeout,
        string scenario)
    {
        var timeline = new List<string>();
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            var statusElement = session.TryFindByAutomationId("ChatView.LoadingOverlayStatus", TimeSpan.FromMilliseconds(120));
            var statusText = statusElement?.Name?.Trim() ?? string.Empty;
            var overlayVisible = session.TryFindByAutomationId("ChatView.LoadingOverlay", TimeSpan.FromMilliseconds(120)) is not null;

            timeline.Add($"{DateTime.UtcNow:HH:mm:ss.fff} overlay={overlayVisible} status={statusText}");

            if (!string.IsNullOrWhiteSpace(statusText) && predicate(statusText))
            {
                return statusText;
            }

            Thread.Sleep(120);
        }

        ThrowWithScreenshot(
            session,
            appData,
            scenario,
            $"Timed out waiting for loading pill status match. Timeline:{Environment.NewLine}{string.Join(Environment.NewLine, timeline)}");
        throw new Xunit.Sdk.XunitException("Unreachable");
    }

    private static bool IsUserFriendlyLoadingStatus(string status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return false;
        }

        return (status.StartsWith("正在", StringComparison.Ordinal) || status.StartsWith("即将", StringComparison.Ordinal))
            && (status.Contains("会话", StringComparison.Ordinal)
                || status.Contains("聊天", StringComparison.Ordinal)
                || status.Contains("消息", StringComparison.Ordinal))
            && !status.Contains("ACP", StringComparison.OrdinalIgnoreCase)
            && !status.Contains("协议", StringComparison.Ordinal);
    }
}
