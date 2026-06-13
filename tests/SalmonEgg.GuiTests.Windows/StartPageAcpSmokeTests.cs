using System.Text;
using System.Text.Json;
using FlaUI.Core.AutomationElements;

namespace SalmonEgg.GuiTests.Windows;

public sealed class StartPageAcpSmokeTests
{
    private const string RemoteProfileId = "gui-mock-acp-profile";
    private const string RemoteProfileName = "GUI Remote Mock Agent";
    private const string LocalProfileId = "gui-local-stdio-profile";
    private const string LocalProfileName = "GUI Local Stdio Agent";

    private static readonly string[] ReadyModeLabels = ["Agent 01", "Planner 01"];
    private static readonly string[] UnavailableModeLabels =
    [
        "Mode unavailable",
        "Mode not ready",
        "模式不可用",
        "模式尚未就绪"
    ];

    private static readonly string[] RemoteBlockedErrorLabels =
    [
        "Select a project or configure a remote path mapping before creating a remote session.",
        "Unable to load session configuration. Check the connection and try again."
    ];

    [SkippableFact]
    public void StartupRemoteProfile_CannotPrepareDraft_ThenSwitchingToLocalStdio_RecoversReadyModes()
    {
        using var appData = StartPageAcpSmokeData.CreateMappedProjectScenario(
            startupProfileId: RemoteProfileId,
            remoteScenario: new GuiAppDataScope.MockAcpHarnessScenario
            {
                TransportKind = GuiAppDataScope.MockAcpTransportKind.WebSocket,
                InitializeBehavior = GuiAppDataScope.MockAcpInitializeBehavior.NoResponse
            });
        using var session = WindowsGuiAppSession.LaunchFresh();

        OpenStartPage(session);

        Assert.True(
            WaitUntilRemoteDraftBlocked(session, appData, TimeSpan.FromSeconds(20)),
            BuildFailureMessage("Remote startup profile never settled into an unavailable state.", session, appData));

        SelectAgentProfile(session, LocalProfileName, LocalProfileId);

        Assert.True(
            WaitUntilStartModeReady(session, TimeSpan.FromSeconds(20)),
            BuildFailureMessage("Switching from the startup remote profile to local stdio did not recover ready modes.", session, appData));
    }

    [SkippableFact]
    public void LocalToRemoteToLocalStartPageRoundTrip_RecoversReadyModes()
    {
        using var appData = StartPageAcpSmokeData.CreateMappedProjectScenario(
            startupProfileId: LocalProfileId,
            remoteScenario: new GuiAppDataScope.MockAcpHarnessScenario
            {
                TransportKind = GuiAppDataScope.MockAcpTransportKind.WebSocket,
                InitializeBehavior = GuiAppDataScope.MockAcpInitializeBehavior.NoResponse
            });
        using var session = WindowsGuiAppSession.LaunchFresh();

        OpenStartPage(session);

        Assert.True(
            WaitUntilStartModeReady(session, TimeSpan.FromSeconds(20)),
            BuildFailureMessage("Local stdio startup did not expose ready modes.", session, appData));

        SelectAgentProfile(session, RemoteProfileName, RemoteProfileId);

        Assert.True(
            WaitUntilRemoteDraftBlocked(session, appData, TimeSpan.FromSeconds(20)),
            BuildFailureMessage("Switching from local stdio to remote never settled into the expected unavailable state.", session, appData));

        SelectAgentProfile(session, LocalProfileName, LocalProfileId);

        var recovered = WaitUntilStartModeReady(session, TimeSpan.FromSeconds(12));
        if (!recovered)
        {
            SelectAgentProfile(session, LocalProfileName, LocalProfileId);
            recovered = WaitUntilStartModeReady(session, TimeSpan.FromSeconds(12));
        }

        Assert.True(
            recovered,
            BuildFailureMessage("Returning from remote back to local stdio did not recover ready modes.", session, appData));
    }

