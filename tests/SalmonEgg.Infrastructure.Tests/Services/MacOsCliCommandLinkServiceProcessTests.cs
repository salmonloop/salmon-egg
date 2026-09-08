using System.Diagnostics;
using SalmonEgg.Infrastructure.Desktop.Services;

namespace SalmonEgg.Infrastructure.Tests.Services;

public sealed class MacOsCliCommandLinkServiceProcessTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task RunAuthorizationProcessAsync_WhenCallerCancels_TerminatesCommand()
    {
        // Arrange
        await using var command = await CliCommandProcessFixture.CreateAsync("exec /bin/sleep 600");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        // Act
        var authorization = RunAsync(command, CommandTimeout, cancellation.Token);
        try
        {
            var process = await command.ObserveStartedProcessAsync();
            Assert.NotNull(process);
            await cancellation.CancelAsync();

            // Assert
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => authorization.WaitAsync(TestTimeout, TestContext.Current.CancellationToken));
            Assert.True(process.HasExited, "The cancelled authorization returned while its command was still running.");
        }
        finally
        {
            await cancellation.CancelAsync();
            await command.StopAsync();
            await ObserveCompletionAsync(authorization);
        }
    }

    [Fact]
    public async Task RunAuthorizationProcessAsync_WhenDeadlineExpires_TerminatesCommandAndReportsFailure()
    {
        // Arrange
        await using var command = await CliCommandProcessFixture.CreateAsync("exec /bin/sleep 600");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        // Act
        var authorization = RunAsync(command, TimeSpan.FromSeconds(1), cancellation.Token);
        try
        {
            var process = await command.ObserveStartedProcessAsync();
            Assert.NotNull(process);
            var result = await authorization.WaitAsync(TestTimeout, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(PrivilegedShellStatus.Failed, result.Status);
            Assert.Equal("the authorization prompt was not answered in time", result.Detail);
            Assert.True(process.HasExited, "The timeout returned while its command was still running.");
        }
        finally
        {
            await cancellation.CancelAsync();
            await command.StopAsync();
            await ObserveCompletionAsync(authorization);
        }
    }

    [Fact]
    public async Task RunAuthorizationProcessAsync_WhenStandardOutputExceedsPipeCapacity_Succeeds()
    {
        // Arrange
        await using var command = await CliCommandProcessFixture.CreateAsync("printf '%1048576s' ''");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        // Act
        var authorization = RunAsync(command, CommandTimeout, cancellation.Token);
        try
        {
            await command.ObserveStartedProcessAsync();
            var result = await authorization.WaitAsync(TestTimeout, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(PrivilegedShellStatus.Succeeded, result.Status);
        }
        finally
        {
            await cancellation.CancelAsync();
            await command.StopAsync();
            await ObserveCompletionAsync(authorization);
        }
    }

    [Theory]
    [InlineData("execution error: cancelled (-128)", PrivilegedShellStatus.Cancelled)]
    [InlineData("authorization failed", PrivilegedShellStatus.Failed)]
    public async Task RunAuthorizationProcessAsync_WhenCommandFails_PreservesDiagnosticMeaning(
        string diagnostic,
        PrivilegedShellStatus expected)
    {
        // Arrange
        await using var command = await CliCommandProcessFixture.CreateAsync($"printf '%s' '{diagnostic}' >&2\nexit 1");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        // Act
        var authorization = RunAsync(command, CommandTimeout, cancellation.Token);
        try
        {
            await command.ObserveStartedProcessAsync();
            var result = await authorization.WaitAsync(TestTimeout, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(expected, result.Status);
            Assert.Equal(expected == PrivilegedShellStatus.Failed ? diagnostic : null, result.Detail);
        }
        finally
        {
            await cancellation.CancelAsync();
            await command.StopAsync();
            await ObserveCompletionAsync(authorization);
        }
    }

    private static Task<PrivilegedShellResult> RunAsync(
        CliCommandProcessFixture command,
        TimeSpan timeout,
        CancellationToken cancellationToken)
        // This is the same lifecycle and result translation osascript uses. The test never opens an
        // authorization dialog or changes /usr/local/bin on the machine that runs it.
        => MacOsCliCommandLinkService.RunAuthorizationProcessAsync(
            new ProcessStartInfo(command.ExecutablePath), timeout, cancellationToken);

    private static async Task ObserveCompletionAsync(Task task)
    {
        using var cleanup = new CancellationTokenSource(TestTimeout);
        try
        {
            await task.WaitAsync(cleanup.Token);
        }
        catch (OperationCanceledException) when (!cleanup.IsCancellationRequested)
        {
            // A cancelled command is expected when cleanup follows an assertion failure.
        }
    }
}
