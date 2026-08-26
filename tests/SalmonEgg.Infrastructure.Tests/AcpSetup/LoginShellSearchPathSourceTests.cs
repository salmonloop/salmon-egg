using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Infrastructure.Desktop.AcpSetup;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.AcpSetup;

/// <summary>
/// Guards the source that turns a captured shell environment into search directories.
/// </summary>
/// <remarks>
/// This is the only route to a version manager implemented as a shell function — nvm ships no executable,
/// so nothing on disk reveals the node it activated. The contracts worth pinning down are that the
/// capture happens at most once (an interactive login shell is expensive, and the wizard probes many
/// components), that PATH order survives, and that every failure is silent rather than fatal.
/// </remarks>
public sealed class LoginShellSearchPathSourceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "login-shell-source-" + Guid.NewGuid().ToString("N"));

    public LoginShellSearchPathSourceTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>
    /// The captured PATH becomes the search directories, in the order the shell reported — the first entry
    /// is what the user's own terminal would run.
    /// </summary>
    [Fact]
    public async Task GetSearchDirectoriesAsync_WithCapturedPath_ShouldPreserveShellOrder()
    {
        SkipOnWindows();

        var shell = CreateShell();
        var source = new LoginShellSearchPathSource(
            marker => marker + """{"PATH":"/first:/second:/third"}""" + marker,
            () => shell);

        var directories = await source.GetSearchDirectoriesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new[] { "/first", "/second", "/third" }, directories);
    }

    /// <summary>
    /// Captured at most once. Spawning an interactive login shell runs the user's startup files and
    /// routinely takes seconds; repeating that for every component the wizard probes would multiply the
    /// cost for an answer that does not change while the app runs.
    /// </summary>
    [Fact]
    public async Task GetSearchDirectoriesAsync_CalledRepeatedly_ShouldCaptureOnce()
    {
        SkipOnWindows();

        var shell = CreateShell();
        var captures = 0;
        var source = new LoginShellSearchPathSource(
            marker =>
            {
                Interlocked.Increment(ref captures);
                return marker + """{"PATH":"/once"}""" + marker;
            },
            () => shell);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            Assert.Equal(new[] { "/once" }, await source.GetSearchDirectoriesAsync(TestContext.Current.CancellationToken));
        }

        Assert.Equal(1, captures);
    }

    /// <summary>
    /// Concurrent callers share one capture. The wizard probes components in parallel elsewhere, and a
    /// second interactive shell is exactly the cost the cache exists to avoid.
    /// </summary>
    [Fact]
    public async Task GetSearchDirectoriesAsync_FromConcurrentCallers_ShouldCaptureOnce()
    {
        SkipOnWindows();

        var shell = CreateShell();
        var captures = 0;
        var source = new LoginShellSearchPathSource(
            marker =>
            {
                Interlocked.Increment(ref captures);
                return marker + """{"PATH":"/shared"}""" + marker;
            },
            () => shell);

        var results = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ =>
                source.GetSearchDirectoriesAsync(TestContext.Current.CancellationToken)));

        Assert.All(results, directories => Assert.Equal(new[] { "/shared" }, directories));
        Assert.Equal(1, captures);
    }

    /// <summary>
    /// A shell that cannot be determined contributes nothing rather than failing. The inherited PATH is
    /// still there, so the wizard degrades to the behaviour it had before this source existed.
    /// </summary>
    [Fact]
    public async Task GetSearchDirectoriesAsync_WithNoShell_ShouldReturnEmpty()
    {
        var source = new LoginShellSearchPathSource(
            marker => marker + "{}" + marker,
            () => null);

        Assert.Empty(await source.GetSearchDirectoriesAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>A capture that fails is silent, for the same reason.</summary>
    [Fact]
    public async Task GetSearchDirectoriesAsync_WhenCaptureFails_ShouldReturnEmpty()
    {
        var source = new LoginShellSearchPathSource(
            marker => marker + "{}" + marker,
            () => Path.Combine(_root, "does-not-exist"));

        Assert.Empty(await source.GetSearchDirectoriesAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// An environment without PATH contributes nothing. A shell can report an environment that simply has
    /// no PATH, and inventing directories from that would be worse than adding none.
    /// </summary>
    [Fact]
    public async Task GetSearchDirectoriesAsync_WhenCapturedEnvironmentHasNoPath_ShouldReturnEmpty()
    {
        SkipOnWindows();

        var source = new LoginShellSearchPathSource(
            marker => marker + """{"HOME":"/home/someone"}""" + marker,
            () => CreateShell());

        Assert.Empty(await source.GetSearchDirectoriesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Constructor_WithNullCommandFactory_ShouldThrow()
        => Assert.Throws<ArgumentNullException>(() => new LoginShellSearchPathSource(null!));

    /// <summary>A fake shell that echoes the command it was handed, which carries the payload.</summary>
    private string CreateShell()
    {
        var path = Path.Combine(_root, "shell-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(path, "#!/bin/sh\nprintf '%s' \"$4\"\n");
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
        => Assert.SkipWhen(OperatingSystem.IsWindows(), "The fake shell is a POSIX script.");
}
