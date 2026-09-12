using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using FlaUI.Core.Definitions;

namespace SalmonEgg.GuiTests.Windows;

public sealed class UrlElicitationSmokeTests
{
    [Fact]
    public void UrlConsent_UsesSystemBrowserAndKeepsCompletionWithTheOriginatingAgent()
    {
        // Arrange: the installed product starts a real stdio peer and an ordinary system browser.
        using var fixture = new Fixture();
        using var app = WindowsGuiAppSession.LaunchFresh();
        try
        {
            if (app.MainWindow.Patterns.Window.IsSupported)
                app.MainWindow.Patterns.Window.Pattern.SetWindowVisualState(WindowVisualState.Maximized);
            app.ClickElement(app.FindByAutomationId("MainNav.Session.native-elicitation-conversation", TimeSpan.FromSeconds(30)));
            Assert.True(app.WaitUntil(() => fixture.Rows().Any(row => row.TryGetProperty("method", out var method)
                && method.GetString() == "session/load"), TimeSpan.FromSeconds(30)));
            var initialize = Assert.Single(fixture.Rows(), row => row.TryGetProperty("method", out var method)
                && method.GetString() == "initialize");
            Assert.Equal(JsonValueKind.Object, initialize.GetProperty("capabilities").GetProperty("elicitation").GetProperty("url").ValueKind);

            // Act and assert: each native choice is tied to its own wire response.
            foreach (var action in new[] { "decline", "cancel" })
            {
                fixture.Instruct(action);
                fixture.WaitForCard(app, action);
                Assert.Empty(fixture.Visits);
                ClickButton(app, action == "decline" ? "Decline" : "Cancel");
                fixture.AssertResponse(app, action, action);
                Assert.Empty(fixture.Visits);
            }

            fixture.Instruct("open");
            fixture.WaitForCard(app, "open");
            Assert.Empty(fixture.Visits);
            ClickButton(app, "Open in browser");
            fixture.AssertResponse(app, "open", "accept");
            Assert.True(app.WaitUntil(() => fixture.Reports.Count == 1, TimeSpan.FromSeconds(30)),
                "The real system browser did not load the consented page.");
            app.BringMainWindowToFront();
            ClickButton(app, "Open again");
            Assert.True(app.WaitUntil(() => fixture.Reports.Count == 2, TimeSpan.FromSeconds(30)));
            Assert.Single(fixture.Responses("open"));
            app.BringMainWindowToFront();
            fixture.Instruct("complete");
            Assert.NotNull(app.FindVisibleTextAnywhere("The agent reports that the external step is complete.", TimeSpan.FromSeconds(15)));
            ClickButton(app, "Close notice");
            fixture.Instruct("expire");
            fixture.WaitForCard(app, "expire");
            fixture.Instruct("disconnect");
            Assert.True(app.WaitUntil(() =>
            {
                var link = app.TryFindByAutomationIdAnywhere("Elicitation.FullUrl", TimeSpan.FromMilliseconds(200));
                var open = app.TryFindVisibleElementByNameAnywhere("Open in browser", TimeSpan.FromMilliseconds(200));
                return (link is null || link.IsOffscreen || string.IsNullOrEmpty(link.Name)) && (open is null || !open.IsEnabled);
            }, TimeSpan.FromSeconds(15)), "The disconnected request retained its original URL or an active open action.");
            Assert.Empty(fixture.Responses("expire"));
            Assert.Equal(2, fixture.Visits.Count);
            Assert.All(fixture.Visits, referer => Assert.Equal(string.Empty, referer));
            Assert.Equal(2, fixture.Reports.Count);
            Assert.All(fixture.Reports, report =>
            {
                Assert.True(report.GetProperty("openerAbsent").GetBoolean());
                Assert.Equal(string.Empty, report.GetProperty("referrer").GetString());
                Assert.True(report.GetProperty("bridgeAbsent").GetBoolean());
                Assert.Equal(Fixture.PrivateValue, report.GetProperty("privateValue").GetString());
            });
            Assert.DoesNotContain(Fixture.PrivateValue, File.ReadAllText(fixture.PeerLog), StringComparison.Ordinal);
            GuiAcceptanceDiagnostics.Record("URL: five native consent actions, two isolated system-browser visits and single accept passed");
        }
        finally
        {
            var artifacts = Environment.GetEnvironmentVariable("SALMONEGG_GUI_ACCEPTANCE_ARTIFACTS");
            if (!string.IsNullOrWhiteSpace(artifacts)) app.CaptureMainWindowToFile(Path.Combine(artifacts, "url-product.png"));
        }
    }

