using System;
using System.Collections.Generic;
using SalmonEgg.Infrastructure.Transport;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.Transport;

public sealed class StdioCommandPreflightTests
{
    [Fact]
    public void BuildMissingCommandError_BareNameMiss_SaysNotFoundOnPath()
    {
        var invocation = new LauncherInvocation(
            "absent-agent",
            [],
            "absent-agent")
        {
            ResolvedToExistingFile = false,
            SearchedDirectories = ["/usr/bin"],
        };

        var error = StdioCommandPreflight.BuildMissingCommandError(invocation, isWindows: false);

        Assert.NotNull(error);
        Assert.Contains("not found on PATH", error, StringComparison.Ordinal);
        Assert.Contains("absent-agent", error, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildMissingCommandError_ExplicitPathMissing_SaysDoesNotExist()
    {
        var invocation = new LauncherInvocation(
            @"C:\tools\absent-agent.exe",
            [],
            @"C:\tools\absent-agent.exe")
        {
            ResolvedToExistingFile = false,
        };

        var error = StdioCommandPreflight.BuildMissingCommandError(invocation, isWindows: true);

        Assert.NotNull(error);
        Assert.Contains("does not exist", error, StringComparison.Ordinal);
        Assert.Contains(@"C:\tools\absent-agent.exe", error, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildMissingCommandError_NonWindowsExplicitPathMissing_SaysDoesNotExist()
    {
        var invocation = new LauncherInvocation(
            "tools/agent",
            [],
            "tools/agent")
        {
            ResolvedToExistingFile = false,
        };

        var error = StdioCommandPreflight.BuildMissingCommandError(invocation, isWindows: false);

        Assert.NotNull(error);
        Assert.Contains("does not exist", error, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildMissingCommandError_ResolvedToExistingFile_ReturnsNull()
    {
        var invocation = new LauncherInvocation(
            "/usr/bin/agent",
            [],
            "/usr/bin/agent")
        {
            ResolvedToExistingFile = true,
        };

        Assert.Null(StdioCommandPreflight.BuildMissingCommandError(invocation, isWindows: false));
    }

    [Fact]
    public void BuildMissingCommandError_BatchLauncherWrappedInCmdExe_StillJudgesUnderlyingCommand()
    {
        // A .cmd launcher is wrapped as cmd.exe /c, so FileName always exists and must not clear the
        // check: the batch file the user configured is what is actually missing.
        var invocation = new LauncherInvocation(
            "cmd.exe",
            ["/c", @"C:\tools\absent-agent.cmd"],
            @"C:\tools\absent-agent.cmd")
        {
            ResolvedToExistingFile = false,
        };

        var error = StdioCommandPreflight.BuildMissingCommandError(invocation, isWindows: true);

        Assert.NotNull(error);
        Assert.Contains("does not exist", error, StringComparison.Ordinal);
        Assert.Contains(@"C:\tools\absent-agent.cmd", error, StringComparison.Ordinal);
    }
}
