using SalmonEgg.Domain.Services;
using SalmonEgg.Infrastructure.Services;

namespace SalmonEgg.Infrastructure.Tests.Services;

public sealed class DesktopElicitationUriLauncherTests
{
    [Theory]
    [InlineData(0, ExternalUriOpenResult.Opened)]
    [InlineData(1, ExternalUriOpenResult.Failed)]
    public async Task OpenAsync_OpenerExits_UsesExitStatusAndDrainsPrivateOutput(int exitCode, ExternalUriOpenResult expected)
    {
        // Arrange
        Assert.SkipUnless(OperatingSystem.IsLinux(), "The launcher owns Linux desktop shell behavior.");
        await using var command = await CliCommandProcessFixture.CreateAsync($"""
            printf '%131072s' ''
            printf 'private-canary'
            printf '%131072s' '' >&2
            printf 'private-canary' >&2
            exit {exitCode}
            """);
        var launcher = new DesktopElicitationUriLauncher(new Probe(command.ExecutablePath));
        Assert.True(ExternalUriTarget.TryCreate("https://example.com/authorize?private-canary", out var target));

        // Act
        var result = await launcher.OpenAsync(target!, TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(expected, result);
        var process = await command.ObserveStartedProcessAsync();
        Assert.True(process is null || process.HasExited);
    }

    [Fact]
    public async Task OpenAsync_Cancelled_ReapsOwnedOpenerAndObservesReaders()
    {
        // Arrange
        Assert.SkipUnless(OperatingSystem.IsLinux(), "The launcher owns Linux desktop shell behavior.");
        await using var command = await CliCommandProcessFixture.CreateAsync("exec /bin/sleep 600");
        var launcher = new DesktopElicitationUriLauncher(new Probe(command.ExecutablePath));
        Assert.True(ExternalUriTarget.TryCreate("https://example.com/authorize", out var target));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var opening = launcher.OpenAsync(target!, cancellation.Token);
        try
        {
            var process = await command.ObserveStartedProcessAsync();

            // Act
            await cancellation.CancelAsync();
            var result = await opening.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(ExternalUriOpenResult.Cancelled, result);
            Assert.True(process is null || process.HasExited);
        }
        finally
        {
            await cancellation.CancelAsync();
            await command.StopAsync();
            await opening.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task OpenAsync_AlreadyCancelled_DoesNotLaunch()
    {
        // Arrange
        Assert.SkipUnless(OperatingSystem.IsLinux(), "The launcher owns Linux desktop shell behavior.");
        await using var command = await CliCommandProcessFixture.CreateAsync("exit 0");
        var launcher = new DesktopElicitationUriLauncher(new Probe(command.ExecutablePath));
        Assert.True(ExternalUriTarget.TryCreate("https://example.com/authorize", out var target));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act
        var result = await launcher.OpenAsync(target!, cancellation.Token);

        // Assert
        Assert.Equal(ExternalUriOpenResult.Cancelled, result);
        Assert.False(File.Exists(command.ExecutablePath + ".pid"));
    }

    private sealed class Probe(string executable) : IPlatformRuntimeCapabilityProbe
    {
        public bool IsDesktopProcessHost => true;
        public bool HasExternalFileOpener => true;
        public bool HasInteractiveTerminalSurface => false;
        public string ResolveExternalFileOpener() => executable;
        public bool CanLoadNativeLibrary(string libraryName) => false;
    }
}
