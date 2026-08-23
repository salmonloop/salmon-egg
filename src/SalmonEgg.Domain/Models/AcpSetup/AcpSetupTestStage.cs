namespace SalmonEgg.Domain.Models.AcpSetup;

/// <summary>
/// The link in the launch chain a configuration test reached. Reported on failure so the user learns
/// which part broke instead of a single opaque "test failed".
/// </summary>
public enum AcpSetupTestStage
{
    /// <summary>Launch plan is still incomplete or malformed; nothing was started.</summary>
    Validation,

    /// <summary>Resolving the launch command to an executable on this machine.</summary>
    CommandResolution,

    /// <summary>Starting the adapter process.</summary>
    AdapterStartup,

    /// <summary>ACP <c>initialize</c> handshake over the transport.</summary>
    Handshake,

    /// <summary>Handshake succeeded and the negotiated capabilities were accepted.</summary>
    Completed
}
