using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using SalmonEgg.Acp.Client;
using SalmonEgg.Infrastructure.Client;
using SalmonEgg.Infrastructure.Network;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.Transport;

/// <summary>
/// The bridged half of the same defect. A stdio-to-WebSocket bridge relays the agent's stdout
/// verbatim, so a non-ACP line arrives as a text frame — and the WebSocket transport has no stderr
/// to contrast it with, so it cannot classify the way the stdio transport does.
/// </summary>
/// <remarks>
/// Requires a bridge whose agent deliberately violates the spec, so it is opt-in via environment
/// variable and skips otherwise. The unit-level guard lives in AcpClientTests; this proves the
/// invariant survives a real socket, a real frame boundary, and the real adapter chain.
/// </remarks>
public sealed class WebSocketNonFrameFullStackTests
{
    private const string BridgeEnvironmentVariable = "SALMONEGG_ACP_SMOKE_VIOLATING_WS_URL";

    [Fact]
    public async Task PeerSendsNonFrameOverWebSocket_ShouldNotSurfaceClientError()
    {
        var url = Environment.GetEnvironmentVariable(BridgeEnvironmentVariable);
        Assert.SkipWhen(
            string.IsNullOrWhiteSpace(url),
            $"Set {BridgeEnvironmentVariable} to a bridge fronting a spec-violating agent.");

        var clientErrors = new List<string>();
        var frameSeen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // The production chain: socket -> Domain ITransport adapter -> IAcpTransport adapter.
        using var socket = new WebSocketTransport(Log.Logger);
        using var adapter = new NetworkTransportAdapter(socket, url!);
        using var acpTransport = new DomainAcpTransportAdapter(adapter);
        using var client = new AcpClient(acpTransport);
        client.ErrorOccurred += (_, error) =>
        {
            lock (clientErrors)
            {
                clientErrors.Add(error);
            }
        };
        acpTransport.MessageReceived += (_, e) =>
        {
            if (e.Message.Contains("\"protocolVersion\"", StringComparison.Ordinal))
            {
                frameSeen.TrySetResult();
            }
        };

        Assert.True(await adapter.ConnectAsync(TestContext.Current.CancellationToken));

        string[] surfaced;
        try
        {
            // The real frame arrives last, so seeing it proves the earlier violating frames were
            // already delivered and handled.
            await frameSeen.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        }
        finally
        {
            lock (clientErrors)
            {
                surfaced = [.. clientErrors];
            }

            await adapter.DisconnectAsync();
        }

        Assert.Empty(surfaced);
    }
}
