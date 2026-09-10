using System.Diagnostics;
using System.Text;
using System.Text.Json;
using SalmonEgg.Acp.Client;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Infrastructure.Client;
using SalmonEgg.Infrastructure.Services;
using SalmonEgg.Infrastructure.Transport;

namespace SalmonEgg.TerminalAuth.Windows.Tests;

public sealed class TerminalAuthenticationWindowsTests
{
    [Theory]
    [InlineData("success", 0)]
    [InlineData("failure", 23)]
    public async Task StartAsync_ActualAgentInvocation_UsesConsoleAndPreservesNormalExitResult(string mode, int exitCode)
    {
        // Arrange: obtain the invocation and terminal method from a live ACP process.
        await using var agent = await AgentFixture.CreateAsync(mode);
        await using var factory = new TerminalAuthenticationSessionFactory(new ConPtyGateCapabilities());
        Assert.True(factory.IsSupported);
        await using var session = await factory.StartAsync(agent.LoginInvocation, TestContext.Current.CancellationToken);
        using var output = new TerminalOutput(session);

        // Act: interact over the real ConPTY pipe and observe its actual exit code.
        await output.WaitForAsync("TERMINAL_AUTH_READY");
        await session.ResizeAsync(90, 24, TestContext.Current.CancellationToken);
        await session.WriteInputAsync("user confirmed\r", TestContext.Current.CancellationToken);
        Assert.Equal(exitCode, await session.Completion.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken));
        await output.WaitForAsync("TERMINAL_AUTH_ACCEPTED");

        // Assert: argv boundaries, the effective environment override and cwd reached the process.
        using var observation = await agent.ReadObservationAsync();
        var root = observation.RootElement;
        Assert.False(root.GetProperty("inputRedirected").GetBoolean());
        Assert.False(root.GetProperty("outputRedirected").GetBoolean());
        Assert.Equal("base argument", root.GetProperty("baseValue").GetString());
        Assert.Equal("login argument & literal", root.GetProperty("loginValue").GetString());
        Assert.Equal("method override", root.GetProperty("environmentValue").GetString());
        Assert.Equal(agent.LoginInvocation.WorkingDirectory, root.GetProperty("workingDirectory").GetString(), ignoreCase: true);
        Assert.False(session.CanAcceptInput);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisposeAsync_ActiveConsole_ReclaimsProcessTreeAndDoesNotReportSuccess(bool disposeFactory)
    {
        // Arrange: the login process starts a descendant inside the Windows job.
        await using var agent = await AgentFixture.CreateAsync("cancel");
        await using var factory = new TerminalAuthenticationSessionFactory(new ConPtyGateCapabilities());
        await using var session = await factory.StartAsync(agent.LoginInvocation, TestContext.Current.CancellationToken);
        using var output = new TerminalOutput(session);
        await output.WaitForAsync("TERMINAL_AUTH_READY");
        using var observation = await agent.ReadObservationAsync();
        using var process = Process.GetProcessById(observation.RootElement.GetProperty("processId").GetInt32());
        using var descendant = Process.GetProcessById(observation.RootElement.GetProperty("descendantId").GetInt32());

        // Act: both closing a login dialog and application shutdown must own teardown.
        var teardown = disposeFactory ? factory.DisposeAsync() : session.DisposeAsync();
        await teardown.AsTask().WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);

