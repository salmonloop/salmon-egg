using System;
using System.Collections.Generic;
using System.CommandLine;
using SalmonEgg.Infrastructure.Storage;

namespace SalmonEgg.Cli.Hosting;

/// <summary>
/// Owns the opt-in that allows the CLI to store credentials without platform protection.
/// </summary>
/// <remarks>
/// The CLI defaults to <see cref="SecureStorageDowngradePolicy.FailClosed"/>. A scripted invocation
/// cannot react to a warning stream, so silently writing credentials to a plaintext file would leave
/// unprotected secrets on machines nobody inspects. The downgrade therefore has to be requested.
///
/// The policy is needed before the composition root exists, which is earlier than the command tree can
/// be parsed (the tree needs handlers, and the handlers come from the container). So the value is read
/// twice: once as a bootstrap candidate from the raw arguments, and once authoritatively from the parse
/// result. <see cref="Name"/> is the single owner of the token in both reads, and
/// <see cref="MatchesParsedValue"/> exists so a bootstrap read that disagrees with the parser aborts the
/// invocation instead of running a command under a policy the user did not ask for.
/// </remarks>
internal static class CliSecureStorageOption
{
    /// <summary>
    /// The public option token.
    /// </summary>
    public const string Name = "--allow-insecure-storage";

    // Windows is deliberately not mentioned as a case where this changes behavior: DPAPI needs no keyring
    // daemon and is always available, so there is no downgrade to allow there and the flag is inert.
    private const string Description =
        "Allow credentials to be stored unprotected when the platform secret store is unavailable " +
        "(Linux Secret Service or macOS Keychain). Without this flag, such credential writes fail " +
        "instead of downgrading to plaintext. No effect on Windows, where DPAPI is always available.";

    /// <summary>
    /// Creates the option for the root command. Recursive so every subcommand accepts it.
    /// </summary>
    public static Option<bool> Create() => new(Name)
    {
        Description = Description,
        Recursive = true
    };

    /// <summary>
    /// Reads the bootstrap candidate policy from raw process arguments.
    /// </summary>
    /// <remarks>
    /// Only exact tokens count. A value that merely looks like the flag (for example
    /// <c>--name --allow-insecure-storage</c>) is not treated as the flag here, and any disagreement with
    /// the parser is caught later by <see cref="MatchesParsedValue"/>.
    /// </remarks>
    public static SecureStorageDowngradePolicy ResolveBootstrapPolicy(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        for (var index = 0; index < args.Count; index++)
        {
            var argument = args[index];

            // Everything after the "--" separator is positional input for the command, never an option.
            if (string.Equals(argument, "--", StringComparison.Ordinal))
            {
                break;
            }

            if (string.Equals(argument, Name, StringComparison.Ordinal) ||
                argument.StartsWith(Name + "=", StringComparison.Ordinal) ||
                argument.StartsWith(Name + ":", StringComparison.Ordinal))
            {
                return SecureStorageDowngradePolicy.AllowPlaintextDowngrade;
            }
        }

        return SecureStorageDowngradePolicy.FailClosed;
    }

    /// <summary>
    /// Reports whether the bootstrap policy matches what the parser resolved.
    /// </summary>
    public static bool MatchesParsedValue(SecureStorageDowngradePolicy bootstrapPolicy, bool parsedValue)
        => bootstrapPolicy == ToPolicy(parsedValue);

    /// <summary>
    /// Maps the parsed flag value to a downgrade policy.
    /// </summary>
    public static SecureStorageDowngradePolicy ToPolicy(bool allowInsecureStorage)
        => allowInsecureStorage
            ? SecureStorageDowngradePolicy.AllowPlaintextDowngrade
            : SecureStorageDowngradePolicy.FailClosed;
}
