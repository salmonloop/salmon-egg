using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using SalmonEgg.Acp.Client;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Infrastructure.Client;
using SalmonEgg.Infrastructure.Transport;
using Xunit;

namespace SalmonEgg.Acp.Desktop.Tests;

/// <summary>
/// Linux gate for the actual stdio transport and production adapter. The peer records its stdin,
/// so success requires both recognition of the stdout batch and the aggregated return pipe write.
/// </summary>
public sealed class StdioBatchTests
{
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(AcpProtocolVersion.V1)]
    [InlineData(AcpProtocolVersion.V2)]
    public async Task StdioBatch_NegotiatedVersion_RejectsV1OrDispatchesAndAggregatesV2(int version)
    {
        // Arrange
        Assert.SkipUnless(OperatingSystem.IsLinux(), "The real peer uses /bin/sh; run the Linux ACP batch gate.");
        var directory = Path.Combine(Path.GetTempPath(), "salmon-egg-batch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var script = Path.Combine(directory, "peer.sh");
        var frames = Path.Combine(directory, "received.ndjson");
        var pidFile = Path.Combine(directory, "peer.pid");
        await File.WriteAllTextAsync(script, AgentScript(frames, pidFile, version), TestToken);
        int? peerPid = null;
        try
        {
            using var transport = new StdioTransport("/bin/sh", [script]);
            using var client = new AcpClient(new DomainAcpTransportAdapter(transport));
            var errors = new ConcurrentQueue<string>();
            client.ErrorOccurred += (_, error) => errors.Enqueue(error);
            var form = new TaskCompletionSource<ElicitationRequestEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
            client.ElicitationRequestReceived += (_, value) => form.TrySetResult(value);
            var parameters = new InitializeParams(new ClientInfo("stdio-batch", "1.0"), new ClientCapabilities
            {
                Elicitation = new ElicitationCapabilities { Form = new ElicitationFormCapabilities() }
            })
            { ProtocolVersion = version };
            await (version == AcpProtocolVersion.V2
                ? client.InitializeDraftAsync(parameters, TestToken)
                : client.InitializeAsync(parameters, TestToken)).WaitAsync(TimeSpan.FromSeconds(10), TestToken);
            peerPid = int.Parse(await File.ReadAllTextAsync(pidFile, TestToken));

            // Act: session/new causes the real process to emit its batch on stdout.
            var session = client.CreateSessionAsync(new SessionNewParams(directory, []), TestToken);
            var received = await WaitForResponseAsync(frames);

            // Assert
            if (version == AcpProtocolVersion.V1)
            {
                Assert.Equal(JsonValueKind.Object, received.ValueKind);
                Assert.Equal(JsonValueKind.Null, received.GetProperty("id").ValueKind);
                Assert.Equal(-32600, received.GetProperty("error").GetProperty("code").GetInt32());
                Assert.False(form.Task.IsCompleted);
            }
            else
            {
                Assert.Equal(JsonValueKind.Array, received.ValueKind);
                Assert.Equal(2, received.GetArrayLength());
                Assert.Equal(10, received[0].GetProperty("id").GetInt32());
                Assert.Equal(-32800, received[0].GetProperty("error").GetProperty("code").GetInt32());
                Assert.Equal("missing", received[1].GetProperty("id").GetString());
                Assert.Equal(-32601, received[1].GetProperty("error").GetProperty("code").GetInt32());
                Assert.False(await (await form.Task.WaitAsync(TimeSpan.FromSeconds(5), TestToken)).Accept(null));
            }
            Assert.Equal("pipe-confirmed", (await session.WaitAsync(TimeSpan.FromSeconds(5), TestToken)).SessionId);
            Assert.Empty(errors);
            Assert.True(await client.DisconnectAsync());
            await WaitForExitAsync(peerPid.Value);
            peerPid = null;
        }
        finally
        {
            if (peerPid.HasValue)
            {
                try
                {
                    using var process = Process.GetProcessById(peerPid.Value);
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                    // Cleanup owns a separate bounded token: a cancelled test must still reap its peer.
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await process.WaitForExitAsync(cleanup.Token);
                }
                catch (ArgumentException) { }
            }
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string AgentScript(string frames, string pidFile, int version)
    {
        var batch = """
            [{"jsonrpc":"2.0","id":10,"method":"elicitation/create","params":{"sessionId":"session","mode":"form","message":"Choose","requestedSchema":{"type":"object","properties":{}}}},
             {"jsonrpc":"2.0","method":"_ignored"},
             {"jsonrpc":"2.0","method":"$/cancel_request","params":{"requestId":10}},
             {"jsonrpc":"2.0","id":"missing","method":"_missing"}]
            """.Replace("\n", "", StringComparison.Ordinal).Replace("\r", "", StringComparison.Ordinal);
        var response = "{\"jsonrpc\":\"2.0\",\"id\":%s,\"result\":{\"sessionId\":\"pipe-confirmed\"}}";
        if (version == AcpProtocolVersion.V2) response = "[" + response + "]";
        var infoName = version == AcpProtocolVersion.V2 ? "info" : "agentInfo";
        var capabilitiesName = version == AcpProtocolVersion.V2 ? "capabilities" : "agentCapabilities";
        return "#!/bin/sh\n"
            + "printf '%s\\n' \"$$\" > " + Quote(pidFile) + "\n"
            + "while IFS= read -r frame; do\n"
            + " printf '%s\\n' \"$frame\" >> " + Quote(frames) + "\n"
            + " case \"$frame\" in\n"
            + "  *'\"method\":\"initialize\"'*)\n"
            + "   id=$(printf '%s' \"$frame\" | sed -n 's/.*\"id\":\\([0-9][0-9]*\\).*/\\1/p')\n"
            + "   printf '{\"jsonrpc\":\"2.0\",\"id\":%s,\"result\":{\"protocolVersion\":" + version
            + ",\"" + infoName + "\":{\"name\":\"pipe-peer\",\"version\":\"1\"},\"" + capabilitiesName + "\":{}}}\\n' \"$id\"\n"
            + "   ;;\n"
            + "  *'\"method\":\"session/new\"'*)\n"
            + "   new_id=$(printf '%s' \"$frame\" | sed -n 's/.*\"id\":\\([0-9][0-9]*\\).*/\\1/p')\n"
            + "   printf '%s\\n' " + Quote(batch) + "\n"
            + "   ;;\n"
            + "  *'\"error\"'*)\n"
            + "   printf '" + response + "\\n' \"$new_id\"\n"
            + "   ;;\n"
            + " esac\ndone\n";
    }

    private static string Quote(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    private static async Task<JsonElement> WaitForResponseAsync(string frames)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (true)
        {
            if (File.Exists(frames))
            {
                var text = await File.ReadAllTextAsync(frames, timeout.Token);
                foreach (var line in text.Split('\n').SkipLast(1))
                {
                    using var document = JsonDocument.Parse(line);
                    var item = document.RootElement;
                    if (item.ValueKind == JsonValueKind.Array || item.TryGetProperty("error", out _))
                    {
                        return item.Clone();
                    }
                }
            }
            await Task.Delay(20, timeout.Token);
        }
    }

    private static async Task WaitForExitAsync(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            await process.WaitForExitAsync(TestToken).WaitAsync(TimeSpan.FromSeconds(5), TestToken);
            Assert.True(process.HasExited);
        }
        catch (ArgumentException) { }
    }
}
