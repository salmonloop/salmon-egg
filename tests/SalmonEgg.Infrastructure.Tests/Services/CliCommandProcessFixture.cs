using System.Diagnostics;

namespace SalmonEgg.Infrastructure.Tests.Services;

internal sealed class CliCommandProcessFixture : IAsyncDisposable
{
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(5);

    private readonly string _root;
    private Process? _process;
    private Process? _childProcess;

    private CliCommandProcessFixture(string root, string executablePath)
    {
        _root = root;
        ExecutablePath = executablePath;
    }

    public string ExecutablePath { get; }

    public static async Task<CliCommandProcessFixture> CreateAsync(string body)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The fake commands are POSIX scripts.");
        var root = Path.Combine(Path.GetTempPath(), "salmon-egg-cli-process-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var executablePath = Path.Combine(root, "command");
        try
        {
            // The shell records its own PID before the test payload. No process-name search can match
            // another test's process, and exec keeps that same PID for a long-lived command.
            await File.WriteAllTextAsync(
                executablePath,
                "#!/bin/sh\nprintf '%s\\n' \"$$\" > \"$0.pid\"\n" + body + "\n",
                TestContext.Current.CancellationToken);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(executablePath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            return new CliCommandProcessFixture(root, executablePath);
        }
        catch
        {
            Directory.Delete(root, recursive: true);
            throw;
        }
    }

    public async Task<Process?> ObserveStartedProcessAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(ProcessTimeout);
        var pidFile = ExecutablePath + ".pid";
        while (true)
        {
            if (File.Exists(pidFile)
                && int.TryParse(await File.ReadAllTextAsync(pidFile, timeout.Token), out var processId))
            {
                try
                {
                    _process = Process.GetProcessById(processId);
                    _ = _process.HasExited;
                    return _process;
                }
                catch (ArgumentException)
                {
                    // A successful command may already have exited before the first observation.
                    return null;
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
        }
    }

    public async Task StopAsync()
    {
        using var cleanup = new CancellationTokenSource(ProcessTimeout);
        try
        {
            _process ??= await ReadRecordedProcessAsync(ExecutablePath + ".pid", cleanup.Token);
            await StopProcessAsync(_process, cleanup.Token);
        }
        finally
        {
            using var childCleanup = new CancellationTokenSource(ProcessTimeout);
            _childProcess ??= await ReadRecordedProcessAsync(ExecutablePath + ".child.pid", childCleanup.Token);
            await StopProcessAsync(_childProcess, childCleanup.Token);
        }
    }

    public async Task<Process> ObserveStartedChildProcessAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(ProcessTimeout);
        while (true)
        {
            _childProcess ??= await ReadRecordedProcessAsync(ExecutablePath + ".child.pid", timeout.Token);
            if (_childProcess is not null)
            {
                return _childProcess;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await StopAsync();
        }
        finally
        {
            _process?.Dispose();
            _childProcess?.Dispose();
            Directory.Delete(_root, recursive: true);
        }
    }

    private static async Task<Process?> ReadRecordedProcessAsync(string pidFile, CancellationToken cancellationToken)
    {
        if (!File.Exists(pidFile)
            || !int.TryParse(await File.ReadAllTextAsync(pidFile, cancellationToken), out var processId))
        {
            return null;
        }

        try
        {
            var process = Process.GetProcessById(processId);
            _ = process.HasExited;
            return process;
        }
        catch (ArgumentException)
        {
            // An assertion can fail after a short-lived command has already exited.
            return null;
        }
    }

    private static async Task StopProcessAsync(Process? process, CancellationToken cancellationToken)
    {
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The command exited between inspection and termination.
        }

        await process.WaitForExitAsync(cancellationToken);
    }
}
