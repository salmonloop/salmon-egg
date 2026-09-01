using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Acp.Client;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Infrastructure.Client;
using SalmonEgg.Infrastructure.Transport;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.Transport;

/// <summary>
/// Runs the production stdio chain against a real peer process and inspects the peer's stdin log.
/// This proves the cancellation frame crosses the actual pipe, rather than only reaching a mock
/// transport in the ACP SDK tests.
/// </summary>
public sealed class StdioCancelRequestFullStackTests
{
    [Fact]
    public async Task CallerCancelsDispatchedRequest_SendsMatchingCancelNotificationOverRealStdioPipe()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Uses /bin/sh and mktemp-style POSIX shell syntax.");

        var directory = Path.Combine(Path.GetTempPath(), $"salmon-egg-cancel-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var scriptPath = Path.Combine(directory, "agent.sh");
        var framesPath = Path.Combine(directory, "received.ndjson");
        await File.WriteAllTextAsync(scriptPath, BuildAgentScript(framesPath), TestContext.Current.CancellationToken);
        // Redundant with the skip above at run time, but the platform analyzer cannot see through
        // Assert.SkipWhen, so the Unix-only call needs a guard it does understand.
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                scriptPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        try
        {
            using var transport = new StdioTransport("/bin/sh", [scriptPath]);
            using var client = new AcpClient(new DomainAcpTransportAdapter(transport));
            Assert.True(await transport.ConnectAsync(TestContext.Current.CancellationToken));

            await client.InitializeAsync(
                new InitializeParams(new ClientInfo("test", "1.0"), new ClientCapabilities())
                {
                    ProtocolVersion = AcpProtocolVersion.V1
                },
                TestContext.Current.CancellationToken);

            using var cancellation = new CancellationTokenSource();
            var request = client.CreateSessionAsync(
                new SessionNewParams(Path.GetFullPath(directory), null),
                cancellation.Token);
            await WaitForMethodAsync(framesPath, "session/new");
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
            await WaitForMethodAsync(framesPath, CancelRequestParams.Method);

            var frames = await ReadFramesAsync(framesPath);
            var original = Assert.Single(frames, frame => frame.GetProperty("method").GetString() == "session/new");
            var cancel = Assert.Single(frames, frame => frame.GetProperty("method").GetString() == CancelRequestParams.Method);

            Assert.False(cancel.TryGetProperty("id", out _));
            Assert.Equal(
                original.GetProperty("id").GetRawText(),
                cancel.GetProperty("params").GetProperty("requestId").GetRawText());

            await transport.DisconnectAsync();
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { }
        }
    }

    private static string BuildAgentScript(string framesPath)
    {
        var quotedPath = framesPath.Replace("'", "'\\''", StringComparison.Ordinal);
        return "#!/bin/sh\n"
            + "while IFS= read -r frame; do\n"
            + "  printf '%s\\n' \"$frame\" >> '" + quotedPath + "'\n"
            + "  case \"$frame\" in\n"
            + "    *'\"method\":\"initialize\"'*)\n"
            + "      id=$(printf '%s' \"$frame\" | sed -n 's/.*\"id\":\\([0-9][0-9]*\\).*/\\1/p')\n"
            + "      printf '{\"jsonrpc\":\"2.0\",\"id\":%s,\"result\":{\"protocolVersion\":1,\"agentInfo\":{\"name\":\"test-agent\",\"version\":\"1.0\"},\"agentCapabilities\":{}}}\\n' \"$id\"\n"
            + "      ;;\n"
            + "    *'\"method\":\"$/cancel_request\"'*)\n"
            + "      # Keep the process open until the client tears it down. The production transport\n"
            + "      # owns cleanup and kills the whole child tree at DisconnectAsync.\n"
            + "      ;;\n"
            + "  esac\n"
            + "done\n";
    }

    private static async Task WaitForMethodAsync(string framesPath, string method)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(framesPath)
                && (await File.ReadAllTextAsync(framesPath, TestContext.Current.CancellationToken))
                    .Contains($"\"method\":\"{method}\"", StringComparison.Ordinal))
            {
                return;
            }

            await Task.Delay(25, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException($"Timed out waiting for real stdio peer to receive '{method}'.");
    }

    private static async Task<JsonElement[]> ReadFramesAsync(string framesPath)
    {
        var lines = await File.ReadAllLinesAsync(framesPath, TestContext.Current.CancellationToken);
        var frames = new JsonElement[lines.Length];
        for (var index = 0; index < lines.Length; index++)
        {
            using var document = JsonDocument.Parse(lines[index]);
            frames[index] = document.RootElement.Clone();
        }

        return frames;
    }
}
