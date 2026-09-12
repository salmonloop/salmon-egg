using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace SalmonEgg.GuiTests.Windows;

public sealed class TerminalAuthenticationSmokeTests
{
    [Fact]
    public void SignIn_ConsentedNativeInput_ReconnectsAndRetriesPrompt()
    {
        // Arrange
        using var fixture = new Fixture("success");
        using var app = WindowsGuiAppSession.LaunchFresh();
        fixture.SendPrompt(app);

        // Act
        fixture.WaitForConsent(app);
        Assert.False(File.Exists(fixture.LoginPath));
        ClickNamedButton(app, "Open sign-in");
        fixture.EnterTerminalInput(app);

        // Assert
        Assert.True(app.WaitUntil(() => fixture.Requests().Count(row => row.Method == "session/prompt" && row.Authenticated) == 1,
            TimeSpan.FromSeconds(25)), "Login did not retry the original prompt over a fresh connection.");
        Assert.True(app.WaitUntil(() => app.GetVisibleTexts().Any(text => text.Contains("PACKAGED_AUTH_REPLY", StringComparison.Ordinal)),
            TimeSpan.FromSeconds(10)), "The authenticated reply did not appear in the real transcript.");
        var starts = fixture.Requests().Where(row => row.Method == "initialize").ToArray();
        Assert.True(starts.Select(row => row.ProcessId).Distinct().Count() >= 2, "Authentication did not replace the ACP process.");
        Assert.DoesNotContain(fixture.Requests(), row => row.Method == "authenticate");
        fixture.AssertLoginExited(app);
    }

    [Fact]
    public void SignIn_DeclinedConsent_DoesNotLaunchTerminal()
    {
        // Arrange
        using var fixture = new Fixture("success");
        using var app = WindowsGuiAppSession.LaunchFresh();
        fixture.SendPrompt(app);
        fixture.WaitForConsent(app);
        var connections = fixture.Requests().Count(row => row.Method == "initialize");

        // Act
        ClickNamedButton(app, "Cancel");

        // Assert
        Assert.True(app.WaitUntil(() => app.TryFindVisibleElementByNameAnywhere("Open sign-in", TimeSpan.FromMilliseconds(100)) is null,
            TimeSpan.FromSeconds(10)), "The declined consent dialog remained open.");
        Assert.False(File.Exists(fixture.LoginPath));
        Assert.Equal(connections, fixture.Requests().Count(row => row.Method == "initialize"));
        Assert.DoesNotContain(fixture.Requests(), row => row.Authenticated);
    }

    [Theory]
    [InlineData("failure")]
    [InlineData("cancel")]
    public void SignIn_UnsuccessfulTerminal_ReclaimsProcessWithoutReconnect(string scenario)
    {
        // Arrange
        using var fixture = new Fixture(scenario);
        using var app = WindowsGuiAppSession.LaunchFresh();
        fixture.SendPrompt(app);
        fixture.WaitForConsent(app);
        var connections = fixture.Requests().Count(row => row.Method == "initialize");

        // Act
        ClickNamedButton(app, "Open sign-in");
        if (scenario == "failure") fixture.EnterTerminalInput(app);
        else
        {
            Assert.True(app.WaitUntil(() => File.Exists(fixture.LoginPath), TimeSpan.FromSeconds(20)));
            Assert.True(app.WaitUntilOnscreen("ChatAuth.TerminalDialog", TimeSpan.FromSeconds(10)));
            ClickNamedButton(app, "Cancel");
        }

        // Assert
        fixture.AssertLoginExited(app);
        Assert.Equal(connections, fixture.Requests().Count(row => row.Method == "initialize"));
        Assert.DoesNotContain(fixture.Requests(), row => row.Authenticated);
        Assert.False(File.Exists(Path.Combine(fixture.Root, "signed-in")));
    }