    [SkippableFact]
    public void LocalStdioStartup_WithoutExplicitCwd_StillYieldsUsableModes()
    {
        using var appData = StartPageAcpSmokeData.CreateUnclassifiedScenario(
            startupProfileId: LocalProfileId,
            remoteScenario: new GuiAppDataScope.MockAcpHarnessScenario
            {
                TransportKind = GuiAppDataScope.MockAcpTransportKind.WebSocket,
                InitializeBehavior = GuiAppDataScope.MockAcpInitializeBehavior.NoResponse
            });
        using var session = WindowsGuiAppSession.LaunchFresh();

        OpenStartPage(session);

        Assert.True(
            WaitUntilStartModeReady(session, TimeSpan.FromSeconds(20)),
            BuildFailureMessage("Local stdio startup without an explicit cwd did not yield usable modes.", session, appData));
    }

    [SkippableFact]
    public void RemoteProfile_WithUnmappedCwd_StaysUnavailableInsteadOfSilentlyBecomingReady()
    {
        using var appData = StartPageAcpSmokeData.CreateMappedProjectScenario(
            startupProfileId: RemoteProfileId,
            remoteScenario: new GuiAppDataScope.MockAcpHarnessScenario
            {
                TransportKind = GuiAppDataScope.MockAcpTransportKind.WebSocket,
                InitializeBehavior = GuiAppDataScope.MockAcpInitializeBehavior.Success,
                CwdAcceptancePolicy = GuiAppDataScope.MockAcpCwdAcceptancePolicy.RejectUnmappedRemote
            });
        using var session = WindowsGuiAppSession.LaunchFresh();

        OpenStartPage(session);

        Assert.True(
            WaitUntilRemoteDraftBlocked(session, appData, TimeSpan.FromSeconds(20)),
            BuildFailureMessage("Remote profile accepted an unmapped cwd or otherwise failed to remain unavailable.", session, appData));
    }

    private static void OpenStartPage(WindowsGuiAppSession session)
    {
        var startItem = session.FindByAutomationId("MainNav.Start", TimeSpan.FromSeconds(10));
        session.ActivateElement(startItem);
        Thread.Sleep(500);

        Assert.True(
            session.WaitUntilOnscreen("StartView.AgentSelector", TimeSpan.FromSeconds(10)),
            "StartView.AgentSelector did not appear on the start page.");
        Assert.True(
            session.WaitUntilOnscreen("StartView.ModeSelector", TimeSpan.FromSeconds(10)),
            "StartView.ModeSelector did not appear on the start page.");
    }

    private static bool WaitUntilStartModeReady(WindowsGuiAppSession session, TimeSpan timeout)
        => WaitUntil(
            () => IsKnownReadyModeVisible(session) || TryOpenStartModeSelectorAndDetectKnownMode(session),
            timeout,
            TimeSpan.FromMilliseconds(250));

    private static bool WaitUntilStartModeUnavailable(WindowsGuiAppSession session, TimeSpan timeout)
        => WaitUntil(
            () => IsStartComposerModeUnavailable(session) || IsStartComposerBlockedByError(session),
            timeout,
            TimeSpan.FromMilliseconds(200));

    private static bool WaitUntilRemoteDraftBlocked(
        WindowsGuiAppSession session,
        StartPageAcpSmokeData appData,
        TimeSpan timeout)
        => WaitUntil(
            () => IsStartComposerModeUnavailable(session)
                || IsStartComposerBlockedByError(session)
                || appData.ReadLatestAppLogTail().Contains(
                    "ACP new-session draft cwd resolution failed",
                    StringComparison.Ordinal)
                || appData.ReadLatestAppLogTail().Contains(
                    "Unable to load session configuration",
                    StringComparison.Ordinal),
            timeout,
            TimeSpan.FromMilliseconds(200));

    private static bool IsKnownReadyModeVisible(WindowsGuiAppSession session)
        => ReadyModeLabels.Any(label => session.TryFindVisibleTextAnywhere(label, TimeSpan.FromMilliseconds(120)) is not null);

    private static bool TryOpenStartModeSelectorAndDetectKnownMode(WindowsGuiAppSession session)
    {
        var selector = session.TryFindByAutomationId("StartView.ModeSelector", TimeSpan.FromMilliseconds(200));
        if (selector is null)
        {
            return false;
        }

        try
        {
            session.ClickElement(selector);
            Thread.Sleep(150);
            return IsKnownReadyModeVisible(session);
        }
        catch
        {
            return false;
        }
        finally
        {
            session.PressEscape();
        }
    }

