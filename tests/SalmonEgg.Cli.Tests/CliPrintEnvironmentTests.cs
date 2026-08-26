using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace SalmonEgg.Cli.Tests;

/// <summary>
/// Guards the environment-printing protocol the app uses to recover the user's real shell environment.
/// </summary>
/// <remarks>
/// A GUI-launched process inherits the session environment, not the one a shell profile builds, so a
/// version-manager toolchain is invisible to it. The app asks the user's login shell to run this mode and
/// reads back what that shell produced.
///
/// The contract these tests pin down is what makes the payload findable in a stream the app does not
/// control: rc files print banners and colour codes onto the same stdout. Marker-delimited JSON survives
/// that; bare output does not.
/// </remarks>
public sealed class CliPrintEnvironmentTests
{
    private const string Marker = "abc123marker";

    /// <summary>
    /// The payload is wrapped in the caller's marker so it can be located inside unrelated shell output.
    /// </summary>
    [Fact]
    public async Task RunAsync_WithPrintEnv_ShouldWrapJsonInTheSuppliedMarker()
    {
        await using var stdout = new StringWriter();
        await using var stderr = new StringWriter();

        var exitCode = await CliApplication.RunAsync(
            [$"--printenv={Marker}"],
            stdout,
            stderr,
            TestContext.Current.CancellationToken);

        var raw = stdout.ToString();
        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.StartsWith(Marker, raw, StringComparison.Ordinal);
        Assert.EndsWith(Marker, raw, StringComparison.Ordinal);
        Assert.Empty(stderr.ToString());
    }

    /// <summary>
    /// The payload deserializes to the process environment, which is the whole point: the caller reads
    /// PATH out of it.
    /// </summary>
    [Fact]
    public async Task RunAsync_WithPrintEnv_ShouldEmitTheProcessEnvironmentAsJson()
    {
        const string name = "SALMONEGG_PRINTENV_PROBE";
        var expected = Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(name, expected);
        await using var stdout = new StringWriter();
        await using var stderr = new StringWriter();

        try
        {
            await CliApplication.RunAsync(
                [$"--printenv={Marker}"],
                stdout,
                stderr,
                TestContext.Current.CancellationToken);

            var raw = stdout.ToString();
            var payload = raw[Marker.Length..^Marker.Length];
            var environment = JsonSerializer.Deserialize<Dictionary<string, string>>(payload);

            Assert.NotNull(environment);
            Assert.Equal(expected, environment[name]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    /// <summary>
    /// The JSON stays on one line, so output a shell interleaves cannot split it across the markers.
    /// </summary>
    [Fact]
    public async Task RunAsync_WithPrintEnv_ShouldEmitSingleLineJson()
    {
        await using var stdout = new StringWriter();
        await using var stderr = new StringWriter();

        await CliApplication.RunAsync(
            [$"--printenv={Marker}"],
            stdout,
            stderr,
            TestContext.Current.CancellationToken);

        Assert.DoesNotContain('\n', stdout.ToString());
    }

    /// <summary>
    /// A marker is mandatory. Without one the output would be unwrapped and the caller could not locate
    /// it, so the argument is not recognized and the CLI falls through to ordinary parsing.
    /// </summary>
    [Theory]
    [InlineData("--printenv")]
    [InlineData("--printenv=")]
    public void TryGetMarker_WithoutAMarkerValue_ShouldNotSelectPrintEnvMode(string argument)
        => Assert.False(SalmonEgg.Cli.Hosting.CliPrintEnvironment.TryGetMarker([argument], out _));

    [Fact]
    public void TryGetMarker_WithMarkerValue_ShouldReturnIt()
    {
        Assert.True(SalmonEgg.Cli.Hosting.CliPrintEnvironment.TryGetMarker([$"--printenv={Marker}"], out var marker));
        Assert.Equal(Marker, marker);
    }

    /// <summary>
    /// Recognized wherever it appears, because the shell command line the app builds is not obliged to
    /// place it first.
    /// </summary>
    [Fact]
    public void TryGetMarker_WithLeadingArguments_ShouldStillFindIt()
    {
        Assert.True(
            SalmonEgg.Cli.Hosting.CliPrintEnvironment.TryGetMarker(["config", $"--printenv={Marker}"], out var marker));
        Assert.Equal(Marker, marker);
    }

    [Fact]
    public void TryGetMarker_WithNoPrintEnvArgument_ShouldReturnFalse()
        => Assert.False(SalmonEgg.Cli.Hosting.CliPrintEnvironment.TryGetMarker(["config", "list"], out _));
}
