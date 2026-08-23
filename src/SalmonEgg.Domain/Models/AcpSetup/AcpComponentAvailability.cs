namespace SalmonEgg.Domain.Models.AcpSetup;

/// <summary>
/// Outcome of probing one ACP component on the local machine.
/// </summary>
public enum AcpComponentAvailability
{
    /// <summary>Not probed yet.</summary>
    Unknown,

    /// <summary>Probing is in flight.</summary>
    Checking,

    /// <summary>Found on the machine and callable.</summary>
    Installed,

    /// <summary>Nothing to install; the agent ships ACP support itself.</summary>
    BuiltIn,

    /// <summary>Not found on the machine.</summary>
    Missing,

    /// <summary>
    /// The probe itself could not run (no process support on this platform, or the probe failed for
    /// a reason unrelated to the component). Distinguished from <see cref="Missing"/> so the wizard
    /// does not claim a component is absent when it simply could not look.
    /// </summary>
    Undetermined
}