    private static void ClickButton(WindowsGuiAppSession app, string label)
    {
        var button = app.FindVisibleElementByNameAnywhere(label, TimeSpan.FromSeconds(15));
        Assert.NotNull(button);
        Assert.True(app.WaitUntil(() => button.IsEnabled, TimeSpan.FromSeconds(10)));
        app.ClickElement(button);
        GuiAcceptanceDiagnostics.Record("URL: native button " + label);
    }

    private sealed class Fixture : IDisposable
    {
        public const string PrivateValue = "windows-page-private-canary";
        private readonly string? _previousRoot;
        private readonly HttpListener _server = new();
        private readonly Task _serverTask;
        private readonly HashSet<int> _originalBrowsers = BrowserPids();
        private int _phase;

        public Fixture()
        {
            GuiTestGate.RequireEnabled();
            Root = Path.Combine(Path.GetTempPath(), "SalmonEgg.UrlGui", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(Root, "config", "servers"));
            Directory.CreateDirectory(Path.Combine(Root, "conversations"));
            var project = Path.Combine(Root, "project");
            Directory.CreateDirectory(project);
            using var portProbe = new TcpListener(IPAddress.Loopback, 0);
            portProbe.Start();
            var port = ((IPEndPoint)portProbe.LocalEndpoint).Port;
            portProbe.Stop();
            _server.Prefixes.Add($"http://127.0.0.1:{port}/");
            _server.Start();
            Url = $"http://127.0.0.1:{port}/authorize?token={Guid.NewGuid():N}";
            _serverTask = ServeAsync();
            var python = Environment.GetEnvironmentVariable("SALMONEGG_GUI_PYTHON")
                ?? throw new InvalidOperationException("The gate must supply its actual Python executable.");
            var peer = FindPeer();
            var scenario = Path.Combine(Root, "scenario.json");
            File.WriteAllText(scenario, "{\"url\":" + Json(Url) + ",\"log\":" + Json(PeerLog)
                + ",\"control\":" + Json(ControlPath) + ",\"cwd\":" + Json(project) + "}");
            File.WriteAllText(Path.Combine(Root, "config", "servers", "native-elicitation-profile.yaml"),
                "schema_version: 5\nid: native-elicitation-profile\nname: Native Elicitation Fixture\ntransport: stdio\n"
                + "stdio_command: " + Quote(python) + "\nstdio_arguments:\n  - " + Quote(peer) + "\n  - " + Quote(scenario)
                + "\nconnection_timeout_seconds: 30\nauthentication:\n  mode: none\n");
            File.WriteAllText(Path.Combine(Root, "config", "app.yaml"), "schema_version: 1\nlanguage: en\n"
                + "last_selected_server_id: native-elicitation-profile\nlast_selected_project_id: native-elicitation-project\n"
                + "projects:\n  - project_id: native-elicitation-project\n    name: Native URL\n    root_path: " + Quote(project) + "\n");
            File.WriteAllText(Path.Combine(Root, "conversations", "conversations.v1.json"),
                "{\"version\":1,\"lastActiveConversationId\":null,\"conversations\":[{\"conversationId\":\"native-elicitation-conversation\","
                + "\"displayName\":\"Native URL\",\"createdAt\":\"2026-09-12T00:00:00Z\",\"lastUpdatedAt\":\"2026-09-12T00:00:00Z\","
                + "\"cwd\":" + Json(project) + ",\"boundProfileId\":\"native-elicitation-profile\","
                + "\"remoteSessionId\":\"native-elicitation-session\",\"messages\":[]}]}");
            _previousRoot = Environment.GetEnvironmentVariable("SALMONEGG_APPDATA_ROOT");
            Environment.SetEnvironmentVariable("SALMONEGG_APPDATA_ROOT", Root);
        }

        public string Root { get; }
        public string Url { get; }
        public string PeerLog => Path.Combine(Root, "peer.jsonl");
        private string ControlPath => Path.Combine(Root, "control.json");
        public ConcurrentQueue<string> Visits { get; } = new();
        public ConcurrentQueue<JsonElement> Reports { get; } = new();

        public void Instruct(string action)
        {
            var temporary = ControlPath + ".tmp";
            File.WriteAllText(temporary, "{\"phase\":" + (++_phase) + ",\"action\":" + Json(action) + "}");
            File.Move(temporary, ControlPath, overwrite: true);
        }

