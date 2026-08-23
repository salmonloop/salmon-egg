using SalmonEgg.Domain.Models.AcpSetup;

namespace SalmonEgg.Infrastructure.AcpSetup;

/// <summary>
/// Factories for the launch parameters agents share, so the catalog declares intent rather than
/// repeating localization keys.
///
/// Credentials are deliberately absent. Launch parameters are persisted in the profile YAML in clear
/// text, and the authoritative credential path (<c>authentication.mode</c> plus secure storage) has two
/// fixed slots bound to that mode — an arbitrary API-key environment variable fits neither. ACP agents
/// are signed in through their own CLI and carry that session into the adapter, so the wizard directs
/// users there instead of collecting secrets it cannot store safely.
/// </summary>
internal static class AcpSetupParameters
{
    /// <summary>Model override passed as an environment variable.</summary>
    internal static AcpSetupParameterDefinition ModelEnvironment(string variableName, string example)
        => new()
        {
            Key = variableName,
            DisplayName = variableName,
            Target = AcpSetupParameterTarget.EnvironmentVariable,
            Description = "AcpSetup_Parameter_Model_Description",
            Example = example,
            IsRequired = false
        };
}
