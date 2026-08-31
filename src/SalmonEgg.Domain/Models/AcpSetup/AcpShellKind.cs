namespace SalmonEgg.Domain.Models.AcpSetup;

/// <summary>
/// The shell families whose environment the app can capture. Distinguished only where the invocation has
/// to differ; every shell that accepts <c>-l -i -c</c> is <see cref="Posix"/>.
/// </summary>
public enum AcpShellKind
{
    /// <summary>Any shell driven with <c>-l -i -c</c>: sh, bash, zsh, dash, ksh.</summary>
    Posix,

    /// <summary>
    /// fish. Needs an explicit prompt event, because tools that mutate PATH hook the prompt rather than
    /// the config file.
    /// </summary>
    Fish,

    /// <summary>csh and tcsh. Reject <c>-l</c> combined with <c>-c</c>; take <c>-ic</c> instead.</summary>
    Csh,

    /// <summary>nushell. Rejects a non-interactive login shell; driven with <c>-l -c</c>.</summary>
    Nushell,

    /// <summary>PowerShell and pwsh. Uses word flags rather than single letters.</summary>
    PowerShell
}
