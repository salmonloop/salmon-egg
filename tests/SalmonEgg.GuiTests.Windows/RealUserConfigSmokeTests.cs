using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using FlaUI.Core.Definitions;
using FlaUI.Core.AutomationElements;

namespace SalmonEgg.GuiTests.Windows;

public sealed partial class RealUserConfigSmokeTests
{
    [SkippableFact]
    public void SelectRemoteBoundSession_WithSlowSessionLoad_AutoScrollsToLatestMessageAfterHydration()
    {
        GuiTestGate.RequireEnabled();

        var candidate = RealUserConfigProbe.LoadReplayBackedCandidates()
            .OrderByDescending(item => item.LocalMessageCount)
            .FirstOrDefault(item => item.LocalMessageCount >= 10);
        Skip.If(candidate is null, "No replay-backed remote conversation with enough local transcript history was found to validate bottom auto-scroll.");

        var lastTranscriptText = RealUserConfigProbe.TryLoadLastTranscriptText(candidate.ConversationId);
        Skip.If(string.IsNullOrWhiteSpace(lastTranscriptText), $"Conversation {candidate.ConversationId} has no last transcript text to assert against.");

        using var slowLoad = new EnvironmentVariableScope("SALMONEGG_GUI_SLOW_SESSION_LOAD_MS", "2000");
        using var session = WindowsGuiAppSession.LaunchFresh();

        var startItem = session.FindByAutomationId("MainNav.Start", TimeSpan.FromSeconds(10));
        session.ActivateElement(startItem);
        Thread.Sleep(500);

        var sessionItem = session.FindByAutomationId(SessionAutomationId(candidate.ConversationId), TimeSpan.FromSeconds(10));
        session.ActivateElement(sessionItem);

        var sawOverlayStatus = session.WaitUntilVisible("ChatView.LoadingOverlayStatus", TimeSpan.FromSeconds(10));
        Assert.True(sawOverlayStatus, $"Slow remote hydration never exposed ChatView.LoadingOverlayStatus for conversation {candidate.ConversationId}.");

        var overlayHidden = session.WaitUntilHidden("ChatView.LoadingOverlay", TimeSpan.FromSeconds(25));
        Assert.True(overlayHidden, $"Slow remote hydration overlay did not finish for conversation {candidate.ConversationId}.");

        var messagesList = session.FindByAutomationId("ChatView.MessagesList", TimeSpan.FromSeconds(10));
        var lastMessageVisible = session.FindVisibleText(
            lastTranscriptText!,
            messagesList,
            TimeSpan.FromSeconds(8));

        Assert.NotNull(lastMessageVisible);
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
            var headerVisible = session.TryFindByAutomationId("ChatView.CurrentSessionNameButton", TimeSpan.FromMilliseconds(100)) is not null;
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

        while (DateTime.UtcNow < deadline)
        {
            var blockingMaskVisible = session.TryFindByAutomationId("ChatView.LoadingOverlayMask", TimeSpan.FromMilliseconds(100)) is not null;
            var overlayStatusVisible = session.TryFindByAutomationId("ChatView.LoadingOverlayStatus", TimeSpan.FromMilliseconds(100)) is not null;
            var header = session.TryFindByAutomationId("ChatView.CurrentSessionNameButton", TimeSpan.FromMilliseconds(100));
            var messagesList = session.TryFindByAutomationId("ChatView.MessagesList", TimeSpan.FromMilliseconds(100));
            var visibleTranscriptTextCount = CountVisibleTranscriptText(messagesList);
            var selected = session.TryGetIsSelected(SessionAutomationId(candidate.ConversationId));

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
                session.MainWindow.CaptureToFile(prematureDismissalCapturePath);
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
                session.MainWindow.CaptureToFile(persistentMaskCapturePath);
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
            sawTranscriptVisible,
            $"Real-config smoke did not observe any transcript content for conversation {candidate.ConversationId} within the timeout window.{Environment.NewLine}{failureDetails}");

        Assert.True(
            maskDismissedAfterTranscript,
            $"Blocking loading mask remained visible after transcript content was already visible for conversation {candidate.ConversationId}.{Environment.NewLine}Capture: {persistentMaskCapturePath ?? "<none>"}{Environment.NewLine}{failureDetails}");
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
            var headerVisible = session.TryFindByAutomationId("ChatView.CurrentSessionNameButton", TimeSpan.FromMilliseconds(100)) is not null;

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
                session.MainWindow.CaptureToFile(capturePath);
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
        var headerVisible = session.TryFindByAutomationId("ChatView.CurrentSessionNameButton", TimeSpan.FromSeconds(4)) is not null;
        timeline.Add($"{DateTime.UtcNow:HH:mm:ss.fff} final-local selected={localSelected} header={headerVisible}");

        if (!(startSelected && startViewVisible && localSelected && headerVisible))
        {
            var captureRoot = Path.Combine(Path.GetTempPath(), "SalmonEgg.GuiTests");
            Directory.CreateDirectory(captureRoot);
            var capturePath = Path.Combine(
                captureRoot,
                $"random-switch-interactivity-{DateTime.UtcNow:yyyyMMddHHmmssfff}.png");
            session.MainWindow.CaptureToFile(capturePath);

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

    private static int CountVisibleTranscriptText(AutomationElement? messagesList)
    {
        if (messagesList is null)
        {
            return 0;
        }

        return messagesList
            .FindAllDescendants(cf => cf.ByControlType(ControlType.Text))
            .Count(element => !TryGetIsOffscreen(element) && !string.IsNullOrWhiteSpace(element.Name));
    }

    private static string SessionAutomationId(string conversationId)
        => $"MainNav.Session.{conversationId}";

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

    private sealed record RealReplayCandidate(
        string ConversationId,
        string RemoteSessionId,
        int LocalMessageCount,
        DateTimeOffset LastUpdatedAtUtc);

    private sealed record RealLocalCandidate(
        string ConversationId,
        int LocalMessageCount,
        DateTimeOffset LastUpdatedAtUtc);

    private static class RealUserConfigProbe
    {
        private static readonly TimeSpan ReplayEvidenceRecencyWindow = TimeSpan.FromDays(14);
        private const int ReplayEvidenceLogScanLimit = 20;

        public static IReadOnlyList<RealReplayCandidate> LoadReplayBackedCandidates()
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
            if (string.IsNullOrWhiteSpace(selectedProfileId))
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
                var remoteSessionId = ReadString(conversationElement, "remoteSessionId");
                if (!string.Equals(boundProfileId, selectedProfileId, StringComparison.Ordinal)
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

        public static string? TryLoadLastTranscriptText(string conversationId)
        {
            if (string.IsNullOrWhiteSpace(conversationId))
            {
                return null;
            }

            var appDataRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SalmonEgg");
            var conversationsPath = Path.Combine(appDataRoot, "conversations", "conversations.v1.json");
            if (!File.Exists(conversationsPath))
            {
                return null;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(conversationsPath));
            if (!document.RootElement.TryGetProperty("conversations", out var conversationsElement)
                || conversationsElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var conversationElement in conversationsElement.EnumerateArray())
            {
                if (!string.Equals(ReadString(conversationElement, "conversationId"), conversationId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!conversationElement.TryGetProperty("messages", out var messagesElement)
                    || messagesElement.ValueKind != JsonValueKind.Array)
                {
                    return null;
                }

                foreach (var messageElement in messagesElement.EnumerateArray().Reverse())
                {
                    var text = ReadString(messageElement, "textContent");
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text;
                    }
                }

                return null;
            }

            return null;
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

        private static string? ReadYamlScalar(string yamlPath, string key)
        {
            foreach (var line in File.ReadLines(yamlPath))
            {
                var prefix = key + ":";
                if (!line.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                return line[prefix.Length..].Trim();
            }

            return null;
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
    }

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
