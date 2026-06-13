using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace SalmonEgg.GuiTests.Windows;

internal sealed class GuiAppDataScope : IDisposable
{
    private const string AppDataRootEnvVar = "SALMONEGG_APPDATA_ROOT";
    private const string FakeReplaySessionIdEnvVar = "SALMONEGG_GUI_FAKE_REMOTE_REPLAY_SESSION_ID";
    private const string FakeReplayMessageCountEnvVar = "SALMONEGG_GUI_FAKE_REMOTE_REPLAY_MESSAGE_COUNT";
    private const string PromptAckModeEnvVar = "SALMONEGG_GUI_PROMPT_ACK_MODE";
    private const string LateUserMessageIdEnvVar = "SALMONEGG_GUI_LATE_USER_MESSAGE_ID";
    private const string LateUserMessageTextEnvVar = "SALMONEGG_GUI_LATE_USER_MESSAGE_TEXT";
    private const string LateUserMessageDelayMsEnvVar = "SALMONEGG_GUI_LATE_USER_MESSAGE_DELAY_MS";
    private const string GuiControlFileEnvVar = "SALMONEGG_GUI_CONTROL_FILE";
    private const string MockAcpHarnessScenarioFileName = "mock-acp-harness.scenario.json";
    private const string MockAcpHarnessReadySignalFileName = "mock-acp-harness.ready";
    private readonly string _appDataRoot;
    private readonly string _configDirectory;
    private readonly string _conversationsDirectory;
    private readonly string _serversDirectory;
    private readonly string _appYamlPath;
    private readonly string _mcpYamlPath;
    private readonly string _conversationsPath;
    private readonly string? _serverYamlPath;
    private readonly string? _secondaryServerYamlPath;
    private readonly byte[]? _originalAppYaml;
    private readonly byte[]? _originalConversations;
    private readonly byte[]? _originalServerYaml;
    private readonly byte[]? _originalSecondaryServerYaml;
    private readonly bool _appYamlExisted;
    private readonly bool _conversationsExisted;
    private readonly bool _serverYamlExisted;
    private readonly bool _secondaryServerYamlExisted;
    private readonly string _projectRootPath;
    private readonly string? _previousGuiAppDataRootOverride;
    private readonly string? _previousFakeReplaySessionId;
    private readonly string? _previousFakeReplayMessageCount;
    private readonly string? _previousGuiControlFile;
    private readonly IReadOnlyDictionary<string, string?> _environmentRestoreMap;
    private readonly Process? _mockAcpHarnessProcess;
    private bool _disposed;

    internal enum MockAcpTransportKind
    {
        Stdio,
        WebSocket
    }

    internal enum MockAcpInitializeBehavior
    {
        Success,
        NoResponse
    }

    internal enum MockAcpSessionNewBehavior
    {
        Success,
        Error,
        DelayedResponse,
        NoResponse
    }

    internal enum MockAcpCwdAcceptancePolicy
    {
        AcceptAny,
        RejectMissing,
        RejectNonexistent,
        RejectUnmappedRemote
    }

    internal enum MockAcpModesVariant
    {
        Normal,
        Empty
    }

    internal sealed record MockAcpHarnessScenario
    {
        public MockAcpTransportKind TransportKind { get; init; } = MockAcpTransportKind.Stdio;

        public MockAcpInitializeBehavior InitializeBehavior { get; init; } = MockAcpInitializeBehavior.Success;

        public MockAcpSessionNewBehavior SessionNewBehavior { get; init; } = MockAcpSessionNewBehavior.Success;

        public MockAcpCwdAcceptancePolicy CwdAcceptancePolicy { get; init; } = MockAcpCwdAcceptancePolicy.AcceptAny;

        public MockAcpModesVariant ModesVariant { get; init; } = MockAcpModesVariant.Normal;

        public string SessionId { get; init; } = "gui-remote-session-01";

        public string? AcceptedCwd { get; init; }

        public string? MappedRemoteCwd { get; init; }

        public int InitializeDelayMs { get; init; }

        public int SessionNewDelayMs { get; init; }

        public int SessionNewErrorCode { get; init; } = -32602;

        public string SessionNewErrorMessage { get; init; } = "session/new rejected by GUI mock ACP harness.";
    }

    private GuiAppDataScope(
        string appDataRoot,
        string appYamlPath,
        string conversationsPath,
        string? serverYamlPath,
        string? secondaryServerYamlPath,
        byte[]? originalAppYaml,
        bool appYamlExisted,
        byte[]? originalConversations,
        bool conversationsExisted,
        byte[]? originalServerYaml,
        bool serverYamlExisted,
        byte[]? originalSecondaryServerYaml,
        bool secondaryServerYamlExisted,
        string projectRootPath,
        string? previousGuiAppDataRootOverride,
        string? previousFakeReplaySessionId = null,
        string? previousFakeReplayMessageCount = null,
        string? previousGuiControlFile = null,
        IReadOnlyDictionary<string, string?>? environmentRestoreMap = null,
        Process? mockAcpHarnessProcess = null)
    {
        _appDataRoot = appDataRoot;
        _configDirectory = Path.GetDirectoryName(appYamlPath)!;
        _conversationsDirectory = Path.GetDirectoryName(conversationsPath)!;
        _serversDirectory = Path.Combine(_configDirectory, "servers");
        _appYamlPath = appYamlPath;
        _mcpYamlPath = Path.Combine(_configDirectory, "mcp.yaml");
        _conversationsPath = conversationsPath;
        _serverYamlPath = serverYamlPath;
        _secondaryServerYamlPath = secondaryServerYamlPath;
        _originalAppYaml = originalAppYaml;
        _appYamlExisted = appYamlExisted;
        _originalConversations = originalConversations;
        _conversationsExisted = conversationsExisted;
        _originalServerYaml = originalServerYaml;
        _serverYamlExisted = serverYamlExisted;
        _originalSecondaryServerYaml = originalSecondaryServerYaml;
        _secondaryServerYamlExisted = secondaryServerYamlExisted;
        _projectRootPath = projectRootPath;
        _previousGuiAppDataRootOverride = previousGuiAppDataRootOverride;
        _previousFakeReplaySessionId = previousFakeReplaySessionId;
        _previousFakeReplayMessageCount = previousFakeReplayMessageCount;
        _previousGuiControlFile = previousGuiControlFile;
        _environmentRestoreMap = environmentRestoreMap ?? new Dictionary<string, string?>();
        _mockAcpHarnessProcess = mockAcpHarnessProcess;
    }

