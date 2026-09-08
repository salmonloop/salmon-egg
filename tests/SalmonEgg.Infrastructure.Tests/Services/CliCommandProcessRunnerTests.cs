using System.Diagnostics;
using SalmonEgg.Infrastructure.Desktop.Services;

namespace SalmonEgg.Infrastructure.Tests.Services;

public sealed class CliCommandProcessRunnerTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task RunAsync_WhenBothStreamsExceedCaptureLimit_DrainsBothAndKeepsBoundedTails()
    {
        // Arrange
        var outputSize = CliCommandProcessRunner.MaxCapturedCharacters * 16;
        await using var command = await CliCommandProcessFixture.CreateAsync($"""
            printf '%{outputSize}s' ''
            printf 'stdout-tail'
            printf '%{outputSize}s' '' >&2
            printf 'stderr-tail' >&2
            """);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var timeout = TimeSpan.FromSeconds(10);

        // Act
        var run = CliCommandProcessRunner.RunAsync(new ProcessStartInfo(command.ExecutablePath), timeout, cancellation.Token);
        try
        {
            await command.ObserveStartedProcessAsync();
            var result = await run.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);

            // Assert
            Assert.NotNull(result);
            Assert.False(result.TimedOut);
            Assert.Equal(0, result.ExitCode);
            Assert.Equal(CliCommandProcessRunner.MaxCapturedCharacters, result.StandardOutput.Length);
            Assert.Equal(CliCommandProcessRunner.MaxCapturedCharacters, result.StandardError.Length);
            Assert.EndsWith("stdout-tail", result.StandardOutput, StringComparison.Ordinal);
            Assert.EndsWith("stderr-tail", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            await cancellation.CancelAsync();
            await command.StopAsync();
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            try
            {
                await run.WaitAsync(cleanup.Token);
            }
            catch (OperationCanceledException) when (!cleanup.IsCancellationRequested)
            {
                // An assertion failure still cancels and observes the invocation.
            }
        }
    }

    [Fact]
    public async Task RunAsync_WhenCancelledAndTerminationFails_PreservesCancellationAndCleanupError()
    {
        // Arrange
        await using var command = await CliCommandProcessFixture.CreateAsync("exec /bin/sleep 600");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var cleanupFailure = new IOException("The operating system refused termination.");

        // Act
        var run = CliCommandProcessRunner.RunAsync(
            new ProcessStartInfo(command.ExecutablePath),
            TimeSpan.FromSeconds(10),
            cancellation.Token,
            _ => Task.FromException(cleanupFailure));
        try
        {
            var process = await command.ObserveStartedProcessAsync();
            Assert.NotNull(process);
            await cancellation.CancelAsync();

            // Assert
            var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => run.WaitAsync(TestTimeout, TestContext.Current.CancellationToken));
            Assert.Equal(cancellation.Token, exception.CancellationToken);
            Assert.Same(cleanupFailure, exception.InnerException);
            Assert.False(process.HasExited, "The injected kill failure must leave a live pipe for reader cancellation to exercise.");
        }
        finally
        {
            await cancellation.CancelAsync();
            await command.StopAsync();
            await ObserveCompletionAsync(run);
        }
    }

    [Fact]
    public async Task RunAsync_WhenParentExitsButChildHoldsPipes_ReturnsAtDeadline()
    {
        // Arrange
        await using var command = await CliCommandProcessFixture.CreateAsync("""
            /bin/sleep 600 &
            printf '%s\n' "$!" > "$0.child.pid"
            exit 0
            """);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        // Act
        var run = CliCommandProcessRunner.RunAsync(
            new ProcessStartInfo(command.ExecutablePath), TimeSpan.FromSeconds(1), cancellation.Token);
        try
        {
            await command.ObserveStartedProcessAsync();
            var child = await command.ObserveStartedChildProcessAsync();
            var result = await run.WaitAsync(TestTimeout, TestContext.Current.CancellationToken);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.TimedOut);
            Assert.Equal(0, result.ExitCode);
            // An exited parent's PID cannot identify a child that the OS has already reparented.
            // The runner must stop its readers; the fixture owns and reaps this deliberately orphaned PID.
            Assert.False(child.HasExited);
        }
        finally
        {
            await cancellation.CancelAsync();
            await command.StopAsync();
            await ObserveCompletionAsync(run);
        }
    }

    private static async Task ObserveCompletionAsync(Task task)
    {
        using var cleanup = new CancellationTokenSource(TestTimeout);
        try
        {
            await task.WaitAsync(cleanup.Token);
        }
        catch (OperationCanceledException) when (!cleanup.IsCancellationRequested)
        {
            // Test failure cleanup must still observe a cancelled invocation.
        }
    }
}
