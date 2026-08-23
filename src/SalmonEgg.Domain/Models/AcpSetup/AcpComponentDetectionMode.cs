namespace SalmonEgg.Domain.Models.AcpSetup;

/// <summary>
/// How a component's presence is established. Declared per component because "is it installed" means
/// different things for a CLI on PATH and for a package the launcher resolves.
/// </summary>
public enum AcpComponentDetectionMode
{
    /// <summary>Nothing to detect; the component ships with the agent.</summary>
    None,

    /// <summary>Probe for an executable on PATH.</summary>
    ExecutableOnPath,

    /// <summary>Query the Node global package list for the declared package.</summary>
    GlobalNodePackage,

    /// <summary>Query the uv tool list for the declared package.</summary>
    GlobalUvTool,

    /// <summary>
    /// Cannot be detected automatically; the wizard shows install documentation and lets the user
    /// confirm manually.
    /// </summary>
    Manual
}
