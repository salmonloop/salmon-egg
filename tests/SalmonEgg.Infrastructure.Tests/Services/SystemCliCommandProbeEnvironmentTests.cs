using SalmonEgg.Infrastructure.Desktop.Services;

namespace SalmonEgg.Infrastructure.Tests.Services;

public sealed class SystemCliCommandProbeEnvironmentTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task ProbeVersionAsync_WhenCallerCancels_TerminatesStartedCommand()
    {
        // Arrange
        await using var command = await CliCommandProcessFixture.CreateAsync("exec /bin/sleep 600");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var environment = new SystemCliCommandProbeEnvironment();

        // Act
        var probe = environment.ProbeVersionAsync(command.ExecutablePath, cancellation.Token);
        try
        {
            var process = await command.ObserveStartedProcessAsync();
            Assert.NotNull(process);
            await cancellation.CancelAsync();

            // Assert
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => probe.WaitAsync(TestTimeout, TestContext.Current.CancellationToken));
            Assert.True(process.HasExited, "The cancelled probe returned while its command was still running.");
        }
        finally
        {
            await cancellation.CancelAsync();
            await command.StopAsync();
            await ObserveCompletionAsync(probe);
        }
    }

    [Fact]
    public async Task ProbeVersionAsync_WhenStandardErrorExceedsPipeCapacity_ReturnsVersion()
    {
        // Arrange
        await using var command = await CliCommandProcessFixture.CreateAsync("""
            printf '%1048576s' '' >&2
            printf 'probe-version\n'
            """);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var environment = new SystemCliCommandProbeEnvironment();

        // Act
        var probe = environment.ProbeVersionAsync(command.ExecutablePath, cancellation.Token);
        try
        {
            await command.ObserveStartedProcessAsync();
            var result = await probe.WaitAsync(TestTimeout, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal("probe-version", result.Version);
            Assert.Null(result.FailureDetail);
        }
        finally
        {
            await cancellation.CancelAsync();
            await command.StopAsync();
            await ObserveCompletionAsync(probe);
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
            // Cancellation is the expected outcome during cleanup, including after a failed assertion.
        }
    }
}
