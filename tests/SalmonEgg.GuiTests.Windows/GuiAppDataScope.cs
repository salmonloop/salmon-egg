using System.Text;
using System.Text.Json;

namespace SalmonEgg.GuiTests.Windows;

internal sealed class GuiAppDataScope : IDisposable
{
    private const string AppDataRootEnvVar = "SALMONEGG_APPDATA_ROOT";
    private readonly string _appDataRoot;
    private readonly string _configDirectory;
    private readonly string _conversationsDirectory;
    private readonly string _serversDirectory;
    private readonly string _appYamlPath;
    private readonly string _conversationsPath;
    private readonly string? _serverYamlPath;
    private readonly byte[]? _originalAppYaml;
    private readonly byte[]? _originalConversations;
    private readonly byte[]? _originalServerYaml;
    private readonly bool _appYamlExisted;
    private readonly bool _conversationsExisted;
    private readonly bool _serverYamlExisted;
    private readonly string _projectRootPath;
    private readonly string? _previousGuiAppDataRootOverride;
    private readonly string? _previousFakeReplaySessionId;
    private readonly string? _previousFakeReplayMessageCount;
    private bool _disposed;

    private GuiAppDataScope(
        string appDataRoot,
        string appYamlPath,
        string conversationsPath,
        string? serverYamlPath,
        byte[]? originalAppYaml,
        bool appYamlExisted,
        byte[]? originalConversations,
        bool conversationsExisted,
        byte[]? originalServerYaml,
        bool serverYamlExisted,
        string projectRootPath,
        string? previousGuiAppDataRootOverride,
        string? previousFakeReplaySessionId = null,
        string? previousFakeReplayMessageCount = null)
    {
        _appDataRoot = appDataRoot;
        _configDirectory = Path.GetDirectoryName(appYamlPath)!;
        _conversationsDirectory = Path.GetDirectoryName(conversationsPath)!;
        _serversDirectory = Path.Combine(_configDirectory, "servers");
        _appYamlPath = appYamlPath;
        _conversationsPath = conversationsPath;
        _serverYamlPath = serverYamlPath;
        _originalAppYaml = originalAppYaml;
        _appYamlExisted = appYamlExisted;
        _originalConversations = originalConversations;
        _conversationsExisted = conversationsExisted;
        _originalServerYaml = originalServerYaml;
        _serverYamlExisted = serverYamlExisted;
        _projectRootPath = projectRootPath;
        _previousGuiAppDataRootOverride = previousGuiAppDataRootOverride;
        _previousFakeReplaySessionId = previousFakeReplaySessionId;
        _previousFakeReplayMessageCount = previousFakeReplayMessageCount;
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
            File.Exists(appYamlPath) ? File.ReadAllBytes(appYamlPath) : null,
            File.Exists(appYamlPath),
            File.Exists(conversationsPath) ? File.ReadAllBytes(conversationsPath) : null,
            File.Exists(conversationsPath),
            originalServerYaml: null,
            serverYamlExisted: false,
            projectRootPath,
            previousGuiAppDataRootOverride);

        scope.Seed(sessionCount, withContent, messageCountPerSession);
        return scope;
    }

    public static GuiAppDataScope CreateDeterministicSlowRemoteReplayData(
        int cachedMessageCount = 1,
        int replayMessageCount = 60)
    {
        if (cachedMessageCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cachedMessageCount));
        }

        if (replayMessageCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(replayMessageCount));
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
            File.Exists(appYamlPath) ? File.ReadAllBytes(appYamlPath) : null,
            File.Exists(appYamlPath),
            File.Exists(conversationsPath) ? File.ReadAllBytes(conversationsPath) : null,
            File.Exists(conversationsPath),
            File.Exists(serverYamlPath) ? File.ReadAllBytes(serverYamlPath) : null,
            File.Exists(serverYamlPath),
            projectRootPath,
            previousGuiAppDataRootOverride,
            previousFakeReplaySessionId,
            previousFakeReplayMessageCount);

        scope.SeedSlowRemoteReplay(profileId, cachedMessageCount, replayMessageCount);
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
        DeleteDirectoryIfEmpty(_configDirectory);
        DeleteDirectoryIfEmpty(_conversationsDirectory);
        DeleteDirectoryIfEmpty(_serversDirectory);
        DeleteDirectoryIfEmpty(_appDataRoot);
        Environment.SetEnvironmentVariable(AppDataRootEnvVar, _previousGuiAppDataRootOverride);
        Environment.SetEnvironmentVariable("SALMONEGG_GUI_FAKE_REMOTE_REPLAY_SESSION_ID", _previousFakeReplaySessionId);
        Environment.SetEnvironmentVariable("SALMONEGG_GUI_FAKE_REMOTE_REPLAY_MESSAGE_COUNT", _previousFakeReplayMessageCount);

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
            return string.Join(
                Environment.NewLine,
                File.ReadLines(bootLogPath)
                    .TakeLast(lineCount));
        }
        catch (Exception ex)
        {
            return $"<boot.log unreadable: {ex.Message}>";
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

    private void SeedSlowRemoteReplay(
        string profileId,
        int cachedMessageCount,
        int replayMessageCount)
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
            BuildSlowRemoteReplayConversationsJson(_projectRootPath, profileId, cachedMessageCount),
            Encoding.UTF8);
        File.WriteAllText(
            _serverYamlPath,
            BuildServerYaml(profileId, "powershell.exe", $"-NoLogo -NoProfile -ExecutionPolicy Bypass -File {QuoteCommandLineArgument(agentScriptPath)}"),
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

    private static string BuildSlowRemoteReplayConversationsJson(
        string projectRootPath,
        string profileId,
        int cachedMessageCount)
    {
        var timestamp = new DateTimeOffset(2026, 03, 29, 12, 00, 00, TimeSpan.Zero);
        var cachedMessages = cachedMessageCount <= 0
            ? Array.Empty<object>()
            : Enumerable.Range(1, cachedMessageCount)
                .Select(messageIndex => new
                {
                    id = $"cached-{messageIndex}",
                    timestamp = timestamp.AddSeconds(messageIndex),
                    contentType = "text",
                    textContent = $"GUI Remote Session 01 cached {messageIndex:000}",
                    isOutgoing = messageIndex % 2 != 0
                })
                .Cast<object>()
                .ToArray();

        var document = new
        {
            version = 1,
            lastActiveConversationId = (string?)null,
            conversations = new[]
            {
                new
                {
                    conversationId = "gui-remote-conversation-01",
                    displayName = "GUI Remote Session 01",
                    createdAt = timestamp,
                    lastUpdatedAt = timestamp,
                    cwd = projectRootPath,
                    remoteSessionId = "gui-remote-session-01",
                    boundProfileId = profileId,
                    messages = cachedMessages
                }
            }
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
}