        public void WaitForCard(WindowsGuiAppSession app, string action)
        {
            Assert.NotNull(app.FindVisibleTextAnywhere("native-url-" + action, TimeSpan.FromSeconds(20)));
            Assert.Equal(Url, app.FindByAutomationIdAnywhere("Elicitation.FullUrl", TimeSpan.FromSeconds(10)).Name);
            Assert.Equal("127.0.0.1", app.FindByAutomationIdAnywhere("Elicitation.UrlHost", TimeSpan.FromSeconds(10)).Name);
        }

        public JsonElement[] Rows()
        {
            if (!File.Exists(PeerLog)) return [];
            using var stream = File.Open(PeerLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd().Split('\n').SkipLast(1).Where(line => line.Length > 0).Select(line =>
            {
                using var document = JsonDocument.Parse(line);
                return document.RootElement.Clone();
            }).ToArray();
        }

        public JsonElement[] Responses(string action) => Rows().Where(row => row.TryGetProperty("id", out var id)
            && id.GetString() == "native-url-" + action).ToArray();

        public void AssertResponse(WindowsGuiAppSession app, string id, string action)
        {
            Assert.True(app.WaitUntil(() => Responses(id).Length > 0, TimeSpan.FromSeconds(15)));
            var response = Assert.Single(Responses(id));
            Assert.Equal(action, response.GetProperty("result").GetProperty("action").GetString());
            Assert.Single(response.GetProperty("result").EnumerateObject());
        }

        private async Task ServeAsync()
        {
            while (_server.IsListening)
            {
                HttpListenerContext request;
                try { request = await _server.GetContextAsync(); }
                catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException) { break; }
                if (request.Request.HttpMethod == "POST" && request.Request.RawUrl == "/report")
                {
                    using var document = await JsonDocument.ParseAsync(request.Request.InputStream);
                    Reports.Enqueue(document.RootElement.Clone());
                    request.Response.StatusCode = 204;
                }
                else if (request.Request.Url?.AbsoluteUri == Url)
                {
                    Visits.Enqueue(request.Request.Headers["Referer"] ?? string.Empty);
                    var html = "<!doctype html><input id=private value=" + PrivateValue + ">"
                        + "<script>fetch('/report',{method:'POST',body:JSON.stringify({openerAbsent:window.opener===null,"
                        + "referrer:document.referrer,bridgeAbsent:!window.chrome?.webview&&!window.unoWebView,"
                        + "privateValue:document.querySelector('#private').value})});</script>";
                    var bytes = Encoding.UTF8.GetBytes(html);
                    request.Response.ContentType = "text/html";
                    request.Response.ContentLength64 = bytes.Length;
                    await request.Response.OutputStream.WriteAsync(bytes);
                }
                else request.Response.StatusCode = 404;
                request.Response.Close();
            }
        }

        public void Dispose()
        {
            WindowsGuiAppSession.StopAllRunningInstances();
            _server.Close();
            Assert.True(_serverTask.Wait(TimeSpan.FromSeconds(5)), "The loopback browser fixture did not stop.");
            foreach (var pid in BrowserPids().Except(_originalBrowsers))
            {
                try
                {
                    using var process = Process.GetProcessById(pid);
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5000);
                }
                catch (ArgumentException) { }
                catch (InvalidOperationException) { }
            }
            Environment.SetEnvironmentVariable("SALMONEGG_APPDATA_ROOT", _previousRoot);
            var artifacts = Environment.GetEnvironmentVariable("SALMONEGG_GUI_ACCEPTANCE_ARTIFACTS");
            if (!string.IsNullOrWhiteSpace(artifacts) && File.Exists(PeerLog))
                File.Copy(PeerLog, Path.Combine(artifacts, "url-peer.jsonl"), overwrite: true);
            Directory.Delete(Root, recursive: true);
        }

        private static HashSet<int> BrowserPids()
        {
            var result = new HashSet<int>();
            foreach (var name in new[] { "msedge", "chrome", "firefox" })
            foreach (var process in Process.GetProcessesByName(name))
            {
                using (process) result.Add(process.Id);
            }
            return result;
        }

        private static string FindPeer()
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            {
                var candidate = Path.Combine(directory.FullName, "scripts", "gates", "fixtures", "native-elicitation-peer.py");
                if (File.Exists(candidate)) return candidate;
            }
            throw new FileNotFoundException("The native stdio fixture is missing.");
        }

        private static string Json(string value) => "\"" + JsonEncodedText.Encode(value).ToString() + "\"";
        private static string Quote(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
    }
}