    private static bool IsStartComposerModeUnavailable(WindowsGuiAppSession session)
        => UnavailableModeLabels.Any(label => session.TryFindVisibleTextAnywhere(label, TimeSpan.FromMilliseconds(120)) is not null);

    private static bool IsStartComposerBlockedByError(WindowsGuiAppSession session)
        => RemoteBlockedErrorLabels.Any(label => session.TryFindVisibleTextAnywhere(label, TimeSpan.FromMilliseconds(120)) is not null);

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
        session.ActivateElement(FindSelectableAncestor(target));

        Assert.True(
            WaitUntil(
                () => session.TryFindVisibleTextAnywhere(expectedVisibleName, TimeSpan.FromMilliseconds(150)) is not null,
                TimeSpan.FromSeconds(5),
                TimeSpan.FromMilliseconds(120)),
            $"Selector '{selectorAutomationId}' did not visibly settle to '{expectedVisibleName}'.");
    }

    private static void SelectComboBoxItemByAutomationId(
        WindowsGuiAppSession session,
        string selectorAutomationId,
        string itemAutomationId)
    {
        var selector = session.FindByAutomationId(selectorAutomationId, TimeSpan.FromSeconds(10));
        session.ClickElement(selector);

        var target = session.FindByAutomationIdAnywhere(itemAutomationId, TimeSpan.FromSeconds(5));
        session.ActivateElement(FindSelectableAncestor(target));
        Thread.Sleep(300);
    }

    private static void SelectAgentProfile(
        WindowsGuiAppSession session,
        string expectedVisibleName,
        string profileId)
    {
        var selector = session.FindByAutomationId("StartView.AgentSelector", TimeSpan.FromSeconds(10));
        session.ClickElement(selector);

        var visibleTarget = session.TryFindVisibleTextAnywhere(expectedVisibleName, TimeSpan.FromSeconds(3));
        if (visibleTarget is not null)
        {
            session.ActivateElement(FindSelectableAncestor(visibleTarget));
            Thread.Sleep(300);
            return;
        }

        SelectComboBoxItemByAutomationId(session, "StartView.AgentSelector", AgentItemId(profileId));
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

        throw new Xunit.Sdk.XunitException("Could not find a selectable ancestor for the combo-box item.");
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

    private static string BuildFailureMessage(string headline, WindowsGuiAppSession session, StartPageAcpSmokeData appData)
        => string.Join(
            Environment.NewLine,
            headline,
            $"AgentSelector={session.TryGetElementName("StartView.AgentSelector", TimeSpan.FromMilliseconds(200)) ?? "<missing>"}",
            $"ModeSelector={session.TryGetElementName("StartView.ModeSelector", TimeSpan.FromMilliseconds(200)) ?? "<missing>"}",
            $"ProjectSelector={session.TryGetElementName("StartView.ProjectSelector", TimeSpan.FromMilliseconds(200)) ?? "<missing>"}",
            $"VisibleTexts=[{string.Join(" | ", session.GetVisibleTexts())}]",
            appData.ReadLatestAppLogTail(),
            appData.ReadBootLogTail());

    private static string AgentItemId(string profileId)
        => $"ComposerSelectorItem.Agent.{SanitizeAutomationSegment(profileId)}";

    private static string SanitizeAutomationSegment(string value)
        => string.IsNullOrWhiteSpace(value)
            ? "Empty"
            : new string(value.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());

    private sealed class StartPageAcpSmokeData : IDisposable
    {
        private readonly GuiAppDataScope _scope;

        private StartPageAcpSmokeData(GuiAppDataScope scope)
        {
            _scope = scope;
        }

        public static StartPageAcpSmokeData CreateUnclassifiedScenario(
            string startupProfileId,
            GuiAppDataScope.MockAcpHarnessScenario remoteScenario)
            => CreateScenario(
                startupProfileId,
                remoteScenario,
                hasProject: false,
                projectRootPath: null);

        public static StartPageAcpSmokeData CreateMappedProjectScenario(
            string startupProfileId,
            GuiAppDataScope.MockAcpHarnessScenario remoteScenario)
        {
            var projectRootPath = Path.Combine(
                Path.GetTempPath(),
                "SalmonEgg.GuiTests",
                "start-page-acp-project",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(projectRootPath);
            return CreateScenario(
                startupProfileId,
                remoteScenario with
                {
                    AcceptedCwd = projectRootPath
                },
                hasProject: true,
                projectRootPath);
        }

        public string ReadBootLogTail() => _scope.ReadBootLogTail();

        public string ReadLatestAppLogTail() => _scope.ReadLatestAppLogTail();

        public void Dispose()
        {
            _scope.Dispose();
        }

        private static StartPageAcpSmokeData CreateScenario(
            string startupProfileId,
            GuiAppDataScope.MockAcpHarnessScenario remoteScenario,
            bool hasProject,
            string? projectRootPath)
        {
            var scope = GuiAppDataScope.CreateDeterministicMockAcpHarnessData(remoteScenario);
            var appDataRoot = RequireAppDataRoot();
            var remoteServerYamlPath = Path.Combine(appDataRoot, "config", "servers", RemoteProfileId + ".yaml");
            var localServerYamlPath = Path.Combine(appDataRoot, "config", "servers", LocalProfileId + ".yaml");
            var localScenarioPath = Path.Combine(appDataRoot, "control", "start-page-local.scenario.json");

            EnsureDirectory(Path.Combine(appDataRoot, "control"));
            EnsureDirectory(Path.Combine(appDataRoot, "config", "servers"));

            RenameRemoteProfile(remoteServerYamlPath, RemoteProfileName);
            WriteLocalProfile(localServerYamlPath, localScenarioPath);
            WriteAppYaml(
                Path.Combine(appDataRoot, "config", "app.yaml"),
                startupProfileId,
                hasProject && !string.IsNullOrWhiteSpace(projectRootPath)
                    ? [("project-1", "GUI Project", projectRootPath)]
                    : [],
                hasProject ? "project-1" : "__unclassified__");
            WriteEmptyConversations(appDataRoot);

            return new StartPageAcpSmokeData(scope);
        }

        private static void WriteAppYaml(
            string appYamlPath,
            string startupProfileId,
            IReadOnlyList<(string ProjectId, string Name, string RootPath)> projects,
            string selectedProjectId)
        {
            var lines = new List<string>
            {
                "schema_version: 1",
                "theme: System",
                "is_animation_enabled: true",
                "backdrop: System",
                "projects:"
            };

            if (projects.Count == 0)
            {
                lines[4] = "projects: []";
            }
            else
            {
                foreach (var project in projects)
                {
                    lines.Add($"  - project_id: '{EscapeYaml(project.ProjectId)}'");
                    lines.Add($"    name: '{EscapeYaml(project.Name)}'");
                    lines.Add($"    root_path: '{EscapeYaml(project.RootPath)}'");
                }
            }

            lines.Add($"last_selected_server_id: '{EscapeYaml(startupProfileId)}'");
            lines.Add($"last_selected_project_id: '{EscapeYaml(selectedProjectId)}'");
            lines.Add(string.Empty);

            Directory.CreateDirectory(Path.GetDirectoryName(appYamlPath)!);
            File.WriteAllText(appYamlPath, string.Join(Environment.NewLine, lines), Encoding.UTF8);
        }

        private static void WriteEmptyConversations(string appDataRoot)
        {
            var conversationsPath = Path.Combine(appDataRoot, "conversations", "conversations.v1.json");
            EnsureDirectory(Path.GetDirectoryName(conversationsPath)!);
            File.WriteAllText(
                conversationsPath,
                """
                {
                  "version": 1,
                  "lastActiveConversationId": null,
                  "conversations": []
                }
                """,
                Encoding.UTF8);
        }

        private static void WriteLocalProfile(string serverYamlPath, string scenarioPath)
        {
            var scenario = new
            {
                transportKind = GuiAppDataScope.MockAcpTransportKind.Stdio.ToString(),
                initializeBehavior = GuiAppDataScope.MockAcpInitializeBehavior.Success.ToString(),
                sessionNewBehavior = GuiAppDataScope.MockAcpSessionNewBehavior.Success.ToString(),
                cwdAcceptancePolicy = GuiAppDataScope.MockAcpCwdAcceptancePolicy.AcceptAny.ToString(),
                modesVariant = GuiAppDataScope.MockAcpModesVariant.Normal.ToString(),
                sessionId = "gui-local-session-01",
                acceptedCwd = (string?)null,
                mappedRemoteCwd = (string?)null,
                initializeDelayMs = 0,
                sessionNewDelayMs = 0,
                sessionNewErrorCode = -32602,
                sessionNewErrorMessage = "session/new rejected by GUI local stdio harness."
            };

            File.WriteAllText(
                scenarioPath,
                JsonSerializer.Serialize(scenario),
                Encoding.UTF8);

            var agentScriptPath = ResolveFixtureScriptPath("SlowReplayAgent.ps1");
            var stdioArgs =
                $"-NoLogo -NoProfile -ExecutionPolicy Bypass -File {QuoteCommandLineArgument(agentScriptPath)} -ScenarioJsonPath {QuoteCommandLineArgument(scenarioPath)}";
            var yaml = BuildServerYaml(
                LocalProfileId,
                LocalProfileName,
                transport: "stdio",
                stdioCommand: "powershell.exe",
                stdioArgs: stdioArgs);
            File.WriteAllText(serverYamlPath, yaml, Encoding.UTF8);
        }

        private static string BuildServerYaml(
            string profileId,
            string name,
            string transport,
            string? stdioCommand = null,
            string? stdioArgs = null,
            string? serverUrl = null)
        {
            var lines = new List<string>
            {
                "schema_version: 1",
                $"id: '{EscapeYaml(profileId)}'",
                $"name: '{EscapeYaml(name)}'",
                $"transport: '{EscapeYaml(transport)}'",
                "connection_timeout_seconds: 10",
                "authentication:",
                "  mode: 'none'",
                "proxy:",
                "  enabled: false",
                "  proxy_url: ''"
            };

            if (string.Equals(transport, "stdio", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(stdioCommand))
                {
                    throw new ArgumentException("stdioCommand is required for stdio transport.", nameof(stdioCommand));
                }

                lines.Insert(4, $"stdio_command: '{EscapeYaml(stdioCommand)}'");
                lines.Insert(5, $"stdio_args: '{EscapeYaml(stdioArgs ?? string.Empty)}'");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(serverUrl))
                {
                    throw new ArgumentException("serverUrl is required for remote transport.", nameof(serverUrl));
                }

                lines.Insert(4, $"server_url: '{EscapeYaml(serverUrl)}'");
            }

            lines.Add(string.Empty);
            return string.Join(Environment.NewLine, lines);
        }

        private static void RenameRemoteProfile(string serverYamlPath, string name)
        {
            if (!File.Exists(serverYamlPath))
            {
                throw new FileNotFoundException("The remote server YAML was not created by the mock harness.", serverYamlPath);
            }

            var yaml = File.ReadAllText(serverYamlPath, Encoding.UTF8);
            var updated = yaml.Replace(
                "name: 'GUI Mock ACP Harness'",
                $"name: '{EscapeYaml(name)}'",
                StringComparison.Ordinal);
            File.WriteAllText(serverYamlPath, updated, Encoding.UTF8);
        }

        private static string RequireAppDataRoot()
        {
            var root = Environment.GetEnvironmentVariable("SALMONEGG_APPDATA_ROOT");
            if (string.IsNullOrWhiteSpace(root))
            {
                throw new InvalidOperationException("SALMONEGG_APPDATA_ROOT was not set for deterministic GUI smoke data.");
            }

            return root;
        }

        private static void EnsureDirectory(string path)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                Directory.CreateDirectory(path);
            }
        }

        private static string ResolveFixtureScriptPath(string fileName)
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, "SalmonEgg.sln")))
                {
                    return Path.Combine(current.FullName, "tests", "SalmonEgg.GuiTests.Windows", "Fixtures", fileName);
                }

                current = current.Parent;
            }

            throw new DirectoryNotFoundException("Failed to locate repository root from GUI test output directory.");
        }

        private static string EscapeYaml(string value)
            => value.Replace("'", "''", StringComparison.Ordinal);

        private static string QuoteCommandLineArgument(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "\"\"";
            }

            return value.IndexOfAny([' ', '\t', '"']) >= 0
                ? $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
                : value;
        }
    }
}
