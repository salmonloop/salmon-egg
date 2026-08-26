using System;
using System.Collections.Generic;

namespace SalmonEgg.Domain.Models.AcpSetup;

/// <summary>
/// The package-manager commands to try, in order, for one component.
/// </summary>
/// <remarks>
/// More than one is needed because a derived manager path is a preference rather than a fact. Deriving
/// <c>npm</c> from a user-named <c>npx</c> is right whenever the two are siblings — which is how npm and
/// uv install them — but a launcher can be a lone shim with no manager beside it. Without a fallback that
/// case would turn a query PATH could still have answered into a hard "manager missing", which reads to
/// the user as a component they must install.
///
/// Ordering carries the intent: the toolchain the user named is asked first, and the bare name is only a
/// last resort. A command the user supplied explicitly has no fallback at all, since falling back from it
/// would answer about a toolchain they did not choose.
/// </remarks>
public sealed class AcpPackageManagerCandidates
{
    private readonly string[] _commands;

    private AcpPackageManagerCandidates(string[] commands)
    {
        _commands = commands;
    }

    /// <summary>The commands to try, most preferred first.</summary>
    public IReadOnlyList<string> Commands => _commands;

    /// <summary>The command tried first; what diagnostics should name.</summary>
    public string Preferred => _commands.Length > 0 ? _commands[0] : string.Empty;

    /// <summary>Exactly one command, with no fallback.</summary>
    public static AcpPackageManagerCandidates Exact(string command)
        => new(new[] { command ?? string.Empty });

    /// <summary>
    /// A derived <paramref name="preferred"/> command, falling back to <paramref name="fallback"/> when
    /// the derivation does not exist on this machine.
    /// </summary>
    public static AcpPackageManagerCandidates PreferredWithFallback(string preferred, string fallback)
        => string.Equals(preferred, fallback, StringComparison.Ordinal)
            ? Exact(preferred)
            : new(new[] { preferred, fallback });
}