        // Assert: a closed console never becomes a successful login, and no descendants survive.
        Assert.Null(await session.Completion);
        Assert.False(session.CanAcceptInput);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await descendant.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.True(process.HasExited);
        Assert.True(descendant.HasExited);
    }

    // This gate exercises the actual PTY host before the application opts into the capability.
    // Product advertisement remains governed by PlatformCapabilityService and the application GUI gate.
    private sealed class ConPtyGateCapabilities : IPlatformCapabilityService
    {
        public bool SupportsTerminalAuthentication => OperatingSystem.IsWindows();
        public bool SupportsStdioTransport => OperatingSystem.IsWindows();
        public bool SupportsInteractiveTerminalSurface => OperatingSystem.IsWindows();
        public bool SupportsLocalTerminal => OperatingSystem.IsWindows();
        public bool SupportsLaunchOnStartup => false;
        public bool SupportsTray => false;
        public bool SupportsLanguageOverride => false;
        public bool SupportsMiniWindow => false;
        public bool SupportsExternalFileOpen => false;
        public bool SupportsLocalFileExport => false;
        public bool SupportsGamepadInput => false;
        public bool SupportsCliCommandInspection => false;
        public bool SupportsCliCommandLinking => false;
    }

    private sealed class AgentFixture : IAsyncDisposable
    {
        private readonly string _directory;
        private readonly StdioTransport _transport;
        private readonly AcpClient _client;

        private AgentFixture(string directory, StdioTransport transport, AcpClient client, StdioInvocationSnapshot invocation)
        {
            _directory = directory;
            _transport = transport;
            _client = client;
            LoginInvocation = invocation;
        }

        public StdioInvocationSnapshot LoginInvocation { get; }

        public static async Task<AgentFixture> CreateAsync(string mode)
        {
            // The platform project must fail when run in an unsuitable environment; never skip.
            Assert.True(OperatingSystem.IsWindows(), "This gate requires a Windows host with ConPTY.");
            var directory = Path.Combine(Path.GetTempPath(), $"salmon-egg-terminal-auth-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            var command = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
            var script = Path.Combine(AppContext.BaseDirectory, "Fixtures", "TerminalAuthAgent.ps1");
            var transport = new StdioTransport(command,
                ["-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script, "-BaseValue", "base argument"],
                environment: new Dictionary<string, string>
                {
                    ["SALMONEGG_AUTH_FIXTURE_MODE"] = mode,
                    ["SALMONEGG_AUTH_FIXTURE_VALUE"] = "base value",
                    ["SALMONEGG_AUTH_FIXTURE_OBSERVATION"] = Path.Combine(directory, "observation.json")
                });
            var client = new AcpClient(new DomainAcpTransportAdapter(transport));
            try
            {
                Assert.True(await transport.ConnectAsync(TestContext.Current.CancellationToken));
                var response = await client.InitializeAsync(new InitializeParams(
                    new ClientInfo("terminal-auth-gate", "1.0"),
                    new ClientCapabilities { Auth = new AuthCapabilities { Terminal = true } }), TestContext.Current.CancellationToken);
                var method = Assert.Single(response.AuthMethods!);
                Assert.Equal("terminal", method.ResolvedType);
                var invocation = Assert.IsType<StdioInvocationSnapshot>(transport.StdioInvocation);
                return new AgentFixture(directory, transport, client, invocation.WithAuthenticationMethod(method.Args, method.Env));
            }
            catch
            {
                client.Dispose();
                transport.Dispose();
                Directory.Delete(directory, recursive: true);
                throw;
            }
        }

        public async Task<JsonDocument> ReadObservationAsync() => JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(_directory, "observation.json"), TestContext.Current.CancellationToken));

        public async ValueTask DisposeAsync()
        {
            _client.Dispose();
            await _transport.DisconnectAsync();
            _transport.Dispose();
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class TerminalOutput : IDisposable
    {
        private readonly ITerminalAuthenticationSession _session;
        private readonly StringBuilder _output = new();
        private readonly object _gate = new();
        private TaskCompletionSource _changed = NewSignal();

        public TerminalOutput(ITerminalAuthenticationSession session)
        {
            _session = session;
            session.OutputReceived += OnOutput;
        }

        public void Dispose() => _session.OutputReceived -= OnOutput;

        public async Task WaitForAsync(string marker)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            while (true)
            {
                Task changed;
                lock (_gate)
                {
                    if (_output.ToString().Contains(marker, StringComparison.Ordinal)) return;
                    changed = _changed.Task;
                }

                await changed.WaitAsync(timeout.Token);
            }
        }

        private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

        private void OnOutput(object? sender, string text)
        {
            lock (_gate)
            {
                _output.Append(text);
                if (_output.Length > 128 * 1024) _output.Remove(0, _output.Length - 64 * 1024);
                _changed.TrySetResult();
                _changed = NewSignal();
            }
        }
    }
}
