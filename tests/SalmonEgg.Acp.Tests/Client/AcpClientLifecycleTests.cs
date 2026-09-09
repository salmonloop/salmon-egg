using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using SalmonEgg.Acp.Client;
using SalmonEgg.Acp.Protocol;
using Xunit;

namespace SalmonEgg.Acp.Tests.Client;

public sealed class AcpClientLifecycleTests
{
    [Fact]
    public async Task InitializeAsync_DuringPhysicalDisconnect_FailsBeforeConnectingOrSending()
    {
        using var fixture = new LifecycleFixture();
        await fixture.InitializeAsync();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Transport.Setup(x => x.DisconnectAsync()).Returns(async () =>
        {
            started.TrySetResult();
            await release.Task;
            fixture.IsConnected = false;
            return true;
        });
        var disconnect = fixture.Client.DisconnectAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        try
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(fixture.InitializeAsync);
            Assert.Contains("disconnect", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1, fixture.InitializeFrames);
            fixture.Transport.Verify(x => x.ConnectAsync(It.IsAny<CancellationToken>()), Times.Never);
        }
        finally
        {
            release.TrySetResult();
            await disconnect;
        }

        await fixture.InitializeAsync();
        Assert.True(fixture.Client.IsInitialized);
        Assert.Equal(2, fixture.InitializeFrames);
    }

    [Fact]
    public async Task DisconnectAsync_ConcurrentCallsShareOnePhysicalTeardown()
    {
        using var fixture = new LifecycleFixture();
        await fixture.InitializeAsync();
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Transport.Setup(x => x.DisconnectAsync()).Returns(release.Task);

        var first = fixture.Client.DisconnectAsync();
        var second = fixture.Client.DisconnectAsync();
        try
        {
            Assert.Same(first, second);
            fixture.Transport.Verify(x => x.DisconnectAsync(), Times.Once);
        }
        finally
        {
            release.TrySetResult(true);
            await Task.WhenAll(first, second);
        }
    }

    [Fact]
    public async Task DisconnectAsync_FailureIsSharedAndCanBeRetried()
    {
        using var fixture = new LifecycleFixture();
        await fixture.InitializeAsync();
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Transport.SetupSequence(x => x.DisconnectAsync()).Returns(release.Task).ReturnsAsync(true);
        var first = fixture.Client.DisconnectAsync();
        var second = fixture.Client.DisconnectAsync();
        release.TrySetException(new IOException("teardown failed"));

        await Assert.ThrowsAsync<IOException>(() => first);
        await Assert.ThrowsAsync<IOException>(() => second);
        Assert.Same(first, second);
        await Assert.ThrowsAsync<InvalidOperationException>(fixture.InitializeAsync);
        Assert.True(await fixture.Client.DisconnectAsync());
        fixture.Transport.Verify(x => x.DisconnectAsync(), Times.Exactly(2));
        await fixture.InitializeAsync();
        Assert.True(fixture.Client.IsInitialized);
    }

    [Fact]
    public async Task DisconnectAsync_TransportReturnsFalseRequiresRetryBeforeInitialize()
    {
        using var fixture = new LifecycleFixture();
        await fixture.InitializeAsync();
        fixture.Transport.SetupSequence(x => x.DisconnectAsync()).ReturnsAsync(false).ReturnsAsync(true);

        Assert.False(await fixture.Client.DisconnectAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(fixture.InitializeAsync);
        Assert.Equal(1, fixture.InitializeFrames);

        Assert.True(await fixture.Client.DisconnectAsync());
        await fixture.InitializeAsync();
        Assert.True(fixture.Client.IsInitialized);
    }

    [Fact]
    public async Task DisconnectAsync_DisposeDuringTeardownCannotReinitialize()
    {
        using var fixture = new LifecycleFixture();
        await fixture.InitializeAsync();
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Transport.Setup(x => x.DisconnectAsync()).Returns(release.Task);
        var disconnect = fixture.Client.DisconnectAsync();
        fixture.Client.Dispose();
        try
        {
            await Assert.ThrowsAsync<ObjectDisposedException>(fixture.InitializeAsync);
            Assert.Equal(1, fixture.InitializeFrames);
        }
        finally
        {
            release.TrySetResult(true);
            await disconnect;
        }

        await Assert.ThrowsAsync<ObjectDisposedException>(fixture.InitializeAsync);
        fixture.Transport.Verify(x => x.Dispose(), Times.Once);
    }

    private sealed class LifecycleFixture : IDisposable
    {
        private readonly ConcurrentQueue<string> _sent = new();
        public Mock<IAcpTransport> Transport { get; } = new();
        public AcpClient Client { get; }
        public bool IsConnected { get; set; } = true;
        public int InitializeFrames => _sent.Count(message => message.Contains("\"method\":\"initialize\"", StringComparison.Ordinal));

        public LifecycleFixture()
        {
            Transport.SetupGet(x => x.IsConnected).Returns(() => IsConnected);
            Transport.Setup(x => x.ConnectAsync(It.IsAny<CancellationToken>())).Callback(() => IsConnected = true).ReturnsAsync(true);
            Transport.Setup(x => x.SendMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback<string, CancellationToken>((message, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    _sent.Enqueue(message);
                    using var document = JsonDocument.Parse(message);
                    if (document.RootElement.TryGetProperty("method", out var method) && method.GetString() == "initialize")
                    {
                        var id = document.RootElement.GetProperty("id").GetRawText();
                        Transport.Raise(x => x.MessageReceived += null, new AcpTransportMessageReceivedEventArgs(
                            "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"result\":{\"protocolVersion\":1,\"agentInfo\":{\"name\":\"agent\",\"version\":\"1\"},\"agentCapabilities\":{}}}"));
                    }
                }).ReturnsAsync(true);
            Client = new AcpClient(Transport.Object);
        }

        public Task<InitializeResponse> InitializeAsync() => Client.InitializeAsync(
            new InitializeParams(new ClientInfo("lifecycle", "1"), new ClientCapabilities()), TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);

        public void Dispose() => Client.Dispose();
    }
}
