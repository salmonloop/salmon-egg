using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using SalmonEgg.Domain.Interfaces.Transport;
using SalmonEgg.Infrastructure.Client;
using SalmonEgg.Infrastructure.Network;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.Transport;

/// <summary>
/// Smoke test against a live ACP-over-WebSocket bridge through the real
/// <see cref="WebSocketTransport"/> and <see cref="NetworkTransportAdapter"/>.
/// </summary>
/// <remarks>
/// The stdout classification is stdio-only — a WebSocket carries no stderr, so there is no stream
/// for an agent to confuse. What this guards is the shared layer the stdout work also touched:
/// <c>JsonRpcResponse</c> serialization (id now always written, IsSuccess/IsError no longer written)
/// and the parse-error reply added to <c>AcpClient</c>. Those are transport-agnostic, so a
/// regression here would be invisible to the stdio tests.
///
/// Skipped unless the bridge URL is supplied, so it is a no-op without one.
/// </remarks>
public sealed class RealAgentWebSocketSmokeTests
{
    private const string BridgeEnvironmentVariable = "SALMONEGG_ACP_SMOKE_WS_URL";

    [Fact]
    public async Task Initialize_AgainstLiveBridge_ShouldRoundTripWithoutErrors()
    {
        var url = Environment.GetEnvironmentVariable(BridgeEnvironmentVariable);
        Assert.SkipWhen(
            string.IsNullOrWhiteSpace(url),
            $"Set {BridgeEnvironmentVariable} to an ACP WebSocket endpoint to run this smoke test.");

        var frames = new List<string>();
        var errors = new List<TransportErrorEventArgs>();
        var firstFrame = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var socket = new WebSocketTransport(Log.Logger);
        using var transport = new NetworkTransportAdapter(socket, url!);
        transport.MessageReceived += (_, e) =>
        {
            lock (frames)
            {
                frames.Add(e.Message);
            }

            firstFrame.TrySetResult();
        };
        transport.ErrorOccurred += (_, e) =>
        {
            lock (errors)
            {
                errors.Add(e);
            }
        };

        Assert.True(await transport.ConnectAsync(TestContext.Current.CancellationToken));

        string[] received;
        TransportErrorEventArgs[] raised;
        try
        {
            Assert.True(await transport.SendMessageAsync(
                """
                {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":1,"clientCapabilities":{"fs":{"readTextFile":false,"writeTextFile":false}}}}
                """,
                TestContext.Current.CancellationToken));

            await firstFrame.Task.WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);
        }
        finally
        {
            lock (frames)
            {
                received = [.. frames];
            }

            lock (errors)
            {
                raised = [.. errors];
            }

            await transport.DisconnectAsync();
        }

        var initializeResult = Assert.Single(received);
        Assert.StartsWith("{", initializeResult, StringComparison.Ordinal);
        Assert.Contains("\"id\":1", initializeResult, StringComparison.Ordinal);
        Assert.Contains("\"protocolVersion\"", initializeResult, StringComparison.Ordinal);

        Assert.Empty(raised);
    }
}
