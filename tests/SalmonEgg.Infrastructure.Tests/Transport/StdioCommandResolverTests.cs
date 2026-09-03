using System;
using System.IO;
using SalmonEgg.Infrastructure.Transport;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.Transport;

public sealed class StdioCommandResolverTests
{
    [Fact]
    public void TryResolve_WindowsBareNameHitOnPath_ReportsFound()
    {
        var commandDirectory = CreateCommandDirectory();
        var commandPath = Path.Combine(commandDirectory, "npm.cmd");
        File.WriteAllText(commandPath, "@echo off");

        try
        {
            var resolution = StdioCommandResolver.TryResolve(
                "npm",
                isWindows: true,
                currentDirectory: Path.GetTempPath(),
                pathEnvironment: commandDirectory,
                pathExtensions: ".com;.exe;.cmd");

            Assert.Equal(commandPath, resolution.Command);
            Assert.True(resolution.ResolvedToExistingFile);
            Assert.Equal([Path.GetTempPath(), commandDirectory], resolution.SearchedDirectories);
        }
        finally
        {
            Directory.Delete(commandDirectory, recursive: true);
        }
    }

    [Fact]
    public void TryResolve_WindowsBareNameMiss_ReportsNotFoundWithTrail()
    {
        var first = CreateCommandDirectory();
        var second = CreateCommandDirectory();

        try
        {
            var resolution = StdioCommandResolver.TryResolve(
                "absent-agent",
                isWindows: true,
                currentDirectory: Path.GetTempPath(),
                pathEnvironment: $"{first};{second}",
                pathExtensions: ".EXE;.CMD");

            Assert.Equal("absent-agent", resolution.Command);
            Assert.False(resolution.ResolvedToExistingFile);
            // The trail covers the current directory and every PATH entry, so a log line can say
            // where the name was looked for.
            Assert.Equal([Path.GetTempPath(), first, second], resolution.SearchedDirectories);
        }
        finally
        {
            Directory.Delete(first, recursive: true);
            Directory.Delete(second, recursive: true);
        }
    }

    [Fact]
    public void TryResolve_WindowsPathextOrdering_DotExeBeforeDotCmd()
    {
        var commandDirectory = CreateCommandDirectory();
        // Both extensions exist; PATHEXT order must decide, not filesystem order. File names are
        // written in the extension's own case: on a case-sensitive test filesystem that is the only
        // way to exercise resolution as Windows's case-insensitive File.Exists would see it.
        File.WriteAllText(Path.Combine(commandDirectory, "agent.EXE"), "exe");
        File.WriteAllText(Path.Combine(commandDirectory, "agent.CMD"), "@echo off");

        try
        {
            var resolution = StdioCommandResolver.TryResolve(
                "agent",
                isWindows: true,
                currentDirectory: Path.GetTempPath(),
                pathEnvironment: commandDirectory,
                pathExtensions: ".EXE;.CMD");

            Assert.Equal(Path.Combine(commandDirectory, "agent.EXE"), resolution.Command);
            Assert.True(resolution.ResolvedToExistingFile);
        }
        finally
        {
            Directory.Delete(commandDirectory, recursive: true);
        }
    }

    [Fact]
    public void TryResolve_WindowsBareNameWithExtension_SearchesLiteralNameNotPathext()
    {
        var commandDirectory = CreateCommandDirectory();
        // CreateProcess does not apply PATHEXT to a name that already carries an extension: only the
        // literal file would launch, so only the literal file counts as found. A differently-named
        // file under the same PATHEXT family must not satisfy the check.
        File.WriteAllText(Path.Combine(commandDirectory, "agent.cmd"), "@echo off");

        try
        {
            var resolution = StdioCommandResolver.TryResolve(
                "agent.exe",
                isWindows: true,
                currentDirectory: Path.GetTempPath(),
                pathEnvironment: commandDirectory,
                pathExtensions: ".EXE;.CMD");

            Assert.Equal("agent.exe", resolution.Command);
            Assert.False(resolution.ResolvedToExistingFile);
        }
        finally
        {
            Directory.Delete(commandDirectory, recursive: true);
        }
    }

