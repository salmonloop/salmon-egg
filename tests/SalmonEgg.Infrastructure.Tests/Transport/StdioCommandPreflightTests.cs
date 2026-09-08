using System;
using System.Collections.Generic;
using SalmonEgg.Domain.Interfaces.Transport;
using SalmonEgg.Infrastructure.Transport;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.Transport;

public sealed class StdioCommandPreflightTests
{
    [Fact]
    public void BuildMissingCommandError_SearchedOnPath_SaysNotFoundOnPath()
    {
        var invocation = new LauncherInvocation(
            "absent-agent",
            [],
            "absent-agent")
        {
            ResolvedToExistingFile = false,
            SearchedOnPath = true,
            SearchedDirectories = ["/usr/bin"],
        };

        var error = StdioCommandPreflight.BuildMissingCommandError(invocation);

        Assert.NotNull(error);
        Assert.Contains("not found on PATH", error.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains("absent-agent", error.ErrorMessage, StringComparison.Ordinal);
        Assert.Equal(new StdioCommandResolutionFailure("absent-agent", SearchedOnPath: true), error.CommandResolutionFailure);
    }

    [Fact]
    public void BuildMissingCommandError_ExplicitLocation_SaysDoesNotExist()
    {
        var invocation = new LauncherInvocation(
            @"C:\tools\absent-agent.exe",
            [],
            @"C:\tools\absent-agent.exe")
        {
            ResolvedToExistingFile = false,
        };

        var error = StdioCommandPreflight.BuildMissingCommandError(invocation);

        Assert.NotNull(error);
        Assert.Contains("does not exist", error.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains(@"C:\tools\absent-agent.exe", error.ErrorMessage, StringComparison.Ordinal);
        Assert.Equal(new StdioCommandResolutionFailure(@"C:\tools\absent-agent.exe", SearchedOnPath: false), error.CommandResolutionFailure);
    }

    [Fact]
    public void BuildMissingCommandError_UnlaunchableCommand_SaysDoesNotExist()
    {
        // A blank or otherwise unlaunchable command never went near PATH, so the "install the agent"
        // advice would be wrong; the location wording is the safer of the two.
        var invocation = new LauncherInvocation(
            " ",
            [],
            " ")
        {
            ResolvedToExistingFile = false,
            SearchedOnPath = false,
        };

        var error = StdioCommandPreflight.BuildMissingCommandError(invocation);

        Assert.NotNull(error);
        Assert.Contains("does not exist", error.ErrorMessage, StringComparison.Ordinal);
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

        Assert.Null(StdioCommandPreflight.BuildMissingCommandError(invocation));
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

        var error = StdioCommandPreflight.BuildMissingCommandError(invocation);

        Assert.NotNull(error);
        Assert.Contains("does not exist", error.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains(@"C:\tools\absent-agent.cmd", error.ErrorMessage, StringComparison.Ordinal);
        Assert.Equal(new StdioCommandResolutionFailure(@"C:\tools\absent-agent.cmd", SearchedOnPath: false), error.CommandResolutionFailure);
    }
}
