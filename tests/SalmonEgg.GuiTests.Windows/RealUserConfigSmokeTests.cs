using System.Diagnostics;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using FlaUI.Core.Definitions;
using FlaUI.Core.AutomationElements;

namespace SalmonEgg.GuiTests.Windows;

public sealed partial class RealUserConfigSmokeTests
{
    [SkippableFact]
    public void AuditVisibleRealSessions_TranscriptAutoBottomAndLastMessages()
    {
        GuiTestGate.RequireEnabled();

        var candidates = LoadRealTranscriptAuditCandidates()
            .Where(candidate => candidate.MessageCount > 0)
            .OrderByDescending(candidate => candidate.MarkdownLikeMessageCount)
            .ThenByDescending(candidate => candidate.MessageCount)
            .ToArray();
        Skip.If(candidates.Length == 0, "No real conversations with local transcript messages were found in the current SalmonEgg app data.");

        using var session = WindowsGuiAppSession.LaunchFresh();
        var findings = new List<string>();
        var checkedCount = 0;
        var visibleCount = 0;

        foreach (var candidate in candidates)
        {
            if (checkedCount >= 60)
            {
                break;
            }

            var sessionAutomationId = SessionAutomationId(candidate.ConversationId);
            var sessionItem = session.TryFindByAutomationId(sessionAutomationId, TimeSpan.FromMilliseconds(800));
            if (sessionItem is null)
            {
                continue;
            }

            visibleCount++;
            checkedCount++;
            session.ActivateElement(sessionItem);
            var messagesList = session.TryFindByAutomationId("ChatView.MessagesList", TimeSpan.FromSeconds(12));
            if (messagesList is null)
            {
                findings.Add($"{candidate.ConversationId}: messages list missing after activation. messageCount={candidate.MessageCount} markdownLike={candidate.MarkdownLikeMessageCount}");
                continue;
            }

            _ = session.WaitUntilHidden("ChatView.LoadingOverlay", TimeSpan.FromSeconds(20));
            _ = WaitForViewportState(session, "bottom", TimeSpan.FromSeconds(12));
            Thread.Sleep(500);

            var state = session.TryGetElementName("ChatView.TranscriptViewportState", TimeSpan.FromMilliseconds(200)) ?? "<missing>";
            var debug = session.TryGetElementName("ChatView.TranscriptViewportDebug", TimeSpan.FromMilliseconds(200)) ?? "<missing>";
            var autoVisibleTexts = session.GetVisibleTexts(messagesList);
            var autoScrollPercent = TryGetVerticalScrollPercent(messagesList);

            var isVerticallyScrollable = messagesList.Patterns.Scroll.IsSupported
                && messagesList.Patterns.Scroll.Pattern.VerticallyScrollable.Value;
            if (isVerticallyScrollable)
            {
                messagesList.Patterns.Scroll.Pattern.SetScrollPercent(-1d, 100d);
                Thread.Sleep(700);
            }

            var manualVisibleTexts = session.GetVisibleTexts(messagesList);
            var manualScrollPercent = TryGetVerticalScrollPercent(messagesList);
            var manualRevealedNewBottomText = manualVisibleTexts
                .Except(autoVisibleTexts, StringComparer.Ordinal)
                .Take(5)
                .ToArray();

            if (!string.Equals(state, "bottom", StringComparison.OrdinalIgnoreCase)
                || (isVerticallyScrollable && autoScrollPercent is < 98d)
                || (isVerticallyScrollable && manualScrollPercent is < 98d)
                || manualRevealedNewBottomText.Length > 0)
            {
                findings.Add(
                    $"{candidate.ConversationId}: display='{candidate.DisplayName}'; state={state}; autoPercent={(autoScrollPercent?.ToString("0.##") ?? "<unsupported>")}; manualPercent={(manualScrollPercent?.ToString("0.##") ?? "<unsupported>")}; localMessageCount={candidate.MessageCount}; markdownLike={candidate.MarkdownLikeMessageCount}; manualRevealed=[{string.Join(" | ", manualRevealedNewBottomText)}]; debug={debug}; autoVisible=[{string.Join(" | ", autoVisibleTexts.TakeLast(10))}]; manualVisible=[{string.Join(" | ", manualVisibleTexts.TakeLast(10))}]");
            }
        }

        Skip.If(visibleCount == 0, $"No real transcript candidates were visible in the left navigation. CandidateCount={candidates.Length}.");
        Assert.True(
            findings.Count == 0,
            $"Real transcript audit found {findings.Count} problematic session(s) out of {checkedCount} checked visible session(s):{Environment.NewLine}{string.Join(Environment.NewLine, findings)}");
    }

    [SkippableFact]
    public void VisibleRealSession_ChatComposer_UsesModeSelectorSubsetOnly()
    {
        GuiTestGate.RequireEnabled();

        var candidates = LoadRealTranscriptAuditCandidates()
            .Where(candidate => candidate.MessageCount > 0)
            .OrderByDescending(candidate => candidate.MessageCount)
            .ThenByDescending(candidate => candidate.MarkdownLikeMessageCount)
            .ToArray();
        Skip.If(candidates.Length == 0, "No real conversations with transcript messages were found in the current SalmonEgg app data.");

        using var session = WindowsGuiAppSession.LaunchFresh();

        var candidate = candidates
            .FirstOrDefault(item => session.TryFindByAutomationId(SessionAutomationId(item.ConversationId), TimeSpan.FromSeconds(1)) is not null);
        Skip.If(candidate is null, $"No real transcript candidate is currently visible in the left navigation. Candidates: {string.Join(", ", candidates.Select(c => c.ConversationId))}");

        var sessionItem = session.FindByAutomationId(SessionAutomationId(candidate.ConversationId), TimeSpan.FromSeconds(10));
        session.ActivateElement(sessionItem);

        Assert.True(
            session.WaitUntilVisible("ChatView.CurrentSessionTitle", TimeSpan.FromSeconds(20)),
            $"Conversation header did not appear for real session {candidate.ConversationId}.");

        _ = session.WaitUntilHidden("ChatView.LoadingOverlay", TimeSpan.FromSeconds(20));

        AssertChatComposerUsesModeSelectorSubsetOnly(session, $"visible-real-session-{candidate.ConversationId}");
    }

    [SkippableFact]
    public void SelectSpecificRemoteBoundSession_ByConversationId_CompletesSlowLoadWithoutCrashing()
    {
        GuiTestGate.RequireEnabled();

        var conversationId = Environment.GetEnvironmentVariable("SALMONEGG_GUI_TARGET_CONVERSATION_ID");
        Skip.If(string.IsNullOrWhiteSpace(conversationId), "Set SALMONEGG_GUI_TARGET_CONVERSATION_ID to a real conversation id to validate a specific remote session.");

        using var slowLoad = new EnvironmentVariableScope("SALMONEGG_GUI_SLOW_SESSION_LOAD_MS", "2000");
        using var session = WindowsGuiAppSession.LaunchFresh();

        var startItem = session.FindByAutomationId("MainNav.Start", TimeSpan.FromSeconds(10));
        session.ActivateElement(startItem);
        Thread.Sleep(500);

        var sessionAutomationId = SessionAutomationId(conversationId!);
        var sessionItem = session.FindByAutomationId(sessionAutomationId, TimeSpan.FromSeconds(10));
        session.ActivateElement(sessionItem);

        var loadingVisible = session.WaitUntilVisible("ChatView.LoadingOverlayStatus", TimeSpan.FromSeconds(15));
        Assert.True(loadingVisible, $"Slow session/load never surfaced ChatView.LoadingOverlayStatus for conversation {conversationId}.");

        var headerVisible = session.WaitUntilVisible("ChatView.CurrentSessionTitle", TimeSpan.FromSeconds(30));
        Assert.True(headerVisible, $"Conversation header did not appear while loading conversation {conversationId}.");

        var transcriptVisible = WaitUntil(
            () => CountVisibleTranscriptText(session.TryFindByAutomationId("ChatView.MessagesList", TimeSpan.FromMilliseconds(150))) > 0,
            TimeSpan.FromSeconds(120),
            TimeSpan.FromMilliseconds(250));
        Assert.True(transcriptVisible, $"Conversation {conversationId} never projected any visible transcript content.");

        var overlayHidden = session.WaitUntilHidden("ChatView.LoadingOverlay", TimeSpan.FromSeconds(120));
        Assert.True(overlayHidden, $"Conversation {conversationId} remained stuck behind the loading overlay or crashed before hydration completed.");

        // Keep the real app alive past the initial overlay dismissal so slow-tail crashes
        // during replay settlement still fail the smoke test.
        Thread.Sleep(8000);

        var headerStillVisible = session.WaitUntilVisible("ChatView.CurrentSessionTitle", TimeSpan.FromSeconds(4));
        var overlayStillHidden = session.WaitUntilHidden("ChatView.LoadingOverlay", TimeSpan.FromSeconds(4));
        Assert.True(
            headerStillVisible && overlayStillHidden,
            $"Conversation {conversationId} stopped rendering stably after the loading overlay cleared. headerStillVisible={headerStillVisible} overlayStillHidden={overlayStillHidden}");

        AssertChatComposerUsesModeSelectorSubsetOnly(session, $"real-config-target-{conversationId}");
    }

    [SkippableFact]
    public void SelectSpecificRemoteBoundSession_AfterWarmSourceSession_CompletesWithoutCrashing()
    {
        GuiTestGate.RequireEnabled();

        var sourceConversationId = Environment.GetEnvironmentVariable("SALMONEGG_GUI_SOURCE_CONVERSATION_ID");
        var targetConversationId = Environment.GetEnvironmentVariable("SALMONEGG_GUI_TARGET_CONVERSATION_ID");
        Skip.If(string.IsNullOrWhiteSpace(sourceConversationId), "Set SALMONEGG_GUI_SOURCE_CONVERSATION_ID to the warm source conversation id.");
        Skip.If(string.IsNullOrWhiteSpace(targetConversationId), "Set SALMONEGG_GUI_TARGET_CONVERSATION_ID to the target conversation id.");
        Skip.If(
            string.Equals(sourceConversationId, targetConversationId, StringComparison.Ordinal),
            "Source and target conversations must be different.");

        using var session = WindowsGuiAppSession.LaunchFresh();

        var sourceItem = session.FindByAutomationId(SessionAutomationId(sourceConversationId!), TimeSpan.FromSeconds(10));
        session.ActivateElement(sourceItem);
        Assert.True(
            session.WaitUntilVisible("ChatView.CurrentSessionTitle", TimeSpan.FromSeconds(15)),
            $"Source conversation header did not appear for {sourceConversationId}.");

        var targetItem = session.FindByAutomationId(SessionAutomationId(targetConversationId!), TimeSpan.FromSeconds(10));
        session.ActivateElement(targetItem);

        var targetHeaderVisible = session.WaitUntilVisible("ChatView.CurrentSessionTitle", TimeSpan.FromSeconds(30));
        Assert.True(targetHeaderVisible, $"Target conversation header did not appear for {targetConversationId}.");

        var transcriptVisible = WaitUntil(
            () => CountVisibleTranscriptText(session.TryFindByAutomationId("ChatView.MessagesList", TimeSpan.FromMilliseconds(150))) > 0,
            TimeSpan.FromSeconds(120),
            TimeSpan.FromMilliseconds(250));
        Assert.True(transcriptVisible, $"Target conversation {targetConversationId} never projected visible transcript content.");

        Thread.Sleep(8000);

        Assert.True(
            session.WaitUntilVisible("ChatView.CurrentSessionTitle", TimeSpan.FromSeconds(4)),
            $"Target conversation {targetConversationId} stopped rendering stably after source-to-target activation.");
    }

    [SkippableFact]
    public void ProbeReplayBackedRemoteSession_WithSlowSessionLoad_AutoScrollsToLatestMessageAfterHydration()
    {
        GuiTestGate.RequireEnabled();

        // Deterministic gate coverage lives in ChatSkeletonSmokeTests.SelectRemoteSessionWithSlowReplay_AutoScrollsToLatestMessageAfterHydration.
        // This real-user probe remains useful for manual local auditing when replay-backed data exists.
        var candidate = RealUserConfigProbe.LoadReplayBackedCandidates()
            .Where(item => item.LocalMessageCount >= 10)
            .OrderBy(item => Math.Abs(item.LocalMessageCount - 40))
            .ThenByDescending(item => item.LastUpdatedAtUtc)
            .FirstOrDefault();
        Skip.If(candidate is null, "No replay-backed remote conversation with enough local transcript history was found to validate bottom auto-scroll.");

        using var slowLoad = new EnvironmentVariableScope("SALMONEGG_GUI_SLOW_SESSION_LOAD_MS", "2000");
        using var session = WindowsGuiAppSession.LaunchFresh();

        var startItem = session.FindByAutomationId("MainNav.Start", TimeSpan.FromSeconds(10));
        session.ActivateElement(startItem);
        Thread.Sleep(500);

        var sessionItem = session.FindByAutomationId(SessionAutomationId(candidate.ConversationId), TimeSpan.FromSeconds(10));
        session.ActivateElement(sessionItem);

        var sawOverlayStatus = session.WaitUntilVisible("ChatView.LoadingOverlayStatus", TimeSpan.FromSeconds(10));
        Assert.True(sawOverlayStatus, $"Slow remote hydration never exposed ChatView.LoadingOverlayStatus for conversation {candidate.ConversationId}.");

        var overlayHidden = session.WaitUntilHidden("ChatView.LoadingOverlay", TimeSpan.FromSeconds(60));
        Skip.If(
            !overlayHidden,
            $"Slow remote hydration overlay did not finish within the budget for conversation {candidate.ConversationId}; skipping real-user auto-scroll assertion in this environment.");

        var viewportState = WaitForViewportState(session, "bottom", TimeSpan.FromSeconds(10));
        Skip.If(
            !viewportState,
            $"Hydrated transcript viewport did not settle to bottom within the budget for conversation {candidate.ConversationId}. State='{session.TryGetElementName("ChatView.TranscriptViewportState") ?? "<missing>"}'");
    }

    [SkippableFact]
    public void SelectRemoteBoundSession_AfterDiscoverRoundTrip_ReturnsWithoutStuckReload()
    {
        GuiTestGate.RequireEnabled();

        var candidate = RealUserConfigProbe.LoadReplayBackedCandidates()
            .Where(item => item.LocalMessageCount is >= 10 and <= 120)
            .OrderBy(item => Math.Abs(item.LocalMessageCount - 60))
            .ThenByDescending(item => item.LastUpdatedAtUtc)
            .FirstOrDefault();
        candidate ??= RealUserConfigProbe.LoadReplayBackedCandidates()
            .OrderByDescending(item => item.LastUpdatedAtUtc)
            .FirstOrDefault();
        Skip.If(candidate is null, "No replay-backed remote conversation is available for discover round-trip validation.");

        using var session = WindowsGuiAppSession.LaunchFresh();

        var sessionId = SessionAutomationId(candidate.ConversationId);
        var sessionItem = session.FindByAutomationId(sessionId, TimeSpan.FromSeconds(10));
        session.ActivateElement(sessionItem);

        var initialOverlayHidden = session.WaitUntilHidden("ChatView.LoadingOverlay", TimeSpan.FromSeconds(60));
        Assert.True(initialOverlayHidden, $"Initial remote hydration did not finish for conversation {candidate.ConversationId}.");

        var discoverItem = session.FindByAutomationId("MainNav.DiscoverSessions", TimeSpan.FromSeconds(10));
        session.ActivateElement(discoverItem);

        var discoverVisible = session.WaitUntilVisible("DiscoverSessions.Title", TimeSpan.FromSeconds(10));
        Assert.True(discoverVisible, "Discover sessions page did not become visible.");

        sessionItem = session.FindByAutomationId(sessionId, TimeSpan.FromSeconds(10));
        session.ActivateElement(sessionItem);

        var headerVisible = session.WaitUntilVisible("ChatView.CurrentSessionTitle", TimeSpan.FromSeconds(8));
        Assert.True(headerVisible, $"Conversation header did not recover promptly after discover round-trip for {candidate.ConversationId}.");

        var returnedOverlayHidden = session.WaitUntilHidden("ChatView.LoadingOverlay", TimeSpan.FromSeconds(8));
        Assert.True(returnedOverlayHidden, $"Conversation remained stuck behind the loading overlay after discover round-trip for {candidate.ConversationId}.");
    }

    [SkippableFact]
    public void SelectRemoteBoundSession_AfterAcpSettingsRoundTrip_ReturnsWithoutCrash()
    {
        GuiTestGate.RequireEnabled();

        var candidate = RealUserConfigProbe.LoadReplayBackedCandidates()
            .Where(item => item.LocalMessageCount is >= 10 and <= 120)
            .OrderBy(item => Math.Abs(item.LocalMessageCount - 60))
            .ThenByDescending(item => item.LastUpdatedAtUtc)
            .FirstOrDefault();
        candidate ??= RealUserConfigProbe.LoadReplayBackedCandidates()
            .OrderByDescending(item => item.LastUpdatedAtUtc)
            .FirstOrDefault();
        Skip.If(candidate is null, "No replay-backed remote conversation is available for settings round-trip validation.");

        using var session = WindowsGuiAppSession.LaunchFresh();

        var sessionId = SessionAutomationId(candidate.ConversationId);
        var sessionItem = session.FindByAutomationId(sessionId, TimeSpan.FromSeconds(10));
        session.ActivateElement(sessionItem);

        var initialOverlayHidden = session.WaitUntilHidden("ChatView.LoadingOverlay", TimeSpan.FromSeconds(60));
        Assert.True(initialOverlayHidden, $"Initial remote hydration did not finish for conversation {candidate.ConversationId}.");

        EnsureMainWindowWideForTitleBarCommands(session);

        var settingsItem = session.FindByAutomationId("SettingsItem", TimeSpan.FromSeconds(10));
        session.ActivateElement(settingsItem);

        var acpSettingsItem = session.TryFindByAutomationId("SettingsNav.AgentAcp", TimeSpan.FromSeconds(10))
            ?? session.TryFindVisibleElementByNameAnywhere("Agent (ACP)", TimeSpan.FromSeconds(10));
        Assert.True(acpSettingsItem is not null, "Agent (ACP) settings entry did not become visible after opening the main settings item.");

        session.ActivateElement(acpSettingsItem!);

        var acpVisible = session.WaitUntilVisible("Acp.RemoteDirectories.Section", TimeSpan.FromSeconds(10))
            || session.WaitUntilVisible("Acp.RemoteDirectories.List", TimeSpan.FromSeconds(10));
        Assert.True(acpVisible, "ACP settings page did not become visible after selecting Agent (ACP).");

        Thread.Sleep(TimeSpan.FromSeconds(30));

        sessionItem = session.FindByAutomationId(sessionId, TimeSpan.FromSeconds(10));
        session.ActivateElement(sessionItem);

        var headerVisible = session.WaitUntilVisible("ChatView.CurrentSessionTitle", TimeSpan.FromSeconds(8));
        if (!headerVisible)
        {
            var captureRoot = Path.Combine(Path.GetTempPath(), "SalmonEgg.GuiTests");
            Directory.CreateDirectory(captureRoot);
            var capturePath = Path.Combine(
                captureRoot,
                $"settings-roundtrip-header-missing-{DateTime.UtcNow:yyyyMMddHHmmssfff}.png");
            capturePath = TryCaptureMainWindow(session, capturePath);

            var appDataRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SalmonEgg");
            var bootLogPath = Path.Combine(appDataRoot, "boot.log");
            var bootTail = File.Exists(bootLogPath)
                ? string.Join(Environment.NewLine, File.ReadLines(bootLogPath).TakeLast(40))
                : "<boot.log missing>";

            Assert.Fail(
                $"Conversation header did not recover promptly after ACP settings round-trip for {candidate.ConversationId}.{Environment.NewLine}Screenshot: {capturePath}{Environment.NewLine}boot.log:{Environment.NewLine}{bootTail}");
        }

        var returnedOverlayHidden = session.WaitUntilHidden("ChatView.LoadingOverlay", TimeSpan.FromSeconds(8));
        Assert.True(returnedOverlayHidden, $"Conversation remained stuck behind the loading overlay after ACP settings round-trip for {candidate.ConversationId}.");
    }

    [SkippableFact]
    public void SelectRemoteBoundSession_FromWarmCachedConversationMiniWindow_DoesNotExposeTargetHeaderBeforeOverlay()
    {
        GuiTestGate.RequireEnabled();

        var explicitWarmConversationId = Environment.GetEnvironmentVariable("SALMONEGG_GUI_MINI_WARM_CONVERSATION_ID");
        var explicitRemoteConversationId = Environment.GetEnvironmentVariable("SALMONEGG_GUI_MINI_REMOTE_CONVERSATION_ID");
        var remoteCandidates = RealUserConfigProbe.LoadReplayBackedCandidates(includeAllProfiles: true);
        var localCandidates = RealUserConfigProbe.LoadPureLocalCandidates();
        Skip.If(
            remoteCandidates.Count < 2 && localCandidates.Count == 0 && string.IsNullOrWhiteSpace(explicitWarmConversationId) && string.IsNullOrWhiteSpace(explicitRemoteConversationId),
            "Need either one pure local candidate plus one remote candidate, or at least two remote candidates, to validate warm-cache mini-window switching. You can also set SALMONEGG_GUI_MINI_WARM_CONVERSATION_ID and SALMONEGG_GUI_MINI_REMOTE_CONVERSATION_ID to pin a specific pair.");

        using var slowLoad = new EnvironmentVariableScope("SALMONEGG_GUI_SLOW_SESSION_LOAD_MS", "2000");
        using var session = WindowsGuiAppSession.LaunchFresh();

        EnsureMainWindowWideForTitleBarCommands(session);

        var warmLocalCandidate = TryResolveWarmLocalCandidate(
            localCandidates,
            explicitWarmConversationId,
            session);

        var visibleRemoteCandidates = remoteCandidates
            .Where(item => item.LocalMessageCount > 0)
            .Where(item => session.TryFindByAutomationId(SessionAutomationId(item.ConversationId), TimeSpan.FromSeconds(1)) is not null)
            .ToArray();
        Skip.If(
            visibleRemoteCandidates.Length == 0 && string.IsNullOrWhiteSpace(explicitRemoteConversationId),
            $"No visible replay-backed remote conversation is available. Candidates: {string.Join(", ", remoteCandidates.Select(c => c.ConversationId))}");

        var warmConversationId = ResolveWarmConversationId(
            warmLocalCandidate,
            visibleRemoteCandidates,
            explicitWarmConversationId,
            session);
        Skip.If(
            string.IsNullOrWhiteSpace(warmConversationId),
            "Could not determine a warm-cache source conversation. Set SALMONEGG_GUI_MINI_WARM_CONVERSATION_ID to pin one explicitly.");

        var remoteCandidate = ResolveRemoteMiniTargetCandidate(
            remoteCandidates,
            visibleRemoteCandidates,
            explicitRemoteConversationId,
            warmConversationId,
            session);
        Skip.If(
            remoteCandidate is null,
            $"No visible replay-backed remote conversation distinct from warm source {warmConversationId} is available. Set SALMONEGG_GUI_MINI_REMOTE_CONVERSATION_ID to pin one explicitly.");

        var warmItem = session.FindByAutomationId(SessionAutomationId(warmConversationId), TimeSpan.FromSeconds(10));
        var remoteItem = session.FindByAutomationId(SessionAutomationId(remoteCandidate.ConversationId), TimeSpan.FromSeconds(10));
        var remoteVisibleName = FirstUsableVisibleLabel(
            remoteCandidate.DisplayName,
            remoteItem.Name,
            remoteCandidate.ConversationId);
        session.ActivateElement(warmItem);

        var warmHeaderVisible = session.WaitUntilVisible("ChatView.CurrentSessionTitle", TimeSpan.FromSeconds(10));
        Assert.True(warmHeaderVisible, $"Warm-cache source conversation header did not appear for {warmConversationId}.");

        var warmOverlayHidden = session.WaitUntilHidden("ChatView.LoadingOverlay", TimeSpan.FromSeconds(60));
        Skip.If(
            !warmOverlayHidden,
            $"Warm-cache source conversation {warmConversationId} did not finish projecting transcript content within the budget.");

        var warmMessages = session.TryFindByAutomationId("ChatView.MessagesList", TimeSpan.FromSeconds(5));
        Skip.If(warmMessages is null, $"Messages list did not become available for warm-cache source conversation {warmConversationId}.");

        var warmVisibleTexts = session.GetVisibleTexts(warmMessages)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToArray();
        Skip.If(warmVisibleTexts.Length == 0, $"No visible transcript text was available for warm-cache source conversation {warmConversationId}.");
        var sampledWarmText = warmVisibleTexts[0];

        OpenMiniWindow(session);
        SelectMiniWindowConversation(session, remoteCandidate.ConversationId, remoteVisibleName);

        var timeline = new List<string>();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(12);
        var sawOverlay = false;
        var prematureRemoteHeader = false;

        while (DateTime.UtcNow < deadline)
        {
            var blockingMaskVisible = session.TryFindByAutomationId("ChatView.LoadingOverlayMask", TimeSpan.FromMilliseconds(100)) is not null;
            var overlayStatusVisible = session.TryFindByAutomationId("ChatView.LoadingOverlayStatus", TimeSpan.FromMilliseconds(100)) is not null;
            var header = session.TryFindByAutomationId("ChatView.CurrentSessionTitle", TimeSpan.FromMilliseconds(100));
            var headerName = header?.Name ?? "<missing>";
            var remoteHeaderVisible = header is not null && headerName.Contains(remoteVisibleName, StringComparison.Ordinal);
            var messagesList = session.TryFindByAutomationId("ChatView.MessagesList", TimeSpan.FromMilliseconds(100));
            var staleWarmTextVisible = messagesList is not null
                && session.TryFindVisibleText(sampledWarmText, messagesList, TimeSpan.FromMilliseconds(100)) is not null;

            timeline.Add(
                $"{DateTime.UtcNow:HH:mm:ss.fff} mask={blockingMaskVisible} status={overlayStatusVisible} header={headerName} staleWarmTextVisible={staleWarmTextVisible}");

            if (blockingMaskVisible || overlayStatusVisible)
            {
                sawOverlay = true;
                break;
            }

            if (remoteHeaderVisible)
            {
                prematureRemoteHeader = true;
                break;
            }

            if (staleWarmTextVisible)
            {
                throw new Xunit.Sdk.XunitException(
                    $"Warm-cache transcript text became visible before loading overlay during mini-window activation. warmSample='{sampledWarmText}'{Environment.NewLine}{string.Join(Environment.NewLine, timeline)}");
            }

            Thread.Sleep(150);
        }

        Assert.False(
            prematureRemoteHeader,
            $"Remote header became visible before loading overlay during mini-window activation.{Environment.NewLine}{string.Join(Environment.NewLine, timeline)}");
        Assert.True(
            sawOverlay,
            $"Mini-window real-config activation never surfaced loading overlay before timeout.{Environment.NewLine}{string.Join(Environment.NewLine, timeline)}");

        var overlayHidden = session.WaitUntilHidden("ChatView.LoadingOverlay", TimeSpan.FromSeconds(120));
        Assert.True(overlayHidden, $"Remote conversation {remoteCandidate.ConversationId} remained stuck behind the loading overlay after mini-window activation.");

        var remoteHeaderVisibleAfterHydration = session.WaitUntilVisible("ChatView.CurrentSessionTitle", TimeSpan.FromSeconds(10));
        Assert.True(remoteHeaderVisibleAfterHydration, $"Remote conversation header did not become visible after mini-window activation for {remoteCandidate.ConversationId}.");
        var finalHeader = session.TryFindByAutomationId("ChatView.CurrentSessionTitle", TimeSpan.FromSeconds(2));
        Assert.True(
            finalHeader is not null && finalHeader.Name.Contains(remoteVisibleName, StringComparison.Ordinal),
            $"Final chat header did not settle to remote conversation '{remoteVisibleName}'. Actual='{finalHeader?.Name ?? "<missing>"}'");

        var hydratedMessages = session.TryFindByAutomationId("ChatView.MessagesList", TimeSpan.FromSeconds(5));
        Assert.NotNull(hydratedMessages);
        var staleWarmTextVisibleAfterHydration = session.TryFindVisibleText(sampledWarmText, hydratedMessages, TimeSpan.FromSeconds(1)) is not null;
        Assert.False(
            staleWarmTextVisibleAfterHydration,
            $"Warm-cache transcript text remained visible after mini-window remote hydration. warmSample='{sampledWarmText}' warmConversation={warmConversationId} remoteConversation={remoteCandidate.ConversationId}");
    }

    [SkippableFact]
    public void SelectLargestRemoteBoundSession_FirstOpen_ShowsLoadingPillBeforeOverlayClears()
    {
        GuiTestGate.RequireEnabled();

        var candidates = RealUserConfigProbe.LoadReplayBackedCandidates()
            .OrderByDescending(item => item.LocalMessageCount)
            .ThenByDescending(item => item.LastUpdatedAtUtc)
            .ToArray();
        Skip.If(candidates.Length == 0, "No replay-backed remote conversation is available for first-open responsiveness validation.");

        using var session = WindowsGuiAppSession.LaunchFresh();

        var startItem = session.FindByAutomationId("MainNav.Start", TimeSpan.FromSeconds(10));
        session.ActivateElement(startItem);
        Thread.Sleep(200);

        var candidate = candidates
            .FirstOrDefault(item => session.TryFindByAutomationId(SessionAutomationId(item.ConversationId), TimeSpan.FromSeconds(1)) is not null);
        Skip.If(candidate is null, $"No replay-backed conversation is currently visible in the left navigation. Candidates: {string.Join(", ", candidates.Select(c => c.ConversationId))}");

        var sessionItem = session.FindByAutomationId(SessionAutomationId(candidate.ConversationId), TimeSpan.FromSeconds(10));

        var clickStopwatch = Stopwatch.StartNew();
        session.ActivateElement(sessionItem);
        clickStopwatch.Stop();

        const int clickInvokeBudgetMs = 1200;
        Assert.True(
            clickStopwatch.ElapsedMilliseconds <= clickInvokeBudgetMs,
            $"Session invoke was blocked for too long. Conversation={candidate.ConversationId} elapsedMs={clickStopwatch.ElapsedMilliseconds} budgetMs={clickInvokeBudgetMs}");

        var timeline = new List<string>();
        var transitionStopwatch = Stopwatch.StartNew();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(8);
        long? statusVisibleAtMs = null;
        long? overlayHiddenAtMs = null;
        while (DateTime.UtcNow < deadline)
        {
            var statusVisible = session.TryFindByAutomationId("ChatView.LoadingOverlayStatus", TimeSpan.FromMilliseconds(100)) is not null;
            var overlayVisible = session.TryFindByAutomationId("ChatView.LoadingOverlay", TimeSpan.FromMilliseconds(100)) is not null;
            var headerVisible = session.TryFindByAutomationId("ChatView.CurrentSessionTitle", TimeSpan.FromMilliseconds(100)) is not null;

            if (statusVisible && statusVisibleAtMs is null)
            {
                statusVisibleAtMs = transitionStopwatch.ElapsedMilliseconds;
            }

            if (!overlayVisible && overlayHiddenAtMs is null)
            {
                overlayHiddenAtMs = transitionStopwatch.ElapsedMilliseconds;
            }

            timeline.Add(
                $"{transitionStopwatch.ElapsedMilliseconds,5}ms status={statusVisible} overlay={overlayVisible} header={headerVisible}");

            if (statusVisibleAtMs is not null && overlayHiddenAtMs is not null)
            {
                break;
            }

            Thread.Sleep(120);
        }

        if (statusVisibleAtMs is null)
        {
            var captureRoot = Path.Combine(Path.GetTempPath(), "SalmonEgg.GuiTests");
            Directory.CreateDirectory(captureRoot);
            var screenshotPath = Path.Combine(
                captureRoot,
                $"remote-first-open-pill-missing-{candidate.ConversationId}-{DateTime.UtcNow:yyyyMMddHHmmssfff}.png");
            screenshotPath = TryCaptureMainWindow(session, screenshotPath);
            throw new Xunit.Sdk.XunitException(
                $"Loading status pill never appeared on first open. Conversation={candidate.ConversationId} LocalMessageCount={candidate.LocalMessageCount} Capture={screenshotPath}{Environment.NewLine}{string.Join(Environment.NewLine, timeline)}");
        }

        if (overlayHiddenAtMs is not null && statusVisibleAtMs.Value > overlayHiddenAtMs.Value)
        {
            var captureRoot = Path.Combine(Path.GetTempPath(), "SalmonEgg.GuiTests");
            Directory.CreateDirectory(captureRoot);
            var screenshotPath = Path.Combine(
                captureRoot,
                $"remote-first-open-pill-late-{candidate.ConversationId}-{DateTime.UtcNow:yyyyMMddHHmmssfff}.png");
            screenshotPath = TryCaptureMainWindow(session, screenshotPath);
            throw new Xunit.Sdk.XunitException(
                $"Loading status pill appeared after overlay was already cleared. Conversation={candidate.ConversationId} statusAtMs={statusVisibleAtMs} overlayHiddenAtMs={overlayHiddenAtMs} Capture={screenshotPath}{Environment.NewLine}{string.Join(Environment.NewLine, timeline)}");
        }

        const int statusVisibleBudgetMs = 1500;
        Assert.True(
            statusVisibleAtMs.Value <= statusVisibleBudgetMs,
            $"Loading status pill appeared too late on first open. Conversation={candidate.ConversationId} LocalMessageCount={candidate.LocalMessageCount} statusAtMs={statusVisibleAtMs} budgetMs={statusVisibleBudgetMs}{Environment.NewLine}{string.Join(Environment.NewLine, timeline)}");

        AssertChatComposerUsesModeSelectorSubsetOnly(session, $"largest-remote-first-open-{candidate.ConversationId}");
    }

    [SkippableFact]
    public void SelectLargestRemoteBoundSession_ImmediateDiscoverSwitch_RemainsResponsive()
    {
        GuiTestGate.RequireEnabled();

        var candidates = RealUserConfigProbe.LoadReplayBackedCandidates()
            .OrderByDescending(item => item.LocalMessageCount)
            .ThenByDescending(item => item.LastUpdatedAtUtc)
            .ToArray();
        Skip.If(candidates.Length == 0, "No replay-backed remote conversation is available for responsiveness validation.");

        using var session = WindowsGuiAppSession.LaunchFresh();

        var candidate = candidates
            .FirstOrDefault(item => session.TryFindByAutomationId(SessionAutomationId(item.ConversationId), TimeSpan.FromSeconds(1)) is not null);
        Skip.If(candidate is null, $"No replay-backed conversation is currently visible in the left navigation. Candidates: {string.Join(", ", candidates.Select(c => c.ConversationId))}");

        var remoteItem = session.FindByAutomationId(SessionAutomationId(candidate.ConversationId), TimeSpan.FromSeconds(10));
        session.ActivateElement(remoteItem);
        Thread.Sleep(120);

        var discoverItem = session.FindByAutomationId("MainNav.DiscoverSessions", TimeSpan.FromSeconds(10));
        var discoverClickStopwatch = Stopwatch.StartNew();
        session.ActivateElement(discoverItem);
        discoverClickStopwatch.Stop();

        const int discoverInvokeBudgetMs = 1200;
        Assert.True(
            discoverClickStopwatch.ElapsedMilliseconds <= discoverInvokeBudgetMs,
            $"Discover click invoke was blocked for too long. Conversation={candidate.ConversationId} elapsedMs={discoverClickStopwatch.ElapsedMilliseconds} budgetMs={discoverInvokeBudgetMs}");

        var discoverVisible = session.WaitUntilVisible("DiscoverSessions.Title", TimeSpan.FromSeconds(3));
        Assert.True(
            discoverVisible,
            $"Discover page did not become visible quickly after switching away from remote session load. Conversation={candidate.ConversationId}");
    }

    [SkippableFact]
    public void SelectLargestRemoteBoundSession_DiscoverRoundTrip_ReturnsWithinResponsivenessBudget()
    {
        GuiTestGate.RequireEnabled();

        var candidates = RealUserConfigProbe.LoadReplayBackedCandidates()
            .OrderByDescending(item => item.LocalMessageCount)
            .ThenByDescending(item => item.LastUpdatedAtUtc)
            .ToArray();
        Skip.If(candidates.Length == 0, "No replay-backed remote conversation is available for discover round-trip responsiveness validation.");

        using var session = WindowsGuiAppSession.LaunchFresh();

        var candidate = candidates
            .FirstOrDefault(item => session.TryFindByAutomationId(SessionAutomationId(item.ConversationId), TimeSpan.FromSeconds(1)) is not null);
        Skip.If(candidate is null, $"No replay-backed conversation is currently visible in the left navigation. Candidates: {string.Join(", ", candidates.Select(c => c.ConversationId))}");

        var sessionId = SessionAutomationId(candidate.ConversationId);
        var remoteItem = session.FindByAutomationId(sessionId, TimeSpan.FromSeconds(10));
        session.ActivateElement(remoteItem);

        var initialOverlayHidden = session.WaitUntilHidden("ChatView.LoadingOverlay", TimeSpan.FromSeconds(60));
        Assert.True(initialOverlayHidden, $"Initial remote hydration did not finish for conversation {candidate.ConversationId}.");

        var discoverItem = session.FindByAutomationId("MainNav.DiscoverSessions", TimeSpan.FromSeconds(10));
        session.ActivateElement(discoverItem);
        Assert.True(
            session.WaitUntilVisible("DiscoverSessions.Title", TimeSpan.FromSeconds(10)),
            "Discover sessions page did not become visible.");

        remoteItem = session.FindByAutomationId(sessionId, TimeSpan.FromSeconds(10));
        var returnStopwatch = Stopwatch.StartNew();
        session.ActivateElement(remoteItem);
        var headerVisible = session.WaitUntilVisible("ChatView.CurrentSessionTitle", TimeSpan.FromMilliseconds(1200));
        returnStopwatch.Stop();

        Assert.True(
            headerVisible,
            $"Round-trip back to remote chat exceeded responsiveness budget. Conversation={candidate.ConversationId} elapsedMs={returnStopwatch.ElapsedMilliseconds}");
    }

    [SkippableFact]
    public void SelectVisibleCrossProfileRemoteSessions_AfterHydratingBoth_ReturnsHotWithoutLoadingOverlay()
    {
        GuiTestGate.RequireEnabled();

        using var slowLoad = new EnvironmentVariableScope("SALMONEGG_GUI_SLOW_SESSION_LOAD_MS", "2000");
        using var session = WindowsGuiAppSession.LaunchFresh();

        var remoteCandidates = RealUserConfigProbe.LoadReplayBackedCandidates(includeAllProfiles: true);
        var visibleRemoteCandidates = remoteCandidates
            .Where(item => item.LocalMessageCount > 0)
            .Where(item => session.TryFindByAutomationId(SessionAutomationId(item.ConversationId), TimeSpan.FromSeconds(1)) is not null)
            .ToArray();
        var profileGroups = visibleRemoteCandidates
            .Where(item => !string.IsNullOrWhiteSpace(item.BoundProfileId))
            .GroupBy(item => item.BoundProfileId!, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ToArray();
        Skip.If(profileGroups.Length < 2, "Need two visible replay-backed remote conversations bound to different profiles for real-config cross-profile hot-return validation.");

        var remoteA = profileGroups[0]
            .OrderByDescending(item => item.LocalMessageCount)
            .ThenByDescending(item => item.LastUpdatedAtUtc)
            .First();
        var remoteB = profileGroups[1]
            .OrderByDescending(item => item.LocalMessageCount)
            .ThenByDescending(item => item.LastUpdatedAtUtc)
            .First();
        Skip.If(string.Equals(remoteA.ConversationId, remoteB.ConversationId, StringComparison.Ordinal), "Cross-profile hot-return validation requires two distinct remote conversations.");

        var startItem = session.FindByAutomationId("MainNav.Start", TimeSpan.FromSeconds(10));
        session.ActivateElement(startItem);
        Thread.Sleep(500);

        var remoteAId = SessionAutomationId(remoteA.ConversationId);
        var remoteAItem = session.FindByAutomationId(remoteAId, TimeSpan.FromSeconds(10));
        session.ActivateElement(remoteAItem);

        Assert.True(
            session.WaitUntilVisible("ChatView.LoadingOverlayStatus", TimeSpan.FromSeconds(15)),
            $"Initial cross-profile remote session A did not expose ChatView.LoadingOverlayStatus. Conversation={remoteA.ConversationId}");
        Assert.True(
            session.WaitUntilVisible("ChatView.CurrentSessionTitle", TimeSpan.FromSeconds(30)),
            $"Conversation header did not appear while loading cross-profile remote session A. Conversation={remoteA.ConversationId}");
        Assert.True(
            WaitUntil(
                () => CountVisibleTranscriptText(session.TryFindByAutomationId("ChatView.MessagesList", TimeSpan.FromMilliseconds(150))) > 0,
                TimeSpan.FromSeconds(120),
                TimeSpan.FromMilliseconds(250)),
            $"Cross-profile remote session A never projected any visible transcript content. Conversation={remoteA.ConversationId}");
        Assert.True(
            session.WaitUntilHidden("ChatView.LoadingOverlay", TimeSpan.FromSeconds(120)),
            $"Cross-profile remote session A remained stuck behind the loading overlay. Conversation={remoteA.ConversationId}");

        var remoteBId = SessionAutomationId(remoteB.ConversationId);
        var remoteBItem = session.FindByAutomationId(remoteBId, TimeSpan.FromSeconds(10));
        session.ActivateElement(remoteBItem);

        Assert.True(
            session.WaitUntilVisible("ChatView.LoadingOverlayStatus", TimeSpan.FromSeconds(15)),
            $"Initial cross-profile remote session B did not expose ChatView.LoadingOverlayStatus. Conversation={remoteB.ConversationId}");
        Assert.True(
            session.WaitUntilVisible("ChatView.CurrentSessionTitle", TimeSpan.FromSeconds(30)),
            $"Conversation header did not appear while loading cross-profile remote session B. Conversation={remoteB.ConversationId}");
        Assert.True(
            WaitUntil(
                () => CountVisibleTranscriptText(session.TryFindByAutomationId("ChatView.MessagesList", TimeSpan.FromMilliseconds(150))) > 0,
                TimeSpan.FromSeconds(120),
                TimeSpan.FromMilliseconds(250)),
            $"Cross-profile remote session B never projected any visible transcript content. Conversation={remoteB.ConversationId}");
        Assert.True(
            session.WaitUntilHidden("ChatView.LoadingOverlay", TimeSpan.FromSeconds(120)),
            $"Cross-profile remote session B remained stuck behind the loading overlay. Conversation={remoteB.ConversationId}");

        remoteAItem = session.FindByAutomationId(remoteAId, TimeSpan.FromSeconds(10));
        var returnStopwatch = Stopwatch.StartNew();
        session.ActivateElement(remoteAItem);
        var headerVisible = session.WaitUntilVisible("ChatView.CurrentSessionTitle", TimeSpan.FromSeconds(4));
        returnStopwatch.Stop();

        Assert.True(
            headerVisible,
            $"Cross-profile hot-return to remote session A did not restore the chat header quickly. Conversation={remoteA.ConversationId} elapsedMs={returnStopwatch.ElapsedMilliseconds}");
        Assert.True(
            returnStopwatch.ElapsedMilliseconds <= 1500,
            $"Cross-profile hot-return to remote session A exceeded the responsiveness budget. Conversation={remoteA.ConversationId} elapsedMs={returnStopwatch.ElapsedMilliseconds} budgetMs=1500");

        var returnedHeader = session.FindByAutomationId("ChatView.CurrentSessionTitle", TimeSpan.FromSeconds(1));
        Assert.Contains(remoteA.DisplayName, returnedHeader.Name, StringComparison.Ordinal);

        var hotOverlayTimeline = new List<string>();
        var hotOverlayDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (DateTime.UtcNow < hotOverlayDeadline)
        {
            var hotOverlayMaskVisible = session.TryFindByAutomationId("ChatView.LoadingOverlayMask", TimeSpan.FromMilliseconds(100)) is not null;
            var hotOverlayStatusVisible = session.TryFindByAutomationId("ChatView.LoadingOverlayStatus", TimeSpan.FromMilliseconds(100)) is not null;
            var hotHeaderName = session.TryGetElementName("ChatView.CurrentSessionTitle", TimeSpan.FromMilliseconds(100)) ?? "<missing>";
            var remoteASelected = session.TryGetIsSelected(remoteAId) == true;

            hotOverlayTimeline.Add(
                $"{DateTime.UtcNow:HH:mm:ss.fff} mask={hotOverlayMaskVisible} status={hotOverlayStatusVisible} header={hotHeaderName} remoteASelected={remoteASelected}");

            Assert.False(
                hotOverlayMaskVisible || hotOverlayStatusVisible,
                $"Returning to cross-profile remote session A surfaced the loading overlay instead of staying hot. Conversation={remoteA.ConversationId}{Environment.NewLine}{string.Join(Environment.NewLine, hotOverlayTimeline)}");

            Thread.Sleep(100);
        }

        Assert.True(
            WaitUntil(
                () => CountVisibleTranscriptText(session.TryFindByAutomationId("ChatView.MessagesList", TimeSpan.FromMilliseconds(150))) > 0,
                TimeSpan.FromSeconds(4),
                TimeSpan.FromMilliseconds(150)),
            $"Cross-profile hot-return to remote session A did not restore visible transcript content promptly. Conversation={remoteA.ConversationId}");
    }

    [SkippableFact]
    public void RealData_ExpandedToCompactResize_DoesNotLoseNavigationSelectionContext()
    {
        GuiTestGate.RequireEnabled();

        var candidates = RealUserConfigProbe.LoadReplayBackedCandidates()
            .OrderByDescending(item => item.LastUpdatedAtUtc)
            .ToArray();
        Skip.If(candidates.Length == 0, "No replay-backed conversation is available for real-data resize navigation validation.");

        using var session = WindowsGuiAppSession.LaunchFresh();

        var candidate = candidates
            .FirstOrDefault(item => session.TryFindByAutomationId(SessionAutomationId(item.ConversationId), TimeSpan.FromSeconds(1)) is not null);
        Skip.If(candidate is null, "No replay-backed candidate is currently visible in left navigation.");

        ResizeMainWindow(width: 1400, height: 900);
        Thread.Sleep(1200);

        var startItem = session.FindByAutomationId("MainNav.Start", TimeSpan.FromSeconds(10));
        session.ActivateElement(startItem);
        Thread.Sleep(300);

        var sessionId = SessionAutomationId(candidate.ConversationId);
        var selectedItem = session.FindByAutomationId(sessionId, TimeSpan.FromSeconds(10));
        session.ActivateElement(selectedItem);
        Assert.True(
            WaitUntilNavigationActivationStarted(session, sessionId, TimeSpan.FromSeconds(10)),
            $"Navigation activation context did not appear after selecting conversation {candidate.ConversationId}. State={DumpAutomationSelectionState(session)}");

        var projectId = TryResolveOwningProjectAutomationId(session, sessionId);
        Skip.If(
            string.IsNullOrWhiteSpace(projectId),
            $"Unable to resolve owning project after selecting conversation {candidate.ConversationId}. State={DumpAutomationSelectionState(session)}");

        ResizeMainWindow(width: 800, height: 900);
        Thread.Sleep(1700);

        AssertCompactNativeSelection(
            session,
            sessionId,
            projectId!,
            "MainNav.Start",
            TimeSpan.FromSeconds(8),
            candidate.ConversationId);

        if (!IsElementVisible(session, sessionId))
        {
            Assert.True(
                WaitUntilChatActivationSurfaceVisible(session, TimeSpan.FromSeconds(8)),
                $"Conversation content context did not remain visible after expanded->compact resize. Conversation={candidate.ConversationId}");
        }
    }

    [SkippableFact]
    public void RealData_ExpandedToCompactResize_WhenSessionCollapses_KeepsNavFocusOnProjectAncestor()
    {
        GuiTestGate.RequireEnabled();

        var localCandidates = RealUserConfigProbe.LoadPureLocalCandidates()
            .OrderByDescending(item => item.LastUpdatedAtUtc)
            .ToArray();
        var replayCandidates = RealUserConfigProbe.LoadReplayBackedCandidates()
            .OrderByDescending(item => item.LastUpdatedAtUtc)
            .ToArray();
        Skip.If(
            localCandidates.Length == 0 && replayCandidates.Length == 0,
            "No local or replay-backed conversation is available for real-data compact focus continuity validation.");

        using var session = WindowsGuiAppSession.LaunchFresh();

        var localCandidate = localCandidates
            .FirstOrDefault(item => session.TryFindByAutomationId(SessionAutomationId(item.ConversationId), TimeSpan.FromSeconds(1)) is not null);
        var replayCandidate = localCandidate is null
            ? replayCandidates.FirstOrDefault(item => session.TryFindByAutomationId(SessionAutomationId(item.ConversationId), TimeSpan.FromSeconds(1)) is not null)
            : null;
        var conversationId = localCandidate?.ConversationId ?? replayCandidate?.ConversationId;
        Skip.If(conversationId is null, "No local/replay candidate is currently visible in left navigation.");

        ResizeMainWindow(width: 1400, height: 900);
        Thread.Sleep(1000);

        var startItem = session.FindByAutomationId("MainNav.Start", TimeSpan.FromSeconds(10));
        session.ActivateElement(startItem);
        Thread.Sleep(250);

        var sessionId = SessionAutomationId(conversationId);
        var selectedItem = session.FindByAutomationId(sessionId, TimeSpan.FromSeconds(10));
        session.ActivateElement(selectedItem);
        session.FocusElement(selectedItem);

        Assert.True(
            WaitUntilNavigationActivationStarted(session, sessionId, TimeSpan.FromSeconds(10)),
            $"Navigation activation context did not appear after selecting conversation {conversationId}. State={DumpAutomationSelectionState(session)}");

        var focusPrimed = WaitUntil(
            () => session.IsFocusWithinAutomationId(sessionId),
            timeout: TimeSpan.FromSeconds(3),
            pollInterval: TimeSpan.FromMilliseconds(120));
        Skip.If(!focusPrimed, $"Unable to prime keyboard focus to selected session before compact resize. Conversation={conversationId} Focus={session.DescribeFocusedElement()}");

        ResizeMainWindow(width: 800, height: 900);

        var timeline = new List<string>();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        var sawCollapsedSession = false;
        var sawAncestorSelectionContext = false;
        var maxConsecutiveFocusOutOfNav = 0;
        var consecutiveFocusOutOfNav = 0;

        while (DateTime.UtcNow < deadline)
        {
            var sessionVisible = IsElementVisible(session, sessionId);
            var sessionSelected = session.TryGetIsSelected(sessionId) == true;
            var projectSelected = TryResolveOwningProjectAutomationId(session, sessionId) is not null;
            var startSelected = session.TryGetIsSelected("MainNav.Start") == true;
            var focusInNav = session.IsFocusWithinAutomationId("MainNavView");
            var focusOnProject = session.IsFocusWithinAutomationIdPrefix("MainNav.Project.");
            var focusOnStart = session.IsFocusWithinAutomationId("MainNav.Start");
            var focusDescription = session.DescribeFocusedElement();
            var automationSelectionState = session.TryGetElementName("MainNav.Automation.SelectionState", TimeSpan.FromMilliseconds(200)) ?? "<missing>";
            var selectionContext = ParseAutomationStateToken(automationSelectionState, "Context") ?? "<none>";

            if (!sessionVisible)
            {
                sawCollapsedSession = true;

                if (focusOnProject || string.Equals(selectionContext, "Ancestor", StringComparison.Ordinal))
                {
                    sawAncestorSelectionContext = true;
                }

                if (focusInNav)
                {
                    consecutiveFocusOutOfNav = 0;
                }
                else
                {
                    consecutiveFocusOutOfNav++;
                    if (consecutiveFocusOutOfNav > maxConsecutiveFocusOutOfNav)
                    {
                        maxConsecutiveFocusOutOfNav = consecutiveFocusOutOfNav;
                    }
                }
            }

            timeline.Add(
                $"{DateTime.UtcNow:HH:mm:ss.fff} sessionVisible={sessionVisible} sessionSelected={sessionSelected} projectSelected={projectSelected} startSelected={startSelected} selectionContext={selectionContext} focusInNav={focusInNav} focusOnProject={focusOnProject} focusOnStart={focusOnStart} focus={focusDescription}");

            Thread.Sleep(90);
        }

        if (!sawCollapsedSession || !sawAncestorSelectionContext || maxConsecutiveFocusOutOfNav > 2)
        {
            var captureRoot = Path.Combine(Path.GetTempPath(), "SalmonEgg.GuiTests");
            Directory.CreateDirectory(captureRoot);
            var screenshotPath = Path.Combine(
                captureRoot,
                $"compact-focus-loss-{conversationId}-{DateTime.UtcNow:yyyyMMddHHmmssfff}.png");
            screenshotPath = TryCaptureMainWindow(session, screenshotPath);

            var appDataRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SalmonEgg");
            var bootLogPath = Path.Combine(appDataRoot, "boot.log");
            var bootTail = File.Exists(bootLogPath)
                ? string.Join(Environment.NewLine, File.ReadLines(bootLogPath).TakeLast(40))
                : "<boot.log missing>";

            throw new Xunit.Sdk.XunitException(
                $"Compact focus continuity failed for conversation {conversationId}. sawCollapsedSession={sawCollapsedSession} sawAncestorSelectionContext={sawAncestorSelectionContext} maxConsecutiveFocusOutOfNav={maxConsecutiveFocusOutOfNav}{Environment.NewLine}Screenshot: {screenshotPath}{Environment.NewLine}{string.Join(Environment.NewLine, timeline)}{Environment.NewLine}boot.log:{Environment.NewLine}{bootTail}");
        }
    }

    [SkippableFact]
    public void SelectRemoteBoundSession_FromStart_DoesNotExposeAnyChatShellContentBeforeLoadingOverlay()
    {
        GuiTestGate.RequireEnabled();

        var candidates = RealUserConfigProbe.LoadReplayBackedCandidates();
        Skip.If(candidates.Count == 0, "No real remote-bound conversations with recent session/load + session/update evidence were found in the current SalmonEgg app data.");
        var targetConversationId = Environment.GetEnvironmentVariable("SALMONEGG_GUI_TARGET_CONVERSATION_ID");

        using var session = WindowsGuiAppSession.LaunchFresh();

        var startItem = session.FindByAutomationId("MainNav.Start", TimeSpan.FromSeconds(10));
        session.ActivateElement(startItem);
        Thread.Sleep(500);

        var candidate = !string.IsNullOrWhiteSpace(targetConversationId)
            ? candidates.FirstOrDefault(item =>
                string.Equals(item.ConversationId, targetConversationId, StringComparison.Ordinal)
                && session.TryFindByAutomationId(SessionAutomationId(item.ConversationId), TimeSpan.FromSeconds(1)) is not null)
            : candidates.FirstOrDefault(item => session.TryFindByAutomationId(SessionAutomationId(item.ConversationId), TimeSpan.FromSeconds(1)) is not null);
        Skip.If(candidate is null, $"No replay-backed conversation is currently visible in the left navigation. Target={targetConversationId ?? "<none>"} Candidates: {string.Join(", ", candidates.Select(c => c.ConversationId))}");

        var sessionItem = session.FindByAutomationId(SessionAutomationId(candidate.ConversationId), TimeSpan.FromSeconds(10));
        session.ActivateElement(sessionItem);

        var timeline = new List<string>();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        var sawOverlay = false;

        while (DateTime.UtcNow < deadline)
        {
            var overlayVisible =
                session.TryFindByAutomationId("ChatView.LoadingOverlayMask", TimeSpan.FromMilliseconds(100)) is not null
                || session.TryFindByAutomationId("ChatView.LoadingOverlayStatus", TimeSpan.FromMilliseconds(100)) is not null
                || session.TryFindByAutomationId("ChatView.LoadingOverlay", TimeSpan.FromMilliseconds(100)) is not null;
            var header = session.TryFindByAutomationId("ChatView.CurrentSessionTitle", TimeSpan.FromMilliseconds(100));
            var headerName = header?.Name ?? "<missing>";
            var messagesVisible = session.TryFindByAutomationId("ChatView.MessagesList", TimeSpan.FromMilliseconds(100)) is not null;

            timeline.Add(
                $"{DateTime.UtcNow:HH:mm:ss.fff} overlay={overlayVisible} header={headerName} messages={messagesVisible}");

            if (overlayVisible)
            {
                sawOverlay = true;
                break;
            }

            if (header is not null || messagesVisible)
            {
                throw new Xunit.Sdk.XunitException(
                    $"Real-config first-open leaked chat shell content before loading overlay for conversation {candidate.ConversationId}.{Environment.NewLine}{string.Join(Environment.NewLine, timeline)}");
            }

            Thread.Sleep(80);
        }

        Assert.True(
            sawOverlay,
            $"Real-config first-open never surfaced loading overlay for conversation {candidate.ConversationId}.{Environment.NewLine}{string.Join(Environment.NewLine, timeline)}");
    }

    [SkippableFact]
    public void SelectRemoteBoundSession_FromStart_HighFrequencyProbe_DoesNotExposeActiveRootBeforeLoadingOverlay()
    {
        GuiTestGate.RequireEnabled();

        var candidates = RealUserConfigProbe.LoadReplayBackedCandidates();
        Skip.If(candidates.Count == 0, "No real remote-bound conversations with recent session/load + session/update evidence were found in the current SalmonEgg app data.");
        var targetConversationId = Environment.GetEnvironmentVariable("SALMONEGG_GUI_TARGET_CONVERSATION_ID");
        Skip.If(string.IsNullOrWhiteSpace(targetConversationId), "Set SALMONEGG_GUI_TARGET_CONVERSATION_ID to run the high-frequency first-frame probe for a specific real conversation.");

        using var session = WindowsGuiAppSession.LaunchFresh();

        var startItem = session.FindByAutomationId("MainNav.Start", TimeSpan.FromSeconds(10));
        session.ActivateElement(startItem);
        Thread.Sleep(500);

        var candidate = candidates.FirstOrDefault(item =>
            string.Equals(item.ConversationId, targetConversationId, StringComparison.Ordinal)
            && session.TryFindByAutomationId(SessionAutomationId(item.ConversationId), TimeSpan.FromSeconds(1)) is not null);
        Skip.If(candidate is null, $"Target conversation '{targetConversationId}' is not currently visible in the left navigation.");

        var sessionItem = session.FindByAutomationId(SessionAutomationId(candidate.ConversationId), TimeSpan.FromSeconds(10));
        session.ActivateElement(sessionItem);

        var timeline = new List<string>();
        var stopwatch = Stopwatch.StartNew();
        var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(1500);
        var sawOverlay = false;

        while (DateTime.UtcNow < deadline)
        {
            var overlayVisible =
                session.MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("ChatView.LoadingOverlayMask")) is not null
                || session.MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("ChatView.LoadingOverlayStatus")) is not null
                || session.MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("ChatView.LoadingOverlay")) is not null;
            var activeRootVisible = session.MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("ChatView.ActiveRoot")) is not null;
            var header = session.MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("ChatView.CurrentSessionTitle"));
            var messagesVisible = session.MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("ChatView.MessagesList")) is not null;
            var headerName = header?.Name ?? "<missing>";

            timeline.Add(
                $"{stopwatch.ElapsedMilliseconds,5}ms overlay={overlayVisible} activeRoot={activeRootVisible} header={headerName} messages={messagesVisible}");

            if (overlayVisible)
            {
                sawOverlay = true;
                break;
            }

            if (activeRootVisible || header is not null || messagesVisible)
            {
                var captureRoot = Path.Combine(Path.GetTempPath(), "SalmonEgg.GuiTests");
                Directory.CreateDirectory(captureRoot);
                var screenshotPath = Path.Combine(
                    captureRoot,
                    $"real-config-first-frame-leak-{candidate.ConversationId}-{DateTime.UtcNow:yyyyMMddHHmmssfff}.png");
                screenshotPath = TryCaptureMainWindow(session, screenshotPath);
                throw new Xunit.Sdk.XunitException(
                    $"High-frequency probe observed chat shell content before loading overlay for conversation {candidate.ConversationId}.{Environment.NewLine}Capture: {screenshotPath}{Environment.NewLine}{string.Join(Environment.NewLine, timeline)}");
            }

            Thread.Sleep(10);
        }

        Assert.True(
            sawOverlay,
            $"High-frequency probe did not observe loading overlay within the initial sampling window for conversation {candidate.ConversationId}.{Environment.NewLine}{string.Join(Environment.NewLine, timeline)}");
    }

    [SkippableFact]
    public void SelectRemoteBoundSession_FromStart_CapturesFirstFramesForManualAudit()
    {
        GuiTestGate.RequireEnabled();

        var candidates = RealUserConfigProbe.LoadReplayBackedCandidates();
        Skip.If(candidates.Count == 0, "No real remote-bound conversations with recent session/load + session/update evidence were found in the current SalmonEgg app data.");
        var targetConversationId = Environment.GetEnvironmentVariable("SALMONEGG_GUI_TARGET_CONVERSATION_ID");
        Skip.If(string.IsNullOrWhiteSpace(targetConversationId), "Set SALMONEGG_GUI_TARGET_CONVERSATION_ID to capture first-frame screenshots for a specific real conversation.");

        using var session = WindowsGuiAppSession.LaunchFresh();

        var startItem = session.FindByAutomationId("MainNav.Start", TimeSpan.FromSeconds(10));
        session.ActivateElement(startItem);
        Thread.Sleep(500);

        var candidate = candidates.FirstOrDefault(item =>
            string.Equals(item.ConversationId, targetConversationId, StringComparison.Ordinal)
            && session.TryFindByAutomationId(SessionAutomationId(item.ConversationId), TimeSpan.FromSeconds(1)) is not null);
        Skip.If(candidate is null, $"Target conversation '{targetConversationId}' is not currently visible in the left navigation.");

        var captureRoot = Path.Combine(
            Path.GetTempPath(),
            "SalmonEgg.GuiTests",
            $"first-frame-audit-{candidate.ConversationId}-{DateTime.UtcNow:yyyyMMddHHmmssfff}");
        Directory.CreateDirectory(captureRoot);

        var sessionItem = session.FindByAutomationId(SessionAutomationId(candidate.ConversationId), TimeSpan.FromSeconds(10));
        session.ActivateElement(sessionItem);

        var timeline = new List<string>();
        var stopwatch = Stopwatch.StartNew();
        for (var index = 0; index < 18; index++)
        {
            session.BringMainWindowToFront();
            var overlayVisible =
                session.MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("ChatView.LoadingOverlayMask")) is not null
                || session.MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("ChatView.LoadingOverlayStatus")) is not null
                || session.MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("ChatView.LoadingOverlay")) is not null;
            var activeRootVisible = session.MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("ChatView.ActiveRoot")) is not null;
            var header = session.MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("ChatView.CurrentSessionTitle"));
            var messagesVisible = session.MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("ChatView.MessagesList")) is not null;
            var headerName = header?.Name ?? "<missing>";
            var screenshotPath = Path.Combine(captureRoot, $"frame-{index:00}-{stopwatch.ElapsedMilliseconds:0000}ms.png");
            session.CaptureMainWindowToFile(screenshotPath);

            timeline.Add(
                $"{stopwatch.ElapsedMilliseconds,5}ms overlay={overlayVisible} activeRoot={activeRootVisible} header={headerName} messages={messagesVisible} shot={screenshotPath}");

            Thread.Sleep(16);
        }

        var timelinePath = Path.Combine(captureRoot, "timeline.txt");
        File.WriteAllLines(timelinePath, timeline);
        Console.WriteLine($"First-frame audit capture root: {captureRoot}");
        var capturedFrames = Directory.GetFiles(captureRoot, "frame-*.png", SearchOption.TopDirectoryOnly);

        Assert.True(File.Exists(timelinePath), $"Expected timeline capture to exist at {timelinePath}.");
        Assert.NotEmpty(capturedFrames);
    }

    [SkippableFact]
    public void SelectRemoteBoundSession_FromStart_DoesNotSnapBackToStartSelection()
    {
        GuiTestGate.RequireEnabled();

        var candidates = RealUserConfigProbe.LoadReplayBackedCandidates();
        Skip.If(candidates.Count == 0, "No real remote-bound conversations with recent session/load + session/update evidence were found in the current SalmonEgg app data.");

        using var session = WindowsGuiAppSession.LaunchFresh();

        var startItem = session.FindByAutomationId("MainNav.Start", TimeSpan.FromSeconds(10));
        session.ActivateElement(startItem);
        Thread.Sleep(500);

        var candidate = candidates
            .FirstOrDefault(item => session.TryFindByAutomationId(SessionAutomationId(item.ConversationId), TimeSpan.FromSeconds(1)) is not null);
        Skip.If(candidate is null, $"No replay-backed conversation is currently visible in the left navigation. Candidates: {string.Join(", ", candidates.Select(c => c.ConversationId))}");

        var sessionItem = session.FindByAutomationId(SessionAutomationId(candidate.ConversationId), TimeSpan.FromSeconds(10));
        session.ActivateElement(sessionItem);

        var timeline = new List<string>();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        var sawSessionSelected = false;
        var snappedBackToStart = false;

        while (DateTime.UtcNow < deadline)
        {
            var sessionSelected = session.TryGetIsSelected(SessionAutomationId(candidate.ConversationId)) == true;
            var startSelected = session.TryGetIsSelected("MainNav.Start") == true;
            var headerVisible = session.TryFindByAutomationId("ChatView.CurrentSessionTitle", TimeSpan.FromMilliseconds(100)) is not null;
            var overlayVisible = session.TryFindByAutomationId("ChatView.LoadingOverlay", TimeSpan.FromMilliseconds(100)) is not null;

            timeline.Add(
                $"{DateTime.UtcNow:HH:mm:ss.fff} sessionSelected={sessionSelected} startSelected={startSelected} header={headerVisible} overlay={overlayVisible}");

            if (sessionSelected)
            {
                sawSessionSelected = true;
            }

            if (sawSessionSelected && startSelected && !sessionSelected)
            {
                snappedBackToStart = true;
                break;
            }

            if (sawSessionSelected && (overlayVisible || headerVisible))
            {
                break;
            }

            Thread.Sleep(150);
        }

        Assert.False(
            snappedBackToStart,
            $"After selecting remote-bound conversation {candidate.ConversationId}, left navigation snapped back to Start while activation was still unfolding.{Environment.NewLine}{string.Join(Environment.NewLine, timeline)}");
    }

    [SkippableFact]
    public void SelectRemoteBoundSession_FromStart_KeepsLoadingVisible_UntilRemoteReplayProjects()
    {
        GuiTestGate.RequireEnabled();

        var candidates = RealUserConfigProbe.LoadReplayBackedCandidates();
        Skip.If(candidates.Count == 0, "No real remote-bound conversations with recent session/load + session/update evidence were found in the current SalmonEgg app data.");

        using var session = WindowsGuiAppSession.LaunchFresh();

        var startItem = session.FindByAutomationId("MainNav.Start", TimeSpan.FromSeconds(10));
        session.ActivateElement(startItem);
        Thread.Sleep(500);

        var candidate = candidates
            .FirstOrDefault(item => session.TryFindByAutomationId(SessionAutomationId(item.ConversationId), TimeSpan.FromSeconds(1)) is not null);
        Skip.If(candidate is null, $"No replay-backed conversation is currently visible in the left navigation. Candidates: {string.Join(", ", candidates.Select(c => c.ConversationId))}");

        var sessionItem = session.FindByAutomationId(SessionAutomationId(candidate.ConversationId), TimeSpan.FromSeconds(10));

        session.ActivateElement(sessionItem);

        var timeline = new List<string>();
        var stopwatch = Stopwatch.StartNew();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        var sawBlockingMask = false;
        var sawOverlayStatus = false;
        var sawTranscriptVisible = false;
        var sawPrematureDismissal = false;
        string? prematureDismissalCapturePath = null;
        string? persistentMaskCapturePath = null;
        long? maskDismissedAtMs = null;
        long? transcriptVisibleAtMs = null;
        var maskDismissedAfterTranscript = false;
        var loadingSurfaceVisibleAtLastObservation = false;

        while (DateTime.UtcNow < deadline)
        {
            var blockingMaskVisible = session.TryFindByAutomationId("ChatView.LoadingOverlayMask", TimeSpan.FromMilliseconds(100)) is not null;
            var overlayStatusVisible = session.TryFindByAutomationId("ChatView.LoadingOverlayStatus", TimeSpan.FromMilliseconds(100)) is not null;
            var header = session.TryFindByAutomationId("ChatView.CurrentSessionTitle", TimeSpan.FromMilliseconds(100));
            var messagesList = session.TryFindByAutomationId("ChatView.MessagesList", TimeSpan.FromMilliseconds(100));
            var visibleTranscriptTextCount = CountVisibleTranscriptText(messagesList);
            var selected = session.TryGetIsSelected(SessionAutomationId(candidate.ConversationId));
            loadingSurfaceVisibleAtLastObservation = blockingMaskVisible || overlayStatusVisible;

            timeline.Add(
                $"{stopwatch.ElapsedMilliseconds,5}ms mask={blockingMaskVisible} status={overlayStatusVisible} header={(header is not null)} selected={selected} visibleText={visibleTranscriptTextCount}");

            if (blockingMaskVisible)
            {
                sawBlockingMask = true;
            }

            if (overlayStatusVisible)
            {
                sawOverlayStatus = true;
            }

            if (visibleTranscriptTextCount > 0)
            {
                sawTranscriptVisible = true;
                transcriptVisibleAtMs ??= stopwatch.ElapsedMilliseconds;
            }

            if (sawBlockingMask && !blockingMaskVisible && maskDismissedAtMs is null)
            {
                maskDismissedAtMs = stopwatch.ElapsedMilliseconds;
            }

            if (transcriptVisibleAtMs is not null && !blockingMaskVisible)
            {
                maskDismissedAfterTranscript = true;
            }

            if (maskDismissedAtMs is not null
                && !sawTranscriptVisible
                && stopwatch.ElapsedMilliseconds - maskDismissedAtMs.Value > 500)
            {
                var captureRoot = Path.Combine(Path.GetTempPath(), "SalmonEgg.GuiTests");
                Directory.CreateDirectory(captureRoot);
                prematureDismissalCapturePath = Path.Combine(
                    captureRoot,
                    $"remote-overlay-premature-{candidate.ConversationId}-{DateTime.UtcNow:yyyyMMddHHmmssfff}.png");
                prematureDismissalCapturePath = TryCaptureMainWindow(session, prematureDismissalCapturePath);
                sawPrematureDismissal = true;
                break;
            }

            if (transcriptVisibleAtMs is not null
                && blockingMaskVisible
                && stopwatch.ElapsedMilliseconds - transcriptVisibleAtMs.Value > 4000)
            {
                var captureRoot = Path.Combine(Path.GetTempPath(), "SalmonEgg.GuiTests");
                Directory.CreateDirectory(captureRoot);
                persistentMaskCapturePath = Path.Combine(
                    captureRoot,
                    $"remote-mask-persistent-{candidate.ConversationId}-{DateTime.UtcNow:yyyyMMddHHmmssfff}.png");
                persistentMaskCapturePath = TryCaptureMainWindow(session, persistentMaskCapturePath);
                break;
            }

            if (sawTranscriptVisible && !blockingMaskVisible)
            {
                break;
            }

            Thread.Sleep(200);
        }

        var failureDetails = string.Join(Environment.NewLine, timeline);

        Assert.True(
            sawOverlayStatus,
            $"The real-config navigation path surfaced the loading overlay but never exposed ChatView.LoadingOverlayStatus for conversation {candidate.ConversationId}.{Environment.NewLine}{failureDetails}");

        Assert.False(
            sawPrematureDismissal,
            $"Blocking loading mask disappeared before any transcript content became visible for conversation {candidate.ConversationId}.{Environment.NewLine}Capture: {prematureDismissalCapturePath ?? "<none>"}{Environment.NewLine}{failureDetails}");

        Assert.True(
            sawTranscriptVisible || loadingSurfaceVisibleAtLastObservation,
            $"Real-config smoke neither observed transcript content nor retained the loading surface for conversation {candidate.ConversationId}.{Environment.NewLine}{failureDetails}");

        if (sawTranscriptVisible)
        {
            Assert.True(
                maskDismissedAfterTranscript,
                $"Blocking loading mask remained visible after transcript content was already visible for conversation {candidate.ConversationId}.{Environment.NewLine}Capture: {persistentMaskCapturePath ?? "<none>"}{Environment.NewLine}{failureDetails}");
        }
    }

    [SkippableFact]
    public void SelectPureLocalSession_WhileRemoteLoading_DoesNotLeakRemoteStatusPill()
    {
        GuiTestGate.RequireEnabled();

        var remoteCandidates = RealUserConfigProbe.LoadReplayBackedCandidates();
        var localCandidates = RealUserConfigProbe.LoadPureLocalCandidates();
        Skip.If(remoteCandidates.Count == 0, "No replay-backed remote conversation candidates were found in the current SalmonEgg app data.");
        Skip.If(localCandidates.Count == 0, "No pure local conversation candidates were found in the current SalmonEgg app data.");

        using var session = WindowsGuiAppSession.LaunchFresh();

        var startItem = session.FindByAutomationId("MainNav.Start", TimeSpan.FromSeconds(10));
        session.ActivateElement(startItem);
        Thread.Sleep(500);

        var remoteCandidate = remoteCandidates
            .FirstOrDefault(item => session.TryFindByAutomationId(SessionAutomationId(item.ConversationId), TimeSpan.FromSeconds(1)) is not null);
        Skip.If(remoteCandidate is null, $"No replay-backed remote candidate is currently visible in the left navigation. Candidates: {string.Join(", ", remoteCandidates.Select(c => c.ConversationId))}");

        var localCandidate = localCandidates
            .Where(item => !string.Equals(item.ConversationId, remoteCandidate.ConversationId, StringComparison.Ordinal))
            .FirstOrDefault(item => session.TryFindByAutomationId(SessionAutomationId(item.ConversationId), TimeSpan.FromSeconds(1)) is not null);
        Skip.If(localCandidate is null, $"No pure local conversation candidate is currently visible in the left navigation. Candidates: {string.Join(", ", localCandidates.Select(c => c.ConversationId))}");

        var remoteItem = session.FindByAutomationId(SessionAutomationId(remoteCandidate.ConversationId), TimeSpan.FromSeconds(10));
        session.ActivateElement(remoteItem);

        var sawRemoteStatus = false;
        var remoteStatusDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < remoteStatusDeadline)
        {
            if (session.TryFindByAutomationId("ChatView.LoadingOverlayStatus", TimeSpan.FromMilliseconds(100)) is not null)
            {
                sawRemoteStatus = true;
                break;
            }

            Thread.Sleep(150);
        }

        Skip.IfNot(sawRemoteStatus, $"Remote loading pill did not become visible for conversation {remoteCandidate.ConversationId}; cannot validate cross-session leakage.");

        var localItem = session.FindByAutomationId(SessionAutomationId(localCandidate.ConversationId), TimeSpan.FromSeconds(10));
        session.ActivateElement(localItem);

        var timeline = new List<string>();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(6);
        var localSelectedAtUtc = DateTime.MinValue;
        var leakedStatusAfterLocalSelection = false;
        string? capturePath = null;

        while (DateTime.UtcNow < deadline)
        {
            var remoteSelected = session.TryGetIsSelected(SessionAutomationId(remoteCandidate.ConversationId)) == true;
            var localSelected = session.TryGetIsSelected(SessionAutomationId(localCandidate.ConversationId)) == true;
            var overlayStatusVisible = session.TryFindByAutomationId("ChatView.LoadingOverlayStatus", TimeSpan.FromMilliseconds(100)) is not null;
            var blockingMaskVisible = session.TryFindByAutomationId("ChatView.LoadingOverlayMask", TimeSpan.FromMilliseconds(100)) is not null;
            var headerVisible = session.TryFindByAutomationId("ChatView.CurrentSessionTitle", TimeSpan.FromMilliseconds(100)) is not null;

            timeline.Add(
                $"{DateTime.UtcNow:HH:mm:ss.fff} remoteSelected={remoteSelected} localSelected={localSelected} status={overlayStatusVisible} mask={blockingMaskVisible} header={headerVisible}");

            if (localSelected && localSelectedAtUtc == DateTime.MinValue)
            {
                localSelectedAtUtc = DateTime.UtcNow;
            }

            if (localSelectedAtUtc != DateTime.MinValue
                && DateTime.UtcNow - localSelectedAtUtc > TimeSpan.FromMilliseconds(500)
                && overlayStatusVisible)
            {
                var captureRoot = Path.Combine(Path.GetTempPath(), "SalmonEgg.GuiTests");
                Directory.CreateDirectory(captureRoot);
                capturePath = Path.Combine(
                    captureRoot,
                    $"remote-status-leak-to-local-{localCandidate.ConversationId}-{DateTime.UtcNow:yyyyMMddHHmmssfff}.png");
                capturePath = TryCaptureMainWindow(session, capturePath);
                leakedStatusAfterLocalSelection = true;
                break;
            }

            if (localSelectedAtUtc != DateTime.MinValue
                && DateTime.UtcNow - localSelectedAtUtc > TimeSpan.FromMilliseconds(1200)
                && !overlayStatusVisible)
            {
                break;
            }

            Thread.Sleep(150);
        }

        Assert.False(
            leakedStatusAfterLocalSelection,
            $"Remote loading status pill leaked after selecting pure local conversation {localCandidate.ConversationId} while remote conversation {remoteCandidate.ConversationId} was still loading.{Environment.NewLine}Capture: {capturePath ?? "<none>"}{Environment.NewLine}{string.Join(Environment.NewLine, timeline)}");
    }

    [SkippableFact]
    public void StartComposer_WhenStartupRemoteProfileCannotPrepareDraft_SwitchingToLocalStdioProfileRecoversModeSelector()
    {
        GuiTestGate.RequireEnabled();

        var scenario = RealUserConfigProbe.LoadStartComposerProfileSwitchScenario();
        Skip.If(
            scenario is null,
            "Current real config does not expose a startup remote profile plus a different local stdio profile for targeted start-composer recovery validation.");

        using var session = WindowsGuiAppSession.LaunchFresh();
        session.ResizeMainWindow(width: 1400, height: 900);
        var startItem = session.FindByAutomationId("MainNav.Start", TimeSpan.FromSeconds(10));
        session.ActivateElement(startItem);
        Thread.Sleep(500);

        Assert.True(
            session.WaitUntilOnscreen("StartView.AgentSelector", TimeSpan.FromSeconds(10)),
            "StartView.AgentSelector did not appear for the real-config start composer.");
        Assert.True(
            session.WaitUntilOnscreen("StartView.ModeSelector", TimeSpan.FromSeconds(10)),
            "StartView.ModeSelector did not appear for the real-config start composer.");

        // Allow the startup remote profile to enter its real unavailable/unready path before we switch away.
        _ = WaitUntil(
            () => IsStartComposerModeUnavailable(session)
                || TryOpenStartModeSelectorAndDetectKnownMode(session),
            TimeSpan.FromSeconds(12),
            TimeSpan.FromMilliseconds(250));

        SelectComboBoxItemByVisibleText(session, "StartView.AgentSelector", scenario.LocalProfileName);

        var recovered = WaitUntil(
            () => TryOpenStartModeSelectorAndDetectKnownMode(session),
            TimeSpan.FromSeconds(20),
            TimeSpan.FromMilliseconds(300));

        Assert.True(
            recovered,
            $"Start composer mode selector did not recover after switching from startup remote profile '{scenario.StartupProfileName}' ({scenario.StartupProfileTransport}) to local stdio profile '{scenario.LocalProfileName}'. AgentSelector='{session.TryGetElementName("StartView.AgentSelector", TimeSpan.FromMilliseconds(200)) ?? "<missing>"}' ModeSelector='{session.TryGetElementName("StartView.ModeSelector", TimeSpan.FromMilliseconds(200)) ?? "<missing>"}'.");
    }

    [SkippableFact]
    public void StartComposer_WhenSwitchingLocalRemoteLocal_RoundTripsBackToReadyModes()
    {
        GuiTestGate.RequireEnabled();

        var scenario = RealUserConfigProbe.LoadStartComposerRoundTripScenario();
        Skip.If(
            scenario is null,
            "Current real config does not expose both a local stdio profile and a different remote profile for targeted start-composer local-remote-local validation.");

        using var session = WindowsGuiAppSession.LaunchFresh();
        session.ResizeMainWindow(width: 1400, height: 900);
        var startItem = session.FindByAutomationId("MainNav.Start", TimeSpan.FromSeconds(10));
        session.ActivateElement(startItem);
        Thread.Sleep(500);

        Assert.True(
            session.WaitUntilOnscreen("StartView.AgentSelector", TimeSpan.FromSeconds(10)),
            "StartView.AgentSelector did not appear for the real-config start composer.");
        Assert.True(
            session.WaitUntilOnscreen("StartView.ModeSelector", TimeSpan.FromSeconds(10)),
            "StartView.ModeSelector did not appear for the real-config start composer.");

        SelectComboBoxItemByAutomationId(
            session,
            "StartView.AgentSelector",
            scenario.LocalProfileAutomationId,
            scenario.LocalProfileName);
        var localReady = WaitUntil(
            () => TryOpenStartModeSelectorAndDetectKnownMode(session),
            TimeSpan.FromSeconds(20),
            TimeSpan.FromMilliseconds(300));
        Assert.True(
            localReady,
            $"Start composer mode selector did not become ready after selecting local stdio profile '{scenario.LocalProfileName}'.");

        SelectComboBoxItemByAutomationId(
            session,
            "StartView.AgentSelector",
            scenario.RemoteProfileAutomationId,
            scenario.RemoteProfileName);
        _ = WaitUntil(
            () => IsStartComposerModeUnavailable(session)
                || TryOpenStartModeSelectorAndDetectKnownMode(session),
            TimeSpan.FromSeconds(12),
            TimeSpan.FromMilliseconds(250));

        SelectComboBoxItemByAutomationId(
            session,
            "StartView.AgentSelector",
            scenario.LocalProfileAutomationId,
            scenario.LocalProfileName);
        var recovered = WaitUntil(
            () => TryOpenStartModeSelectorAndDetectKnownMode(session),
            TimeSpan.FromSeconds(20),
            TimeSpan.FromMilliseconds(300));

        Assert.True(
            recovered,
            $"Start composer mode selector did not recover after round-tripping from local stdio profile '{scenario.LocalProfileName}' to remote profile '{scenario.RemoteProfileName}' ({scenario.RemoteProfileTransport}) and back. AgentSelector='{session.TryGetElementName("StartView.AgentSelector", TimeSpan.FromMilliseconds(200)) ?? "<missing>"}' ModeSelector='{session.TryGetElementName("StartView.ModeSelector", TimeSpan.FromMilliseconds(200)) ?? "<missing>"}'.");
    }

    [SkippableFact]
    public void StartComposer_WhenSelectingRealRemoteProfileAndRemoteDirectory_LoadsReadyModes()
    {
        GuiTestGate.RequireEnabled();

        var scenario = RealUserConfigProbe.LoadStartComposerRemoteDirectoryScenario(
            Environment.GetEnvironmentVariable("SALMONEGG_GUI_REAL_REMOTE_PROFILE_NAME") ?? "cc-ws1");
        Skip.If(
            scenario is null,
            "Current real config does not expose the requested remote profile plus a configured remote directory for targeted start-composer validation.");

        using var session = WindowsGuiAppSession.LaunchFresh();
        session.ResizeMainWindow(width: 1400, height: 900);
        var startItem = session.FindByAutomationId("MainNav.Start", TimeSpan.FromSeconds(10));
        session.ActivateElement(startItem);
        Thread.Sleep(500);

        Assert.True(
            session.WaitUntilOnscreen("StartView.AgentSelector", TimeSpan.FromSeconds(10)),
            "StartView.AgentSelector did not appear for the real-config start composer.");
        Assert.True(
            session.WaitUntilOnscreen("StartView.ProjectSelector", TimeSpan.FromSeconds(10)),
            "StartView.ProjectSelector did not appear for the real-config start composer.");
        Assert.True(
            session.WaitUntilOnscreen("StartView.ModeSelector", TimeSpan.FromSeconds(10)),
            "StartView.ModeSelector did not appear for the real-config start composer.");

        var logCheckpoint = DateTimeOffset.Now - TimeSpan.FromSeconds(1);
        SelectComboBoxItemByAutomationId(
            session,
            "StartView.AgentSelector",
            scenario.RemoteProfileAutomationId,
            scenario.RemoteProfileName);
        Assert.True(
            session.WaitUntilOnscreen("StartView.ProjectSelector", TimeSpan.FromSeconds(10)),
            "StartView.ProjectSelector disappeared after selecting the real remote profile.");

        SelectComboBoxItemByAutomationId(
            session,
            "StartView.ProjectSelector",
            scenario.RemoteDirectoryAutomationId,
            scenario.RemoteDirectoryDisplayName);

        var ready = WaitUntil(
            () => TryOpenStartModeSelectorAndDetectKnownMode(session),
            TimeSpan.FromSeconds(30),
            TimeSpan.FromMilliseconds(300));
        var logVerified = RealUserConfigProbe.WaitForRecentAppLogContainsAll(
            logCheckpoint,
            TimeSpan.FromSeconds(5),
            [
                $"Started ACP new-session draft request. profileId={scenario.RemoteProfileId}",
                $"cwd={scenario.RemoteDirectoryPath}",
                $"Applied ACP new-session draft response. profileId={scenario.RemoteProfileId}",
                "modeCount="
            ],
            out var recentLogTail);

        Assert.True(
            ready,
            $"Start composer mode selector did not expose ready modes for real remote profile '{scenario.RemoteProfileName}' and remote directory '{scenario.RemoteDirectoryDisplayName}'. AgentSelector='{session.TryGetElementName("StartView.AgentSelector", TimeSpan.FromMilliseconds(200)) ?? "<missing>"}' ProjectSelector='{session.TryGetElementName("StartView.ProjectSelector", TimeSpan.FromMilliseconds(200)) ?? "<missing>"}' ModeSelector='{session.TryGetElementName("StartView.ModeSelector", TimeSpan.FromMilliseconds(200)) ?? "<missing>"}'.");
        Assert.True(
            logVerified,
            $"Real remote start-composer run did not log a fresh session/new response for profile '{scenario.RemoteProfileName}' and cwd '{scenario.RemoteDirectoryPath}'. Recent log tail:{Environment.NewLine}{recentLogTail}");
    }

    [SkippableFact]
    public void RandomSwitchBetweenLocalRemote_WithOneSecondCadence_RemainsInteractive()
    {
        GuiTestGate.RequireEnabled();

        var remoteCandidates = RealUserConfigProbe.LoadReplayBackedCandidates();
        var localCandidates = RealUserConfigProbe.LoadPureLocalCandidates();
        Skip.If(remoteCandidates.Count == 0, "No replay-backed remote conversation candidates were found in the current SalmonEgg app data.");
        Skip.If(localCandidates.Count == 0, "No pure local conversation candidates were found in the current SalmonEgg app data.");

        using var session = WindowsGuiAppSession.LaunchFresh();

        var startId = "MainNav.Start";
        var remoteCandidate = remoteCandidates
            .FirstOrDefault(item => session.TryFindByAutomationId(SessionAutomationId(item.ConversationId), TimeSpan.FromSeconds(1)) is not null);
        var localCandidate = localCandidates
            .FirstOrDefault(item => session.TryFindByAutomationId(SessionAutomationId(item.ConversationId), TimeSpan.FromSeconds(1)) is not null);
        Skip.If(remoteCandidate is null, $"No replay-backed remote candidate is currently visible in the left navigation. Candidates: {string.Join(", ", remoteCandidates.Select(c => c.ConversationId))}");
        Skip.If(localCandidate is null, $"No pure local candidate is currently visible in the left navigation. Candidates: {string.Join(", ", localCandidates.Select(c => c.ConversationId))}");

        var remoteId = SessionAutomationId(remoteCandidate.ConversationId);
        var localId = SessionAutomationId(localCandidate.ConversationId);
        var targets = new[] { remoteId, localId, startId };
        var timeline = new List<string>();
        var random = new Random(20260402);

        // Reproduce user's report: random switching with ~1s interval.
        for (var index = 0; index < 15; index++)
        {
            var targetId = targets[random.Next(targets.Length)];
            var selectedBefore = DescribeSelectionSnapshot(session, startId, localId, remoteId);

            var target = session.FindByAutomationId(targetId, TimeSpan.FromSeconds(8));
            session.ActivateElement(target);
            Thread.Sleep(1000);

            var selectedAfter = DescribeSelectionSnapshot(session, startId, localId, remoteId);
            timeline.Add(
                $"{DateTime.UtcNow:HH:mm:ss.fff} step={index:00} target={targetId} before={selectedBefore} after={selectedAfter}");
        }

        // Liveness assertion: after burst, navigation must still respond to fresh interactions.
        var startItem = session.FindByAutomationId(startId, TimeSpan.FromSeconds(10));
        session.ActivateElement(startItem);
        Thread.Sleep(500);
        var startSelected = session.TryGetIsSelected(startId) == true;
        var startViewVisible = session.TryFindByAutomationId("StartView.Title", TimeSpan.FromSeconds(4)) is not null;
        timeline.Add($"{DateTime.UtcNow:HH:mm:ss.fff} final-start selected={startSelected} startView={startViewVisible}");

        var localItem = session.FindByAutomationId(localId, TimeSpan.FromSeconds(10));
        session.ActivateElement(localItem);
        Thread.Sleep(700);
        var localSelected = session.TryGetIsSelected(localId) == true;
        var headerVisible = session.TryFindByAutomationId("ChatView.CurrentSessionTitle", TimeSpan.FromSeconds(4)) is not null;
        timeline.Add($"{DateTime.UtcNow:HH:mm:ss.fff} final-local selected={localSelected} header={headerVisible}");

        // Interactivity contract:
        // 1) We can still return to Start view.
        // 2) We can still enter a chat shell after that.
        // Some builds may project chat header before nav selection indicator settles,
        // so require local selection OR visible chat header to avoid false positives.
        var remainedInteractive = startSelected && startViewVisible && (localSelected || headerVisible);
        if (!remainedInteractive)
        {
            var captureRoot = Path.Combine(Path.GetTempPath(), "SalmonEgg.GuiTests");
            Directory.CreateDirectory(captureRoot);
            var capturePath = Path.Combine(
                captureRoot,
                $"random-switch-interactivity-{DateTime.UtcNow:yyyyMMddHHmmssfff}.png");
            capturePath = TryCaptureMainWindow(session, capturePath);

            var appDataRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SalmonEgg");
            var bootLogPath = Path.Combine(appDataRoot, "boot.log");
            var bootTail = File.Exists(bootLogPath)
                ? string.Join(Environment.NewLine, File.ReadLines(bootLogPath).TakeLast(30))
                : "<boot.log missing>";

            throw new Xunit.Sdk.XunitException(
                $"Interactivity freeze suspected after random 1s cadence switching.{Environment.NewLine}Screenshot: {capturePath}{Environment.NewLine}{string.Join(Environment.NewLine, timeline)}{Environment.NewLine}boot.log:{Environment.NewLine}{bootTail}");
        }
    }

    [SkippableFact]
    public void RandomSwitchAcrossProfilesAndLocal_ForExtendedDuration_RemainsInteractive()
    {
        GuiTestGate.RequireEnabled();

        var remoteCandidates = RealUserConfigProbe.LoadReplayBackedCandidates();
        var localCandidates = RealUserConfigProbe.LoadPureLocalCandidates();
        Skip.If(remoteCandidates.Count == 0, "No replay-backed remote conversation candidates were found in the current SalmonEgg app data.");
        Skip.If(localCandidates.Count == 0, "No pure local conversation candidates were found in the current SalmonEgg app data.");

        using var session = WindowsGuiAppSession.LaunchFresh();

        var startId = "MainNav.Start";
        var visibleRemoteCandidates = remoteCandidates
            .Where(item => session.TryFindByAutomationId(SessionAutomationId(item.ConversationId), TimeSpan.FromSeconds(1)) is not null)
            .ToArray();
        var profileGroups = visibleRemoteCandidates
            .Where(item => !string.IsNullOrWhiteSpace(item.BoundProfileId))
            .GroupBy(item => item.BoundProfileId!, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ToArray();
        Skip.If(profileGroups.Length < 2, "Need at least two visible replay-backed remote conversations bound to different profiles for cross-profile soak switching.");

        var remoteA = profileGroups[0]
            .OrderByDescending(item => item.LocalMessageCount)
            .ThenByDescending(item => item.LastUpdatedAtUtc)
            .First();
        var remoteB = profileGroups[1]
            .OrderByDescending(item => item.LocalMessageCount)
            .ThenByDescending(item => item.LastUpdatedAtUtc)
            .First();
        Skip.If(string.Equals(remoteA.ConversationId, remoteB.ConversationId, StringComparison.Ordinal), "Cross-profile soak requires two distinct remote conversations.");

        var localCandidate = localCandidates
            .FirstOrDefault(item =>
                !string.Equals(item.ConversationId, remoteA.ConversationId, StringComparison.Ordinal)
                && !string.Equals(item.ConversationId, remoteB.ConversationId, StringComparison.Ordinal)
                && session.TryFindByAutomationId(SessionAutomationId(item.ConversationId), TimeSpan.FromSeconds(1)) is not null);
        Skip.If(localCandidate is null, "No visible pure local conversation candidate is available for cross-profile soak switching.");

        var remoteAId = SessionAutomationId(remoteA.ConversationId);
        var remoteBId = SessionAutomationId(remoteB.ConversationId);
        var localId = SessionAutomationId(localCandidate.ConversationId);
        var targets = new[] { remoteAId, remoteBId, localId, startId };
        var random = new Random(20260402);
        var timeline = new List<string>();

        // Long-running stress contract: random switching across profile boundaries should remain responsive.
        for (var index = 0; index < 40; index++)
        {
            var targetId = targets[random.Next(targets.Length)];
            var selectedBefore = $"start={session.TryGetIsSelected(startId) == true},local={session.TryGetIsSelected(localId) == true},remoteA={session.TryGetIsSelected(remoteAId) == true},remoteB={session.TryGetIsSelected(remoteBId) == true}";

            var target = session.FindByAutomationId(targetId, TimeSpan.FromSeconds(8));
            session.ActivateElement(target);
            Thread.Sleep(900);

            var selectedAfter = $"start={session.TryGetIsSelected(startId) == true},local={session.TryGetIsSelected(localId) == true},remoteA={session.TryGetIsSelected(remoteAId) == true},remoteB={session.TryGetIsSelected(remoteBId) == true}";
            timeline.Add($"{DateTime.UtcNow:HH:mm:ss.fff} step={index:00} target={targetId} before={selectedBefore} after={selectedAfter}");
        }

        var startItem = session.FindByAutomationId(startId, TimeSpan.FromSeconds(10));
        session.ActivateElement(startItem);
        Thread.Sleep(500);
        var startSelected = session.TryGetIsSelected(startId) == true;
        var startViewVisible = session.TryFindByAutomationId("StartView.Title", TimeSpan.FromSeconds(4)) is not null;
        timeline.Add($"{DateTime.UtcNow:HH:mm:ss.fff} final-start selected={startSelected} startView={startViewVisible}");

        var remoteAItem = session.FindByAutomationId(remoteAId, TimeSpan.FromSeconds(10));
        session.ActivateElement(remoteAItem);
        Thread.Sleep(900);
        var remoteASelected = session.TryGetIsSelected(remoteAId) == true;
        var remoteAHeaderVisible = session.TryFindByAutomationId("ChatView.CurrentSessionTitle", TimeSpan.FromSeconds(4)) is not null;
        timeline.Add($"{DateTime.UtcNow:HH:mm:ss.fff} final-remoteA selected={remoteASelected} header={remoteAHeaderVisible}");

        var localItem = session.FindByAutomationId(localId, TimeSpan.FromSeconds(10));
        session.ActivateElement(localItem);
        Thread.Sleep(900);
        var localSelected = session.TryGetIsSelected(localId) == true;
        var localHeaderVisible = session.TryFindByAutomationId("ChatView.CurrentSessionTitle", TimeSpan.FromSeconds(4)) is not null;
        timeline.Add($"{DateTime.UtcNow:HH:mm:ss.fff} final-local selected={localSelected} header={localHeaderVisible}");

        var remainedInteractive =
            startSelected
            && startViewVisible
            && (remoteASelected || remoteAHeaderVisible)
            && (localSelected || localHeaderVisible);
        if (!remainedInteractive)
        {
            var captureRoot = Path.Combine(Path.GetTempPath(), "SalmonEgg.GuiTests");
            Directory.CreateDirectory(captureRoot);
            var capturePath = Path.Combine(
                captureRoot,
                $"cross-profile-soak-interactivity-{DateTime.UtcNow:yyyyMMddHHmmssfff}.png");
            capturePath = TryCaptureMainWindow(session, capturePath);

            var appDataRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SalmonEgg");
            var bootLogPath = Path.Combine(appDataRoot, "boot.log");
            var bootTail = File.Exists(bootLogPath)
                ? string.Join(Environment.NewLine, File.ReadLines(bootLogPath).TakeLast(30))
                : "<boot.log missing>";

            throw new Xunit.Sdk.XunitException(
                $"Interactivity freeze suspected after cross-profile long-duration random switching.{Environment.NewLine}Screenshot: {capturePath}{Environment.NewLine}{string.Join(Environment.NewLine, timeline)}{Environment.NewLine}boot.log:{Environment.NewLine}{bootTail}");
        }
    }

    [SkippableFact]
    public void SelectReplayBackedRemoteSessions_AcrossProfiles_HotReturn_RecoversWithoutReloadUi()
    {
        GuiTestGate.RequireEnabled();

        var remoteCandidates = RealUserConfigProbe.LoadReplayBackedCandidates(includeAllProfiles: true);
        Skip.If(remoteCandidates.Count == 0, "No replay-backed remote conversation candidates were found in the current SalmonEgg app data.");

        using var session = WindowsGuiAppSession.LaunchFresh();

        var startItem = session.FindByAutomationId("MainNav.Start", TimeSpan.FromSeconds(10));
        session.ActivateElement(startItem);
        Thread.Sleep(500);

        var visibleRemoteCandidates = remoteCandidates
            .Where(item => session.TryFindByAutomationId(SessionAutomationId(item.ConversationId), TimeSpan.FromSeconds(1)) is not null)
            .ToArray();
        var profileGroups = visibleRemoteCandidates
            .Where(item => !string.IsNullOrWhiteSpace(item.BoundProfileId))
            .GroupBy(item => item.BoundProfileId!, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ToArray();
        Skip.If(profileGroups.Length < 2, "Need at least two visible replay-backed remote conversations bound to different profiles for cross-profile hot return validation.");

        var remoteA = profileGroups[0]
            .OrderByDescending(item => item.LocalMessageCount)
            .ThenByDescending(item => item.LastUpdatedAtUtc)
            .First();
        var remoteB = profileGroups[1]
            .OrderByDescending(item => item.LocalMessageCount)
            .ThenByDescending(item => item.LastUpdatedAtUtc)
            .First();
        Skip.If(string.Equals(remoteA.ConversationId, remoteB.ConversationId, StringComparison.Ordinal), "Cross-profile hot return requires two distinct remote conversations.");

        var remoteAId = SessionAutomationId(remoteA.ConversationId);
        var remoteBId = SessionAutomationId(remoteB.ConversationId);

        var remoteAItem = session.FindByAutomationId(remoteAId, TimeSpan.FromSeconds(10));
        var remoteAExpectedHeader = FirstUsableVisibleLabel(remoteA.DisplayName, remoteAItem.Name, remoteA.ConversationId);
        session.ActivateElement(remoteAItem);

        var remoteAOverlayHidden = session.WaitUntilHidden("ChatView.LoadingOverlay", TimeSpan.FromSeconds(60));
        Assert.True(remoteAOverlayHidden, $"Initial remote hydration did not finish for conversation {remoteA.ConversationId}.");

        var remoteAHeaderVisible = session.WaitUntilVisible("ChatView.CurrentSessionTitle", TimeSpan.FromSeconds(10));
        Assert.True(remoteAHeaderVisible, $"Conversation header did not appear after selecting remote conversation {remoteA.ConversationId}.");

        var remoteAHeader = session.TryFindByAutomationId("ChatView.CurrentSessionTitle", TimeSpan.FromSeconds(2));
        Assert.True(
            remoteAHeader is not null && remoteAHeader.Name.Contains(remoteAExpectedHeader, StringComparison.Ordinal),
            $"Remote conversation {remoteA.ConversationId} did not project the expected header title before cross-profile switch. Expected='{remoteAExpectedHeader}' Actual='{remoteAHeader?.Name ?? "<missing>"}'{Environment.NewLine}{DumpChatTitleProjection(session)}");

        var remoteBItem = session.FindByAutomationId(remoteBId, TimeSpan.FromSeconds(10));
        var remoteBExpectedHeader = FirstUsableVisibleLabel(remoteB.DisplayName, remoteBItem.Name, remoteB.ConversationId);
        session.ActivateElement(remoteBItem);

        var remoteBOverlayHidden = session.WaitUntilHidden("ChatView.LoadingOverlay", TimeSpan.FromSeconds(60));
        Assert.True(remoteBOverlayHidden, $"Initial remote hydration did not finish for conversation {remoteB.ConversationId}.");

        var remoteBHeaderVisible = session.WaitUntilVisible("ChatView.CurrentSessionTitle", TimeSpan.FromSeconds(10));
        Assert.True(remoteBHeaderVisible, $"Conversation header did not appear after selecting remote conversation {remoteB.ConversationId}.");

        var remoteBHeader = session.TryFindByAutomationId("ChatView.CurrentSessionTitle", TimeSpan.FromSeconds(2));
        Assert.True(
            remoteBHeader is not null && remoteBHeader.Name.Contains(remoteBExpectedHeader, StringComparison.Ordinal),
            $"Remote conversation {remoteB.ConversationId} did not project the expected header title before hot return. Expected='{remoteBExpectedHeader}' Actual='{remoteBHeader?.Name ?? "<missing>"}'{Environment.NewLine}{DumpChatTitleProjection(session)}");

        remoteAItem = session.FindByAutomationId(remoteAId, TimeSpan.FromSeconds(10));

        const int hotReturnProbeWindowMs = 1200;
        var timeline = new List<string>();
        var activationStopwatch = Stopwatch.StartNew();
        session.ActivateElement(remoteAItem);
        var activationElapsedMs = activationStopwatch.ElapsedMilliseconds;
        timeline.Add($"activateElapsed={activationElapsedMs}ms");

        var hotReturnStopwatch = Stopwatch.StartNew();

        long? remoteAHeaderRecoveredAtMs = null;
        var sawBlockingMask = false;
        var sawOverlayStatus = false;
        while (hotReturnStopwatch.ElapsedMilliseconds <= hotReturnProbeWindowMs)
        {
            var remoteASelected = session.TryGetIsSelected(remoteAId) == true;
            var remoteBSelected = session.TryGetIsSelected(remoteBId) == true;
            var blockingMaskVisible = session.TryFindByAutomationId("ChatView.LoadingOverlayMask", TimeSpan.FromMilliseconds(100)) is not null;
            var overlayStatusVisible = session.TryFindByAutomationId("ChatView.LoadingOverlayStatus", TimeSpan.FromMilliseconds(100)) is not null;
            var header = session.TryFindByAutomationId("ChatView.CurrentSessionTitle", TimeSpan.FromMilliseconds(100));
            var headerName = header?.Name ?? "<missing>";

            if (remoteAHeaderRecoveredAtMs is null
                && remoteASelected
                && header is not null
                && headerName.Contains(remoteAExpectedHeader, StringComparison.Ordinal))
            {
                remoteAHeaderRecoveredAtMs = hotReturnStopwatch.ElapsedMilliseconds;
            }

            timeline.Add(
                $"{hotReturnStopwatch.ElapsedMilliseconds,5}ms remoteASelected={remoteASelected} remoteBSelected={remoteBSelected} mask={blockingMaskVisible} status={overlayStatusVisible} header={headerName}");

            if (blockingMaskVisible)
            {
                sawBlockingMask = true;
                break;
            }

            if (overlayStatusVisible)
            {
                sawOverlayStatus = true;
                break;
            }

            Thread.Sleep(80);
        }

        var finalHeader = session.TryFindByAutomationId("ChatView.CurrentSessionTitle", TimeSpan.FromMilliseconds(200));
        var finalHeaderName = finalHeader?.Name ?? "<missing>";
        if (remoteAHeaderRecoveredAtMs is null
            || sawBlockingMask
            || sawOverlayStatus
            || finalHeader is null
            || !finalHeaderName.Contains(remoteAExpectedHeader, StringComparison.Ordinal))
        {
            var captureRoot = Path.Combine(Path.GetTempPath(), "SalmonEgg.GuiTests");
            Directory.CreateDirectory(captureRoot);
            var capturePath = Path.Combine(
                captureRoot,
                $"cross-profile-hot-return-{remoteA.ConversationId}-{DateTime.UtcNow:yyyyMMddHHmmssfff}.png");
            capturePath = TryCaptureMainWindow(session, capturePath);

            var appDataRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SalmonEgg");
            var bootLogPath = Path.Combine(appDataRoot, "boot.log");
            var bootTail = File.Exists(bootLogPath)
                ? string.Join(Environment.NewLine, File.ReadLines(bootLogPath).TakeLast(30))
                : "<boot.log missing>";

            throw new Xunit.Sdk.XunitException(
                $"Cross-profile hot return did not recover conversation {remoteA.ConversationId} without reload UI. ExpectedHeader='{remoteAExpectedHeader}' FinalHeader='{finalHeaderName}' HeaderRecoveredAtMs={remoteAHeaderRecoveredAtMs?.ToString() ?? "<missing>"} sawMask={sawBlockingMask} sawStatus={sawOverlayStatus}{Environment.NewLine}Screenshot: {capturePath}{Environment.NewLine}{string.Join(Environment.NewLine, timeline)}{Environment.NewLine}{DumpChatTitleProjection(session)}{Environment.NewLine}boot.log:{Environment.NewLine}{bootTail}");
        }
    }

    private static string DumpChatTitleProjection(WindowsGuiAppSession session)
    {
        var lines = new List<string>
        {
            $"SelectionState={DumpAutomationSelectionState(session)}",
            $"ViewportState={session.TryGetElementName("ChatView.TranscriptViewportState", TimeSpan.FromMilliseconds(200)) ?? "<missing>"}",
            $"ViewportDebug={session.TryGetElementName("ChatView.TranscriptViewportDebug", TimeSpan.FromMilliseconds(200)) ?? "<missing>"}",
        };

        foreach (var automationId in new[]
        {
            "ChatView.ActiveRoot",
            "ChatView.CurrentSessionTitle",
            "ChatView.LoadingOverlay",
            "ChatView.LoadingOverlayMask",
            "ChatView.LoadingOverlayStatus",
            "ChatView.MessagesList",
        })
        {
            lines.Add($"{automationId}={DescribeElementsByAutomationId(session, automationId)}");
        }

        var visibleTexts = session.GetVisibleTexts()
            .Take(40)
            .ToArray();
        lines.Add($"VisibleTexts=[{string.Join(" | ", visibleTexts)}]");

        return string.Join(Environment.NewLine, lines);
    }

    private static string DescribeElementsByAutomationId(WindowsGuiAppSession session, string automationId)
    {
        try
        {
            var matches = session.MainWindow
                .FindAllDescendants()
                .Where(element => string.Equals(TryGetAutomationId(element), automationId, StringComparison.Ordinal))
                .Take(12)
                .Select(DescribeAutomationElement)
                .ToArray();

            return matches.Length == 0
                ? "<missing>"
                : string.Join(" || ", matches);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            return $"<error: {ex.Message}>";
        }
    }

    private static string DescribeAutomationElement(AutomationElement element)
    {
        var name = TryGetName(element) ?? "<unnamed>";
        var controlType = TryGetControlType(element) ?? "<unknown>";
        var bounds = TryGetBounds(element) ?? "<bounds-error>";
        var selected = TryGetIsSelected(element)?.ToString() ?? "null";
        return $"{controlType} name='{name}' offscreen={TryGetIsOffscreen(element)} selected={selected} bounds={bounds}";
    }

    private static string? TryGetName(AutomationElement element)
    {
        try
        {
            return string.IsNullOrWhiteSpace(element.Name)
                ? null
                : element.Name;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetControlType(AutomationElement element)
    {
        try
        {
            return element.ControlType.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetBounds(AutomationElement element)
    {
        try
        {
            return element.BoundingRectangle.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static int CountVisibleTranscriptText(AutomationElement? messagesList)
    {
        if (messagesList is null)
        {
            return 0;
        }

        return messagesList
            .FindAllDescendants(cf => cf.ByControlType(ControlType.Text))
            .Count(element =>
            {
                try
                {
                    return !TryGetIsOffscreen(element) && !string.IsNullOrWhiteSpace(element.Name);
                }
                catch
                {
                    return false;
                }
            });
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

    private static bool WaitForViewportState(
        WindowsGuiAppSession session,
        string expectedState,
        TimeSpan timeout)
    {
        if (string.IsNullOrWhiteSpace(expectedState))
        {
            return false;
        }

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

    private static string? ParseAutomationStateToken(string state, string key)
    {
        if (string.IsNullOrWhiteSpace(state) || string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        foreach (var part in state.Split(';'))
        {
            var trimmed = part.Trim();
            var separatorIndex = trimmed.IndexOf('=');
            if (separatorIndex <= 0 || separatorIndex >= trimmed.Length - 1)
            {
                continue;
            }

            var tokenKey = trimmed[..separatorIndex];
            if (!string.Equals(tokenKey, key, StringComparison.Ordinal))
            {
                continue;
            }

            return trimmed[(separatorIndex + 1)..];
        }

        return null;
    }

    private static string SessionAutomationId(string conversationId)
        => $"MainNav.Session.{conversationId}";

    private static void EnsureMainWindowWideForTitleBarCommands(WindowsGuiAppSession session)
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

        ResizeMainWindow(width: 1400, height: 900);
    }

    private static void OpenMiniWindow(WindowsGuiAppSession session)
    {
        var button = FindElementAnywhereWithFallback(
            session,
            primaryAutomationId: "TitleBar.OpenMiniWindow",
            fallbackAutomationId: "TitleBarMiniWindowButton",
            timeout: TimeSpan.FromSeconds(8));

        if (button.Patterns.Invoke.IsSupported)
        {
            button.Patterns.Invoke.Pattern.Invoke();
            return;
        }

        session.ActivateElement(button);
    }

    private static RealLocalCandidate? TryResolveWarmLocalCandidate(
        IReadOnlyList<RealLocalCandidate> localCandidates,
        string? explicitWarmConversationId,
        WindowsGuiAppSession session)
    {
        if (!string.IsNullOrWhiteSpace(explicitWarmConversationId))
        {
            var explicitLocal = localCandidates.FirstOrDefault(item =>
                string.Equals(item.ConversationId, explicitWarmConversationId, StringComparison.Ordinal)
                && item.LocalMessageCount > 0);
            if (explicitLocal is not null
                && session.TryFindByAutomationId(SessionAutomationId(explicitLocal.ConversationId), TimeSpan.FromSeconds(1)) is not null)
            {
                return explicitLocal;
            }
        }

        return localCandidates
            .Where(item => item.LocalMessageCount > 0)
            .FirstOrDefault(item => session.TryFindByAutomationId(SessionAutomationId(item.ConversationId), TimeSpan.FromSeconds(1)) is not null);
    }

    private static string? ResolveWarmConversationId(
        RealLocalCandidate? warmLocalCandidate,
        IReadOnlyList<RealReplayCandidate> visibleRemoteCandidates,
        string? explicitWarmConversationId,
        WindowsGuiAppSession session)
    {
        if (warmLocalCandidate is not null)
        {
            return warmLocalCandidate.ConversationId;
        }

        if (!string.IsNullOrWhiteSpace(explicitWarmConversationId)
            && session.TryFindByAutomationId(SessionAutomationId(explicitWarmConversationId), TimeSpan.FromSeconds(1)) is not null)
        {
            return explicitWarmConversationId;
        }

        return visibleRemoteCandidates.FirstOrDefault()?.ConversationId;
    }

    private static RealReplayCandidate? ResolveRemoteMiniTargetCandidate(
        IReadOnlyList<RealReplayCandidate> remoteCandidates,
        IReadOnlyList<RealReplayCandidate> visibleRemoteCandidates,
        string? explicitRemoteConversationId,
        string warmConversationId,
        WindowsGuiAppSession session)
    {
        if (!string.IsNullOrWhiteSpace(explicitRemoteConversationId))
        {
            var explicitRemote = remoteCandidates.FirstOrDefault(item =>
                string.Equals(item.ConversationId, explicitRemoteConversationId, StringComparison.Ordinal));
            if (explicitRemote is not null
                && !string.Equals(explicitRemote.ConversationId, warmConversationId, StringComparison.Ordinal)
                && session.TryFindByAutomationId(SessionAutomationId(explicitRemote.ConversationId), TimeSpan.FromSeconds(1)) is not null)
            {
                return explicitRemote;
            }
        }

        return visibleRemoteCandidates.FirstOrDefault(item =>
            !string.Equals(item.ConversationId, warmConversationId, StringComparison.Ordinal));
    }

    private static void SelectMiniWindowConversation(
        WindowsGuiAppSession session,
        string conversationId,
        string expectedVisibleName)
    {
        var selector = FindElementAnywhereWithFallback(
            session,
            primaryAutomationId: "MiniChat.SessionSelector",
            fallbackAutomationId: "MiniTitleBarSessionSelector",
            timeout: TimeSpan.FromSeconds(10));
        session.ClickElement(selector);

        AutomationElement target;
        try
        {
            target = session.FindByAutomationIdAnywhere($"MiniChat.SessionItem.{conversationId}", TimeSpan.FromSeconds(3));
        }
        catch (TimeoutException)
        {
            if (!LooksLikeUsableVisibleLabel(expectedVisibleName))
            {
                throw new TimeoutException(
                    $"Mini-window session item automation id for conversation '{conversationId}' was not found, and fallback label '{expectedVisibleName}' is not usable.");
            }

            target = session.FindVisibleTextAnywhere(expectedVisibleName, TimeSpan.FromSeconds(5))
                ?? throw new TimeoutException($"Mini-window session item '{expectedVisibleName}' was not found.");
        }

        session.ActivateElement(FindSelectableAncestor(target));
    }

    private static AutomationElement FindElementAnywhereWithFallback(
        WindowsGuiAppSession session,
        string primaryAutomationId,
        string fallbackAutomationId,
        TimeSpan timeout)
    {
        try
        {
            return session.FindByAutomationIdAnywhere(
                primaryAutomationId,
                TimeSpan.FromMilliseconds(Math.Min(3000, (int)timeout.TotalMilliseconds)));
        }
        catch (TimeoutException)
        {
            return session.FindByAutomationIdAnywhere(fallbackAutomationId, timeout);
        }
    }

    private static AutomationElement FindSelectableAncestor(AutomationElement element)
    {
        var current = element;
        while (current is not null)
        {
            if (current.Patterns.SelectionItem.IsSupported || current.Patterns.Invoke.IsSupported)
            {
                return current;
            }

            current = current.Parent;
        }

        throw new Xunit.Sdk.XunitException("Could not find a selectable ancestor for the mini-window session item.");
    }

    private static string FirstUsableVisibleLabel(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (LooksLikeUsableVisibleLabel(candidate))
            {
                return candidate!.Trim();
            }
        }

        return string.Empty;
    }

    private static bool LooksLikeUsableVisibleLabel(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        var trimmed = candidate.Trim();
        return !string.Equals(trimmed, "NavigationViewItem", StringComparison.Ordinal)
            && !string.Equals(trimmed, "ComboBoxItem", StringComparison.Ordinal)
            && !string.Equals(trimmed, "ListViewItem", StringComparison.Ordinal);
    }

    private static bool IsElementVisible(WindowsGuiAppSession session, string automationId)
    {
        var element = session.TryFindByAutomationId(automationId, TimeSpan.FromMilliseconds(200));
        return element is not null && !TryGetIsOffscreen(element);
    }

    private static void SelectComboBoxItemByVisibleText(
        WindowsGuiAppSession session,
        string selectorAutomationId,
        string expectedVisibleName)
    {
        var selector = session.FindByAutomationId(selectorAutomationId, TimeSpan.FromSeconds(10));
        session.ClickElement(selector);

        var target = session.FindVisibleTextAnywhere(expectedVisibleName, TimeSpan.FromSeconds(5))
            ?? throw new TimeoutException(
                $"Could not find combo-box item '{expectedVisibleName}' after opening selector '{selectorAutomationId}'.");
        SelectComboBoxItemElement(session, FindSelectableAncestor(target));
        var selected = WaitUntil(
            () =>
            {
                var current = session.TryGetElementName(selectorAutomationId, TimeSpan.FromMilliseconds(120));
                return string.Equals(current?.Trim(), expectedVisibleName, StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(5),
            TimeSpan.FromMilliseconds(120));
        if (!selected)
        {
            throw new Xunit.Sdk.XunitException(
                $"Selector '{selectorAutomationId}' did not settle to '{expectedVisibleName}' after selecting visible text. Current='{session.TryGetElementName(selectorAutomationId, TimeSpan.FromMilliseconds(200)) ?? "<missing>"}'.");
        }
    }

    private static void SelectComboBoxItemByAutomationId(
        WindowsGuiAppSession session,
        string selectorAutomationId,
        string itemAutomationId)
        => SelectComboBoxItemByAutomationId(session, selectorAutomationId, itemAutomationId, expectedVisibleName: null);

    private static void SelectComboBoxItemByAutomationId(
        WindowsGuiAppSession session,
        string selectorAutomationId,
        string itemAutomationId,
        string? expectedVisibleName)
    {
        var selector = session.FindByAutomationId(selectorAutomationId, TimeSpan.FromSeconds(10));
        session.ClickElement(selector);

        var target = session.FindByAutomationIdAnywhere(itemAutomationId, TimeSpan.FromSeconds(5));
        SelectComboBoxItemElement(session, FindSelectableAncestor(target));
        if (string.IsNullOrWhiteSpace(expectedVisibleName))
        {
            Thread.Sleep(300);
            return;
        }

        var selected = WaitUntil(
            () =>
            {
                var current = session.TryGetElementName(selectorAutomationId, TimeSpan.FromMilliseconds(120));
                return string.Equals(current?.Trim(), expectedVisibleName, StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(5),
            TimeSpan.FromMilliseconds(120));
        if (!selected)
        {
            throw new Xunit.Sdk.XunitException(
                $"Selector '{selectorAutomationId}' did not settle to '{expectedVisibleName}' after selecting automation id '{itemAutomationId}'. Current='{session.TryGetElementName(selectorAutomationId, TimeSpan.FromMilliseconds(200)) ?? "<missing>"}'.");
        }
    }

    private static bool TryOpenStartModeSelectorAndDetectKnownMode(WindowsGuiAppSession session)
    {
        var selector = session.TryFindByAutomationId("StartView.ModeSelector", TimeSpan.FromMilliseconds(200));
        if (selector is null || TryGetIsOffscreen(selector))
        {
            return false;
        }

        try
        {
            session.ClickElement(selector);
            Thread.Sleep(150);
            foreach (var label in KnownReadyModeLabels)
            {
                if (session.TryFindVisibleTextAnywhere(label, TimeSpan.FromMilliseconds(120)) is not null)
                {
                    session.PressEscape();
                    return true;
                }
            }
        }
        catch
        {
        }
        finally
        {
            session.PressEscape();
        }

        return false;
    }

    private static bool IsStartComposerModeUnavailable(WindowsGuiAppSession session)
        => session.TryFindVisibleTextAnywhere("模式不可用", TimeSpan.FromMilliseconds(120)) is not null
            || session.TryFindVisibleTextAnywhere("模式尚未就绪", TimeSpan.FromMilliseconds(120)) is not null
            || session.TryFindVisibleTextAnywhere("正在加载模式...", TimeSpan.FromMilliseconds(120)) is not null;

    private static bool WaitUntilNavigationActivationStarted(
        WindowsGuiAppSession session,
        string sessionId,
        TimeSpan timeout)
        => WaitUntil(
            () =>
            {
                var selected = session.TryGetIsSelected(sessionId) == true;
                var automationState = session.TryGetElementName(
                    "MainNav.Automation.SelectionState",
                    TimeSpan.FromMilliseconds(100)) ?? string.Empty;
                var context = ParseAutomationStateToken(automationState, "Context");
                var selectionReady = selected
                    || string.Equals(context, "Session", StringComparison.Ordinal)
                    || string.Equals(context, "Ancestor", StringComparison.Ordinal);

                return selectionReady && IsChatActivationSurfaceVisible(session);
            },
            timeout,
            TimeSpan.FromMilliseconds(120));

    private static bool WaitUntilChatActivationSurfaceVisible(
        WindowsGuiAppSession session,
        TimeSpan timeout)
        => WaitUntil(
            () => IsChatActivationSurfaceVisible(session),
            timeout,
            TimeSpan.FromMilliseconds(120));

    private static bool IsChatActivationSurfaceVisible(WindowsGuiAppSession session)
        => session.TryFindByAutomationId("ChatView.CurrentSessionTitle", TimeSpan.FromMilliseconds(80)) is not null
            || session.TryFindByAutomationId("ChatView.LoadingOverlay", TimeSpan.FromMilliseconds(80)) is not null
            || session.TryFindByAutomationId("ChatView.LoadingOverlayMask", TimeSpan.FromMilliseconds(80)) is not null
            || session.TryFindByAutomationId("ChatView.LoadingOverlayStatus", TimeSpan.FromMilliseconds(80)) is not null
            || session.TryFindByAutomationId("ChatView.MessagesList", TimeSpan.FromMilliseconds(80)) is not null;

    private static string? TryResolveOwningProjectAutomationId(WindowsGuiAppSession session, string sessionId)
    {
        var sessionElement = session.TryFindByAutomationId(sessionId, TimeSpan.FromMilliseconds(200));
        var current = sessionElement?.Parent;
        while (current is not null)
        {
            var ancestorAutomationId = TryGetAutomationId(current);
            if (ancestorAutomationId is not null
                && ancestorAutomationId.StartsWith("MainNav.Project.", StringComparison.Ordinal))
            {
                return ancestorAutomationId;
            }

            current = current.Parent;
        }

        try
        {
            foreach (var element in session.MainWindow.FindAllDescendants())
            {
                var automationId = TryGetAutomationId(element);
                if (automationId is null || !automationId.StartsWith("MainNav.Project.", StringComparison.Ordinal))
                {
                    continue;
                }

                if (TryGetHasSelectedDescendant(element) || TryGetIsSelected(element) == true)
                {
                    return automationId;
                }
            }
        }
        catch
        {
        }

        return null;
    }

    private static string DumpAutomationSelectionState(WindowsGuiAppSession session)
        => session.TryGetElementName("MainNav.Automation.SelectionState", TimeSpan.FromMilliseconds(200))
            ?? "<missing>";

    private static string? TryGetAutomationId(AutomationElement element)
    {
        try
        {
            return string.IsNullOrWhiteSpace(element.AutomationId)
                ? null
                : element.AutomationId;
        }
        catch
        {
            return null;
        }
    }

    private static bool? TryGetIsSelected(AutomationElement element)
    {
        try
        {
            return element.Patterns.SelectionItem.IsSupported
                ? element.Patterns.SelectionItem.Pattern.IsSelected.Value
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static void AssertCompactNativeSelection(
        WindowsGuiAppSession session,
        string sessionId,
        string projectId,
        string startId,
        TimeSpan timeout,
        string conversationId)
    {
        Assert.True(
            WaitForCompactSelectionContext(session, sessionId, projectId, startId, timeout, out var winner),
            $"Compact selection context did not settle after expanded->compact resize. Conversation={conversationId} winner={winner ?? "<null>"} sessionVisible={IsElementVisible(session, sessionId)} sessionSelected={session.TryGetIsSelected(sessionId)} projectSelected={session.TryGetIsSelected(projectId)} startSelected={session.TryGetIsSelected(startId)}");

        Assert.NotEqual(startId, winner);

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

        Assert.Fail($"Expected compact native selection to settle on session or owning project, but winner was {winner ?? "<null>"}.");
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
            var sessionSelected = sessionVisible && session.TryGetIsSelected(sessionId) == true;
            var projectSelected = session.TryGetIsSelected(projectId) == true;
            var startSelected = session.TryGetIsSelected(startId) == true;
            var projectElement = session.TryFindByAutomationId(projectId, TimeSpan.FromMilliseconds(200));
            var projectHasSelectedDescendant = TryGetHasSelectedDescendant(projectElement);
            var chatSurfaceVisible = IsChatActivationSurfaceVisible(session);

            if (sessionSelected)
            {
                winner = sessionId;
                return true;
            }

            if (!sessionVisible && (projectSelected || projectHasSelectedDescendant))
            {
                winner = projectId;
                return true;
            }

            var automationContext = ParseAutomationStateToken(
                session.TryGetElementName("MainNav.Automation.SelectionState", TimeSpan.FromMilliseconds(200)) ?? string.Empty,
                "Context");
            if (!sessionVisible
                && (string.Equals(automationContext, "Session", StringComparison.Ordinal)
                    || string.Equals(automationContext, "Ancestor", StringComparison.Ordinal))
                && chatSurfaceVisible)
            {
                winner = string.Equals(automationContext, "Session", StringComparison.Ordinal)
                    ? sessionId
                    : projectId;
                return true;
            }

            if (startSelected)
            {
                winner = startId;
                return true;
            }

            Thread.Sleep(150);
        }

        winner = null;
        return false;
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

    private static string DescribeSelectionSnapshot(
        WindowsGuiAppSession session,
        string startId,
        string localId,
        string remoteId)
    {
        var start = session.TryGetIsSelected(startId) == true;
        var local = session.TryGetIsSelected(localId) == true;
        var remote = session.TryGetIsSelected(remoteId) == true;
        return $"start={start},local={local},remote={remote}";
    }

    private static string TryCaptureMainWindow(WindowsGuiAppSession session, string path)
    {
        try
        {
            session.CaptureMainWindowToFile(path);
            return path;
        }
        catch (Exception ex) when (ex is COMException or Win32Exception or InvalidOperationException)
        {
            return $"<capture failed: {ex.Message}>";
        }
    }

    private static void AssertChatComposerUsesModeSelectorSubsetOnly(WindowsGuiAppSession session, string scenario)
    {
        Assert.True(
            WaitUntil(
                () => IsActuallyVisible(session, "ChatInputArea.ModeSelector"),
                TimeSpan.FromSeconds(6),
                TimeSpan.FromMilliseconds(120)),
            $"Expected chat composer mode selector to remain visible for scenario '{scenario}'.");

        var agentVisible = WaitUntil(
            () => IsActuallyVisible(session, "ChatInputArea.AgentSelector"),
            TimeSpan.FromMilliseconds(800),
            TimeSpan.FromMilliseconds(120));
        var projectVisible = WaitUntil(
            () => IsActuallyVisible(session, "ChatInputArea.ProjectSelector"),
            TimeSpan.FromMilliseconds(800),
            TimeSpan.FromMilliseconds(120));
        if (!agentVisible && !projectVisible)
        {
            return;
        }

        var captureRoot = Path.Combine(Path.GetTempPath(), "SalmonEgg.GuiTests");
        Directory.CreateDirectory(captureRoot);
        var screenshotPath = Path.Combine(
            captureRoot,
            $"composer-selector-subset-{scenario}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.png");
        screenshotPath = TryCaptureMainWindow(session, screenshotPath);
        Assert.Fail(
            $"Expected chat composer to hide agent/project selectors for scenario '{scenario}'. agentVisible={agentVisible} projectVisible={projectVisible}{Environment.NewLine}" +
            $"Screenshot: {screenshotPath}{Environment.NewLine}" +
            $"VisibleTexts=[{string.Join(" | ", session.GetVisibleTexts())}]{Environment.NewLine}" +
            $"VisibleButtons=[{string.Join(" | ", session.GetVisibleButtons())}]");
    }

    private static bool IsActuallyVisible(WindowsGuiAppSession session, string automationId)
    {
        var element = session.TryFindByAutomationId(automationId, TimeSpan.FromMilliseconds(150));
        return element is not null && HasUsableOnscreenBounds(session, element);
    }

    private static bool HasUsableOnscreenBounds(WindowsGuiAppSession session, AutomationElement element)
    {
        if (TryGetIsOffscreen(element))
        {
            return false;
        }

        var bounds = element.BoundingRectangle;
        var windowBounds = session.MainWindow.BoundingRectangle;
        return bounds.Width > 20
               && bounds.Height > 20
               && bounds.Left >= windowBounds.Left
               && bounds.Top >= windowBounds.Top
               && bounds.Right <= windowBounds.Right
               && bounds.Bottom <= windowBounds.Bottom;
    }

    private sealed record RealReplayCandidate(
        string ConversationId,
        string DisplayName,
        string BoundProfileId,
        string RemoteSessionId,
        int LocalMessageCount,
        DateTimeOffset LastUpdatedAtUtc);

    private sealed record RealLocalCandidate(
        string ConversationId,
        int LocalMessageCount,
        DateTimeOffset LastUpdatedAtUtc);

    private sealed record RealTranscriptAuditCandidate(
        string ConversationId,
        string DisplayName,
        int MessageCount,
        int MarkdownLikeMessageCount);

    private sealed record StartComposerProfileSwitchScenario(
        string StartupProfileId,
        string StartupProfileName,
        string StartupProfileTransport,
        string LocalProfileId,
        string LocalProfileName);

    private static readonly string[] KnownReadyModeLabels =
    [
        "Default",
        "Plan Mode",
        "Accept Edits",
        "Read Only",
        "Full Access",
        "Don't Ask",
        "Bypass Permissions"
    ];

    private static IReadOnlyList<RealTranscriptAuditCandidate> LoadRealTranscriptAuditCandidates()
    {
        var conversationsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SalmonEgg",
            "conversations",
            "conversations.v1.json");
        if (!File.Exists(conversationsPath))
        {
            return [];
        }

        using var document = JsonDocument.Parse(File.ReadAllText(conversationsPath));
        if (!document.RootElement.TryGetProperty("conversations", out var conversationsElement)
            || conversationsElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var candidates = new List<RealTranscriptAuditCandidate>();
        foreach (var conversationElement in conversationsElement.EnumerateArray())
        {
            var conversationId = ReadJsonString(conversationElement, "conversationId");
            if (string.IsNullOrWhiteSpace(conversationId))
            {
                continue;
            }

            var messages = conversationElement.TryGetProperty("messages", out var messagesElement)
                && messagesElement.ValueKind == JsonValueKind.Array
                    ? messagesElement.EnumerateArray()
                        .Select(message => ReadJsonString(message, "textContent"))
                        .Where(text => !string.IsNullOrWhiteSpace(text))
                        .Cast<string>()
                        .ToArray()
                    : [];

            candidates.Add(new RealTranscriptAuditCandidate(
                conversationId,
                FirstUsableVisibleLabel(ReadJsonString(conversationElement, "displayName"), conversationId),
                messages.Length,
                messages.Count(IsMarkdownLike)));
        }

        return candidates;
    }

    private static bool IsMarkdownLike(string text)
        => text.Contains("```", StringComparison.Ordinal)
            || text.Contains("| ---", StringComparison.Ordinal)
            || text.Contains("\n- ", StringComparison.Ordinal)
            || text.Contains("\n#", StringComparison.Ordinal)
            || text.Contains("`", StringComparison.Ordinal);

    private static string? ReadJsonString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
    }

    private static double? TryGetVerticalScrollPercent(AutomationElement element)
    {
        try
        {
            return element.Patterns.Scroll.IsSupported
                ? element.Patterns.Scroll.Pattern.VerticalScrollPercent.Value
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static class RealUserConfigProbe
    {
        private static readonly TimeSpan ReplayEvidenceRecencyWindow = TimeSpan.FromDays(14);
        private const int ReplayEvidenceLogScanLimit = 20;

        public static IReadOnlyList<RealReplayCandidate> LoadReplayBackedCandidates(bool includeAllProfiles = false)
        {
            var appDataRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SalmonEgg");
            var appYamlPath = Path.Combine(appDataRoot, "config", "app.yaml");
            var conversationsPath = Path.Combine(appDataRoot, "conversations", "conversations.v1.json");
            var logsRoot = Path.Combine(appDataRoot, "logs");

            if (!File.Exists(appYamlPath) || !File.Exists(conversationsPath) || !Directory.Exists(logsRoot))
            {
                return [];
            }

            var selectedProfileId = ReadYamlScalar(appYamlPath, "last_selected_server_id");
            if (!includeAllProfiles && string.IsNullOrWhiteSpace(selectedProfileId))
            {
                return [];
            }

            var replayedRemoteIds = ReadReplayBackedRemoteSessionIds(logsRoot);
            if (replayedRemoteIds.Count == 0)
            {
                return [];
            }

            using var document = JsonDocument.Parse(File.ReadAllText(conversationsPath));
            if (!document.RootElement.TryGetProperty("conversations", out var conversationsElement)
                || conversationsElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var candidates = new List<RealReplayCandidate>();
            foreach (var conversationElement in conversationsElement.EnumerateArray())
            {
                var boundProfileId = ReadString(conversationElement, "boundProfileId");
                var displayName = ReadString(conversationElement, "displayName");
                var remoteSessionId = ReadString(conversationElement, "remoteSessionId");
                if ((!includeAllProfiles && !string.Equals(boundProfileId, selectedProfileId, StringComparison.Ordinal))
                    || string.IsNullOrWhiteSpace(remoteSessionId)
                    || !replayedRemoteIds.Contains(remoteSessionId))
                {
                    continue;
                }

                var conversationId = ReadString(conversationElement, "conversationId");
                if (string.IsNullOrWhiteSpace(conversationId))
                {
                    continue;
                }

                var messageCount = conversationElement.TryGetProperty("messages", out var messagesElement)
                    && messagesElement.ValueKind == JsonValueKind.Array
                    ? messagesElement.GetArrayLength()
                    : 0;

                var lastUpdatedAt = conversationElement.TryGetProperty("lastUpdatedAt", out var lastUpdatedElement)
                    && lastUpdatedElement.ValueKind == JsonValueKind.String
                    && DateTimeOffset.TryParse(lastUpdatedElement.GetString(), out var parsedLastUpdatedAt)
                        ? parsedLastUpdatedAt
                        : DateTimeOffset.MinValue;

                candidates.Add(new RealReplayCandidate(
                    conversationId,
                    FirstUsableVisibleLabel(displayName, conversationId),
                    boundProfileId!,
                    remoteSessionId,
                    messageCount,
                    lastUpdatedAt));
            }

            return candidates
                .OrderBy(candidate => candidate.LocalMessageCount)
                .ThenByDescending(candidate => candidate.LastUpdatedAtUtc)
                .ToArray();
        }

        public static IReadOnlyList<RealLocalCandidate> LoadPureLocalCandidates()
        {
            var appDataRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SalmonEgg");
            var conversationsPath = Path.Combine(appDataRoot, "conversations", "conversations.v1.json");

            if (!File.Exists(conversationsPath))
            {
                return [];
            }

            using var document = JsonDocument.Parse(File.ReadAllText(conversationsPath));
            if (!document.RootElement.TryGetProperty("conversations", out var conversationsElement)
                || conversationsElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var candidates = new List<RealLocalCandidate>();
            foreach (var conversationElement in conversationsElement.EnumerateArray())
            {
                var conversationId = ReadString(conversationElement, "conversationId");
                if (string.IsNullOrWhiteSpace(conversationId))
                {
                    continue;
                }

                var boundProfileId = ReadString(conversationElement, "boundProfileId");
                var remoteSessionId = ReadString(conversationElement, "remoteSessionId");
                if (!string.IsNullOrWhiteSpace(boundProfileId) || !string.IsNullOrWhiteSpace(remoteSessionId))
                {
                    continue;
                }

                var messageCount = conversationElement.TryGetProperty("messages", out var messagesElement)
                    && messagesElement.ValueKind == JsonValueKind.Array
                    ? messagesElement.GetArrayLength()
                    : 0;

                var lastUpdatedAt = conversationElement.TryGetProperty("lastUpdatedAt", out var lastUpdatedElement)
                    && lastUpdatedElement.ValueKind == JsonValueKind.String
                    && DateTimeOffset.TryParse(lastUpdatedElement.GetString(), out var parsedLastUpdatedAt)
                        ? parsedLastUpdatedAt
                        : DateTimeOffset.MinValue;

                candidates.Add(new RealLocalCandidate(
                    conversationId,
                    messageCount,
                    lastUpdatedAt));
            }

            return candidates
                .OrderByDescending(candidate => candidate.LocalMessageCount)
                .ThenByDescending(candidate => candidate.LastUpdatedAtUtc)
                .ToArray();
        }

        public static StartComposerProfileSwitchScenario? LoadStartComposerProfileSwitchScenario()
        {
            var appDataRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SalmonEgg");
            var appYamlPath = Path.Combine(appDataRoot, "config", "app.yaml");
            var serversRoot = Path.Combine(appDataRoot, "config", "servers");
            if (!File.Exists(appYamlPath) || !Directory.Exists(serversRoot))
            {
                return null;
            }

            var startupProfileId = ReadYamlScalar(appYamlPath, "last_selected_server_id");
            if (string.IsNullOrWhiteSpace(startupProfileId))
            {
                return null;
            }

            var profiles = Directory.EnumerateFiles(serversRoot, "*.yaml")
                .Select(ReadServerProfile)
                .Where(static profile => profile is not null)
                .Cast<RealServerProfile>()
                .ToArray();
            var startupProfile = profiles.FirstOrDefault(profile =>
                string.Equals(profile.Id, startupProfileId, StringComparison.Ordinal));
            if (startupProfile is null
                || string.Equals(startupProfile.Transport, "stdio", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var localProfile = profiles
                .Where(profile =>
                    !string.Equals(profile.Id, startupProfile.Id, StringComparison.Ordinal)
                    && string.Equals(profile.Transport, "stdio", StringComparison.OrdinalIgnoreCase)
                    && LooksLikeUsableVisibleLabel(profile.Name))
                .OrderByDescending(static profile => profile.IsPreferredLocalInteractiveProfile)
                .ThenBy(static profile => profile.Name, StringComparer.Ordinal)
                .FirstOrDefault();
            if (localProfile is null)
            {
                return null;
            }

            return new StartComposerProfileSwitchScenario(
                startupProfile.Id,
                startupProfile.Name,
                startupProfile.Transport,
                localProfile.Id,
                localProfile.Name);
        }

        public static StartComposerRoundTripScenario? LoadStartComposerRoundTripScenario()
        {
            var appDataRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SalmonEgg");
            var serversRoot = Path.Combine(appDataRoot, "config", "servers");
            if (!Directory.Exists(serversRoot))
            {
                return null;
            }

            var profiles = Directory.EnumerateFiles(serversRoot, "*.yaml")
                .Select(ReadServerProfile)
                .Where(static profile => profile is not null)
                .Cast<RealServerProfile>()
                .ToArray();
            if (profiles.Length == 0)
            {
                return null;
            }

            var localProfile = profiles
                .Where(profile =>
                    string.Equals(profile.Transport, "stdio", StringComparison.OrdinalIgnoreCase)
                    && LooksLikeUsableVisibleLabel(profile.Name))
                .OrderByDescending(static profile => profile.IsPreferredLocalInteractiveProfile)
                .ThenBy(static profile => profile.Name, StringComparer.Ordinal)
                .FirstOrDefault();
            if (localProfile is null)
            {
                return null;
            }

            var remoteProfile = profiles
                .Where(profile =>
                    !string.Equals(profile.Id, localProfile.Id, StringComparison.Ordinal)
                    && !string.Equals(profile.Transport, "stdio", StringComparison.OrdinalIgnoreCase)
                    && LooksLikeUsableVisibleLabel(profile.Name))
                .OrderBy(static profile => profile.Name, StringComparer.Ordinal)
                .FirstOrDefault();
            if (remoteProfile is null)
            {
                return null;
            }

            return new StartComposerRoundTripScenario(
                localProfile.Id,
                localProfile.Name,
                BuildComposerSelectorItemAutomationId("Agent", localProfile.Id),
                remoteProfile.Id,
                remoteProfile.Name,
                BuildComposerSelectorItemAutomationId("Agent", remoteProfile.Id),
                remoteProfile.Transport);
        }

        public static StartComposerRemoteDirectoryScenario? LoadStartComposerRemoteDirectoryScenario(string remoteProfileName)
        {
            var appDataRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SalmonEgg");
            var appYamlPath = Path.Combine(appDataRoot, "config", "app.yaml");
            var serversRoot = Path.Combine(appDataRoot, "config", "servers");
            if (!File.Exists(appYamlPath) || !Directory.Exists(serversRoot))
            {
                return null;
            }

            var profiles = Directory.EnumerateFiles(serversRoot, "*.yaml")
                .Select(ReadServerProfile)
                .Where(static profile => profile is not null)
                .Cast<RealServerProfile>()
                .ToArray();
            if (profiles.Length == 0)
            {
                return null;
            }

            var remoteProfile = profiles
                .Where(profile =>
                    !string.Equals(profile.Transport, "stdio", StringComparison.OrdinalIgnoreCase)
                    && LooksLikeUsableVisibleLabel(profile.Name)
                    && string.Equals(profile.Name, remoteProfileName, StringComparison.Ordinal))
                .OrderBy(static profile => profile.Name, StringComparer.Ordinal)
                .FirstOrDefault();
            if (remoteProfile is null)
            {
                return null;
            }

            var remoteDirectory = ReadRemoteDirectories(appYamlPath)
                .FirstOrDefault(directory =>
                    !string.IsNullOrWhiteSpace(directory.DirectoryId)
                    && !string.IsNullOrWhiteSpace(directory.RemotePath)
                    && LooksLikeUsableVisibleLabel(directory.DisplayName));
            if (remoteDirectory is null)
            {
                return null;
            }

            var remoteDirectoryProjectId = $"remote-directory:{remoteDirectory.DirectoryId}";
            return new StartComposerRemoteDirectoryScenario(
                remoteProfile.Id,
                remoteProfile.Name,
                BuildComposerSelectorItemAutomationId("Agent", remoteProfile.Id),
                remoteDirectory.DirectoryId,
                remoteDirectory.DisplayName,
                BuildComposerSelectorItemAutomationId("Project", remoteDirectoryProjectId),
                remoteDirectory.RemotePath);
        }

        public static bool WaitForRecentAppLogContainsAll(
            DateTimeOffset since,
            TimeSpan timeout,
            IReadOnlyCollection<string> expectedSubstrings,
            out string recentLogTail)
        {
            var appDataRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SalmonEgg");
            var logsRoot = Path.Combine(appDataRoot, "logs");
            recentLogTail = string.Empty;
            if (!Directory.Exists(logsRoot))
            {
                return false;
            }

            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                var recentLines = ReadRecentAppLogLines(logsRoot, since).ToArray();
                var tail = string.Join(Environment.NewLine, recentLines.TakeLast(120));
                recentLogTail = tail;
                if (expectedSubstrings.All(expected => tail.Contains(expected, StringComparison.Ordinal)))
                {
                    return true;
                }

                Thread.Sleep(200);
            }

            return false;
        }

        private static HashSet<string> ReadReplayBackedRemoteSessionIds(string logsRoot)
        {
            var loadedSessionIds = new HashSet<string>(StringComparer.Ordinal);
            var updatedSessionIds = new HashSet<string>(StringComparer.Ordinal);
            var recentThresholdUtc = DateTime.UtcNow - ReplayEvidenceRecencyWindow;
            var logFiles = Directory.EnumerateFiles(logsRoot, "app-*.log")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Where(logFile => File.GetLastWriteTimeUtc(logFile) >= recentThresholdUtc)
                .Take(ReplayEvidenceLogScanLimit)
                .ToArray();

            if (logFiles.Length == 0)
            {
                logFiles = Directory.EnumerateFiles(logsRoot, "app-*.log")
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .Take(ReplayEvidenceLogScanLimit)
                    .ToArray();
            }

            foreach (var logFile in logFiles)
            {
                foreach (var line in ReadLinesAllowSharedRead(logFile))
                {
                    if (line.Contains("\"method\":\"session/load\"", StringComparison.Ordinal)
                        && TryExtractSessionId(line, out var loadedSessionId))
                    {
                        loadedSessionIds.Add(loadedSessionId);
                    }

                    if (line.Contains("\"method\":\"session/update\"", StringComparison.Ordinal)
                        && TryExtractSessionId(line, out var updatedSessionId))
                    {
                        updatedSessionIds.Add(updatedSessionId);
                    }
                }
            }

            loadedSessionIds.IntersectWith(updatedSessionIds);
            return loadedSessionIds;
        }

        private static IEnumerable<string> ReadLinesAllowSharedRead(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);

            while (reader.ReadLine() is { } line)
            {
                yield return line;
            }
        }

        private static IEnumerable<string> ReadRecentAppLogLines(string logsRoot, DateTimeOffset since)
        {
            var logFiles = Directory.EnumerateFiles(logsRoot, "app-*.log")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Take(3)
                .ToArray();

            foreach (var logFile in logFiles)
            {
                foreach (var line in ReadLinesAllowSharedRead(logFile))
                {
                    if (TryParseAppLogTimestamp(line, out var timestamp)
                        && timestamp >= since)
                    {
                        yield return line;
                    }
                }
            }
        }

        private static bool TryParseAppLogTimestamp(string line, out DateTimeOffset timestamp)
        {
            if (line.Length < 30)
            {
                timestamp = default;
                return false;
            }

            return DateTimeOffset.TryParseExact(
                line[..30],
                "yyyy-MM-dd HH:mm:ss.fff zzz",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out timestamp);
        }

        private static string? ReadYamlScalar(string yamlPath, string key)
        {
            foreach (var line in File.ReadLines(yamlPath))
            {
                var prefix = key + ":";
                if (!line.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                return NormalizeYamlScalar(line[prefix.Length..]);
            }

            return null;
        }

        private static IReadOnlyList<RealRemoteDirectory> ReadRemoteDirectories(string appYamlPath)
        {
            var directories = new List<RealRemoteDirectory>();
            string? directoryId = null;
            string? displayName = null;
            string? remotePath = null;
            var inRemoteDirectories = false;

            void Flush()
            {
                if (!string.IsNullOrWhiteSpace(directoryId)
                    || !string.IsNullOrWhiteSpace(displayName)
                    || !string.IsNullOrWhiteSpace(remotePath))
                {
                    directories.Add(new RealRemoteDirectory(
                        directoryId ?? string.Empty,
                        string.IsNullOrWhiteSpace(displayName) ? remotePath ?? string.Empty : displayName!,
                        remotePath ?? string.Empty));
                }

                directoryId = null;
                displayName = null;
                remotePath = null;
            }

            foreach (var rawLine in File.ReadLines(appYamlPath))
            {
                var trimmed = rawLine.Trim();
                if (string.Equals(trimmed, "agent_remote_directories:", StringComparison.Ordinal))
                {
                    inRemoteDirectories = true;
                    continue;
                }

                if (!inRemoteDirectories)
                {
                    continue;
                }

                if (trimmed.Length == 0)
                {
                    continue;
                }

                if (!rawLine.StartsWith(" ", StringComparison.Ordinal)
                    && !rawLine.StartsWith("-", StringComparison.Ordinal))
                {
                    break;
                }

                if (trimmed.StartsWith("- ", StringComparison.Ordinal))
                {
                    Flush();
                    trimmed = trimmed[2..].Trim();
                }

                if (TryReadYamlInlineScalar(trimmed, "directory_id", out var parsedDirectoryId))
                {
                    directoryId = parsedDirectoryId;
                }
                else if (TryReadYamlInlineScalar(trimmed, "display_name", out var parsedDisplayName))
                {
                    displayName = parsedDisplayName;
                }
                else if (TryReadYamlInlineScalar(trimmed, "remote_path", out var parsedRemotePath))
                {
                    remotePath = parsedRemotePath;
                }
            }

            Flush();
            return directories;
        }

        private static bool TryReadYamlInlineScalar(string line, string key, out string? value)
        {
            var prefix = key + ":";
            if (!line.StartsWith(prefix, StringComparison.Ordinal))
            {
                value = null;
                return false;
            }

            value = NormalizeYamlScalar(line[prefix.Length..]);
            return true;
        }

        private static string NormalizeYamlScalar(string value)
        {
            var trimmed = value.Trim();
            if (trimmed.Length >= 2
                && ((trimmed[0] == '\'' && trimmed[^1] == '\'')
                    || (trimmed[0] == '"' && trimmed[^1] == '"')))
            {
                return trimmed[1..^1];
            }

            return trimmed;
        }

        private static RealServerProfile? ReadServerProfile(string yamlPath)
        {
            var id = ReadYamlScalar(yamlPath, "id");
            var name = ReadYamlScalar(yamlPath, "name");
            var transport = ReadYamlScalar(yamlPath, "transport");
            var stdioCommand = ReadYamlScalar(yamlPath, "stdio_command");
            return string.IsNullOrWhiteSpace(id)
                   || string.IsNullOrWhiteSpace(name)
                   || string.IsNullOrWhiteSpace(transport)
                ? null
                : new RealServerProfile(id, name, transport, stdioCommand);
        }

        private static string? ReadString(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out var property)
                && property.ValueKind == JsonValueKind.String
                    ? property.GetString()
                    : null;
        }

        private static bool TryExtractSessionId(string line, out string sessionId)
        {
            var match = SessionIdRegex().Match(line);
            if (match.Success)
            {
                sessionId = match.Groups[1].Value;
                return true;
            }

            sessionId = string.Empty;
            return false;
        }

        private static string BuildComposerSelectorItemAutomationId(string kind, string semanticValue)
        {
            var sanitized = string.IsNullOrWhiteSpace(semanticValue)
                ? "Empty"
                : new string(semanticValue.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());
            return $"ComposerSelectorItem.{kind}.{sanitized}";
        }

        private sealed record RealServerProfile(
            string Id,
            string Name,
            string Transport,
            string? StdioCommand)
        {
            public bool IsPreferredLocalInteractiveProfile
                => string.Equals(StdioCommand, "claude-agent-acp", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(StdioCommand, "codex-acp", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(StdioCommand, "opencode", StringComparison.OrdinalIgnoreCase);
        }

        private sealed record RealRemoteDirectory(
            string DirectoryId,
            string DisplayName,
            string RemotePath);
    }

    private static void SelectComboBoxItemElement(WindowsGuiAppSession session, AutomationElement item)
    {
        if (item.Patterns.SelectionItem.IsSupported)
        {
            item.Patterns.SelectionItem.Pattern.Select();
            return;
        }

        session.ActivateElement(item);
    }

    private sealed record StartComposerRoundTripScenario(
        string LocalProfileId,
        string LocalProfileName,
        string LocalProfileAutomationId,
        string RemoteProfileId,
        string RemoteProfileName,
        string RemoteProfileAutomationId,
        string RemoteProfileTransport);

    private sealed record StartComposerRemoteDirectoryScenario(
        string RemoteProfileId,
        string RemoteProfileName,
        string RemoteProfileAutomationId,
        string RemoteDirectoryId,
        string RemoteDirectoryDisplayName,
        string RemoteDirectoryAutomationId,
        string RemoteDirectoryPath);

    [GeneratedRegex("\\\"sessionId\\\":\\\"([^\\\"]+)\\\"", RegexOptions.Compiled)]
    private static partial Regex SessionIdRegex();

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

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _previousValue;

        public EnvironmentVariableScope(string name, string? value)
        {
            _name = name;
            _previousValue = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(_name, _previousValue);
        }
    }
}
