using System;
using System.IO;
using System.Threading.Tasks;
using SalmonEgg.Infrastructure.Desktop.AcpSetup;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.AcpSetup;

/// <summary>
/// Guards the capture of a user's shell environment against the ways real startup files misbehave.
/// </summary>
/// <remarks>
/// A user's rc files are arbitrary code this app does not control. In the wild they block on input, print
/// banners onto the same stdout the payload uses, and exit non-zero while still having produced a usable
/// environment. Each test here pins one of those down, because the failure mode of getting it wrong is
/// silent: the wizard reports every component missing and blames the machine.
/// </remarks>
public sealed class ShellEnvironmentCaptureTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "shell-capture-" + Guid.NewGuid().ToString("N"));

    public ShellEnvironmentCaptureTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>The straightforward case: a shell that prints the payload yields the environment.</summary>
    [Fact]
    public async Task CaptureAsync_WithCooperativeShell_ShouldReturnTheEnvironment()
    {
        SkipOnWindows();

        var shell = CreateShell("""
            #!/bin/sh
            # $4 is the command, matching the POSIX invocation `-l -i -c <command>`.
            printf '%s' "$4"
            """);

        var captured = await ShellEnvironmentCapture.CaptureAsync(
            shell,
            marker => marker + """{"PATH":"/from/shell"}""" + marker,
            timeout: TimeSpan.FromSeconds(30),
            TestContext.Current.CancellationToken);

        Assert.NotNull(captured);
        Assert.Equal("/from/shell", captured["PATH"]);
    }

    /// <summary>
    /// A startup file that reads from stdin must not hang the capture. Verified interactively: the same
    /// shell blocks indefinitely when stdin stays open, and returns at once when it is closed.
    /// </summary>
    [Fact]
    public async Task CaptureAsync_WhenShellReadsStdin_ShouldNotHang()
    {
        SkipOnWindows();

        var shell = CreateShell("""
            #!/bin/sh
            read -r ignored
            printf '%s' "$4"
            """);

        var captured = await ShellEnvironmentCapture.CaptureAsync(
            shell,
            marker => marker + """{"PATH":"/read-then-print"}""" + marker,
            timeout: TimeSpan.FromSeconds(30),
            TestContext.Current.CancellationToken);

        Assert.NotNull(captured);
        Assert.Equal("/read-then-print", captured["PATH"]);
    }

    /// <summary>
    /// A shell that never exits is abandoned at the timeout rather than blocking the wizard forever.
    /// </summary>
    [Fact]
    public async Task CaptureAsync_WhenShellNeverExits_ShouldGiveUpAtTheTimeout()
    {
        SkipOnWindows();

        var shell = CreateShell("""
            #!/bin/sh
            sleep 600
            """);

        var captured = await ShellEnvironmentCapture.CaptureAsync(
            shell,
            marker => marker + "{}" + marker,
            timeout: TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        Assert.Null(captured);
    }

    /// <summary>
    /// Banners and colour codes around the payload are ordinary, so the marker has to locate it rather
    /// than stdout being assumed to be the payload.
    /// </summary>
    [Fact]
    public async Task CaptureAsync_WithNoisyStartupFiles_ShouldStillFindThePayload()
    {
        SkipOnWindows();

        var shell = CreateShell("""
            #!/bin/sh
            printf 'Welcome to your shell!\n'
            printf '\033[32mnvm: now using node v24\033[0m\n'
            printf '%s' "$4"
            printf '\nHave a nice day\n'
            """);

        var captured = await ShellEnvironmentCapture.CaptureAsync(
            shell,
            marker => marker + """{"PATH":"/despite/noise"}""" + marker,
            timeout: TimeSpan.FromSeconds(30),
            TestContext.Current.CancellationToken);

        Assert.NotNull(captured);
        Assert.Equal("/despite/noise", captured["PATH"]);
    }

    /// <summary>
    /// A non-zero exit does not discard a payload that parsed. rc-file failures are common and unrelated
    /// to whether the environment was reported.
    /// </summary>
    [Fact]
    public async Task CaptureAsync_WhenShellExitsNonZero_ShouldStillUseThePayload()
    {
        SkipOnWindows();

        var shell = CreateShell("""
            #!/bin/sh
            printf '%s' "$4"
            exit 3
            """);

        var captured = await ShellEnvironmentCapture.CaptureAsync(
            shell,
            marker => marker + """{"PATH":"/nonzero/exit"}""" + marker,
            timeout: TimeSpan.FromSeconds(30),
            TestContext.Current.CancellationToken);

        Assert.NotNull(captured);
        Assert.Equal("/nonzero/exit", captured["PATH"]);
    }

    /// <summary>
    /// The guard variable reaches the child, so a user can make their startup files skip work that would
    /// hang or pollute a capture.
    /// </summary>
    /// <remarks>
    /// The fake shell cuts the marker out of the command text it was handed and builds the payload around
    /// the guard's value, so the assertion is that the variable was set in the <em>child's</em>
    /// environment rather than merely present in this process. Built by the shell rather than through
    /// <c>eval</c> of the supplied command, because eval would strip the JSON's quotes.
    /// </remarks>
    [Fact]
    public async Task CaptureAsync_ShouldSetTheGuardVariableForTheChild()
    {
        SkipOnWindows();

        var shell = CreateShell("""
            #!/bin/sh
            command="$4"
            marker="${command%%PLACEHOLDER*}"
            printf '%s{"PATH":"%s"}%s' "$marker" "$SALMONEGG_RESOLVING_ENVIRONMENT" "$marker"
            """);

        var captured = await ShellEnvironmentCapture.CaptureAsync(
            shell,
            marker => marker + "PLACEHOLDER" + marker,
            timeout: TimeSpan.FromSeconds(30),
            TestContext.Current.CancellationToken);

        Assert.NotNull(captured);
        Assert.Equal("1", captured["PATH"]);
    }

    /// <summary>A shell that cannot be started is a failed capture, not a crash.</summary>
    [Fact]
    public async Task CaptureAsync_WithMissingShell_ShouldReturnNull()
    {
        var captured = await ShellEnvironmentCapture.CaptureAsync(
            Path.Combine(_root, "does-not-exist"),
            marker => marker + "{}" + marker,
            timeout: TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken);

        Assert.Null(captured);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CaptureAsync_WithBlankShellPath_ShouldReturnNull(string shellPath)
    {
        var captured = await ShellEnvironmentCapture.CaptureAsync(
            shellPath,
            marker => marker + "{}" + marker,
            timeout: TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken);

        Assert.Null(captured);
    }

    /// <summary>
    /// A shell that printed nothing recognizable yields null rather than an empty environment, so the
    /// caller falls back to the inherited PATH instead of treating "nothing" as an answer.
    /// </summary>
    [Fact]
    public async Task CaptureAsync_WhenPayloadIsAbsent_ShouldReturnNull()
    {
        SkipOnWindows();

        var shell = CreateShell("""
            #!/bin/sh
            printf 'no payload here\n'
            """);

        var captured = await ShellEnvironmentCapture.CaptureAsync(
            shell,
            marker => marker + "{}" + marker,
            timeout: TimeSpan.FromSeconds(30),
            TestContext.Current.CancellationToken);

        Assert.Null(captured);
    }

    [Fact]
    public void ExtractEnvironment_WithMarkerInsideAValue_ShouldNotTruncateThePayload()
    {
        const string marker = "MK";
        var extracted = ShellEnvironmentCapture.ExtractEnvironment(
            marker + """{"A":"contains MK inside","PATH":"/tail"}""" + marker,
            marker);

        Assert.NotNull(extracted);
        Assert.Equal("/tail", extracted["PATH"]);
    }

    [Theory]
    [InlineData("", "MK")]
    [InlineData("MK{}", "MK")]
    [InlineData("MKnot-jsonMK", "MK")]
    [InlineData("no marker at all", "MK")]
    public void ExtractEnvironment_WithUnusableOutput_ShouldReturnNull(string output, string marker)
        => Assert.Null(ShellEnvironmentCapture.ExtractEnvironment(output, marker));

    private string CreateShell(string script)
    {
        var path = Path.Combine(_root, "fake-shell-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(path, script);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
        return path;
    }

    private static void SkipOnWindows()
        => Assert.SkipWhen(OperatingSystem.IsWindows(), "The fake shells are POSIX scripts.");
}
