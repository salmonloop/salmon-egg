using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Interfaces.Transport;
using SalmonEgg.Infrastructure.Transport;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.Transport;

/// <summary>
/// A deliberate teardown is not a fault. These pin the boundary between "we ended this" and "it died
/// on us", because the errors are user-visible: a disconnect happens while listeners are still
/// attached (<c>AcpChatCoordinator</c> disconnects before it replaces the service, so the sink is
/// still subscribed), and neither <c>ProcessExited</c> nor <c>StdoutReadFailed</c> is filtered by
/// <c>AcpClient</c> the way <c>AgentStderr</c> is.
/// </summary>
public sealed class StdioTransportTeardownTests
{
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task DisconnectAsync_IdleHealthyAgent_ShouldRaiseNothing()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Uses /bin/sh for a process that parks on stdout.");

        // An agent parked waiting for requests is the ordinary state between prompts, so this is what
        // closing a session looks like. It used to raise three errors: ProcessExited for the process
        // we killed ourselves, plus StdoutReadFailed and StderrReadFailed reading
        // "Operation canceled" — an OS errno message from disposing a reader under a parked read,
        // which arrives as IOException rather than OperationCanceledException and so missed the
        // read loop's silent cancellation path. Two of the three also had SSH bridge guidance
        // appended, advising the user about ssh -t for merely closing a session.
        var errors = new List<TransportErrorEventArgs>();

        using var transport = new StdioTransport("/bin/sh", ["-c", "sleep 60"]);
        transport.ErrorOccurred += (_, e) => { lock (errors) { errors.Add(e); } };

        Assert.True(await transport.ConnectAsync(TestContext.Current.CancellationToken));
        await Task.Delay(Settle, TestContext.Current.CancellationToken);   // let both read loops park

        await transport.DisconnectAsync();
        await Task.Delay(Settle, TestContext.Current.CancellationToken);   // let anything raised arrive

        TransportErrorKind[] raised;
        lock (errors)
        {
            raised = [.. errors.Select(static e => e.Kind)];
        }

        // Asserted per cause rather than as one Assert.Empty, so a regression says which half broke:
        // the stream kinds come from the reads not being token-aware, ProcessExited from the guard on
        // an exit we caused ourselves. Each is independently sufficient to make this test fail.
        Assert.DoesNotContain(TransportErrorKind.StdoutReadFailed, raised);
        Assert.DoesNotContain(TransportErrorKind.StderrReadFailed, raised);
        Assert.DoesNotContain(TransportErrorKind.ProcessExited, raised);
        Assert.Empty(raised);
    }

    [Fact]
    public async Task ProcessExitsOnItsOwn_ShouldStillReportProcessExited()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Uses /bin/sh to exit with a chosen code.");

        // The counterpart the silence must not cost us: an exit nobody asked for is a real fault and
        // has to keep surfacing, or a crashing agent would look like a clean shutdown.
        var errors = new List<TransportErrorEventArgs>();
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var transport = new StdioTransport("/bin/sh", ["-c", "sleep 1; exit 3"]);
        transport.ErrorOccurred += (_, e) =>
        {
            lock (errors)
            {
                errors.Add(e);
            }

            if (e.Kind == TransportErrorKind.ProcessExited)
            {
                exited.TrySetResult();
            }
        };

        Assert.True(await transport.ConnectAsync(TestContext.Current.CancellationToken));

        try
        {
            await exited.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        }
        finally
        {
            await transport.DisconnectAsync();
        }

        lock (errors)
        {
            Assert.Contains(errors, e => e.Kind == TransportErrorKind.ProcessExited);
        }
    }

    [Fact]
    public async Task Dispose_AfterConnectCancelledMidStartup_ShouldReapChildAndStaySilent()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Uses /bin/sh and pgrep to observe the child.");

        // The path TryBeginTeardown does not cover. ConnectAsync starts the child, then awaits the
        // startup observation window; cancelling inside it returns false with IsConnected never set,
        // so teardown declines and never cancels the read token. Two things then have to hold:
        // Dispose must reap the child it started — Process.Dispose only releases the handle and there
        // is no finalizer — and it must not report an exit it is itself causing.
        var marker = $"salmonegg-teardown-test-{Guid.NewGuid():N}";
        var errors = new List<TransportErrorEventArgs>();

        var transport = new StdioTransport("/bin/sh", ["-c", $"# {marker}\nsleep 300"]);
        transport.ErrorOccurred += (_, e) => { lock (errors) { errors.Add(e); } };

        try
        {
            using var connectCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
            try
            {
                await transport.ConnectAsync(connectCts.Token);
            }
            catch (OperationCanceledException)
            {
                // Either shape is fine; what matters is that IsConnected was never set.
            }

            Assert.Equal(1, CountMatchingProcesses(marker));

            int beforeDispose;
            lock (errors)
            {
                beforeDispose = errors.Count;
            }

            transport.Dispose();
            await Task.Delay(Settle, TestContext.Current.CancellationToken);

            Assert.Equal(0, CountMatchingProcesses(marker));

            lock (errors)
            {
                Assert.Empty(errors.Skip(beforeDispose));
            }
        }
        finally
        {
            // Never let a failure here leave a stray child behind.
            if (CountMatchingProcesses(marker) > 0)
            {
                using var pkill = System.Diagnostics.Process.Start("/usr/bin/pkill", ["-f", marker]);
                await pkill!.WaitForExitAsync(TestContext.Current.CancellationToken);
            }
        }
    }

    private static int CountMatchingProcesses(string marker)
    {
        using var pgrep = System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo("/usr/bin/pgrep", $"-fc {marker}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            })!;

        var text = pgrep.StandardOutput.ReadToEnd().Trim();
        pgrep.WaitForExit();
        return int.TryParse(text, out var count) ? count : 0;
    }

    [Fact]
    public async Task DisconnectAsync_AfterAgentAlreadyExited_ShouldNotAddTeardownErrors()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Uses /bin/sh to exit with a chosen code.");

        // Disconnecting an already-dead agent is the normal cleanup after a crash. The crash itself
        // must be reported once; the teardown that follows must not pile on stream-read failures.
        var errors = new List<TransportErrorEventArgs>();
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var transport = new StdioTransport("/bin/sh", ["-c", "sleep 1; exit 3"]);
        transport.ErrorOccurred += (_, e) =>
        {
            lock (errors)
            {
                errors.Add(e);
            }

            if (e.Kind == TransportErrorKind.ProcessExited)
            {
                exited.TrySetResult();
            }
        };

        Assert.True(await transport.ConnectAsync(TestContext.Current.CancellationToken));
        await exited.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);

        await transport.DisconnectAsync();
        await Task.Delay(Settle, TestContext.Current.CancellationToken);

        lock (errors)
        {
            Assert.DoesNotContain(errors, e => e.Kind == TransportErrorKind.StdoutReadFailed);
            Assert.DoesNotContain(errors, e => e.Kind == TransportErrorKind.StderrReadFailed);
            Assert.DoesNotContain(errors, e => e.Kind == TransportErrorKind.DisconnectFailed);
        }
    }
}