    private static void ClickNamedButton(WindowsGuiAppSession app, string text)
    {
        var button = app.FindVisibleElementByNameAnywhere(text, TimeSpan.FromSeconds(15));
        Assert.NotNull(button);
        Assert.True(app.WaitUntil(() => button.IsEnabled, TimeSpan.FromSeconds(10)));
        app.ClickElement(button);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string? _oldRoot;
        private readonly string _scenario;

        public Fixture(string scenario)
        {
            GuiTestGate.RequireEnabled();
            _scenario = scenario;
            GuiAcceptanceDiagnostics.Record("Terminal: create fixture " + scenario);
            Root = Path.Combine(Path.GetTempPath(), "SalmonEgg.TerminalGui", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(Root, "config", "servers"));
            Directory.CreateDirectory(Path.Combine(Root, "conversations"));
            Directory.CreateDirectory(Path.Combine(Root, "project"));
            File.WriteAllText(Path.Combine(Root, "scenario.txt"), scenario);
            var script = FindFixtureScript();
            var command = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
            File.WriteAllText(Path.Combine(Root, "config", "servers", "terminal-profile.yaml"),
                "schema_version: 5\nid: terminal-profile\nname: Packaged Terminal Fixture\ntransport: stdio\n"
                + "stdio_command: " + Quote(command) + "\nstdio_arguments:\n"
                + string.Join("\n", new[] { "-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script, "-StateDirectory", Root }
                    .Select(value => "  - " + Quote(value)))
                + "\nconnection_timeout_seconds: 30\nauthentication:\n  mode: none\n");
            File.WriteAllText(Path.Combine(Root, "config", "app.yaml"),
                "schema_version: 1\nlanguage: en\nlast_selected_server_id: terminal-profile\nlast_selected_project_id: terminal-project\n"
                + "projects:\n  - project_id: terminal-project\n    name: Terminal GUI\n    root_path: " + Quote(Path.Combine(Root, "project")) + "\n");
            var document = new TerminalGuiConversations(1, null,
                [new("terminal-conversation", "Terminal GUI", "2026-09-12T00:00:00Z", "2026-09-12T00:00:00Z",
                    Path.Combine(Root, "project"), "terminal-profile", "packaged-terminal-session", [])]);
            File.WriteAllText(Path.Combine(Root, "conversations", "conversations.v1.json"),
                JsonSerializer.Serialize(document, TerminalGuiJsonContext.Default.TerminalGuiConversations));
            _oldRoot = Environment.GetEnvironmentVariable("SALMONEGG_APPDATA_ROOT");
            Environment.SetEnvironmentVariable("SALMONEGG_APPDATA_ROOT", Root);
        }

        public string Root { get; }

        public string LoginPath => Path.Combine(Root, "login.json");

        public IReadOnlyList<RequestRow> Requests()
        {
            var path = Path.Combine(Root, "requests.jsonl");
            if (!File.Exists(path)) return [];
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var completeLines = reader.ReadToEnd().Split('\n').SkipLast(1).Where(static line => line.Length > 0);
            return completeLines.Select(line =>
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                return new RequestRow(root.TryGetProperty("method", out var method) ? method.GetString() : null,
                    root.GetProperty("processId").GetInt32(), root.TryGetProperty("authenticated", out var authenticated) && authenticated.GetBoolean());
            }).ToArray();
        }

        public void SendPrompt(WindowsGuiAppSession app)
        {
            GuiAcceptanceDiagnostics.Record("Terminal: navigate conversation");
            if (app.MainWindow.Patterns.Window.IsSupported)
                app.MainWindow.Patterns.Window.Pattern.SetWindowVisualState(FlaUI.Core.Definitions.WindowVisualState.Maximized);
            var session = app.FindByAutomationId("MainNav.Session.terminal-conversation", TimeSpan.FromSeconds(30));
            app.ClickElement(session);
            Assert.True(app.WaitUntilOnscreen("InputBox", TimeSpan.FromSeconds(30)));
            app.EnterText("InputBox", "packaged terminal authentication");
            Assert.True(app.WaitUntilEnabled("ChatInputArea.Send", TimeSpan.FromSeconds(15)));
            app.InvokeButton("ChatInputArea.Send");
            GuiAcceptanceDiagnostics.Record("Terminal: prompt invoked");
        }

        public void WaitForConsent(WindowsGuiAppSession app)
        {
            var consent = app.FindVisibleElementByNameAnywhere("Open sign-in", TimeSpan.FromSeconds(20));
            Assert.NotNull(consent);
            GuiAcceptanceDiagnostics.Record("Terminal: consent displayed");
            Assert.False(File.Exists(LoginPath));
        }

        public void EnterTerminalInput(WindowsGuiAppSession app)
        {
            Assert.True(app.WaitUntil(() => File.Exists(LoginPath), TimeSpan.FromSeconds(20)));
            GuiAcceptanceDiagnostics.Record("Terminal: PTY process observed");
            Assert.True(app.WaitUntilOnscreen("ChatAuth.TerminalDialog", TimeSpan.FromSeconds(15)));
            var terminal = app.FindByAutomationIdAnywhere("BottomPanel.TerminalWebView", TimeSpan.FromSeconds(15));
            Assert.True(app.WaitUntil(() => (terminal.Properties.Name.ValueOrDefault ?? string.Empty)
                .Contains("PACKAGED_TERMINAL_READY", StringComparison.Ordinal), TimeSpan.FromSeconds(20)));
            GuiAcceptanceDiagnostics.Record("Terminal: PTY ready output observed");
            try
            {
                // RenderedText is the PTY output projection, not proof that WebView2 has loaded.
                // The actual xterm textarea must exist and own native keyboard focus first.
                AutomationElement? input = null;
                Assert.True(app.WaitUntil(() =>
                {
                    input = terminal.FindFirstDescendant(cf => cf.ByControlType(ControlType.Edit));
                    return input is not null && input.IsEnabled;
                }, TimeSpan.FromSeconds(20)), "The terminal's native accessible input did not load.");
                input!.Focus();
                Assert.True(app.WaitUntil(() => input.Properties.HasKeyboardFocus.Value, TimeSpan.FromSeconds(10)),
                    "The real terminal input did not acquire native keyboard focus.");
                GuiAcceptanceDiagnostics.Record("Terminal: native focus " + app.DescribeFocusedElement());
                Keyboard.Type("user confirmed");
                Keyboard.Press(VirtualKeyShort.RETURN);
                Keyboard.Release(VirtualKeyShort.RETURN);
                GuiAcceptanceDiagnostics.Record("Terminal: native keyboard delivered");
            }
            finally
            {
                GuiAcceptanceDiagnostics.Record("Terminal: input focus " + app.DescribeFocusedElementDetailed());
                var artifacts = Environment.GetEnvironmentVariable("SALMONEGG_GUI_ACCEPTANCE_ARTIFACTS");
                if (!string.IsNullOrWhiteSpace(artifacts))
                    app.CaptureMainWindowToFile(Path.Combine(artifacts, "terminal-" + _scenario + "-input.png"));
            }
        }

        public void AssertLoginExited(WindowsGuiAppSession app)
        {
            Assert.True(app.WaitUntil(() => File.Exists(LoginPath), TimeSpan.FromSeconds(20)));
            using var observation = JsonDocument.Parse(File.ReadAllText(LoginPath));
            var data = observation.RootElement;
            Assert.False(data.GetProperty("inputRedirected").GetBoolean());
            Assert.False(data.GetProperty("outputRedirected").GetBoolean());
            Assert.Equal("method-overlay", data.GetProperty("methodEnvironment").GetString());
            Assert.True(app.WaitUntil(() => Exited(data.GetProperty("processId").GetInt32()), TimeSpan.FromSeconds(15)));
            if (data.GetProperty("descendantId").ValueKind == JsonValueKind.Number)
                Assert.True(app.WaitUntil(() => Exited(data.GetProperty("descendantId").GetInt32()), TimeSpan.FromSeconds(15)));
        }

        public void Dispose()
        {
            WindowsGuiAppSession.StopAllRunningInstances();
            Environment.SetEnvironmentVariable("SALMONEGG_APPDATA_ROOT", _oldRoot);
            var artifacts = Environment.GetEnvironmentVariable("SALMONEGG_GUI_ACCEPTANCE_ARTIFACTS");
            if (!string.IsNullOrWhiteSpace(artifacts))
            {
                var evidence = Path.Combine(artifacts, "terminal-" + _scenario);
                Directory.CreateDirectory(evidence);
                foreach (var file in new[] { "requests.jsonl", "login.json", "boot.log" })
                {
                    var source = Path.Combine(Root, file);
                    if (File.Exists(source)) File.Copy(source, Path.Combine(evidence, file), overwrite: true);
                }
            }
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }

        private static bool Exited(int pid)
        {
            try { using var process = Process.GetProcessById(pid); return process.HasExited; }
            catch (ArgumentException) { return true; }
        }

        private static string Quote(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

        private static string FindFixtureScript()
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            {
                var candidate = Path.Combine(directory.FullName, "tests", "SalmonEgg.GuiTests.Windows", "Fixtures", "PackagedTerminalAuthAgent.ps1");
                if (File.Exists(candidate)) return candidate;
            }
            throw new FileNotFoundException("The packaged terminal authentication fixture was not found.");
        }
    }

    private sealed record RequestRow(string? Method, int ProcessId, bool Authenticated);
}

internal sealed record TerminalGuiConversations(int Version, string? LastActiveConversationId, TerminalGuiConversation[] Conversations);

internal sealed record TerminalGuiConversation(string ConversationId, string DisplayName, string CreatedAt, string LastUpdatedAt,
    string Cwd, string BoundProfileId, string RemoteSessionId, JsonElement[] Messages);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(TerminalGuiConversations))]
internal sealed partial class TerminalGuiJsonContext : JsonSerializerContext;
