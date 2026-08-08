using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Acp.Client;
using SalmonEgg.Infrastructure.Client;
using SalmonEgg.Infrastructure.Transport;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.Transport;

/// <summary>
/// The full inbound chain a user's toast actually travels:
/// child process → <see cref="StdioTransport"/> → DomainAcpTransportAdapter → <see cref="AcpClient"/>
/// → ErrorOccurred → (in the app) the chat failure surface.
/// </summary>
/// <remarks>
/// The transport tests prove classification and the AcpClient tests prove the early return, but
/// neither proves they meet. This is the assertion that matches the report: an agent writing
/// diagnostics to stdout must not raise a client error, because the failure surface holds such a
/// message until the next successful operation clears it.
/// </remarks>
public sealed class StdoutViolationFullStackTests
{
    private const string BomOctal = @"\0357\0273\0277";

    [Fact]
    public async Task AgentWritesDiagnosticsToStdout_ShouldNotSurfaceClientError()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Uses /bin/sh to emit exact bytes on the pipe.");

        // Everything an agent might wrongly put on stdout, then a real frame so the test has a
        // completion signal proving the stream was still being read afterwards.
        var script = string.Join(
            '\n',
            [
                $@"printf '%b' '{BomOctal}\n'",
                @"printf '%s\n' 'Running database migrations'",
                @"printf '%s\n' 'Server listening on port 4000'",
                @"printf '%s\n' '{""jsonrpc"":""2.0"",""id"":1,""result"":{""protocolVersion"":1}}'",
                "sleep 30"
            ]);

        var clientErrors = new List<string>();
        var responded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var transport = new StdioTransport("/bin/sh", ["-c", script]);
        using var adapter = new DomainAcpTransportAdapter(transport);
        using var client = new AcpClient(adapter);
        client.ErrorOccurred += (_, error) =>
        {
            lock (clientErrors)
            {
                clientErrors.Add(error);
            }
        };
        adapter.MessageReceived += (_, e) =>
        {
            if (e.Message.Contains("\"protocolVersion\"", StringComparison.Ordinal))
            {
                responded.TrySetResult();
            }
        };

        Assert.True(await transport.ConnectAsync(TestContext.Current.CancellationToken));

        string[] surfaced;
        try
        {
            await responded.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        }
        finally
        {
            lock (clientErrors)
            {
                surfaced = [.. clientErrors];
            }

            await transport.DisconnectAsync();
        }

        // The reported symptom was a stranded toast reading "Failed to process message: Invalid
        // JSON: '0xEF' is an invalid start of a value". Nothing may reach the user here: the BOM
        // line is blank, and the two log lines are the agent's spec violation, not a client fault.
        Assert.Empty(surfaced);
    }
}
