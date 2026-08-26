using System;
using System.Collections.Generic;

namespace SalmonEgg.Domain.Models.AcpSetup;

/// <summary>
/// How to ask one shell to run a command such that the shell's own startup files have been applied.
/// </summary>
/// <remarks>
/// The app captures the user's real environment by having their shell run the app's own
/// environment-printing mode. Which arguments achieve that differs per shell family, and getting it wrong
/// fails in two distinct ways: the shell refuses the flags outright, or it accepts them and silently
/// applies fewer startup files than the user's terminal does — the second being worse, since it yields a
/// plausible environment that is missing exactly the toolchain the probe was looking for.
///
/// This type decides the argument list and performs no IO. Running the shell, enforcing a timeout, and
/// extracting the payload belong to the platform layer.
/// </remarks>
public sealed class AcpShellInvocation
{
    /// <summary>
    /// Set for the child so a user's startup files can skip work that is pointless or harmful during a
    /// capture.
    /// </summary>
    /// <remarks>
    /// Startup files routinely do things that make a capture hang or fail: attaching to tmux, starting a
    /// pager, prompting for input. VS Code and Zed both ship an equivalent flag
    /// (<c>VSCODE_RESOLVING_ENVIRONMENT</c>, and a Zed request for the same) precisely because users need
    /// a way to guard those blocks. Publishing one here means a user who hits the problem has a fix that
    /// does not involve giving up their shell configuration.
    /// </remarks>
    public const string GuardVariableName = "SALMONEGG_RESOLVING_ENVIRONMENT";

    private AcpShellInvocation(AcpShellKind kind, IReadOnlyList<string> arguments)
    {
        Kind = kind;
        Arguments = arguments;
    }

    public AcpShellKind Kind { get; }

    /// <summary>The complete argument list, ending with the command to run.</summary>
    public IReadOnlyList<string> Arguments { get; }

    /// <summary>
    /// Builds the invocation that runs <paramref name="command"/> through the shell at
    /// <paramref name="shellPath"/>.
    /// </summary>
    /// <remarks>
    /// Login <em>and</em> interactive is deliberate. A login shell alone reads only profile files, and the
    /// most common toolchain setup on Linux puts its PATH mutation in an interactive-only file: nvm's own
    /// installer appends to <c>~/.bashrc</c>, which Debian's stock <c>~/.bashrc</c> guards with an
    /// interactivity check that returns early. Verified on this machine: <c>bash -l -c</c> cannot find
    /// npm, while <c>bash -l -i -c</c> resolves it under ~/.nvm. VS Code and Zed both pay the same cost —
    /// interactive shells are slower and noisier — for the same reason.
    /// </remarks>
    public static AcpShellInvocation Create(string shellPath, string command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var kind = ClassifyShell(shellPath);
        return new AcpShellInvocation(kind, BuildArguments(kind, command));
    }

    private static IReadOnlyList<string> BuildArguments(AcpShellKind kind, string command)
        => kind switch
        {
            // csh and tcsh reject -l together with -c, so interactivity is the only lever available.
            AcpShellKind.Csh => new[] { "-ic", command },

            // fish applies its config file, but asdf, direnv, and friends attach to the fish_prompt event
            // rather than to config.fish — so a capture that never prompts never sees their PATH.
            AcpShellKind.Fish => new[] { "-l", "-i", "-c", "emit fish_prompt; " + command },

            // nushell refuses a login shell that is not interactive, and refuses -i with -c.
            AcpShellKind.Nushell => new[] { "-l", "-c", command },

            // PowerShell profiles are the equivalent of rc files here, so -Login runs them. Word flags,
            // and -Command must come last.
            AcpShellKind.PowerShell => new[] { "-Login", "-Command", command },

            _ => new[] { "-l", "-i", "-c", command }
        };

    /// <summary>
    /// Identifies the shell family from its executable name.
    /// </summary>
    /// <remarks>
    /// Matched on the file name rather than the full path, since the same shell lives in different places
    /// per platform and package manager (<c>/bin/zsh</c>, <c>/opt/homebrew/bin/fish</c>). An unknown name
    /// is treated as POSIX: that is what an unrecognized shell most likely is, and the alternative —
    /// refusing to capture — gives up the user's environment over a name this code has not heard of.
    /// </remarks>
    private static AcpShellKind ClassifyShell(string? shellPath)
    {
        if (string.IsNullOrWhiteSpace(shellPath))
        {
            return AcpShellKind.Posix;
        }

        var name = ExtractFileName(shellPath.Trim());

        // Windows shells carry an extension the family name does not include.
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        return name.ToLowerInvariant() switch
        {
            "fish" => AcpShellKind.Fish,
            "csh" or "tcsh" => AcpShellKind.Csh,
            "nu" or "nushell" => AcpShellKind.Nushell,
            "pwsh" or "powershell" or "pwsh-preview" => AcpShellKind.PowerShell,
            _ => AcpShellKind.Posix
        };
    }

    /// <summary>
    /// Returns the last path segment. Split by hand so the domain stays free of filesystem types; the two
    /// separators below are the only ones the app's platforms use.
    /// </summary>
    private static string ExtractFileName(string path)
    {
        var separator = path.LastIndexOfAny(new[] { '/', '\\' });
        return separator < 0 ? path : path[(separator + 1)..];
    }
}
