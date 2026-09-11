using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.IO.Pipelines;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Logging;
using Moq;
using Porta.Pty;
using SalmonEgg.Acp.Client;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Application.Services.Acp;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Infrastructure.Client;
using SalmonEgg.Infrastructure.Network;
using SalmonEgg.Infrastructure.Transport;
using SalmonEgg.Infrastructure.Services;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace SalmonEgg.Infrastructure.Tests.Transport;

/// <summary>Real local processes and loopback peers. The dedicated Linux gate requires zero skips.</summary>
public sealed class CredentialTransportCanaryTests
{
    private const string Secret = "transport-credential-canary";
    private const string Header = "X-Agent-Credential";
    private const string Initialize = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":1}}";
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData(TransportType.StreamableHttp)]
    [InlineData(TransportType.WebSocket)]
    public async Task BoundTransport_RealPeer_ReceivesSnapshotAndUpdatedConnectionReceivesNewCredential(TransportType kind)
    {
        await using var peer = await CredentialPeer.StartAsync(kind);
        var sink = new RecordingLogSink();
        using var logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger();
        var profile = CreateProfile(kind, peer.Url, Secret);
        profile.CredentialBinding = CredentialBindingPolicy.Create(profile, CredentialSource.Token, CredentialTarget.Header, Header, "Bearer");
        using (var transport = CreateFactory(logger).CreateTransport(profile))
        {
            // A caller editing its profile after factory creation cannot retarget or replace this connection's secret.
            profile.Authentication!.Token = "replacement-credential";
            using var client = new AcpClient(new DomainAcpTransportAdapter(transport));
            await client.InitializeAsync(new InitializeParams(new ClientInfo("canary", "1"), new ClientCapabilities()), TestContext.Current.CancellationToken);
            await peer.StreamReady.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
            if (kind == TransportType.StreamableHttp)
            {
                Assert.True(await transport.SendMessageAsync("{\"jsonrpc\":\"2.0\",\"method\":\"canary/notification\"}", TestContext.Current.CancellationToken));
            }
            await client.DisconnectAsync();
        }

        var firstRequests = peer.Requests.ToArray();
        Assert.NotEmpty(firstRequests);
        Assert.All(firstRequests, request => Assert.Equal("Bearer " + Secret, request.Credential));
        Assert.All(firstRequests, request => Assert.DoesNotContain(Secret, request.Body));
        if (kind == TransportType.StreamableHttp)
        {
            Assert.Contains(firstRequests, request => request.Method == "GET");
            Assert.Contains(firstRequests, request => request.Method == "DELETE");
            Assert.Equal(2, firstRequests.Count(request => request.Method == "POST"));
        }

        using (var transport = CreateFactory(logger).CreateTransport(profile))
        using (var client = new AcpClient(new DomainAcpTransportAdapter(transport)))
        {
            await client.InitializeAsync(new InitializeParams(new ClientInfo("canary", "1"), new ClientCapabilities()), TestContext.Current.CancellationToken);
            await client.DisconnectAsync();
        }

        Assert.Contains(peer.Requests.Skip(firstRequests.Length), request => request.Credential == "Bearer replacement-credential");
        Assert.DoesNotContain(Secret, sink.Text);
        Assert.DoesNotContain("replacement-credential", sink.Text);
    }

    [Theory]
    [InlineData(TransportType.StreamableHttp, false)]
    [InlineData(TransportType.StreamableHttp, true)]
    [InlineData(TransportType.WebSocket, false)]
    [InlineData(TransportType.WebSocket, true)]
    public async Task BoundTransport_Redirect_NeverSendsCredentialToAnotherOriginOrPath(TransportType kind, bool anotherOrigin)
    {
        await using var other = await CredentialPeer.StartAsync(kind);
        await using var source = await CredentialPeer.StartAsync(kind);
        source.RedirectTo = anotherOrigin ? other.HttpUrl + "/unapproved" : source.HttpUrl + "/unapproved";
        var profile = CreateProfile(kind, source.Url, Secret);
        profile.CredentialBinding = CredentialBindingPolicy.Create(profile, CredentialSource.Token, CredentialTarget.Header, Header);
        var resolution = CredentialBindingResolver.Resolve(profile).Value!;

        if (kind == TransportType.StreamableHttp)
        {
            using var http = new StreamableHttpTransport(Log.Logger, connectTimeout: Deadline, credential: resolution);
            await http.ConnectAsync(source.Url, TestContext.Current.CancellationToken);
            await Assert.ThrowsAnyAsync<Exception>(() => http.SendAsync(Initialize, TestContext.Current.CancellationToken));
        }
        else
        {
            using var socket = new WebSocketTransport(Log.Logger, connectTimeout: Deadline, credential: resolution);
            await Assert.ThrowsAnyAsync<Exception>(() => socket.ConnectAsync(source.Url, TestContext.Current.CancellationToken));
        }

        Assert.NotEmpty(source.Requests);
        Assert.All(source.Requests, request => Assert.Equal("/acp", request.Path));
        Assert.All(source.Requests, request => Assert.Equal(Secret, request.Credential));
        Assert.Empty(other.Requests);
    }

    [Fact]
    public async Task StdioBinding_RealProcess_ReceivesOnlyChildEnvironmentAndNeverArguments()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Requires the real Linux /proc process environment and /bin/sh.");
        var directory = Path.Combine(Path.GetTempPath(), "acp-credential-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var script = Path.Combine(directory, "agent.sh");
        await File.WriteAllTextAsync(script, """
            printf '{"jsonrpc":"2.0","method":"canary/ready","params":{"pid":%s}}\n' "$$"
            while IFS= read -r line; do :; done
            """, TestContext.Current.CancellationToken);
        var parentValue = Environment.GetEnvironmentVariable("SALMONEGG_CREDENTIAL_CANARY");
        var profile = new ServerConfiguration
        {
            Transport = TransportType.Stdio,
            StdioCommand = "/bin/sh",
            StdioArguments = [script],
            Authentication = new AuthenticationConfig { Token = Secret },
        };
        profile.CredentialBinding = CredentialBindingPolicy.Create(profile, CredentialSource.Token, CredentialTarget.Environment, "SALMONEGG_CREDENTIAL_CANARY");
        try
        {
            using var transport = CreateFactory(Log.Logger).CreateTransport(profile);
            var ready = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            transport.MessageReceived += (_, message) =>
            {
                using var frame = JsonDocument.Parse(message.Message);
                ready.TrySetResult(frame.RootElement.GetProperty("params").GetProperty("pid").GetInt32());
            };

            Assert.True(await transport.ConnectAsync(TestContext.Current.CancellationToken));
            var pid = await ready.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
            var environment = await File.ReadAllTextAsync($"/proc/{pid}/environ", TestContext.Current.CancellationToken);
            var arguments = await File.ReadAllTextAsync($"/proc/{pid}/cmdline", TestContext.Current.CancellationToken);

            Assert.Contains("SALMONEGG_CREDENTIAL_CANARY=" + Secret + '\0', environment);
            Assert.DoesNotContain(Secret, arguments);
            Assert.Equal(parentValue, Environment.GetEnvironmentVariable("SALMONEGG_CREDENTIAL_CANARY"));
            Assert.False(profile.StdioEnvironment.ContainsKey("SALMONEGG_CREDENTIAL_CANARY"));
            Assert.True(await transport.DisconnectAsync());
            Assert.False(Directory.Exists($"/proc/{pid}"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task BoundStdio_ActualEnvironmentSnapshot_ReachesTerminalAuthenticationSpawn()
    {
        // Arrange: this gate reads a real child environment, without enabling product terminal auth.
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Requires the real Linux /proc process environment and /bin/sh.");
        var directory = Path.Combine(Path.GetTempPath(), "acp-terminal-credential-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var script = Path.Combine(directory, "agent.sh");
        await File.WriteAllTextAsync(script, """
            printf '{"jsonrpc":"2.0","method":"canary/ready","params":{"pid":%s}}\n' "$$"
            while IFS= read -r line; do :; done
            """, TestContext.Current.CancellationToken);
        var profile = new ServerConfiguration
        {
            Transport = TransportType.Stdio,
            StdioCommand = "/bin/sh",
            StdioArguments = [script, "literal argument"],
            Authentication = new AuthenticationConfig { Token = Secret },
        };
        profile.CredentialBinding = CredentialBindingPolicy.Create(profile, CredentialSource.Token,
            CredentialTarget.Environment, "SALMONEGG_CREDENTIAL_CANARY");
        var sourceFactory = new CapturedStdioFactory();
        var platform = new Mock<IPlatformCapabilityService>();
        platform.SetupGet(value => value.SupportsStdioTransport).Returns(true);
        platform.SetupGet(value => value.SupportsTerminalAuthentication).Returns(true);
        using var logger = new LoggerConfiguration().CreateLogger();
        var transportFactory = new TransportFactory(logger, new TransportSupportPolicy(platform.Object), sourceFactory);
        var chatFactory = new ChatServiceFactory(transportFactory, Mock.Of<IErrorLogger>(),
            new SessionManager(), new AcpClientFactory(Mock.Of<IErrorLogger>(), new SessionManager(),
                Mock.Of<ITerminalSessionManager>()), logger,
            decorateChatService: service => new DelayedLoadChatService(service, TimeSpan.FromMilliseconds(1)));

        try
        {
            using var chat = chatFactory.CreateChatService(profile);
            var transport = Assert.IsType<StdioTransport>(sourceFactory.Created);
            var ready = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            transport.MessageReceived += (_, message) =>
            {
                using var frame = JsonDocument.Parse(message.Message);
                ready.TrySetResult(frame.RootElement.GetProperty("params").GetProperty("pid").GetInt32());
            };

            // Act
            Assert.True(await transport.ConnectAsync(TestContext.Current.CancellationToken));
            var pid = await ready.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
            var snapshot = Assert.IsType<StdioInvocationSnapshot>(Assert.IsAssignableFrom<IStdioInvocationSource>(chat).StdioInvocation);
            var environment = await File.ReadAllTextAsync($"/proc/{pid}/environ", TestContext.Current.CancellationToken);
            Assert.Contains("SALMONEGG_CREDENTIAL_CANARY=" + Secret + '\0', environment);
            profile.Authentication!.Token = "edited-after-launch";
            var login = snapshot.WithAuthenticationMethod(["login argument"],
                new Dictionary<string, string> { ["LOGIN_MODE"] = "interactive" });
            using var pty = new CapturedPty();
            PtyOptions? options = null;
            await using var terminalFactory = new TerminalAuthenticationSessionFactory(platform.Object,
                (request, _) => { options = request; return Task.FromResult<IPtyConnection>(pty); });
            await using var terminal = await terminalFactory.StartAsync(login, TestContext.Current.CancellationToken);

            // Assert: the actual running process snapshot, not a re-read edited profile, feeds the PTY.
            Assert.NotNull(options);
            Assert.Equal(Secret, options.Environment["SALMONEGG_CREDENTIAL_CANARY"]);
            Assert.Equal("interactive", options.Environment["LOGIN_MODE"]);
            Assert.Equal(snapshot.Arguments.Concat(["login argument"]), options.CommandLine);
            Assert.Equal(snapshot.Command, options.App);
            Assert.Equal(snapshot.WorkingDirectory, options.Cwd);
            Assert.DoesNotContain(Secret, snapshot.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(Secret, string.Join(" ", options.CommandLine), StringComparison.Ordinal);
            Assert.True(await chat.DisconnectAsync());
            Assert.False(Directory.Exists($"/proc/{pid}"));
            Assert.Null(((IStdioInvocationSource)chat).StdioInvocation);
        }
        finally
        {
            sourceFactory.Created?.Dispose();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Factory_UnsupportedWebSocketHeaders_FailsBeforeCreatingConnection()
    {
        var profile = CreateProfile(TransportType.WebSocket, "wss://agent.example/acp", Secret);
        profile.CredentialBinding = CredentialBindingPolicy.Create(profile, CredentialSource.Token, CredentialTarget.Header, Header);

        var error = Assert.Throws<NotSupportedException>(() => CreateFactory(Log.Logger, supportsWebSocketHeaders: false).CreateTransport(profile));

        Assert.Contains("HTTP endpoint", error.Message);
        Assert.DoesNotContain(Secret, error.Message);
    }

    private static TransportFactory CreateFactory(Serilog.ILogger logger, bool supportsWebSocketHeaders = true)
    {
        var capabilities = new Mock<IPlatformCapabilityService>();
        capabilities.SetupGet(value => value.SupportsStdioTransport).Returns(true);
        capabilities.SetupGet(value => value.SupportsWebSocketRequestHeaders).Returns(supportsWebSocketHeaders);
        return new TransportFactory(logger, new TransportSupportPolicy(capabilities.Object), new DesktopStdioTransportFactory());
    }

    private static ServerConfiguration CreateProfile(TransportType kind, string url, string secret) => new()
    {
        Transport = kind,
        ServerUrl = url,
        Proxy = new ProxyConfig { Mode = ProxyMode.None },
        Authentication = new AuthenticationConfig { Token = secret },
    };

    private sealed record CapturedRequest(string Method, string Path, string Credential, string Body);

    private sealed class CapturedStdioFactory : IStdioTransportFactory
    {
        public SalmonEgg.Domain.Interfaces.Transport.ITransport? Created { get; private set; }

        public SalmonEgg.Domain.Interfaces.Transport.ITransport Create(string command, string[] args,
            Encoding encoding, IReadOnlyDictionary<string, string>? environment = null)
            => Created = new DesktopStdioTransportFactory().Create(command, args, encoding, environment);
    }

    private sealed class CapturedPty : IPtyConnection
    {
        private readonly Pipe _output = new();
        private readonly Stream _reader;
        private readonly MemoryStream _input = new();
        private bool _disposed;

        public CapturedPty() => _reader = _output.Reader.AsStream();
        public event EventHandler<PtyExitedEventArgs>? ProcessExited { add { } remove { } }
        public Stream ReaderStream => _reader;
        public Stream WriterStream => _input;
        public int Pid => 1;
        public int ExitCode => 0;
        public bool WaitForExit(int milliseconds) => true;
        public void Kill() { }
        public void Resize(int cols, int rows) { }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _output.Writer.Complete();
            _reader.Dispose();
            _input.Dispose();
        }
    }

    private sealed class RecordingLogSink : ILogEventSink
    {
        private readonly ConcurrentQueue<string> _messages = new();
        public string Text => string.Join('\n', _messages);
        public void Emit(LogEvent logEvent) => _messages.Enqueue(logEvent.RenderMessage() + logEvent.Exception);
    }

    private sealed class CredentialPeer(WebApplication app) : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stop = new();
        public ConcurrentQueue<CapturedRequest> Requests { get; } = new();
        public TaskCompletionSource StreamReady { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string HttpUrl => app.Urls.Single();
        public string Url { get; private set; } = string.Empty;
        public string? RedirectTo { get; set; }

        public static async Task<CredentialPeer> StartAsync(TransportType kind)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0,
                listener => listener.Protocols = kind == TransportType.WebSocket ? HttpProtocols.Http1 : HttpProtocols.Http2));
            var app = builder.Build();
            app.UseWebSockets();
            var peer = new CredentialPeer(app);
            app.Run(peer.HandleAsync);
            await app.StartAsync(TestContext.Current.CancellationToken);
            peer.Url = (kind == TransportType.WebSocket ? peer.HttpUrl.Replace("http://", "ws://", StringComparison.Ordinal) : peer.HttpUrl) + "/acp";
            return peer;
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await app.StopAsync(timeout.Token);
            await app.DisposeAsync();
            _stop.Dispose();
        }

        private async Task HandleAsync(HttpContext context)
        {
            using var requestStop = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token, context.RequestAborted);
            try
            {
                using var reader = new StreamReader(context.Request.Body);
                var body = await reader.ReadToEndAsync(requestStop.Token);
                Requests.Enqueue(new CapturedRequest(context.Request.Method, context.Request.Path,
                    context.Request.Headers[Header].ToString(), body));
                if (RedirectTo is { } destination && context.Request.Path == "/acp")
                {
                    context.Response.StatusCode = StatusCodes.Status302Found;
                    context.Response.Headers.Location = destination;
                    return;
                }
                if (context.WebSockets.IsWebSocketRequest)
                {
                    await HandleWebSocketAsync(context, requestStop.Token);
                    return;
                }
                if (HttpMethods.IsGet(context.Request.Method))
                {
                    context.Response.ContentType = "text/event-stream";
                    await context.Response.WriteAsync(": connected\n\n", requestStop.Token);
                    await context.Response.Body.FlushAsync(requestStop.Token);
                    StreamReady.TrySetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, requestStop.Token);
                    return;
                }
                if (body.Contains("\"initialize\"", StringComparison.Ordinal))
                {
                    using var frame = JsonDocument.Parse(body);
                    context.Response.Headers["Acp-Connection-Id"] = "credential-canary";
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(InitializeResponse(frame.RootElement), requestStop.Token);
                    return;
                }
                context.Response.StatusCode = StatusCodes.Status202Accepted;
            }
            catch (OperationCanceledException) when (requestStop.IsCancellationRequested)
            {
            }
            catch (WebSocketException) when (_stop.IsCancellationRequested)
            {
            }
        }

        private async Task HandleWebSocketAsync(HttpContext context, CancellationToken cancellationToken)
        {
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            StreamReady.TrySetResult();
            var buffer = new byte[4096];
            while (socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(buffer.AsMemory(), cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, cancellationToken);
                    return;
                }
                using var frame = JsonDocument.Parse(buffer.AsMemory(0, result.Count));
                var response = Encoding.UTF8.GetBytes(InitializeResponse(frame.RootElement));
                await socket.SendAsync(response.AsMemory(), WebSocketMessageType.Text, true, cancellationToken);
            }
        }

        private static string InitializeResponse(JsonElement request)
            => "{\"jsonrpc\":\"2.0\",\"id\":" + request.GetProperty("id").GetRawText()
                + ",\"result\":{\"protocolVersion\":1,\"agentCapabilities\":{},\"agentInfo\":{\"name\":\"credential-peer\",\"version\":\"1\"}}}";
    }
}
