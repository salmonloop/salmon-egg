using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Interfaces.Transport;
using SalmonEgg.Infrastructure.Transport;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.Transport;

/// <summary>
/// Drives a real child process through the real <see cref="StdioTransport"/> so the classification
/// is exercised end to end: actual UTF-8 bytes on an actual pipe, decoded by the actual
/// <see cref="StreamReader"/>. The unit tests cover the pure function; these cover the wiring,
/// including byte-level behaviour a string-level test cannot reach.
/// </summary>
public sealed class StdioTransportStdoutViolationEndToEndTests
{
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(15);

    // dash's printf understands octal (\0357) but not hex (\xEF) escapes — with %b and \xEF it
    // emits the literal backslash text instead of the byte, which would silently change what is
    // being tested. Octal is what actually puts a byte order mark on the pipe here.
    private const string BomOctal = @"\0357\0273\0277";
    private const string EscOctal = @"\033";

    // U+F81C (private-use, e.g. a Nerd Font glyph) = EF A0 9C. Emitted octally for the same
    // reason as the byte order mark: the literal character does not survive every editor.
    private const string PrivateUseOctal = @"\0357\0240\0234";

    [Fact]
    public async Task ReadLoop_AgentMixesDiagnosticsIntoStdout_ForwardsOnlyFramesAndNeverEscalates()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Uses /bin/sh to emit exact bytes on the pipe.");

        // An agent that violates ACP the way real ones do: a byte order mark, startup logging,
        // ANSI-coloured logging, a private-use glyph, and a BOM-prefixed frame, interleaved with
        // valid frames.
        var script = string.Join(
            '\n',
            [
                $@"printf '%b' '{BomOctal}\n'",                          // BOM-only line => blank
                @"printf '%s\n' 'Running database migrations'",           // plain logging
                $@"printf '%b' '{EscOctal}[1;32mINFO{EscOctal}[0m ready\n'", // ANSI logging
                @"printf '%s\n' '{""jsonrpc"":""2.0"",""method"":""a""}'",
                $@"printf '%b' '{BomOctal}{{""jsonrpc"":""2.0"",""method"":""b""}}\n'", // BOM + frame
                $@"printf '%b' '{PrivateUseOctal} loading\n'",           // private-use glyph
                @"printf '%s\n' '{""jsonrpc"":""2.0"",""method"":""c""}'",
                "sleep 30"                                                // keep the pipe open
            ]);

        var messages = new List<string>();
        var errors = new List<TransportErrorEventArgs>();
        var thirdFrame = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var transport = new StdioTransport("/bin/sh", ["-c", script]);
        transport.MessageReceived += (_, e) =>
        {
            lock (messages)
            {
                messages.Add(e.Message);
                if (messages.Count == 3)
                {
                    thirdFrame.TrySetResult();
                }
            }
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
            await thirdFrame.Task.WaitAsync(Settle, TestContext.Current.CancellationToken);
        }
        finally
        {
            // Snapshot before disconnecting: tearing down the pipe while the read loop is parked in
            // ReadLineAsync legitimately raises StdoutReadFailed, which is shutdown noise rather
            // than anything this test is about.
            lock (messages)
            {
                received = [.. messages];
            }

            lock (errors)
            {
                raised = [.. errors];
            }

            await transport.DisconnectAsync();
        }

        // Exactly the three frames reach the JSON-RPC layer, in order, with the BOM stripped.
        Assert.Equal(3, received.Length);
        Assert.Equal(@"{""jsonrpc"":""2.0"",""method"":""a""}", received[0]);
        Assert.Equal(@"{""jsonrpc"":""2.0"",""method"":""b""}", received[1]);
        Assert.Equal(@"{""jsonrpc"":""2.0"",""method"":""c""}", received[2]);
        Assert.All(received, frame => Assert.StartsWith("{", frame, StringComparison.Ordinal));
        Assert.All(received, frame => Assert.DoesNotContain('﻿', frame));

        // The three diagnostic lines are reported as violations, never as generic transport errors,
        // and the BOM-only line raises nothing at all.
        Assert.All(raised, e => Assert.Equal(TransportErrorKind.StdoutProtocolViolation, e.Kind));
        Assert.Equal(3, raised.Length);

        // The offending content and its leading bytes must be recoverable from the report; without
        // them every cause collapses to the same parser message.
        Assert.Contains(raised, e => e.ErrorMessage.Contains("Running database migrations", StringComparison.Ordinal));
        Assert.Contains(raised, e => e.ErrorMessage.Contains("hex: 1B 5B 31", StringComparison.Ordinal));
        Assert.Contains(raised, e => e.ErrorMessage.Contains("hex: EF A0 9C", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReadLoop_AgentEmitsOnlyBom_ShouldRaiseNothing()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Uses /bin/sh to emit exact bytes on the pipe.");

        // The regression behind the reported toast: a lone byte order mark is a blank line, but
        // U+FEFF is not whitespace to IsNullOrWhiteSpace, so it used to reach the parser and
        // surface "'0xEF' is an invalid start of a value" for what is really an empty line.
        var script = string.Join(
            '\n',
            [
                $@"printf '%b' '{BomOctal}\n'",
                $@"printf '%b' '{BomOctal}\n'",
                "sleep 30"
            ]);

        var messages = new List<string>();
        var errors = new List<TransportErrorEventArgs>();

        using var transport = new StdioTransport("/bin/sh", ["-c", script]);
        transport.MessageReceived += (_, e) => { lock (messages) { messages.Add(e.Message); } };
        transport.ErrorOccurred += (_, e) => { lock (errors) { errors.Add(e); } };

        Assert.True(await transport.ConnectAsync(TestContext.Current.CancellationToken));

        string[] received;
        TransportErrorEventArgs[] raised;
        try
        {
            // Nothing to await for a signal: assert on quiescence.
            await Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        }
        finally
        {
            lock (messages)
            {
                received = [.. messages];
            }

            lock (errors)
            {
                raised = [.. errors];
            }

            await transport.DisconnectAsync();
        }

        Assert.Empty(received);
        Assert.Empty(raised);
    }
}
