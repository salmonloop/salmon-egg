using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using SalmonEgg.Domain.Interfaces.Transport;
using SalmonEgg.Infrastructure.Transport;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.Transport;

public sealed class StdioTransportConnectionTests
{
    [Fact]
    public void ResolveCommand_WindowsBareCommand_UsesPathExtAndPathDirectory()
    {
        var commandDirectory = Path.Combine(Path.GetTempPath(), "stdio-command-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(commandDirectory);
        var commandPath = Path.Combine(commandDirectory, "npm.cmd");
        File.WriteAllText(commandPath, "@echo off");

        var resolvedCommand = StdioCommandResolver.Resolve(
            "npm",
            isWindows: true,
            currentDirectory: Path.GetTempPath(),
            pathEnvironment: commandDirectory,
            pathExtensions: ".com;.exe;.cmd");

        Assert.Equal(commandPath, resolvedCommand);
    }

    [Fact]
    public void ResolveCommand_NonWindowsBareCommand_DoesNotProbeWindowsExtensions()
    {
        var resolvedCommand = StdioCommandResolver.Resolve(
            "npm",
            isWindows: false,
            currentDirectory: Path.GetTempPath(),
            pathEnvironment: Path.GetTempPath(),
            pathExtensions: ".CMD");

        Assert.Equal("npm", resolvedCommand);
    }

    [Theory]
    [InlineData("tools/agent")]
    [InlineData(@"tools\agent")]
    [InlineData("agent.exe")]
    public void ResolveCommand_CommandAlreadySpecifiesPathOrExtension_ReturnsUnchanged(string command)
    {
        var resolvedCommand = StdioCommandResolver.Resolve(
            command,
            isWindows: true,
            currentDirectory: Path.GetTempPath(),
            pathEnvironment: Path.GetTempPath(),
            pathExtensions: ".EXE;.CMD");

        Assert.Equal(command, resolvedCommand);
    }

    [Fact]
    public void ResolveWorkingDirectory_WhenResolvedCommandIsAbsolute_UsesCommandDirectory()
    {
        var commandDirectory = Path.Combine(Path.GetTempPath(), "stdio-transport-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(commandDirectory);
        var commandPath = Path.Combine(commandDirectory, "agent.cmd");
        File.WriteAllText(commandPath, "@echo off");

        var workingDirectory = StdioTransport.ResolveWorkingDirectory(
            commandPath,
            currentDirectory: @"C:\Program Files\WindowsApps\FakePackage");

        Assert.Equal(commandDirectory, workingDirectory, ignoreCase: true);
    }

    [Fact]
    public void ResolveWorkingDirectory_WhenCurrentDirectoryIsWindowsApps_FallsBackToUserWritableDirectory()
    {
        var workingDirectory = StdioTransport.ResolveWorkingDirectory(
            "agent-command",
            currentDirectory: @"C:\Program Files\WindowsApps\FakePackage");

        Assert.DoesNotContain("WindowsApps", workingDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(workingDirectory));
    }

    /// <summary>
    /// 回归保护:构造期(以及 working-directory 解析)不得创建目录副作用。
    /// AGENTS.md 缓存/持久化边界:构造函数/getter/VM-init/DI 不得触发真实 FS 写入。
    /// 当 currentDirectory 落在 WindowsApps 沙箱时,ResolveWorkingDirectory 必须返回一个
    /// <em>构造调用之前就已存在</em>的 fallback 目录,而不是现场创建 LocalAppData/SalmonEgg。
    /// </summary>
    [Fact]
    public void ResolveWorkingDirectory_WhenFallingBack_DoesNotCreateDirectoryAsSideEffect()
    {
        var snapshot = Directory.GetCurrentDirectory();
        try
        {
            // 选一个保证不存在的 probe 目录,用作 currentDirectory 触发 fallback,
            // 同时确认 ResolveWorkingDirectory 不会顺手把它创建出来。
            var probeMissing = Path.Combine(Path.GetTempPath(), "stdio-fallback-probe", Guid.NewGuid().ToString("N"), "absent");
            Assert.False(Directory.Exists(probeMissing));

            var workingDirectory = StdioTransport.ResolveWorkingDirectory("agent-command", currentDirectory: probeMissing);

            // 返回的 fallback 目录必须在调用之前就已存在(非本副作用创建)。
            Assert.True(Directory.Exists(workingDirectory));
            // probe 目录仍不得被创建。
            Assert.False(Directory.Exists(probeMissing));
        }
        finally
        {
            Directory.SetCurrentDirectory(snapshot);
        }
    }

    [Fact]
    public async Task ConnectAsync_WhenProcessExitsImmediately_ShouldSurfaceStderrOutput()
    {
        var (command, args) = CreateImmediateFailureCommand("ssh config permissions are invalid");
        using var transport = new StdioTransport(command, args);
        var errors = new List<string>();
        var stderrObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        transport.ErrorOccurred += (_, error) =>
        {
            errors.Add(error.ErrorMessage);
            if (error.ErrorMessage.Contains("ssh config permissions are invalid", StringComparison.Ordinal))
            {
                Assert.Equal(TransportErrorKind.AgentStderr, error.Kind);
                stderrObserved.TrySetResult();
            }
        };

        var connected = await transport.ConnectAsync(TestContext.Current.CancellationToken);

        Assert.False(connected);
        await stderrObserved.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        Assert.Contains(
            errors,
            message => message.Contains("ssh config permissions are invalid", StringComparison.Ordinal));
    }

    [Fact]
    public void CreateProcessStartInfo_UsesArgumentListWithoutStringQuoting()
    {
        using var transport = new StdioTransport(
            "agent-command",
            [
                "--empty",
                string.Empty,
                "value with spaces",
                "quote\"value",
                @"path\with\slashes"
            ]);

        var startInfo = transport.CreateProcessStartInfo();

        Assert.Equal(string.Empty, startInfo.Arguments);
        Assert.Equal(
            [
                "--empty",
                string.Empty,
                "value with spaces",
                "quote\"value",
                @"path\with\slashes"
            ],
            startInfo.ArgumentList);
    }

    [Fact]
    public void CreateProcessStartInfo_CommandScript_UsesCommandInterpreter()
    {
        using var transport = new StdioTransport("agent.cmd", ["--flag"]);

        var startInfo = transport.CreateProcessStartInfo();

        Assert.Equal("cmd.exe", startInfo.FileName);
        Assert.Equal(["/c", "agent.cmd", "--flag"], startInfo.ArgumentList);
    }


    [Fact]
    public void ResolveCommand_NonWindowsBareCommand_WithUnixPathEnvironment_ReturnsBareCommandUnchanged()
    {
        var commandDirectory = Path.Combine(Path.GetTempPath(), "stdio-command-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(commandDirectory);
        var commandPath = Path.Combine(commandDirectory, "agent-bin");
        File.WriteAllText(commandPath, "#!/bin/sh\n");

        var resolvedCommand = StdioCommandResolver.Resolve(
            "agent-bin",
            isWindows: false,
            currentDirectory: Path.GetTempPath(),
            pathEnvironment: commandDirectory,
            pathExtensions: null);

        // Non-Windows resolution intentionally leaves bare commands to the OS PATH lookup.
        Assert.Equal("agent-bin", resolvedCommand);
    }

    [Fact]
    public void ResolveWorkingDirectory_WhenCommandIsAbsoluteUnixPath_UsesCommandDirectory()
    {
        var commandDirectory = Path.Combine(Path.GetTempPath(), "stdio-transport-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(commandDirectory);
        var commandPath = Path.Combine(commandDirectory, "agent");
        File.WriteAllText(commandPath, "#!/bin/sh\n");

        var workingDirectory = StdioTransport.ResolveWorkingDirectory(
            commandPath,
            currentDirectory: Path.GetTempPath());

        Assert.Equal(commandDirectory, workingDirectory);
    }

    [Fact]
    public void ResolveWorkingDirectory_WhenCurrentDirectoryMissing_FallsBackToWritableDirectory()
    {
        var missingDirectory = Path.Combine(Path.GetTempPath(), "stdio-missing-cwd", Guid.NewGuid().ToString("N"), "does-not-exist");

        var workingDirectory = StdioTransport.ResolveWorkingDirectory(
            "agent-command",
            currentDirectory: missingDirectory);

        Assert.True(Directory.Exists(workingDirectory));
        Assert.NotEqual(missingDirectory, workingDirectory);
    }

    [Fact]
    public async Task ConnectAsync_WithAbsoluteScriptPathContainingSpaces_CanConnectOnNonWindowsHosts()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var tempDir = Path.Combine(Path.GetTempPath(), $"stdio-space-path-{Guid.NewGuid():N}", "with space");
        Directory.CreateDirectory(tempDir);
        var scriptPath = Path.Combine(tempDir, "slow agent.sh");
        await File.WriteAllTextAsync(scriptPath, "#!/bin/sh\nsleep 2\n", TestContext.Current.CancellationToken);
        File.SetUnixFileMode(
            scriptPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

        try
        {
            using var transport = new StdioTransport("/bin/sh", [scriptPath]);
            var connected = await transport.ConnectAsync(TestContext.Current.CancellationToken);
            Assert.True(connected);
            await transport.DisconnectAsync();
        }
        finally
        {
            try
            {
                Directory.Delete(tempDir, recursive: true);
            }
            catch
            {
            }
        }
    }

    private static (string Command, string[] Args) CreateImmediateFailureCommand(string stderrMessage)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // PowerShell cold start regularly exceeds StdioTransport's startup-exit observation window.
            // Use a tiny batch file so this fixture really models an immediate process failure on Windows.
            var scriptDirectory = Path.Combine(Path.GetTempPath(), "stdio-transport-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(scriptDirectory);

            var scriptPath = Path.Combine(scriptDirectory, "fail-fast.cmd");
            File.WriteAllLines(
                scriptPath,
                [
                    "@echo off",
                    $"echo {EscapeForBatchEcho(stderrMessage)} 1>&2",
                    "exit /b 1"
                ]);

            return (scriptPath, []);
        }

        return (
            "/bin/sh",
            [
                "-c",
                $"printf '%s\\n' '{stderrMessage}' >&2; exit 1"
            ]);
    }

    private static string EscapeForBatchEcho(string value)
    {
        return value
            .Replace("^", "^^", StringComparison.Ordinal)
            .Replace("&", "^&", StringComparison.Ordinal)
            .Replace("|", "^|", StringComparison.Ordinal)
            .Replace("<", "^<", StringComparison.Ordinal)
            .Replace(">", "^>", StringComparison.Ordinal)
            .Replace("(", "^(", StringComparison.Ordinal)
            .Replace(")", "^)", StringComparison.Ordinal);
    }
}
