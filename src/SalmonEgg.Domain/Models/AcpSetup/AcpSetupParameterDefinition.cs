using System;
using System.Collections.Generic;

namespace SalmonEgg.Domain.Models.AcpSetup;

/// <summary>
/// Where a wizard parameter lands in the launch plan it produces.
/// </summary>
public enum AcpSetupParameterTarget
{
    /// <summary>Appended to the launch arguments.</summary>
    Argument,

    /// <summary>Exported as an environment variable on the agent process.</summary>
    EnvironmentVariable
}

/// <summary>
/// One user-facing launch parameter declared by an agent's launch template. The wizard renders
/// these generically, so adding an agent never requires new form code.
/// </summary>
public sealed class AcpSetupParameterDefinition
{
    /// <summary>
    /// Argument flag (for example <c>--model</c>) or environment variable name, depending on
    /// <see cref="Target"/>. Also the stable key used to correlate user input with the definition.
    /// </summary>
    public required string Key { get; init; }

    public required string DisplayName { get; init; }

    public AcpSetupParameterTarget Target { get; init; } = AcpSetupParameterTarget.Argument;

    /// <summary>Prefilled when the value is known up front; may be empty.</summary>
    public string DefaultValue { get; init; } = string.Empty;

    /// <summary>Shown as placeholder guidance when no value has been entered.</summary>
    public string Example { get; init; } = string.Empty;

    /// <summary>Explains the parameter to a user who has never configured ACP.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>When true, the wizard blocks test and save until a value is present.</summary>
    public bool IsRequired { get; init; }

    /// <summary>
    /// Marks the value as a credential.
    /// </summary>
    /// <remarks>
    /// Launch plans are persisted in profile YAML in clear text, so a secret parameter cannot be carried
    /// there. The authoritative credential path is <c>authentication.mode</c> plus secure storage, which
    /// has fixed slots an arbitrary parameter does not map onto. Until a parameter can be routed into
    /// secure storage, the catalog declares no secret parameters and the launch-plan builder rejects any
    /// that appear, so a future catalog edit fails loudly instead of leaking a credential to disk.
    /// </remarks>
    public bool IsSecret { get; init; }

    /// <summary>
    /// Closed set of accepted values. Empty means free-form text.
    /// </summary>
    public IReadOnlyList<string> AllowedValues { get; init; } = Array.Empty<string>();
}
