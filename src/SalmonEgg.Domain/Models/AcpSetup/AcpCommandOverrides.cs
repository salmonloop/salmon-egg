using System;
using System.Collections.Generic;

namespace SalmonEgg.Domain.Models.AcpSetup;

/// <summary>
/// User-supplied absolute paths for commands the catalog names only by executable name.
/// </summary>
/// <remarks>
/// The catalog says "probe <c>claude</c>" or "launch <c>npx</c>", which relies on PATH resolution. A
/// desktop process does not inherit the PATH a user configured in their shell profile, so version
/// managers and per-user bin directories are invisible to it and every component reports as missing.
/// The wizard cannot fix that PATH, but the user knows where the executable is, so they can name it.
///
/// Overrides are keyed by the command name the catalog uses rather than by component, because one
/// command backs several roles: <c>npx</c> is both the adapter's detection launcher and the launch
/// plan's executable. Keying by name means a single answer from the user applies everywhere that
/// command appears, so detection and launch cannot disagree — which is the failure mode this exists to
/// prevent, since an override honoured only during detection produces a profile that verifies and then
/// fails to start.
/// </remarks>
public sealed class AcpCommandOverrides
{
    /// <summary>Empty set; every command resolves to the name the catalog declared.</summary>
    public static readonly AcpCommandOverrides Empty = new(new Dictionary<string, string>(StringComparer.Ordinal));

    private readonly IReadOnlyDictionary<string, string> _overrides;

    private AcpCommandOverrides(IReadOnlyDictionary<string, string> overrides)
    {
        _overrides = overrides;
    }

    /// <summary>
    /// Builds an override set, ignoring blank entries so an emptied input box clears its override
    /// rather than mapping a command to nothing.
    /// </summary>
    public static AcpCommandOverrides Create(IReadOnlyDictionary<string, string>? overrides)
    {
        if (overrides is null || overrides.Count == 0)
        {
            return Empty;
        }

        var resolved = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (command, path) in overrides)
        {
            if (string.IsNullOrWhiteSpace(command) || string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            resolved[command.Trim()] = path.Trim();
        }

        return resolved.Count == 0 ? Empty : new AcpCommandOverrides(resolved);
    }

    public bool IsEmpty => _overrides.Count == 0;

    /// <summary>
    /// Returns the user's path for <paramref name="command"/>, or <paramref name="command"/> unchanged
    /// when they supplied none.
    /// </summary>
    public string Resolve(string command)
        => string.IsNullOrWhiteSpace(command)
            ? command
            : _overrides.TryGetValue(command, out var path) ? path : command;
}
