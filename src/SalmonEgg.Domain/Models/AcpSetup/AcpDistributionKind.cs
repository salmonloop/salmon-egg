namespace SalmonEgg.Domain.Models.AcpSetup;

/// <summary>
/// How an ACP component (agent runtime or ACP adapter) reaches the local machine.
/// Mirrors the ACP registry <c>distribution</c> shapes the wizard can act on.
/// </summary>
public enum AcpDistributionKind
{
    /// <summary>Shipped with the agent itself; nothing to install.</summary>
    BuiltIn,

    /// <summary>Node package installed through npm; its declared executable may be launched directly.</summary>
    Npx,

    /// <summary>Python package executed through <c>uvx</c>.</summary>
    Uvx,

    /// <summary>Prebuilt binary the user installs out of band.</summary>
    Binary
}
