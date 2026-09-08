using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Acp.Client;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Infrastructure.Client;
using SalmonEgg.Infrastructure.Network;
using Serilog;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.Transport;

/// <summary>
/// Opt-in contract for the deployed stdio↔WebSocket bridge, fronting cancellation-peer.py with a
/// fresh stdin log. The log proves frames reached the actual subprocess, not just the socket queue.
/// </summary>
public sealed class ProductionBridgeCancellationTests
{
    [Fact]
    public async Task ProductionBridge_CancellationReachesStdioPeerAndSettlesWithoutFault()
    {
        var endpoint = Environment.GetEnvironmentVariable("SALMONEGG_ACP_CANCELLATION_BRIDGE_URL");
        var peerLog = Environment.GetEnvironmentVariable("SALMONEGG_ACP_CANCELLATION_PEER_LOG");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(peerLog),
            "Requires the production bridge fronting cancellation-peer.py and its fresh peer stdin log; run the bridge gate.");
        Assert.True(Path.IsPathFullyQualified(peerLog!), "The peer log must be an absolute, dedicated fixture path.");
        Assert.False(File.Exists(peerLog!) && new FileInfo(peerLog!).Length > 0,
            "Use a fresh peer log so stale bridge traffic cannot pass this gate.");

        using var socket = new WebSocketTransport(Log.Logger, connectTimeout: TimeSpan.FromSeconds(10));
        using var network = new NetworkTransportAdapter(socket, endpoint!);
        var probe = new CancellationTestProbe();
        using var client = new AcpClient(new DomainAcpTransportAdapter(network), probe);
        client.ErrorOccurred += probe.RecordError;
        await client.InitializeAsync(new InitializeParams(new ClientInfo("bridge-cancellation", "1.0"), new ClientCapabilities()),
            TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        using var cancellation = new CancellationTokenSource();
        var pending = client.CreateSessionAsync(new SessionNewParams(Path.GetFullPath(Path.GetTempPath()), null), cancellation.Token);
        var request = await WaitForPeerFrameAsync(peerLog!, "session/new");
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        var notification = await WaitForPeerFrameAsync(peerLog!, CancelRequestParams.Method);
        CancellationTestProbe.AssertMatchingNotification(request, notification);
        await probe.Settlement.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);

        var next = await client.CreateSessionAsync(new SessionNewParams(Path.GetFullPath(Path.GetTempPath()), null),
            TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        Assert.Equal("session-after-cancel", next.SessionId);
        var abandoned = client.CreateSessionAsync(new SessionNewParams(Path.GetFullPath(Path.GetTempPath()), null),
            TestContext.Current.CancellationToken);
        await client.DisconnectAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => abandoned);
        Assert.False(network.IsConnected);
        Assert.Empty(probe.Errors);
    }

    private static async Task<JsonElement> WaitForPeerFrameAsync(string path, string method)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(path))
            {
                var contents = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
                // Only complete NDJSON lines are records; an in-progress append is not evidence.
                foreach (var line in contents.Split('\n').SkipLast(1).Where(static line => line.Length > 0))
                {
                    using var frame = JsonDocument.Parse(line);
                    if (frame.RootElement.TryGetProperty("method", out var frameMethod) && frameMethod.GetString() == method)
                    {
                        return frame.RootElement.Clone();
                    }
                }
            }
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
        throw new TimeoutException("Production bridge did not forward the expected frame to the peer stdin log.");
    }
}
