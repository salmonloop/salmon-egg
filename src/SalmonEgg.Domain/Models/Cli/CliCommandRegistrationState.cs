namespace SalmonEgg.Domain.Models.Cli;

/// <summary>
/// Whether the <c>salmon-egg</c> command is usable from a shell on this machine.
/// </summary>
/// <remarks>
/// Every installer registers the command, each through its own mechanism, so the useful question is not
/// "did this build ship a CLI" (it always does) but "does typing the name in a shell reach the one this app
/// was installed with". The states below are the distinct answers a user can act on differently.
/// </remarks>
public enum CliCommandRegistrationState
{
    /// <summary>The platform has neither a PATH nor a process host, so the question does not apply.</summary>
    Unsupported,

    /// <summary>Nothing on PATH answers to the command name.</summary>
    NotRegistered,

    /// <summary>The command resolves and reports the same version as this app.</summary>
    Registered,

    /// <summary>
    /// The command resolves but reports a different version, so PATH is reaching some other installation.
    /// </summary>
    VersionMismatch,

    /// <summary>
    /// The command resolves but would not say which version it is: it failed to start, timed out, or wrote
    /// something unrecognizable. Distinct from <see cref="NotRegistered"/> because the file is there, and
    /// from <see cref="VersionMismatch"/> because nothing is known about what it is.
    /// </summary>
    Unreadable,
}
