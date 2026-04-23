using System.Collections.Generic;
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
    private readonly string _appDataRoot;
    private readonly string _configDirectory;
    private readonly string _conversationsDirectory;
    private readonly string _serversDirectory;
    private readonly string _appYamlPath;
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
    private bool _disposed;

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
        IReadOnlyDictionary<string, string?>? environmentRestoreMap = null)
    {
        _appDataRoot = appDataRoot;
        _configDirectory = Path.GetDirectoryName(appYamlPath)!;
        _conversationsDirectory = Path.GetDirectoryName(conversationsPath)!;
        _serversDirectory = Path.Combine(_configDirectory, "servers");
        _appYamlPath = appYamlPath;
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
    }

    public static GuiAppDataScope CreateDeterministicLeftNavData(
        int sessionCount = 1,
        bool withContent = false,
        int messageCountPerSession = 2)
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
            previousGuiAppDataRootOverride);

        scope.Seed(sessionCount, withContent, messageCountPerSession);
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

        if (remoteConversationCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(remoteConversationCount));
        }

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
            previousFakeReplayMessageCount);

        scope.SeedSlowRemoteReplay(
            profileId,
            cachedMessageCount,
            replayMessageCount,
            includeLocalConversation,
            localMessageCount,
            remoteConversationCount);
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
        const string controlFileEnvVar = "SALMONEGG_GUI_CONTROL_FILE";
        var previousControlFile = Environment.GetEnvironmentVariable(controlFileEnvVar);
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
        Environment.SetEnvironmentVariable(controlFileEnvVar, controlFilePath);
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
        Environment.SetEnvironmentVariable("SALMONEGG_GUI_CONTROL_FILE", _previousGuiControlFile);
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

    private void Seed(int sessionCount, bool withContent = false, int messageCountPerSession = 2)
    {
        Directory.CreateDirectory(_configDirectory);
        Directory.CreateDirectory(_conversationsDirectory);
        Directory.CreateDirectory(_projectRootPath);

        File.WriteAllText(_appYamlPath, BuildAppYaml(_projectRootPath), Encoding.UTF8);
        File.WriteAllText(
            _conversationsPath,
            BuildConversationsJson(_projectRootPath, sessionCount, withContent, messageCountPerSession),
            Encoding.UTF8);
    }

    public void TriggerBackgroundRemoteAgentUpdate(string remoteSessionId, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteSessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var controlPath = Environment.GetEnvironmentVariable("SALMONEGG_GUI_CONTROL_FILE")
            ?? throw new InvalidOperationException("SALMONEGG_GUI_CONTROL_FILE was not set.");

        Directory.CreateDirectory(Path.GetDirectoryName(controlPath)!);
        File.WriteAllText(
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

        File.WriteAllText(_appYamlPath, BuildAppYaml(_projectRootPath), Encoding.UTF8);
        File.WriteAllText(
            _conversationsPath,
            BuildMarkdownRenderConversationsJson(_projectRootPath),
            Encoding.UTF8);
    }

    private void SeedToolCallScenario()
    {
        Directory.CreateDirectory(_configDirectory);
        Directory.CreateDirectory(_conversationsDirectory);
        Directory.CreateDirectory(_projectRootPath);

        File.WriteAllText(_appYamlPath, BuildAppYaml(_projectRootPath), Encoding.UTF8);
        File.WriteAllText(
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
    {
        if (string.IsNullOrWhiteSpace(_serverYamlPath))
        {
            throw new InvalidOperationException("Remote replay seed requires a server YAML path.");
        }

        Directory.CreateDirectory(_configDirectory);
        Directory.CreateDirectory(_conversationsDirectory);
        Directory.CreateDirectory(_serversDirectory);
        Directory.CreateDirectory(_projectRootPath);

        var agentScriptPath = ResolveSlowReplayAgentScriptPath();

        File.WriteAllText(
            _appYamlPath,
            BuildAppYaml(_projectRootPath, profileId),
            Encoding.UTF8);
        File.WriteAllText(
            _conversationsPath,
            BuildSlowRemoteReplayConversationsJson(
                _projectRootPath,
                profileId,
                cachedMessageCount,
                includeLocalConversation,
                localMessageCount,
                remoteConversationCount),
            Encoding.UTF8);
        File.WriteAllText(
            _serverYamlPath,
            BuildServerYaml(
                profileId,
                "powershell.exe",
                $"-NoLogo -NoProfile -ExecutionPolicy Bypass -File {QuoteCommandLineArgument(agentScriptPath)} -SessionId gui-remote-session-01 -MessageCount {replayMessageCount}"),
            Encoding.UTF8);

        Environment.SetEnvironmentVariable("SALMONEGG_GUI_FAKE_REMOTE_REPLAY_SESSION_ID", "gui-remote-session-01");
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

        File.WriteAllText(
            _appYamlPath,
            BuildAppYaml(_projectRootPath, profileA),
            Encoding.UTF8);
        File.WriteAllText(
            _conversationsPath,
            BuildCrossProfileRemoteReplayConversationsJson(
                _projectRootPath,
                profileA,
                profileB,
                cachedMessageCount,
                localMessageCount),
            Encoding.UTF8);
        File.WriteAllText(
            _serverYamlPath,
            BuildServerYaml(profileA, "powershell.exe", $"{agentArgsPrefix} -SessionId gui-remote-session-01 -MessageCount {replayMessageCount} -ListDelayMs 700"),
            Encoding.UTF8);
        File.WriteAllText(
            _secondaryServerYamlPath,
            BuildServerYaml(profileB, "powershell.exe", $"{agentArgsPrefix} -SessionId gui-remote-session-02 -MessageCount {replayMessageCount} -ListDelayMs 0"),
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

    private static string BuildConversationsJson(
        string projectRootPath,
        int sessionCount,
        bool withContent = false,
        int messageCountPerSession = 2)
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
                    displayName = $"GUI Session {index:00}",
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
            var sessionId = $"gui-remote-session-{suffix}";
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

    private static string BuildServerYaml(string profileId, string command, string args)
    {
        var normalizedCommand = command.Replace("'", "''", StringComparison.Ordinal);
        var normalizedArgs = args.Replace("'", "''", StringComparison.Ordinal);

        return string.Join(
            Environment.NewLine,
            "schema_version: 1",
            $"id: '{profileId}'",
            "name: 'GUI Slow Replay Agent'",
            "transport: 'stdio'",
            $"stdio_command: '{normalizedCommand}'",
            $"stdio_args: '{normalizedArgs}'",
            "heartbeat_interval_seconds: 30",
            "connection_timeout_seconds: 10",
            "authentication:",
            "  mode: 'none'",
            "proxy:",
            "  enabled: false",
            "  proxy_url: ''",
            string.Empty);
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
