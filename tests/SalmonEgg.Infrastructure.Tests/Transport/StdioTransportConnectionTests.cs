using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using SalmonEgg.Infrastructure.Transport;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.Transport;

public sealed class StdioTransportConnectionTests
{
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

    [Fact]
    public async Task ConnectAsync_WhenProcessExitsImmediately_ShouldSurfaceStderrOutput()
    {
        var (command, args) = CreateImmediateFailureCommand("ssh config permissions are invalid");
        using var transport = new StdioTransport(command, args);
        var errors = new List<string>();
        transport.ErrorOccurred += (_, error) => errors.Add(error.ErrorMessage);

        var connected = await transport.ConnectAsync();
        await Task.Delay(200);

        Assert.False(connected);
        Assert.Contains(
            errors,
            message => message.Contains("ssh config permissions are invalid", StringComparison.Ordinal));
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
