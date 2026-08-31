using System;
using System.Collections.Generic;

namespace SalmonEgg.Domain.Models.AcpSetup;

/// <summary>
/// A fully resolved ACP launch command: template plus the user's parameter values. This is what the
/// wizard tests, and what it persists into a <see cref="ServerConfiguration"/> on save.
/// </summary>
public sealed class AcpLaunchPlan
{
    public required string Command { get; init; }

    public required IReadOnlyList<string> Arguments { get; init; }

    public IReadOnlyDictionary<string, string> Environment { get; init; }
        = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Single-line rendering used for review surfaces and diagnostics.</summary>
    public string CommandLineDisplay
        => Arguments.Count == 0
            ? Command
            : Command + " " + StdioCommandLine.FormatArgumentsText(Arguments);
}
