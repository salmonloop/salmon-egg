using System;
using SalmonEgg.Domain.Models.AcpSetup;
using Xunit;

namespace SalmonEgg.Domain.Tests.Models.AcpSetup;

/// <summary>
/// Guards the per-shell invocation rules used to capture the user's real environment.
/// </summary>
/// <remarks>
/// Getting these wrong fails in two ways, and the quiet one is worse: the shell either refuses the flags,
/// or accepts them and applies fewer startup files than the user's terminal does — yielding a plausible
/// environment missing exactly the toolchain the probe was looking for.
/// </remarks>
public sealed class AcpShellInvocationTests
{
    private const string Command = "print-env";

    /// <summary>
    /// Login and interactive together. Interactive is not optional: nvm's installer appends to
    /// <c>~/.bashrc</c>, which Debian's stock rc file guards with an interactivity check — verified on a
    /// real nvm install, where <c>bash -l -c</c> cannot find npm but <c>bash -l -i -c</c> can.
    /// </summary>
    [Theory]
    [InlineData("/bin/bash")]
    [InlineData("/bin/sh")]
    [InlineData("/usr/bin/zsh")]
    [InlineData("/bin/dash")]
    [InlineData("/usr/bin/ksh")]
    public void Create_ForPosixShell_ShouldRunLoginAndInteractive(string shellPath)
    {
        var invocation = AcpShellInvocation.Create(shellPath, Command);

        Assert.Equal(AcpShellKind.Posix, invocation.Kind);
        Assert.Equal(new[] { "-l", "-i", "-c", Command }, invocation.Arguments);
    }

    /// <summary>
    /// An unrecognized shell is driven as POSIX rather than refused: giving up the user's environment over
    /// an unfamiliar name is the worse failure.
    /// </summary>
    [Theory]
    [InlineData("/opt/custom/myshell")]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_ForUnknownShell_ShouldFallBackToPosix(string shellPath)
        => Assert.Equal(AcpShellKind.Posix, AcpShellInvocation.Create(shellPath, Command).Kind);

    /// <summary>
    /// fish must be told to emit its prompt event, because asdf and direnv hook that event rather than
    /// config.fish — so a capture that never prompts never sees the PATH they set.
    /// </summary>
    [Fact]
    public void Create_ForFish_ShouldEmitThePromptEvent()
    {
        var invocation = AcpShellInvocation.Create("/opt/homebrew/bin/fish", Command);

        Assert.Equal(AcpShellKind.Fish, invocation.Kind);
        Assert.Equal(new[] { "-l", "-i", "-c", "emit fish_prompt; " + Command }, invocation.Arguments);
    }

    /// <summary>csh and tcsh reject <c>-l</c> combined with <c>-c</c>.</summary>
    [Theory]
    [InlineData("/bin/csh")]
    [InlineData("/bin/tcsh")]
    public void Create_ForCsh_ShouldNotPassLogin(string shellPath)
    {
        var invocation = AcpShellInvocation.Create(shellPath, Command);

        Assert.Equal(AcpShellKind.Csh, invocation.Kind);
        Assert.Equal(new[] { "-ic", Command }, invocation.Arguments);
        Assert.DoesNotContain("-l", invocation.Arguments);
    }

    /// <summary>nushell refuses a non-interactive login shell, and refuses <c>-i</c> with <c>-c</c>.</summary>
    [Theory]
    [InlineData("/usr/bin/nu")]
    [InlineData("/usr/local/bin/nushell")]
    public void Create_ForNushell_ShouldNotPassInteractive(string shellPath)
    {
        var invocation = AcpShellInvocation.Create(shellPath, Command);

        Assert.Equal(AcpShellKind.Nushell, invocation.Kind);
        Assert.Equal(new[] { "-l", "-c", Command }, invocation.Arguments);
        Assert.DoesNotContain("-i", invocation.Arguments);
    }

    /// <summary>PowerShell takes word flags, and the command must come last.</summary>
    [Theory]
    [InlineData("/usr/bin/pwsh")]
    [InlineData(@"C:\Program Files\PowerShell\7\pwsh.exe")]
    [InlineData(@"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe")]
    public void Create_ForPowerShell_ShouldUseWordFlags(string shellPath)
    {
        var invocation = AcpShellInvocation.Create(shellPath, Command);

        Assert.Equal(AcpShellKind.PowerShell, invocation.Kind);
        Assert.Equal(new[] { "-Login", "-Command", Command }, invocation.Arguments);
    }

    /// <summary>
    /// The command is always the final argument, so a shell reads it as the thing to run rather than as a
    /// flag operand.
    /// </summary>
    [Theory]
    [InlineData("/bin/bash")]
    [InlineData("/bin/tcsh")]
    [InlineData("/usr/bin/nu")]
    [InlineData("/usr/bin/pwsh")]
    public void Create_ForEveryShell_ShouldPlaceTheCommandLast(string shellPath)
    {
        var arguments = AcpShellInvocation.Create(shellPath, Command).Arguments;

        Assert.Contains(Command, arguments[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void Create_WithNullCommand_ShouldThrow()
        => Assert.Throws<ArgumentNullException>(() => AcpShellInvocation.Create("/bin/bash", null!));
}
