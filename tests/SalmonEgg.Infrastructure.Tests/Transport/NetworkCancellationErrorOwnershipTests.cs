using System;
using System.IO;
using System.Reactive.Subjects;
using System.Reactive.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Acp.Client;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Infrastructure.Client;
using SalmonEgg.Infrastructure.Network;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.Transport;

/// <summary>Production adapter → SDK event chain for per-send failure ownership.</summary>
public sealed class NetworkCancellationErrorOwnershipTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationSendFailure_StaysDiagnosticUnlessTheConnectionActuallyBreaks(bool fatal)
    {
        using var peer = new FailingNotificationTransport(fatal);
        using var network = new NetworkTransportAdapter(peer, "https://example.com/acp");
        var probe = new CancellationTestProbe();
        using var client = new AcpClient(new DomainAcpTransportAdapter(network), probe);
        client.ErrorOccurred += probe.RecordError;
        await client.InitializeAsync(new InitializeParams(new ClientInfo("ownership-test", "1.0"), new ClientCapabilities()),
            TestContext.Current.CancellationToken);
        using var cancellation = new CancellationTokenSource();
        var pending = client.CreateSessionAsync(new SessionNewParams(Path.GetFullPath(Path.GetTempPath()), null), cancellation.Token);
        var other = client.CreateSessionAsync(new SessionNewParams(Path.GetFullPath(Path.GetTempPath()), null),
            TestContext.Current.CancellationToken);

        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);

        Assert.Equal(1, peer.CancellationSends);
        if (fatal)
        {
            Assert.Single(probe.Errors);
            Assert.False(client.IsInitialized);
            // The transport may reconnect before its failed send unwinds. The old protocol
            // connection and its requests must already have been invalidated at the fatal event.
            Assert.True(network.IsConnected);
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => other);
            Assert.Contains("cancel write failed", failure.Message, StringComparison.Ordinal);
        }
        else
        {
            Assert.Empty(probe.Errors);
            Assert.True(client.IsInitialized);
            Assert.True(network.IsConnected);
            await client.DisconnectAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => other);
        }
    }

    private sealed class FailingNotificationTransport(bool fatal) : ITransport, ITransportStateSource, IDisposable
    {
        private readonly Subject<string> _messages = new();
        private readonly BehaviorSubject<TransportStateChange> _states = new(new(TransportState.Disconnected));
        public int CancellationSends { get; private set; }
        public IObservable<string> Messages => _messages;
        public IObservable<TransportState> StateChanges => _states.Select(static change => change.State);
        public IObservable<TransportStateChange> StateTransitions => _states;

        public Task ConnectAsync(string url, CancellationToken ct)
        {
            _states.OnNext(new(TransportState.Connected));
            return Task.CompletedTask;
        }

        public Task DisconnectAsync()
        {
            _states.OnNext(new(TransportState.Disconnected));
            return Task.CompletedTask;
        }

        public Task SendAsync(string message, CancellationToken ct)
        {
            using var frame = JsonDocument.Parse(message);
            switch (frame.RootElement.GetProperty("method").GetString())
            {
                case "initialize":
                    _messages.OnNext("{\"jsonrpc\":\"2.0\",\"id\":" + frame.RootElement.GetProperty("id").GetRawText()
                        + ",\"result\":{\"protocolVersion\":1,\"agentCapabilities\":{}}}");
                    break;
                case CancelRequestParams.Method:
                    CancellationSends++;
                    var failure = new IOException("cancel write failed");
                    if (fatal)
                    {
                        var change = new TransportStateChange(TransportState.Error, TransportStateChangeOrigin.SendFailure, failure);
                        _states.OnNext(change);
                        _states.OnNext(change); // Repeating the same exception must not double-report.
                        _states.OnNext(new(TransportState.Connected));
                    }
                    throw failure;
            }
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _states.Dispose();
            _messages.Dispose();
        }
    }
}