    public static GuiAppDataScope CreateDeterministicLeftNavData(
        int sessionCount = 1,
        bool withContent = false,
        int messageCountPerSession = 2,
        string? firstSessionDisplayName = null)
    {
        if (sessionCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sessionCount));
        }

        if (messageCountPerSession < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(messageCountPerSession));
        }

        GuiTestGate.RequireEnabled();
        WindowsGuiAppSession.StopAllRunningInstances();

        var appDataRoot = ResolveAppDataRoot();
        var previousGuiAppDataRootOverride = Environment.GetEnvironmentVariable(AppDataRootEnvVar);
        var previousGuiControlFile = Environment.GetEnvironmentVariable(GuiControlFileEnvVar);
        Environment.SetEnvironmentVariable(AppDataRootEnvVar, appDataRoot);
        var appYamlPath = Path.Combine(appDataRoot, "config", "app.yaml");
        var conversationsPath = Path.Combine(appDataRoot, "conversations", "conversations.v1.json");
        var projectRootPath = Path.Combine(Path.GetTempPath(), "SalmonEgg.GuiTests", "project-1");

        var scope = new GuiAppDataScope(
            appDataRoot,
            appYamlPath,
            conversationsPath,
            serverYamlPath: null,
            secondaryServerYamlPath: null,
            File.Exists(appYamlPath) ? File.ReadAllBytes(appYamlPath) : null,
            File.Exists(appYamlPath),
            File.Exists(conversationsPath) ? File.ReadAllBytes(conversationsPath) : null,
            File.Exists(conversationsPath),
            originalServerYaml: null,
            serverYamlExisted: false,
            originalSecondaryServerYaml: null,
            secondaryServerYamlExisted: false,
            projectRootPath,
            previousGuiAppDataRootOverride,
            previousGuiControlFile: previousGuiControlFile);

        scope.Seed(sessionCount, withContent, messageCountPerSession, firstSessionDisplayName);
        return scope;
    }

    public static GuiAppDataScope CreateDeterministicMultiProjectLeftNavData(
        int projectCount = 2,
        int sessionsPerProject = 1,
        bool withContent = false,
        int messageCountPerSession = 2)
    {
        if (projectCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(projectCount));
        }

        if (sessionsPerProject <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sessionsPerProject));
        }

        if (messageCountPerSession < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(messageCountPerSession));
        }

        GuiTestGate.RequireEnabled();
        WindowsGuiAppSession.StopAllRunningInstances();

        var appDataRoot = ResolveAppDataRoot();
        var previousGuiAppDataRootOverride = Environment.GetEnvironmentVariable(AppDataRootEnvVar);
        var previousGuiControlFile = Environment.GetEnvironmentVariable(GuiControlFileEnvVar);
        Environment.SetEnvironmentVariable(AppDataRootEnvVar, appDataRoot);
        var appYamlPath = Path.Combine(appDataRoot, "config", "app.yaml");
        var conversationsPath = Path.Combine(appDataRoot, "conversations", "conversations.v1.json");
        var projectRootPath = Path.Combine(Path.GetTempPath(), "SalmonEgg.GuiTests", "project-1");

        var scope = new GuiAppDataScope(
            appDataRoot,
            appYamlPath,
            conversationsPath,
            serverYamlPath: null,
            secondaryServerYamlPath: null,
            File.Exists(appYamlPath) ? File.ReadAllBytes(appYamlPath) : null,
            File.Exists(appYamlPath),
            File.Exists(conversationsPath) ? File.ReadAllBytes(conversationsPath) : null,
            File.Exists(conversationsPath),
            originalServerYaml: null,
            serverYamlExisted: false,
            originalSecondaryServerYaml: null,
            secondaryServerYamlExisted: false,
            projectRootPath,
            previousGuiAppDataRootOverride,
            previousGuiControlFile: previousGuiControlFile);

        scope.SeedMultiProject(projectCount, sessionsPerProject, withContent, messageCountPerSession);
        return scope;
    }

    public static GuiAppDataScope CreateDeterministicVariableHeightTranscriptData(int messageCount = 400)
    {
        if (messageCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(messageCount));
        }

        GuiTestGate.RequireEnabled();
        WindowsGuiAppSession.StopAllRunningInstances();

        var appDataRoot = ResolveAppDataRoot();
        var previousGuiAppDataRootOverride = Environment.GetEnvironmentVariable(AppDataRootEnvVar);
        Environment.SetEnvironmentVariable(AppDataRootEnvVar, appDataRoot);
        var appYamlPath = Path.Combine(appDataRoot, "config", "app.yaml");
        var conversationsPath = Path.Combine(appDataRoot, "conversations", "conversations.v1.json");
        var projectRootPath = Path.Combine(Path.GetTempPath(), "SalmonEgg.GuiTests", "variable-height-project-1");

        var scope = new GuiAppDataScope(
            appDataRoot,
            appYamlPath,
            conversationsPath,
            serverYamlPath: null,
            secondaryServerYamlPath: null,
            File.Exists(appYamlPath) ? File.ReadAllBytes(appYamlPath) : null,
            File.Exists(appYamlPath),
            File.Exists(conversationsPath) ? File.ReadAllBytes(conversationsPath) : null,
            File.Exists(conversationsPath),
            originalServerYaml: null,
            serverYamlExisted: false,
            originalSecondaryServerYaml: null,
            secondaryServerYamlExisted: false,
            projectRootPath,
            previousGuiAppDataRootOverride);

        scope.SeedVariableHeightTranscript(messageCount);
        return scope;
    }

    public static GuiAppDataScope CreateDeterministicMarkdownRenderData()
    {
        GuiTestGate.RequireEnabled();
        WindowsGuiAppSession.StopAllRunningInstances();

        var appDataRoot = ResolveAppDataRoot();
        var previousGuiAppDataRootOverride = Environment.GetEnvironmentVariable(AppDataRootEnvVar);
        Environment.SetEnvironmentVariable(AppDataRootEnvVar, appDataRoot);
        var appYamlPath = Path.Combine(appDataRoot, "config", "app.yaml");
        var conversationsPath = Path.Combine(appDataRoot, "conversations", "conversations.v1.json");
        var projectRootPath = Path.Combine(Path.GetTempPath(), "SalmonEgg.GuiTests", "markdown-project-1");

        var scope = new GuiAppDataScope(
            appDataRoot,
            appYamlPath,
            conversationsPath,
            serverYamlPath: null,
            secondaryServerYamlPath: null,
            File.Exists(appYamlPath) ? File.ReadAllBytes(appYamlPath) : null,
            File.Exists(appYamlPath),
            File.Exists(conversationsPath) ? File.ReadAllBytes(conversationsPath) : null,
            File.Exists(conversationsPath),
            originalServerYaml: null,
            serverYamlExisted: false,
            originalSecondaryServerYaml: null,
            secondaryServerYamlExisted: false,
            projectRootPath,
            previousGuiAppDataRootOverride);

        scope.SeedMarkdownRenderScenario();
        return scope;
    }

    public static GuiAppDataScope CreateDeterministicMarkdownHeavyTranscriptData(int messageCount = 180)
    {
        if (messageCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(messageCount));
        }

        GuiTestGate.RequireEnabled();
        WindowsGuiAppSession.StopAllRunningInstances();

        var appDataRoot = ResolveAppDataRoot();
        var previousGuiAppDataRootOverride = Environment.GetEnvironmentVariable(AppDataRootEnvVar);
        Environment.SetEnvironmentVariable(AppDataRootEnvVar, appDataRoot);
        var appYamlPath = Path.Combine(appDataRoot, "config", "app.yaml");
        var conversationsPath = Path.Combine(appDataRoot, "conversations", "conversations.v1.json");
        var projectRootPath = Path.Combine(Path.GetTempPath(), "SalmonEgg.GuiTests", "markdown-heavy-project-1");

        var scope = new GuiAppDataScope(
            appDataRoot,
            appYamlPath,
            conversationsPath,
            serverYamlPath: null,
            secondaryServerYamlPath: null,
            File.Exists(appYamlPath) ? File.ReadAllBytes(appYamlPath) : null,
            File.Exists(appYamlPath),
            File.Exists(conversationsPath) ? File.ReadAllBytes(conversationsPath) : null,
            File.Exists(conversationsPath),
            originalServerYaml: null,
            serverYamlExisted: false,
            originalSecondaryServerYaml: null,
            secondaryServerYamlExisted: false,
            projectRootPath,
            previousGuiAppDataRootOverride);

        scope.SeedMarkdownHeavyTranscript(messageCount);
        return scope;
    }

    public static GuiAppDataScope CreateDeterministicTallLastMessageTranscriptData(int messageCount = 80)
    {
        if (messageCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(messageCount));
        }

        GuiTestGate.RequireEnabled();
        WindowsGuiAppSession.StopAllRunningInstances();

        var appDataRoot = ResolveAppDataRoot();
        var previousGuiAppDataRootOverride = Environment.GetEnvironmentVariable(AppDataRootEnvVar);
        Environment.SetEnvironmentVariable(AppDataRootEnvVar, appDataRoot);
        var appYamlPath = Path.Combine(appDataRoot, "config", "app.yaml");
        var conversationsPath = Path.Combine(appDataRoot, "conversations", "conversations.v1.json");
        var projectRootPath = Path.Combine(Path.GetTempPath(), "SalmonEgg.GuiTests", "tall-last-message-project-1");

        var scope = new GuiAppDataScope(
            appDataRoot,
            appYamlPath,
            conversationsPath,
            serverYamlPath: null,
            secondaryServerYamlPath: null,
            File.Exists(appYamlPath) ? File.ReadAllBytes(appYamlPath) : null,
            File.Exists(appYamlPath),
            File.Exists(conversationsPath) ? File.ReadAllBytes(conversationsPath) : null,
            File.Exists(conversationsPath),
            originalServerYaml: null,
            serverYamlExisted: false,
            originalSecondaryServerYaml: null,
            secondaryServerYamlExisted: false,
            projectRootPath,
            previousGuiAppDataRootOverride);

        scope.SeedTallLastMessageTranscript(messageCount);
        return scope;
    }

    public static GuiAppDataScope CreateDeterministicToolCallData()
    {
        GuiTestGate.RequireEnabled();
        WindowsGuiAppSession.StopAllRunningInstances();

        var appDataRoot = ResolveAppDataRoot();
        var previousGuiAppDataRootOverride = Environment.GetEnvironmentVariable(AppDataRootEnvVar);
        Environment.SetEnvironmentVariable(AppDataRootEnvVar, appDataRoot);
        var appYamlPath = Path.Combine(appDataRoot, "config", "app.yaml");
        var conversationsPath = Path.Combine(appDataRoot, "conversations", "conversations.v1.json");
        var projectRootPath = Path.Combine(Path.GetTempPath(), "SalmonEgg.GuiTests", "toolcall-project-1");

        var scope = new GuiAppDataScope(
            appDataRoot,
            appYamlPath,
            conversationsPath,
            serverYamlPath: null,
            secondaryServerYamlPath: null,
            File.Exists(appYamlPath) ? File.ReadAllBytes(appYamlPath) : null,
            File.Exists(appYamlPath),
            File.Exists(conversationsPath) ? File.ReadAllBytes(conversationsPath) : null,
            File.Exists(conversationsPath),
            originalServerYaml: null,
            serverYamlExisted: false,
            originalSecondaryServerYaml: null,
            secondaryServerYamlExisted: false,
            projectRootPath,
            previousGuiAppDataRootOverride);

        scope.SeedToolCallScenario();
        return scope;
    }

    public static GuiAppDataScope CreateDeterministicSlowRemoteReplayData(
        int cachedMessageCount = 1,
        int replayMessageCount = 60,
        bool includeLocalConversation = false,
        int localMessageCount = 3,
        int remoteConversationCount = 1)
        => CreateDeterministicMockAcpHarnessData(
            new MockAcpHarnessScenario(),
            cachedMessageCount,
            replayMessageCount,
            includeLocalConversation,
            localMessageCount,
            remoteConversationCount);

    public static GuiAppDataScope CreateDeterministicMockAcpHarnessData(
        MockAcpHarnessScenario? scenario = null,
        int cachedMessageCount = 1,
        int replayMessageCount = 60,
        bool includeLocalConversation = false,
        int localMessageCount = 3,
        int remoteConversationCount = 1)
    {
        if (cachedMessageCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cachedMessageCount));
        }

        if (replayMessageCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(replayMessageCount));
        }

        if (includeLocalConversation && localMessageCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(localMessageCount));
        }

        if (remoteConversationCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(remoteConversationCount));
        }

        scenario ??= new MockAcpHarnessScenario();
        GuiTestGate.RequireEnabled();
        WindowsGuiAppSession.StopAllRunningInstances();

        const string profileId = "gui-mock-acp-profile";
        const string fakeReplaySessionIdEnvVar = "SALMONEGG_GUI_FAKE_REMOTE_REPLAY_SESSION_ID";
        const string fakeReplayMessageCountEnvVar = "SALMONEGG_GUI_FAKE_REMOTE_REPLAY_MESSAGE_COUNT";
        var appDataRoot = ResolveAppDataRoot();
        var previousGuiAppDataRootOverride = Environment.GetEnvironmentVariable(AppDataRootEnvVar);
        var previousFakeReplaySessionId = Environment.GetEnvironmentVariable(fakeReplaySessionIdEnvVar);
        var previousFakeReplayMessageCount = Environment.GetEnvironmentVariable(fakeReplayMessageCountEnvVar);
        Environment.SetEnvironmentVariable(AppDataRootEnvVar, appDataRoot);

        var appYamlPath = Path.Combine(appDataRoot, "config", "app.yaml");
        var conversationsPath = Path.Combine(appDataRoot, "conversations", "conversations.v1.json");
        var serverYamlPath = Path.Combine(appDataRoot, "config", "servers", profileId + ".yaml");
        var projectRootPath = Path.Combine(Path.GetTempPath(), "SalmonEgg.GuiTests", "mock-acp-project-1");
        var scenarioPath = Path.Combine(appDataRoot, "control", MockAcpHarnessScenarioFileName);
        var readySignalPath = Path.Combine(appDataRoot, "control", MockAcpHarnessReadySignalFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(scenarioPath)!);

        var scenarioToWrite = NormalizeMockAcpHarnessScenario(scenario, projectRootPath);
        TestFileIo.WriteAllTextWithRetry(
            scenarioPath,
            BuildMockAcpHarnessScenarioJson(scenarioToWrite),
            Encoding.UTF8);

        Process? mockAcpHarnessProcess = null;
        string serverYaml;
        if (scenarioToWrite.TransportKind == MockAcpTransportKind.WebSocket)
        {
            var websocketPort = AllocateLoopbackPort();
            var websocketUrl = $"ws://127.0.0.1:{websocketPort}/";
            serverYaml = BuildServerYaml(
                profileId,
                transport: "websocket",
                serverUrl: websocketUrl);
            mockAcpHarnessProcess = StartMockAcpHarnessProcess(
                scenarioPath,
                websocketUrl,
                readySignalPath);
            WaitForMockAcpHarnessReady(readySignalPath);
        }
        else
        {
            var agentScriptPath = ResolveSlowReplayAgentScriptPath();
            serverYaml = BuildServerYaml(
                profileId,
                transport: "stdio",
                stdioCommand: "powershell.exe",
                stdioArgs:
                    $"-NoLogo -NoProfile -ExecutionPolicy Bypass -File {QuoteCommandLineArgument(agentScriptPath)} -ScenarioJsonPath {QuoteCommandLineArgument(scenarioPath)}");
        }

        var scope = new GuiAppDataScope(
            appDataRoot,
            appYamlPath,
            conversationsPath,
            serverYamlPath,
            secondaryServerYamlPath: null,
            File.Exists(appYamlPath) ? File.ReadAllBytes(appYamlPath) : null,
            File.Exists(appYamlPath),
            File.Exists(conversationsPath) ? File.ReadAllBytes(conversationsPath) : null,
            File.Exists(conversationsPath),
            File.Exists(serverYamlPath) ? File.ReadAllBytes(serverYamlPath) : null,
            File.Exists(serverYamlPath),
            originalSecondaryServerYaml: null,
            secondaryServerYamlExisted: false,
            projectRootPath,
            previousGuiAppDataRootOverride,
            previousFakeReplaySessionId,
            previousFakeReplayMessageCount,
            previousGuiControlFile: Environment.GetEnvironmentVariable(GuiControlFileEnvVar),
            environmentRestoreMap: null,
            mockAcpHarnessProcess);

        scope.SeedMockAcpHarness(
            profileId,
            cachedMessageCount,
            replayMessageCount,
            includeLocalConversation,
            localMessageCount,
            remoteConversationCount,
            scenarioToWrite,
            serverYaml);
        return scope;
    }

    public static GuiAppDataScope CreateDeterministicPromptAckRaceData()
    {
        GuiTestGate.RequireEnabled();
        WindowsGuiAppSession.StopAllRunningInstances();

        const string profileId = "gui-slow-remote-profile";
        var appDataRoot = ResolveAppDataRoot();
        var previousGuiAppDataRootOverride = Environment.GetEnvironmentVariable(AppDataRootEnvVar);
        var previousFakeReplaySessionId = Environment.GetEnvironmentVariable(FakeReplaySessionIdEnvVar);
        var previousFakeReplayMessageCount = Environment.GetEnvironmentVariable(FakeReplayMessageCountEnvVar);
        var environmentRestoreMap = CaptureEnvironmentVariables(
            PromptAckModeEnvVar,
            LateUserMessageIdEnvVar,
            LateUserMessageTextEnvVar,
            LateUserMessageDelayMsEnvVar);

        Environment.SetEnvironmentVariable(AppDataRootEnvVar, appDataRoot);

        var appYamlPath = Path.Combine(appDataRoot, "config", "app.yaml");
        var conversationsPath = Path.Combine(appDataRoot, "conversations", "conversations.v1.json");
        var serverYamlPath = Path.Combine(appDataRoot, "config", "servers", profileId + ".yaml");
        var projectRootPath = Path.Combine(Path.GetTempPath(), "SalmonEgg.GuiTests", "remote-project-1");

        var scope = new GuiAppDataScope(
            appDataRoot,
            appYamlPath,
            conversationsPath,
            serverYamlPath,
            secondaryServerYamlPath: null,
            File.Exists(appYamlPath) ? File.ReadAllBytes(appYamlPath) : null,
            File.Exists(appYamlPath),
            File.Exists(conversationsPath) ? File.ReadAllBytes(conversationsPath) : null,
            File.Exists(conversationsPath),
            File.Exists(serverYamlPath) ? File.ReadAllBytes(serverYamlPath) : null,
            File.Exists(serverYamlPath),
            originalSecondaryServerYaml: null,
            secondaryServerYamlExisted: false,
            projectRootPath,
            previousGuiAppDataRootOverride,
            previousFakeReplaySessionId,
            previousFakeReplayMessageCount,
            previousGuiControlFile: null,
            environmentRestoreMap);

        scope.SeedSlowRemoteReplay(
            profileId,
            cachedMessageCount: 0,
            replayMessageCount: 1,
            includeLocalConversation: false,
            localMessageCount: 3,
            remoteConversationCount: 1);

        Environment.SetEnvironmentVariable(PromptAckModeEnvVar, "late-authoritative-update");
        Environment.SetEnvironmentVariable(LateUserMessageIdEnvVar, "gui-server-user-77");
        Environment.SetEnvironmentVariable(LateUserMessageDelayMsEnvVar, "400");
        Environment.SetEnvironmentVariable(LateUserMessageTextEnvVar, "hello");

        return scope;
    }

    public static GuiAppDataScope CreateDeterministicBackgroundAttentionData(
        int cachedMessageCount = 1,
        int replayMessageCount = 24)
    {
        var previousControlFile = Environment.GetEnvironmentVariable(GuiControlFileEnvVar);
        GuiTestGate.RequireEnabled();
        WindowsGuiAppSession.StopAllRunningInstances();

        const string profileId = "gui-slow-remote-profile";
        const string fakeReplaySessionIdEnvVar = "SALMONEGG_GUI_FAKE_REMOTE_REPLAY_SESSION_ID";
        const string fakeReplayMessageCountEnvVar = "SALMONEGG_GUI_FAKE_REMOTE_REPLAY_MESSAGE_COUNT";
        var appDataRoot = ResolveAppDataRoot();
        var previousGuiAppDataRootOverride = Environment.GetEnvironmentVariable(AppDataRootEnvVar);
        var previousFakeReplaySessionId = Environment.GetEnvironmentVariable(fakeReplaySessionIdEnvVar);
        var previousFakeReplayMessageCount = Environment.GetEnvironmentVariable(fakeReplayMessageCountEnvVar);
        Environment.SetEnvironmentVariable(AppDataRootEnvVar, appDataRoot);

        var appYamlPath = Path.Combine(appDataRoot, "config", "app.yaml");
        var conversationsPath = Path.Combine(appDataRoot, "conversations", "conversations.v1.json");
        var serverYamlPath = Path.Combine(appDataRoot, "config", "servers", profileId + ".yaml");
        var projectRootPath = Path.Combine(Path.GetTempPath(), "SalmonEgg.GuiTests", "remote-project-1");

        var scope = new GuiAppDataScope(
            appDataRoot,
            appYamlPath,
            conversationsPath,
            serverYamlPath,
            secondaryServerYamlPath: null,
            File.Exists(appYamlPath) ? File.ReadAllBytes(appYamlPath) : null,
            File.Exists(appYamlPath),
            File.Exists(conversationsPath) ? File.ReadAllBytes(conversationsPath) : null,
            File.Exists(conversationsPath),
            File.Exists(serverYamlPath) ? File.ReadAllBytes(serverYamlPath) : null,
            File.Exists(serverYamlPath),
            originalSecondaryServerYaml: null,
            secondaryServerYamlExisted: false,
            projectRootPath,
            previousGuiAppDataRootOverride,
            previousFakeReplaySessionId,
            previousFakeReplayMessageCount,
            previousControlFile);

        scope.SeedSlowRemoteReplay(
            profileId,
            cachedMessageCount,
            replayMessageCount,
            includeLocalConversation: false,
            localMessageCount: 3,
            remoteConversationCount: 2);

        var controlFilePath = Path.Combine(appDataRoot, "control", "agent-control.json");
        Environment.SetEnvironmentVariable(GuiControlFileEnvVar, controlFilePath);
        return scope;
    }

    public static GuiAppDataScope CreateDeterministicCrossProfileRemoteReplayData(
        int cachedMessageCount = 1,
        int replayMessageCount = 40,
        int localMessageCount = 6)
    {
        if (cachedMessageCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cachedMessageCount));
        }

        if (replayMessageCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(replayMessageCount));
        }

        if (localMessageCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(localMessageCount));
        }

        GuiTestGate.RequireEnabled();
        WindowsGuiAppSession.StopAllRunningInstances();

        const string profileA = "gui-slow-remote-profile-a";
        const string profileB = "gui-slow-remote-profile-b";
        const string fakeReplaySessionIdEnvVar = "SALMONEGG_GUI_FAKE_REMOTE_REPLAY_SESSION_ID";
        const string fakeReplayMessageCountEnvVar = "SALMONEGG_GUI_FAKE_REMOTE_REPLAY_MESSAGE_COUNT";
        var appDataRoot = ResolveAppDataRoot();
        var previousGuiAppDataRootOverride = Environment.GetEnvironmentVariable(AppDataRootEnvVar);
        var previousFakeReplaySessionId = Environment.GetEnvironmentVariable(fakeReplaySessionIdEnvVar);
        var previousFakeReplayMessageCount = Environment.GetEnvironmentVariable(fakeReplayMessageCountEnvVar);
        Environment.SetEnvironmentVariable(AppDataRootEnvVar, appDataRoot);

        var appYamlPath = Path.Combine(appDataRoot, "config", "app.yaml");
        var conversationsPath = Path.Combine(appDataRoot, "conversations", "conversations.v1.json");
        var serverYamlPathA = Path.Combine(appDataRoot, "config", "servers", profileA + ".yaml");
        var serverYamlPathB = Path.Combine(appDataRoot, "config", "servers", profileB + ".yaml");
        var projectRootPath = Path.Combine(Path.GetTempPath(), "SalmonEgg.GuiTests", "cross-profile-project-1");

        var scope = new GuiAppDataScope(
            appDataRoot,
            appYamlPath,
            conversationsPath,
            serverYamlPathA,
            serverYamlPathB,
            File.Exists(appYamlPath) ? File.ReadAllBytes(appYamlPath) : null,
            File.Exists(appYamlPath),
            File.Exists(conversationsPath) ? File.ReadAllBytes(conversationsPath) : null,
            File.Exists(conversationsPath),
            File.Exists(serverYamlPathA) ? File.ReadAllBytes(serverYamlPathA) : null,
            File.Exists(serverYamlPathA),
            File.Exists(serverYamlPathB) ? File.ReadAllBytes(serverYamlPathB) : null,
            File.Exists(serverYamlPathB),
            projectRootPath,
            previousGuiAppDataRootOverride,
            previousFakeReplaySessionId,
            previousFakeReplayMessageCount);

        scope.SeedCrossProfileRemoteReplay(
            profileA,
            profileB,
            cachedMessageCount,
            replayMessageCount,
            localMessageCount);
        return scope;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        TryStopMockAcpHarnessProcess();
        WindowsGuiAppSession.StopAllRunningInstances();
        RestoreFile(_appYamlPath, _originalAppYaml, _appYamlExisted);
        RestoreFile(_conversationsPath, _originalConversations, _conversationsExisted);
        if (!string.IsNullOrWhiteSpace(_serverYamlPath))
        {
            RestoreFile(_serverYamlPath, _originalServerYaml, _serverYamlExisted);
        }
        if (!string.IsNullOrWhiteSpace(_secondaryServerYamlPath))
        {
            RestoreFile(_secondaryServerYamlPath, _originalSecondaryServerYaml, _secondaryServerYamlExisted);
        }
        DeleteDirectoryIfEmpty(_configDirectory);
        DeleteDirectoryIfEmpty(_conversationsDirectory);
        DeleteDirectoryIfEmpty(_serversDirectory);
        DeleteDirectoryIfEmpty(_appDataRoot);
        Environment.SetEnvironmentVariable(AppDataRootEnvVar, _previousGuiAppDataRootOverride);
        Environment.SetEnvironmentVariable("SALMONEGG_GUI_FAKE_REMOTE_REPLAY_SESSION_ID", _previousFakeReplaySessionId);
        Environment.SetEnvironmentVariable("SALMONEGG_GUI_FAKE_REMOTE_REPLAY_MESSAGE_COUNT", _previousFakeReplayMessageCount);
        Environment.SetEnvironmentVariable(GuiControlFileEnvVar, _previousGuiControlFile);
        foreach (var pair in _environmentRestoreMap)
        {
            Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }

        try
        {
            if (Directory.Exists(_projectRootPath))
            {
                Directory.Delete(_projectRootPath, recursive: true);
            }
        }
        catch
        {
        }
    }

    public string ReadBootLogTail(int lineCount = 20)
    {
        var bootLogPath = Path.Combine(_appDataRoot, "boot.log");
        if (!File.Exists(bootLogPath))
        {
            return "<boot.log missing>";
        }

        try
        {
            return ReadTailAllowSharedRead(bootLogPath, lineCount);
        }
        catch (Exception ex)
        {
            return $"<boot.log unreadable: {ex.Message}>";
        }
    }

    public string ReadLatestAppLogTail(int lineCount = 80)
    {
        var logsRoot = Path.Combine(_appDataRoot, "logs");
        if (!Directory.Exists(logsRoot))
        {
            return "<app logs missing>";
        }

        try
        {
            var latestLogPath = Directory.EnumerateFiles(logsRoot, "app-*.log")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(latestLogPath) || !File.Exists(latestLogPath))
            {
                return "<app log missing>";
            }

            return ReadTailAllowSharedRead(latestLogPath, lineCount);
        }
        catch (Exception ex)
        {
            return $"<app log unreadable: {ex.Message}>";
        }
    }

    public string ReadConversationsJson()
    {
        if (!File.Exists(_conversationsPath))
        {
            return "<conversations missing>";
        }

        try
        {
            return File.ReadAllText(_conversationsPath);
        }
        catch (Exception ex)
        {
            return $"<conversations unreadable: {ex.Message}>";
        }
    }

    public string ReadAppYaml()
    {
        if (!File.Exists(_appYamlPath))
        {
            return "<app.yaml missing>";
        }

        try
        {
            return File.ReadAllText(_appYamlPath);
        }
        catch (Exception ex)
        {
            return $"<app.yaml unreadable: {ex.Message}>";
        }
    }

    public string ReadMcpYaml()
    {
        if (!File.Exists(_mcpYamlPath))
        {
            return "<mcp.yaml missing>";
        }

        try
        {
            return File.ReadAllText(_mcpYamlPath);
        }
        catch (Exception ex)
        {
            return $"<mcp.yaml unreadable: {ex.Message}>";
        }
    }

    public void WriteMcpYaml(string yaml)
    {
        Directory.CreateDirectory(_configDirectory);
        TestFileIo.WriteAllTextWithRetry(_mcpYamlPath, yaml, Encoding.UTF8);
    }

    private void Seed(
        int sessionCount,
        bool withContent = false,
        int messageCountPerSession = 2,
        string? firstSessionDisplayName = null)
    {
        Directory.CreateDirectory(_configDirectory);
        Directory.CreateDirectory(_conversationsDirectory);
        Directory.CreateDirectory(_projectRootPath);

        TestFileIo.WriteAllTextWithRetry(_appYamlPath, BuildAppYaml(_projectRootPath), Encoding.UTF8);
        TestFileIo.WriteAllTextWithRetry(
            _conversationsPath,
            BuildConversationsJson(_projectRootPath, sessionCount, withContent, messageCountPerSession, firstSessionDisplayName),
            Encoding.UTF8);
    }

    private void SeedMultiProject(
        int projectCount,
        int sessionsPerProject,
        bool withContent = false,
        int messageCountPerSession = 2)
    {
        Directory.CreateDirectory(_configDirectory);
        Directory.CreateDirectory(_conversationsDirectory);

        var projects = new List<(string ProjectId, string Name, string RootPath)>(projectCount);
        for (var index = 1; index <= projectCount; index++)
        {
            var projectId = $"project-{index}";
            var rootPath = index == 1
                ? _projectRootPath
                : Path.Combine(Path.GetTempPath(), "SalmonEgg.GuiTests", projectId);
            Directory.CreateDirectory(rootPath);
            projects.Add((projectId, $"GUI Project {index:00}", rootPath));
        }

        TestFileIo.WriteAllTextWithRetry(_appYamlPath, BuildMultiProjectAppYaml(projects), Encoding.UTF8);
        TestFileIo.WriteAllTextWithRetry(
            _conversationsPath,
            BuildMultiProjectConversationsJson(
                projects.Select(project => (project.ProjectId, project.RootPath)).ToArray(),
                sessionsPerProject,
                withContent,
                messageCountPerSession),
            Encoding.UTF8);
    }

    private void SeedVariableHeightTranscript(int messageCount)
    {
        Directory.CreateDirectory(_configDirectory);
        Directory.CreateDirectory(_conversationsDirectory);
        Directory.CreateDirectory(_projectRootPath);

        TestFileIo.WriteAllTextWithRetry(_appYamlPath, BuildAppYaml(_projectRootPath), Encoding.UTF8);
        TestFileIo.WriteAllTextWithRetry(
            _conversationsPath,
            BuildVariableHeightConversationsJson(_projectRootPath, messageCount),
            Encoding.UTF8);
    }

    private void SeedMarkdownHeavyTranscript(int messageCount)
    {
        Directory.CreateDirectory(_configDirectory);
        Directory.CreateDirectory(_conversationsDirectory);
        Directory.CreateDirectory(_projectRootPath);
        TestFileIo.WriteAllTextWithRetry(_appYamlPath, BuildAppYaml(_projectRootPath), Encoding.UTF8);
        TestFileIo.WriteAllTextWithRetry(
            _conversationsPath,
            BuildMarkdownHeavyConversationsJson(_projectRootPath, messageCount),
            Encoding.UTF8);
    }

    private void SeedTallLastMessageTranscript(int messageCount)
    {
        Directory.CreateDirectory(_configDirectory);
        Directory.CreateDirectory(_conversationsDirectory);
        Directory.CreateDirectory(_projectRootPath);
        TestFileIo.WriteAllTextWithRetry(_appYamlPath, BuildAppYaml(_projectRootPath), Encoding.UTF8);
        TestFileIo.WriteAllTextWithRetry(
            _conversationsPath,
            BuildTallLastMessageConversationsJson(_projectRootPath, messageCount),
            Encoding.UTF8);
    }

    public void TriggerBackgroundRemoteAgentUpdate(string remoteSessionId, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteSessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var controlPath = Environment.GetEnvironmentVariable(GuiControlFileEnvVar)
            ?? throw new InvalidOperationException("SALMONEGG_GUI_CONTROL_FILE was not set.");

        Directory.CreateDirectory(Path.GetDirectoryName(controlPath)!);
        TestFileIo.WriteAllTextWithRetry(
            controlPath,
            JsonSerializer.Serialize(new
            {
                kind = "background-agent-message",
                sessionId = remoteSessionId,
                text
            }),
            Encoding.UTF8);
    }

    private void SeedMarkdownRenderScenario()
    {
        Directory.CreateDirectory(_configDirectory);
        Directory.CreateDirectory(_conversationsDirectory);
        Directory.CreateDirectory(_projectRootPath);

        TestFileIo.WriteAllTextWithRetry(_appYamlPath, BuildAppYaml(_projectRootPath), Encoding.UTF8);
        TestFileIo.WriteAllTextWithRetry(
            _conversationsPath,
            BuildMarkdownRenderConversationsJson(_projectRootPath),
            Encoding.UTF8);
    }

    private void SeedToolCallScenario()
    {
        Directory.CreateDirectory(_configDirectory);
        Directory.CreateDirectory(_conversationsDirectory);
        Directory.CreateDirectory(_projectRootPath);

        TestFileIo.WriteAllTextWithRetry(_appYamlPath, BuildAppYaml(_projectRootPath), Encoding.UTF8);
        TestFileIo.WriteAllTextWithRetry(
            _conversationsPath,
            BuildToolCallConversationsJson(_projectRootPath),
            Encoding.UTF8);
    }

    private void SeedSlowRemoteReplay(
        string profileId,
        int cachedMessageCount,
        int replayMessageCount,
        bool includeLocalConversation,
        int localMessageCount,
        int remoteConversationCount)
        => SeedMockAcpHarness(
            profileId,
            cachedMessageCount,
            replayMessageCount,
            includeLocalConversation,
            localMessageCount,
            remoteConversationCount,
            new MockAcpHarnessScenario(),
            BuildServerYaml(
                profileId,
                transport: "stdio",
                stdioCommand: "powershell.exe",
                stdioArgs:
                    $"-NoLogo -NoProfile -ExecutionPolicy Bypass -File {QuoteCommandLineArgument(ResolveSlowReplayAgentScriptPath())} -ScenarioJsonPath {QuoteCommandLineArgument(Path.Combine(_appDataRoot, "control", MockAcpHarnessScenarioFileName))}"));

    private void SeedMockAcpHarness(
        string profileId,
        int cachedMessageCount,
        int replayMessageCount,
        bool includeLocalConversation,
        int localMessageCount,
        int remoteConversationCount,
        MockAcpHarnessScenario scenario,
        string serverYaml)
    {
        if (string.IsNullOrWhiteSpace(_serverYamlPath))
        {
            throw new InvalidOperationException("Mock ACP harness seed requires a server YAML path.");
        }

        Directory.CreateDirectory(_configDirectory);
        Directory.CreateDirectory(_conversationsDirectory);
        Directory.CreateDirectory(_serversDirectory);
        Directory.CreateDirectory(_projectRootPath);

        var scenarioPath = Path.Combine(_appDataRoot, "control", MockAcpHarnessScenarioFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(scenarioPath)!);
        TestFileIo.WriteAllTextWithRetry(
            scenarioPath,
            BuildMockAcpHarnessScenarioJson(NormalizeMockAcpHarnessScenario(scenario, _projectRootPath)),
            Encoding.UTF8);

        TestFileIo.WriteAllTextWithRetry(
            _appYamlPath,
            BuildAppYaml(_projectRootPath, profileId),
            Encoding.UTF8);
        TestFileIo.WriteAllTextWithRetry(
            _conversationsPath,
            BuildSlowRemoteReplayConversationsJson(
                _projectRootPath,
                profileId,
                scenario.SessionId,
                cachedMessageCount,
                includeLocalConversation,
                localMessageCount,
                remoteConversationCount),
            Encoding.UTF8);
        TestFileIo.WriteAllTextWithRetry(
            _serverYamlPath,
            serverYaml,
            Encoding.UTF8);

        Environment.SetEnvironmentVariable("SALMONEGG_GUI_FAKE_REMOTE_REPLAY_SESSION_ID", scenario.SessionId);
        Environment.SetEnvironmentVariable("SALMONEGG_GUI_FAKE_REMOTE_REPLAY_MESSAGE_COUNT", replayMessageCount.ToString());
    }

    private void SeedCrossProfileRemoteReplay(
        string profileA,
        string profileB,
        int cachedMessageCount,
        int replayMessageCount,
        int localMessageCount)
    {
        if (string.IsNullOrWhiteSpace(_serverYamlPath) || string.IsNullOrWhiteSpace(_secondaryServerYamlPath))
        {
            throw new InvalidOperationException("Cross-profile replay seed requires two server YAML paths.");
        }

        Directory.CreateDirectory(_configDirectory);
        Directory.CreateDirectory(_conversationsDirectory);
        Directory.CreateDirectory(_serversDirectory);
        Directory.CreateDirectory(_projectRootPath);

        var agentScriptPath = ResolveSlowReplayAgentScriptPath();
        var agentArgsPrefix = $"-NoLogo -NoProfile -ExecutionPolicy Bypass -File {QuoteCommandLineArgument(agentScriptPath)}";

        TestFileIo.WriteAllTextWithRetry(
            _appYamlPath,
            BuildAppYaml(_projectRootPath, profileA),
            Encoding.UTF8);
        TestFileIo.WriteAllTextWithRetry(
            _conversationsPath,
            BuildCrossProfileRemoteReplayConversationsJson(
                _projectRootPath,
                profileA,
                profileB,
                cachedMessageCount,
                localMessageCount),
            Encoding.UTF8);
        TestFileIo.WriteAllTextWithRetry(
            _serverYamlPath,
            BuildServerYaml(
                profileA,
                transport: "stdio",
                stdioCommand: "powershell.exe",
                stdioArgs: $"{agentArgsPrefix} -SessionId gui-remote-session-01 -MessageCount {replayMessageCount} -ListDelayMs 700"),
            Encoding.UTF8);
        TestFileIo.WriteAllTextWithRetry(
            _secondaryServerYamlPath,
            BuildServerYaml(
                profileB,
                transport: "stdio",
                stdioCommand: "powershell.exe",
                stdioArgs: $"{agentArgsPrefix} -SessionId gui-remote-session-02 -MessageCount {replayMessageCount} -ListDelayMs 0"),
            Encoding.UTF8);

        Environment.SetEnvironmentVariable("SALMONEGG_GUI_FAKE_REMOTE_REPLAY_SESSION_ID", "gui-remote-session-01");
        Environment.SetEnvironmentVariable("SALMONEGG_GUI_FAKE_REMOTE_REPLAY_MESSAGE_COUNT", replayMessageCount.ToString());
    }

    private static string ResolveAppDataRoot()
    {
        var overrideRoot = Environment.GetEnvironmentVariable(AppDataRootEnvVar);
        if (!string.IsNullOrWhiteSpace(overrideRoot))
        {
            return overrideRoot.Trim();
        }

        return Path.Combine(
            Path.GetTempPath(),
            "SalmonEgg.GuiTests",
            "appdata",
            Guid.NewGuid().ToString("N"));
    }

    private static string BuildAppYaml(string projectRootPath, string? lastSelectedServerId = null)
    {
        var normalizedPath = projectRootPath.Replace("'", "''", StringComparison.Ordinal);
        var lines = new List<string>
        {
            "schema_version: 1",
            "theme: System",
            "is_animation_enabled: true",
            "backdrop: System",
            "projects:",
            "  - project_id: project-1",
            "    name: GUI Project",
            $"    root_path: '{normalizedPath}'"
        };

        if (!string.IsNullOrWhiteSpace(lastSelectedServerId))
        {
            var normalizedServerId = lastSelectedServerId.Replace("'", "''", StringComparison.Ordinal);
            lines.Add($"last_selected_server_id: '{normalizedServerId}'");
        }

        lines.Add("last_selected_project_id: project-1");
        lines.Add(string.Empty);
        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildMultiProjectAppYaml(
        IReadOnlyList<(string ProjectId, string Name, string RootPath)> projects)
    {
        var lines = new List<string>
        {
            "schema_version: 1",
            "theme: System",
            "is_animation_enabled: true",
            "backdrop: System",
            "projects:"
        };

        foreach (var project in projects)
        {
            var normalizedPath = project.RootPath.Replace("'", "''", StringComparison.Ordinal);
            lines.Add($"  - project_id: {project.ProjectId}");
            lines.Add($"    name: {project.Name}");
            lines.Add($"    root_path: '{normalizedPath}'");
        }

        lines.Add($"last_selected_project_id: {projects[0].ProjectId}");
        lines.Add(string.Empty);
        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildConversationsJson(
        string projectRootPath,
        int sessionCount,
        bool withContent = false,
        int messageCountPerSession = 2,
        string? firstSessionDisplayName = null)
    {
        var baseTime = new DateTimeOffset(2026, 03, 19, 09, 00, 00, TimeSpan.Zero);
        var conversations = Enumerable.Range(1, sessionCount)
            .Select(index =>
            {
                var timestamp = baseTime.AddMinutes(-(index - 1));
                object[] messages = Array.Empty<object>();

                if (withContent)
                {
                    var count = Math.Max(2, messageCountPerSession);
                    messages = Enumerable.Range(1, count)
                        .Select(messageIndex => new
                        {
                            id = $"m-{index}-{messageIndex}",
                            timestamp = timestamp.AddSeconds(messageIndex),
                            contentType = "text",
                            textContent = $"GUI Session {index:00} message {messageIndex:000}",
                            isOutgoing = messageIndex % 2 == 0
                        })
                        .Cast<object>()
                        .ToArray();
                }

                return new
                {
                    conversationId = $"gui-session-{index:00}",
                    displayName = index == 1 && !string.IsNullOrWhiteSpace(firstSessionDisplayName)
                        ? firstSessionDisplayName
                        : $"GUI Session {index:00}",
                    createdAt = timestamp,
                    lastUpdatedAt = timestamp,
                    cwd = projectRootPath,
                    messages = messages
                };
            })
            .ToArray();

        var document = new
        {
            version = 1,
            lastActiveConversationId = (string?)null,
            conversations
        };

        return JsonSerializer.Serialize(document);
    }

    private static string BuildMultiProjectConversationsJson(
        IReadOnlyList<(string ProjectId, string RootPath)> projects,
        int sessionsPerProject,
        bool withContent = false,
        int messageCountPerSession = 2)
    {
        var baseTime = new DateTimeOffset(2026, 03, 19, 09, 00, 00, TimeSpan.Zero);
        var conversations = new List<object>();
        var sessionOrdinal = 1;

        for (var projectIndex = 0; projectIndex < projects.Count; projectIndex++)
        {
            var project = projects[projectIndex];
            for (var sessionIndex = 0; sessionIndex < sessionsPerProject; sessionIndex++, sessionOrdinal++)
            {
                var timestamp = baseTime.AddMinutes(-(sessionOrdinal - 1));
                object[] messages = Array.Empty<object>();

                if (withContent)
                {
                    var count = Math.Max(2, messageCountPerSession);
                    messages = Enumerable.Range(1, count)
                        .Select(messageIndex => new
                        {
                            id = $"mp-{sessionOrdinal}-{messageIndex}",
                            timestamp = timestamp.AddSeconds(messageIndex),
                            contentType = "text",
                            textContent = $"GUI Project {projectIndex + 1:00} Session {sessionIndex + 1:00} message {messageIndex:000}",
                            isOutgoing = messageIndex % 2 == 0
                        })
                        .Cast<object>()
                        .ToArray();
                }

                conversations.Add(new
                {
                    conversationId = $"gui-session-{sessionOrdinal:00}",
                    displayName = $"GUI Session {sessionOrdinal:00}",
                    createdAt = timestamp,
                    lastUpdatedAt = timestamp,
                    cwd = project.RootPath,
                    messages
                });
            }
        }

        var document = new
        {
            version = 1,
            lastActiveConversationId = (string?)null,
            conversations
        };

        return JsonSerializer.Serialize(document);
    }

    private static string BuildVariableHeightConversationsJson(string projectRootPath, int messageCount)
    {
        var timestamp = new DateTimeOffset(2026, 05, 08, 10, 00, 00, TimeSpan.Zero);
        var messages = Enumerable.Range(1, messageCount)
            .Select(messageIndex => new
            {
                id = $"vh-{messageIndex}",
                timestamp = timestamp.AddSeconds(messageIndex),
                contentType = "text",
                textContent = BuildVariableHeightMessageText(messageIndex),
                isOutgoing = messageIndex % 3 == 0
            })
            .Cast<object>()
            .ToArray();

        var document = new
        {
            version = 1,
            lastActiveConversationId = (string?)null,
            conversations = new object[]
            {
                new
                {
                    conversationId = "gui-variable-height-session-01",
                    displayName = "GUI Variable Height Session 01",
                    createdAt = timestamp,
                    lastUpdatedAt = timestamp.AddSeconds(messageCount),
                    cwd = projectRootPath,
                    messages
                }
            }
        };

        return JsonSerializer.Serialize(document);
    }

    private static string BuildVariableHeightMessageText(int messageIndex)
    {
        if (messageIndex % 11 == 0)
        {
            return string.Join(
                Environment.NewLine,
                Enumerable.Range(1, 18)
                    .Select(line => $"GUI variable message {messageIndex:000} expanded line {line:00} with enough text to wrap inside the transcript viewport"));
        }

        if (messageIndex % 5 == 0)
        {
            return $"GUI variable message {messageIndex:000} medium content " + string.Join(
                " ",
                Enumerable.Repeat("wrap-segment", 30));
        }

        return $"GUI variable message {messageIndex:000}";
    }

    private static string BuildMarkdownHeavyConversationsJson(string projectRootPath, int messageCount)
    {
        var timestamp = new DateTimeOffset(2026, 05, 09, 10, 00, 00, TimeSpan.Zero);
        var messages = Enumerable.Range(1, messageCount)
            .Select(messageIndex => new
            {
                id = $"md-heavy-{messageIndex}",
                timestamp = timestamp.AddSeconds(messageIndex),
                contentType = "text",
                textContent = BuildMarkdownHeavyMessageText(messageIndex),
                isOutgoing = messageIndex % 4 == 0
            })
            .Cast<object>()
            .ToArray();

        var document = new
        {
            version = 1,
            lastActiveConversationId = (string?)null,
            conversations = new object[]
            {
                new
                {
                    conversationId = "gui-markdown-heavy-session-01",
                    displayName = "GUI Markdown Heavy Session 01",
                    createdAt = timestamp,
                    lastUpdatedAt = timestamp.AddSeconds(messageCount),
                    cwd = projectRootPath,
                    messages
                }
            }
        };

        return JsonSerializer.Serialize(document);
    }

    private static string BuildTallLastMessageConversationsJson(string projectRootPath, int messageCount)
    {
        var timestamp = new DateTimeOffset(2026, 05, 09, 11, 00, 00, TimeSpan.Zero);
        var messages = Enumerable.Range(1, messageCount)
            .Select(messageIndex => new
            {
                id = $"tall-last-{messageIndex}",
                timestamp = timestamp.AddSeconds(messageIndex),
                contentType = "text",
                textContent = messageIndex == messageCount
                    ? BuildTallLastMessageText(messageIndex)
                    : $"GUI tall last prelude message {messageIndex:000}",
                isOutgoing = messageIndex % 4 == 0
            })
            .Cast<object>()
            .ToArray();

        var document = new
        {
            version = 1,
            lastActiveConversationId = (string?)null,
            conversations = new object[]
            {
                new
                {
                    conversationId = "gui-tall-last-message-session-01",
                    displayName = "GUI Tall Last Message Session 01",
                    createdAt = timestamp,
                    lastUpdatedAt = timestamp.AddSeconds(messageCount),
                    cwd = projectRootPath,
                    messages
                }
            }
        };

        return JsonSerializer.Serialize(document);
    }

    private static string BuildTallLastMessageText(int messageIndex)
    {
        var lines = new List<string>
        {
            $"GUI tall last message start {messageIndex:000}",
            string.Empty,
        };
        lines.AddRange(Enumerable.Range(1, 80)
            .Select(line => $"Tall final markdown line {line:000}: enough text to make the final message exceed the viewport and require true native bottom scrolling."));
        lines.Add($"GUI tall last bottom sentinel {messageIndex:000}");
        return string.Join("\n", lines);
    }

    private static string BuildMarkdownHeavyMessageText(int messageIndex)
    {
        if (messageIndex > 176)
        {
            return $"GUI markdown heavy bottom sentinel {messageIndex:000}";
        }

        if (messageIndex % 9 == 0)
        {
            return string.Join(
                "\n",
                $"### GUI markdown heavy message {messageIndex:000}",
                "",
                "This paragraph contains **bold text**, `inline code`, and enough prose to wrap across several lines in the transcript viewport.",
                "",
                "- first bullet with enough text to wrap inside the message bubble",
                "- second bullet with more content for variable-height layout",
                "",
                "```json",
                $"{{\"message\":{messageIndex},\"status\":\"ok\"}}",
                "```");
        }

        if (messageIndex % 5 == 0)
        {
            return string.Join(
                "\n",
                $"GUI markdown heavy message {messageIndex:000}",
                "",
                "> Quoted markdown block with wrapped content for viewport measurement.",
                "",
                "| key | value |",
                "| --- | --- |",
                $"| message | {messageIndex:000} |");
        }

        return $"GUI markdown heavy message {messageIndex:000}";
    }

    private static string BuildMarkdownRenderConversationsJson(string projectRootPath)
    {
        var timestamp = new DateTimeOffset(2026, 04, 16, 10, 00, 00, TimeSpan.Zero);
        var document = new
        {
            version = 1,
            lastActiveConversationId = (string?)null,
            conversations = new object[]
            {
                new
                {
                    conversationId = "gui-markdown-session-01",
                    displayName = "GUI Markdown Session 01",
                    createdAt = timestamp,
                    lastUpdatedAt = timestamp,
                    cwd = projectRootPath,
                    messages = new object[]
                    {
                        new
                        {
                            id = "md-unclosed-1",
                            timestamp = timestamp.AddSeconds(1),
                            contentType = "text",
                            textContent = "```csharp\nvar partial = true;",
                            isOutgoing = false
                        },
                        new
                        {
                            id = "md-closed-1",
                            timestamp = timestamp.AddSeconds(2),
                            contentType = "text",
                            textContent = "```csharp\nvar done = true;\n```",
                            isOutgoing = false
                        },
                        new
                        {
                            id = "md-rich-1",
                            timestamp = timestamp.AddSeconds(3),
                            contentType = "text",
                            textContent = string.Join(
                                "\n",
                                "## Reading sample",
                                "",
                                "This paragraph checks ordinary markdown reading rhythm with `inline code` inside a longer sentence.",
                                "",
                                "- first list item",
                                "- second list item",
                                "",
                                "> A quoted note should use the quote treatment.",
                                "",
                                "| key | value |",
                                "| --- | --- |",
                                "| alpha | beta |",
                                "",
                                "```json",
                                "{\"status\":\"ok\",\"count\":2}",
                                "```"),
                            isOutgoing = false
                        }
                    }
                }
            }
        };

        return JsonSerializer.Serialize(document);
    }

    private static string BuildToolCallConversationsJson(string projectRootPath)
    {
        var timestamp = new DateTimeOffset(2026, 04, 19, 10, 00, 00, TimeSpan.Zero);
        var document = new
        {
            version = 1,
            lastActiveConversationId = (string?)null,
            conversations = new object[]
            {
                new
                {
                    conversationId = "gui-toolcall-session-01",
                    displayName = "GUI Tool Call Session 01",
                    createdAt = timestamp,
                    lastUpdatedAt = timestamp,
                    cwd = projectRootPath,
                    messages = new object[]
                    {
                        new
                        {
                            id = "toolcall-01",
                            timestamp = timestamp.AddSeconds(1),
                            contentType = "tool_call",
                            title = "",
                            textContent = "",
                            isOutgoing = false,
                            toolCallId = "call-1",
                            toolCallKind = "read",
                            toolCallStatus = "completed",
                            toolCallJson = "{\"path\":\"C:/repo/appsettings.json\",\"query\":\"Logging\",\"arguments\":{\"line\":12}}"
                        }
                    }
                }
            }
        };

        return JsonSerializer.Serialize(document);
    }

    private static string BuildSlowRemoteReplayConversationsJson(
        string projectRootPath,
        string profileId,
        string remoteSessionId,
        int cachedMessageCount,
        bool includeLocalConversation,
        int localMessageCount,
        int remoteConversationCount)
    {
        var remoteTimestamp = new DateTimeOffset(2026, 03, 29, 12, 00, 00, TimeSpan.Zero);
        var cachedMessages = cachedMessageCount <= 0
            ? Array.Empty<object>()
            : Enumerable.Range(1, cachedMessageCount)
                .Select(messageIndex => new
                {
                    id = $"cached-{messageIndex}",
                    timestamp = remoteTimestamp.AddSeconds(messageIndex),
                    contentType = "text",
                    textContent = $"GUI Remote Session 01 cached {messageIndex:000}",
                    isOutgoing = messageIndex % 2 != 0
                })
                .Cast<object>()
                .ToArray();

        var conversations = new List<object>();

        if (includeLocalConversation)
        {
            var localTimestamp = remoteTimestamp.AddMinutes(-1);
            conversations.Add(new
            {
                conversationId = "gui-local-conversation-01",
                displayName = "GUI Local Session 01",
                createdAt = localTimestamp,
                lastUpdatedAt = localTimestamp,
                cwd = projectRootPath,
                messages = Enumerable.Range(1, localMessageCount)
                    .Select(messageIndex => new
                    {
                        id = $"local-{messageIndex}",
                        timestamp = localTimestamp.AddSeconds(messageIndex),
                        contentType = "text",
                        textContent = $"GUI Local Session 01 message {messageIndex:000}",
                        isOutgoing = messageIndex % 2 == 0
                    })
                    .Cast<object>()
                    .ToArray()
            });
        }

        for (var remoteIndex = 1; remoteIndex <= remoteConversationCount; remoteIndex++)
        {
            var suffix = remoteIndex.ToString("00");
            var conversationId = $"gui-remote-conversation-{suffix}";
            var sessionId = remoteIndex == 1 ? remoteSessionId : $"gui-remote-session-{suffix}";
            conversations.Add(new
            {
                conversationId,
                displayName = $"GUI Remote Session {suffix}",
                createdAt = remoteTimestamp.AddSeconds(remoteIndex),
                lastUpdatedAt = remoteTimestamp.AddSeconds(remoteIndex),
                cwd = projectRootPath,
                remoteSessionId = sessionId,
                boundProfileId = profileId,
                messages = cachedMessages
            });
        }

        var document = new
        {
            version = 1,
            lastActiveConversationId = (string?)null,
            conversations = conversations.ToArray()
        };

        return JsonSerializer.Serialize(document);
    }

    private static string BuildCrossProfileRemoteReplayConversationsJson(
        string projectRootPath,
        string profileA,
        string profileB,
        int cachedMessageCount,
        int localMessageCount)
    {
        var remoteTimestamp = new DateTimeOffset(2026, 03, 29, 12, 00, 00, TimeSpan.Zero);
        var cachedMessagesForRemote1 = cachedMessageCount <= 0
            ? Array.Empty<object>()
            : Enumerable.Range(1, cachedMessageCount)
                .Select(messageIndex => new
                {
                    id = $"remote1-cached-{messageIndex}",
                    timestamp = remoteTimestamp.AddSeconds(messageIndex),
                    contentType = "text",
                    textContent = $"GUI Remote Session 01 cached {messageIndex:000}",
                    isOutgoing = messageIndex % 2 != 0
                })
                .Cast<object>()
                .ToArray();
        var cachedMessagesForRemote2 = cachedMessageCount <= 0
            ? Array.Empty<object>()
            : Enumerable.Range(1, cachedMessageCount)
                .Select(messageIndex => new
                {
                    id = $"remote2-cached-{messageIndex}",
                    timestamp = remoteTimestamp.AddSeconds(30 + messageIndex),
                    contentType = "text",
                    textContent = $"GUI Remote Session 02 cached {messageIndex:000}",
                    isOutgoing = messageIndex % 2 == 0
                })
                .Cast<object>()
                .ToArray();

        var localTimestamp = remoteTimestamp.AddMinutes(-1);
        var conversations = new object[]
        {
            new
            {
                conversationId = "gui-local-conversation-01",
                displayName = "GUI Local Session 01",
                createdAt = localTimestamp,
                lastUpdatedAt = localTimestamp,
                cwd = projectRootPath,
                messages = Enumerable.Range(1, localMessageCount)
                    .Select(messageIndex => new
                    {
                        id = $"local-{messageIndex}",
                        timestamp = localTimestamp.AddSeconds(messageIndex),
                        contentType = "text",
                        textContent = $"GUI Local Session 01 message {messageIndex:000}",
                        isOutgoing = messageIndex % 2 == 0
                    })
                    .Cast<object>()
                    .ToArray()
            },
            new
            {
                conversationId = "gui-remote-conversation-01",
                displayName = "GUI Remote Session 01",
                createdAt = remoteTimestamp.AddSeconds(1),
                lastUpdatedAt = remoteTimestamp.AddSeconds(1),
                cwd = projectRootPath,
                remoteSessionId = "gui-remote-session-01",
                boundProfileId = profileA,
                messages = cachedMessagesForRemote1
            },
            new
            {
                conversationId = "gui-remote-conversation-02",
                displayName = "GUI Remote Session 02",
                createdAt = remoteTimestamp.AddSeconds(2),
                lastUpdatedAt = remoteTimestamp.AddSeconds(2),
                cwd = projectRootPath,
                remoteSessionId = "gui-remote-session-02",
                boundProfileId = profileB,
                messages = cachedMessagesForRemote2
            }
        };

        var document = new
        {
            version = 1,
            lastActiveConversationId = (string?)null,
            conversations
        };

        return JsonSerializer.Serialize(document);
    }

    private static string BuildServerYaml(
        string profileId,
        string transport,
        string? stdioCommand = null,
        string? stdioArgs = null,
        string? serverUrl = null)
    {
        var normalizedTransport = transport.Replace("'", "''", StringComparison.Ordinal);
        var lines = new List<string>
        {
            "schema_version: 1",
            $"id: '{profileId}'",
            "name: 'GUI Mock ACP Harness'",
            $"transport: '{normalizedTransport}'",
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

            var normalizedCommand = stdioCommand.Replace("'", "''", StringComparison.Ordinal);
            var normalizedArgs = (stdioArgs ?? string.Empty).Replace("'", "''", StringComparison.Ordinal);
            lines.Insert(4, $"stdio_command: '{normalizedCommand}'");
            lines.Insert(5, $"stdio_args: '{normalizedArgs}'");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(serverUrl))
            {
                throw new ArgumentException("serverUrl is required for remote transport.", nameof(serverUrl));
            }

            lines.Insert(4, $"server_url: '{serverUrl.Replace("'", "''", StringComparison.Ordinal)}'");
        }

        lines.Add(string.Empty);
        return string.Join(Environment.NewLine, lines);
    }

    private static MockAcpHarnessScenario NormalizeMockAcpHarnessScenario(
        MockAcpHarnessScenario scenario,
        string projectRootPath)
    {
        var acceptedCwd = string.IsNullOrWhiteSpace(scenario.AcceptedCwd)
            ? projectRootPath
            : scenario.AcceptedCwd.Trim();

        var mappedRemoteCwd = string.IsNullOrWhiteSpace(scenario.MappedRemoteCwd)
            ? acceptedCwd
            : scenario.MappedRemoteCwd.Trim();

        return scenario with
        {
            AcceptedCwd = acceptedCwd,
            MappedRemoteCwd = mappedRemoteCwd
        };
    }

    private static string BuildMockAcpHarnessScenarioJson(MockAcpHarnessScenario scenario)
    {
        var document = new
        {
            transportKind = scenario.TransportKind.ToString(),
            initializeBehavior = scenario.InitializeBehavior.ToString(),
            sessionNewBehavior = scenario.SessionNewBehavior.ToString(),
            cwdAcceptancePolicy = scenario.CwdAcceptancePolicy.ToString(),
            modesVariant = scenario.ModesVariant.ToString(),
            sessionId = scenario.SessionId,
            acceptedCwd = scenario.AcceptedCwd,
            mappedRemoteCwd = scenario.MappedRemoteCwd,
            initializeDelayMs = scenario.InitializeDelayMs,
            sessionNewDelayMs = scenario.SessionNewDelayMs,
            sessionNewErrorCode = scenario.SessionNewErrorCode,
            sessionNewErrorMessage = scenario.SessionNewErrorMessage
        };

        return JsonSerializer.Serialize(document);
    }

    private static Process StartMockAcpHarnessProcess(
        string scenarioPath,
        string websocketUrl,
        string readySignalPath)
    {
        var scriptPath = ResolveMockAcpHarnessScriptPath();
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add("-ScenarioJsonPath");
        startInfo.ArgumentList.Add(scenarioPath);
        startInfo.ArgumentList.Add("-ListenUrl");
        startInfo.ArgumentList.Add(websocketUrl);
        startInfo.ArgumentList.Add("-ReadySignalPath");
        startInfo.ArgumentList.Add(readySignalPath);

        var process = Process.Start(startInfo);
        if (process is null)
        {
            throw new InvalidOperationException("Failed to start the mock ACP websocket harness process.");
        }

        return process;
    }

    private static void WaitForMockAcpHarnessReady(string readySignalPath, int timeoutMs = 15000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(readySignalPath))
            {
                var signal = File.ReadAllText(readySignalPath);
                if (!string.IsNullOrWhiteSpace(signal))
                {
                    return;
                }
            }

            Thread.Sleep(50);
        }

        throw new TimeoutException($"Timed out waiting for the mock ACP websocket harness to become ready: {readySignalPath}");
    }

    private static int AllocateLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private void TryStopMockAcpHarnessProcess()
    {
        if (_mockAcpHarnessProcess is null)
        {
            return;
        }

        try
        {
            if (!_mockAcpHarnessProcess.HasExited)
            {
                _mockAcpHarnessProcess.Kill(entireProcessTree: true);
                _mockAcpHarnessProcess.WaitForExit(5000);
            }
        }
        catch
        {
        }
        finally
        {
            _mockAcpHarnessProcess.Dispose();
        }
    }

    private static string QuoteCommandLineArgument(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "\"\"";
        }

        return value.IndexOfAny(new[] { ' ', '\t', '"' }) >= 0
            ? $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : value;
    }

    private static string ResolveSlowReplayAgentScriptPath()
    {
        var repoRoot = ResolveRepoRoot();
        var scriptPath = Path.Combine(repoRoot, "tests", "SalmonEgg.GuiTests.Windows", "Fixtures", "SlowReplayAgent.ps1");
        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException("Slow replay ACP agent script was not found.", scriptPath);
        }

        return scriptPath;
    }

    private static string ResolveMockAcpHarnessScriptPath()
    {
        var repoRoot = ResolveRepoRoot();
        var scriptPath = Path.Combine(repoRoot, "tests", "SalmonEgg.GuiTests.Windows", "Fixtures", "MockAcpHarness.ps1");
        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException("Mock ACP harness script was not found.", scriptPath);
        }

        return scriptPath;
    }

    private static string ResolveRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "SalmonEgg.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Failed to locate repository root from GUI test output directory.");
    }

    private static void RestoreFile(string path, byte[]? content, bool existed)
    {
        try
        {
            if (existed && content != null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, content);
                return;
            }

            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static void DeleteDirectoryIfEmpty(string path)
    {
        try
        {
            if (Directory.Exists(path) &&
                !Directory.EnumerateFileSystemEntries(path).Any())
            {
                Directory.Delete(path, recursive: false);
            }
        }
        catch
        {
        }
    }

    private static IReadOnlyDictionary<string, string?> CaptureEnvironmentVariables(params string[] names)
    {
        var snapshot = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            snapshot[name] = Environment.GetEnvironmentVariable(name);
        }

        return snapshot;
    }

    private static string ReadTailAllowSharedRead(string path, int lineCount)
    {
        var lines = new Queue<string>(Math.Max(lineCount, 1));
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);

        while (reader.ReadLine() is { } line)
        {
            if (lines.Count == lineCount)
            {
                lines.Dequeue();
            }

            lines.Enqueue(line);
        }

        return string.Join(Environment.NewLine, lines);
    }
}
