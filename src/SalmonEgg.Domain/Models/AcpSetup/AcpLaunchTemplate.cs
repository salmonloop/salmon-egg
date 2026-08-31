using System;
using System.Collections.Generic;

namespace SalmonEgg.Domain.Models.AcpSetup;

/// <summary>
/// The invariant part of an agent's ACP launch command, plus the parameters the user still owns.
/// A template plus resolved parameter values yields an <see cref="AcpLaunchPlan"/>.
/// </summary>
public sealed class AcpLaunchTemplate
{
    /// <summary>Executable that starts the ACP conversation (for example <c>npx</c>).</summary>
    public required string Command { get; init; }

    /// <summary>Arguments that are always present, in order, before user parameters.</summary>
    public IReadOnlyList<string> FixedArguments { get; init; } = Array.Empty<string>();

    /// <summary>Environment variables that are always present.</summary>
    public IReadOnlyDictionary<string, string> FixedEnvironment { get; init; }
        = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Parameters surfaced to the user, in display order.</summary>
    public IReadOnlyList<AcpSetupParameterDefinition> Parameters { get; init; }
        = Array.Empty<AcpSetupParameterDefinition>();
}
