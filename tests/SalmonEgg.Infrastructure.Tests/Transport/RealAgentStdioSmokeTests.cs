using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Interfaces.Transport;
using SalmonEgg.Infrastructure.Transport;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.Transport;

/// <summary>
/// Smoke test against a real ACP agent over the real <see cref="StdioTransport"/>. The stdout
/// classification gates every inbound frame, so this guards the regression that matters most: a
/// spec-compliant agent must be entirely unaffected by it. Skipped unless the agent is installed,
/// so it is a no-op on machines without one.
/// </summary>
public sealed class RealAgentStdioSmokeTests
{
    private const string AgentEnvironmentVariable = "SALMONEGG_ACP_SMOKE_AGENT";

    [Fact]
    public async Task Initialize_AgainstRealAgent_ShouldForwardFramesWithoutViolations()
    {
        var agent = Environment.GetEnvironmentVariable(AgentEnvironmentVariable);
        Assert.SkipWhen(
            string.IsNullOrWhiteSpace(agent),
            $"Set {AgentEnvironmentVariable} to an ACP stdio agent command to run this smoke test.");

        var frames = new List<string>();
        var errors = new List<TransportErrorEventArgs>();
        var firstFrame = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var transport = new StdioTransport(agent!);
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
            await transport.SendMessageAsync(
                """
                {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":1,"clientCapabilities":{"fs":{"readTextFile":false,"writeTextFile":false}}}}
                """,
                TestContext.Current.CancellationToken);

            await firstFrame.Task.WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);
        }
        finally
        {
            // Snapshot before teardown: closing the pipe under a parked ReadLineAsync legitimately
            // raises StdoutReadFailed, which is shutdown noise rather than an agent violation.
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

        // The initialize result must survive the gate intact.
        var initializeResult = Assert.Single(received);
        Assert.StartsWith("{", initializeResult, StringComparison.Ordinal);
        Assert.Contains("\"id\":1", initializeResult, StringComparison.Ordinal);
        Assert.Contains("\"protocolVersion\"", initializeResult, StringComparison.Ordinal);

        // A compliant agent writes only ACP frames to stdout, so nothing may be classified as a
        // violation. If this ever fires, the agent regressed (or the gate became too strict).
        Assert.DoesNotContain(raised, e => e.Kind == TransportErrorKind.StdoutProtocolViolation);
    }
}
