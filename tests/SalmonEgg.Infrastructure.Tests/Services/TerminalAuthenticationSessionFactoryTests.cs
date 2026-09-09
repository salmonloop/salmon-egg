using System.IO.Pipelines;
using System.Text;
using Moq;
using Porta.Pty;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Infrastructure.Services;

namespace SalmonEgg.Infrastructure.Tests.Services;

public sealed class TerminalAuthenticationSessionFactoryTests
{
    [Fact]
    public async Task Start_InvocationSnapshot_SpawnsWithoutShellReparsingAndCapturesEarlyExit()
    {
        // Arrange
        using var connection = new FakeConnection { Exited = true, ExitCode = 0 };
        PtyOptions? options = null;
        await using var factory = CreateFactory((value, _) => { options = value; return Task.FromResult<IPtyConnection>(connection); });
        var invocation = new StdioInvocationSnapshot("agent", ["--acp", "literal & $(not-shell)"],
            new Dictionary<string, string> { ["TOKEN"] = "private" }, "/work", true);

        // Act
        await using var session = await factory.StartAsync(invocation, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, await session.Completion.WaitAsync(TestContext.Current.CancellationToken));
        Assert.Equal(invocation.Arguments, options!.CommandLine);
        Assert.Equal("private", options.Environment["TOKEN"]);
        Assert.Equal(invocation.Command, options.App);
        Assert.Equal(invocation.WorkingDirectory, options.Cwd);
    }

    [Fact]
    public async Task Session_OutputInputAndResize_UsePtyStreams()
    {
        // Arrange
        using var connection = new FakeConnection();
        await using var factory = CreateFactory((_, _) => Task.FromResult<IPtyConnection>(connection));
        await using var session = await factory.StartAsync(Invocation(), TestContext.Current.CancellationToken);
        var output = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.OutputReceived += (_, value) => output.TrySetResult(value);

        // Act
        await connection.Output.Writer.WriteAsync(Encoding.UTF8.GetBytes("sign-in prompt"), TestContext.Current.CancellationToken);
        await session.WriteInputAsync("response\r", TestContext.Current.CancellationToken);
        await session.ResizeAsync(80, 24, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("sign-in prompt", await output.Task.WaitAsync(TestContext.Current.CancellationToken));
        Assert.Equal("response\r", Encoding.UTF8.GetString(connection.Input.ToArray()));
        Assert.Equal((80, 24), connection.Size);
    }

    [Fact]
    public async Task Session_OutputClosesWithoutExit_DoesNotReportSuccess()
    {
        // Arrange
        using var connection = new FakeConnection();
        await using var factory = CreateFactory((_, _) => Task.FromResult<IPtyConnection>(connection));
        await using var session = await factory.StartAsync(Invocation(), TestContext.Current.CancellationToken);

        // Act
        await connection.Output.Writer.CompleteAsync();

        // Assert
        Assert.Null(await session.Completion.WaitAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Session_OutputConsumerFails_FailsClosedAndStillReclaimsNativeConnection()
    {
        // Arrange
        using var connection = new FakeConnection();
        await using var factory = CreateFactory((_, _) => Task.FromResult<IPtyConnection>(connection));
        var session = await factory.StartAsync(Invocation(), TestContext.Current.CancellationToken);
        session.OutputReceived += (_, _) => throw new InvalidOperationException("View detached");

        // Act
        await connection.Output.Writer.WriteAsync(Encoding.UTF8.GetBytes("sign-in prompt"), TestContext.Current.CancellationToken);
        Assert.Null(await session.Completion.WaitAsync(TestContext.Current.CancellationToken));
        await session.DisposeAsync();

        // Assert
        Assert.Equal(1, connection.KillCount);
        Assert.Equal(1, connection.DisposeCount);
        Assert.False(session.CanAcceptInput);
    }

    [Fact]
    public async Task Dispose_KillFailsBecauseProcessExited_StillDisposesConnectionAndStopsReader()
    {
        // Arrange
        using var connection = new FakeConnection { ThrowOnKill = true };
        await using var factory = CreateFactory((_, _) => Task.FromResult<IPtyConnection>(connection));
        var session = await factory.StartAsync(Invocation(), TestContext.Current.CancellationToken);

        // Act
        await session.DisposeAsync();
        await session.DisposeAsync();

        // Assert
        Assert.Equal(1, connection.KillCount);
        Assert.Equal(1, connection.DisposeCount);
        Assert.Null(await session.Completion);
        Assert.False(session.CanAcceptInput);
    }

    [Fact]
    public async Task DisposeFactory_ActiveSessions_DrainsEverySessionAndRejectsLaterStart()
    {
        // Arrange
        var connections = new List<FakeConnection>();
        await using var factory = CreateFactory((_, _) =>
        {
            var connection = new FakeConnection();
            connections.Add(connection);
            return Task.FromResult<IPtyConnection>(connection);
        });
        await factory.StartAsync(Invocation(), TestContext.Current.CancellationToken);
        await factory.StartAsync(Invocation(), TestContext.Current.CancellationToken);

        // Act
        await factory.DisposeAsync();

        // Assert
        Assert.All(connections, connection => Assert.Equal(1, connection.DisposeCount));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => factory.StartAsync(Invocation(), TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task Start_CancelledAfterNativeSpawn_ReclaimsUnpublishedConnection()
    {
        // Arrange
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var connection = new FakeConnection();
        await using var factory = CreateFactory((_, _) => { cancellation.Cancel(); return Task.FromResult<IPtyConnection>(connection); });

        // Act / Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => factory.StartAsync(Invocation(), cancellation.Token).AsTask());
        Assert.Equal(1, connection.KillCount);
        Assert.Equal(1, connection.DisposeCount);
    }

    private static StdioInvocationSnapshot Invocation() => new("agent", [], new Dictionary<string, string>(), "/work", true);

    private static TerminalAuthenticationSessionFactory CreateFactory(Func<PtyOptions, CancellationToken, Task<IPtyConnection>> spawn)
    {
        var platform = new Mock<IPlatformCapabilityService>();
        platform.SetupGet(x => x.SupportsTerminalAuthentication).Returns(true);
        return new TerminalAuthenticationSessionFactory(platform.Object, spawn);
    }

    private sealed class FakeConnection : IPtyConnection
    {
        public readonly Pipe Output = new();
        public readonly MemoryStream Input = new();
        private readonly Stream _reader;
        public int KillCount;
        public int DisposeCount;
        public bool ThrowOnKill;
        public bool Exited;
        public (int, int) Size;
        public FakeConnection() => _reader = Output.Reader.AsStream();
        public event EventHandler<PtyExitedEventArgs>? ProcessExited { add { } remove { } }
        public Stream ReaderStream => _reader;
        public Stream WriterStream => Input;
        public int Pid => 1;
        public int ExitCode { get; set; }
        public bool WaitForExit(int milliseconds) => Exited;
        public void Kill() { KillCount++; if (ThrowOnKill) throw new InvalidOperationException("Exited"); }
        public void Resize(int cols, int rows) => Size = (cols, rows);
        public void Dispose()
        {
            if (DisposeCount > 0) return;
            DisposeCount++;
            Output.Writer.Complete();
            _reader.Dispose();
            Input.Dispose();
        }
    }
}
