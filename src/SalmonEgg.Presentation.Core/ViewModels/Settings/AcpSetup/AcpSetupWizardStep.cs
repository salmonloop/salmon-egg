namespace SalmonEgg.Presentation.ViewModels.Settings.AcpSetup;

/// <summary>
/// The wizard's steps, in the order the user walks them. Ordinal order is meaningful: the step machine
/// advances and rewinds by comparing values, so new steps must be inserted at their real position.
/// </summary>
public enum AcpSetupWizardStep
{
    /// <summary>Pick an agent from the catalog, after detection reports what is on the machine.</summary>
    AgentSelection,

    /// <summary>Install or verify the agent runtime and the ACP adapter it needs.</summary>
    ComponentSetup,

    /// <summary>Fill in the launch parameters the chosen adapter declares.</summary>
    Parameters,

    /// <summary>Run the end-to-end connectivity test.</summary>
    Test,

    /// <summary>Name the profile and persist it.</summary>
    Save
}