    [Fact]
    public void TryResolve_WindowsBareNameWithExtension_FoundViaLiteralSearch()
    {
        var commandDirectory = CreateCommandDirectory();
        File.WriteAllText(Path.Combine(commandDirectory, "agent.exe"), "exe");

        try
        {
            var resolution = StdioCommandResolver.TryResolve(
                "agent.exe",
                isWindows: true,
                currentDirectory: Path.GetTempPath(),
                pathEnvironment: commandDirectory,
                pathExtensions: ".EXE;.CMD");

            Assert.Equal("agent.exe", resolution.Command);
            Assert.True(resolution.ResolvedToExistingFile);
        }
        finally
        {
            Directory.Delete(commandDirectory, recursive: true);
        }
    }

    [Fact]
    public void TryResolve_NonWindowsBareName_CommandUnchangedButExistenceReported()
    {
        var commandDirectory = CreateCommandDirectory();
        var commandPath = Path.Combine(commandDirectory, "agent-bin");
        File.WriteAllText(commandPath, "#!/bin/sh\n");

        try
        {
            var resolution = StdioCommandResolver.TryResolve(
                "agent-bin",
                isWindows: false,
                currentDirectory: Path.GetTempPath(),
                pathEnvironment: commandDirectory,
                pathExtensions: null);

            // Non-Windows resolution still leaves bare commands to the OS PATH lookup; the addition
            // is the existence verdict, which the preflight reads.
            Assert.Equal("agent-bin", resolution.Command);
            Assert.True(resolution.ResolvedToExistingFile);
            Assert.Equal([Path.GetTempPath(), commandDirectory], resolution.SearchedDirectories);
        }
        finally
        {
            Directory.Delete(commandDirectory, recursive: true);
        }
    }

    [Fact]
    public void TryResolve_NonWindowsBareNameMiss_ReportsNotFound()
    {
        var resolution = StdioCommandResolver.TryResolve(
            "absent-agent",
            isWindows: false,
            currentDirectory: Path.GetTempPath(),
            pathEnvironment: string.Empty,
            pathExtensions: null);

        Assert.Equal("absent-agent", resolution.Command);
        Assert.False(resolution.ResolvedToExistingFile);
    }

    [Theory]
    [InlineData("/definitely/not/a/real/binary")]
    [InlineData("./absent-agent")]
    [InlineData("tools/agent")]
    public void TryResolve_NonWindowsExplicitPathMissing_ReportsNotFound(string command)
    {
        var resolution = StdioCommandResolver.TryResolve(
            command,
            isWindows: false,
            currentDirectory: Path.GetTempPath(),
            pathEnvironment: null,
            pathExtensions: null);

        Assert.Equal(command, resolution.Command);
        Assert.False(resolution.ResolvedToExistingFile);
    }

    [Fact]
    public void TryResolve_WindowsExplicitPathMissing_ReportsNotFound()
    {
        var resolution = StdioCommandResolver.TryResolve(
            @"tools\agent.exe",
            isWindows: true,
            currentDirectory: Path.GetTempPath(),
            pathEnvironment: null,
            pathExtensions: null);

        Assert.Equal(@"tools\agent.exe", resolution.Command);
        Assert.False(resolution.ResolvedToExistingFile);
    }

    [Fact]
    public void TryResolve_EmptyCommand_ReportsNotFound()
    {
        var resolution = StdioCommandResolver.TryResolve(
            "  ",
            isWindows: true,
            currentDirectory: Path.GetTempPath(),
            pathEnvironment: null,
            pathExtensions: null);

        Assert.Equal("  ", resolution.Command);
        Assert.False(resolution.ResolvedToExistingFile);
    }

    private static string CreateCommandDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "stdio-command-resolver-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
