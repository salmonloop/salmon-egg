using System;
using System.Collections.Generic;

namespace SalmonEgg.Acp.Client;

/// <summary>Explicit host policy for protocol versions which are not enabled by default.</summary>
public sealed class AcpClientOptions
{
    /// <summary>
    /// Experimental major versions this host explicitly permits. Empty by default. This is not a
    /// version offer: InitializeParams.ProtocolVersion must still request the chosen version and
    /// the Agent must negotiate it. Unknown and incompletely supported versions remain rejected.
    /// </summary>
    public IReadOnlyList<int> ExperimentalProtocolVersions { get; init; } = Array.Empty<int>();
}
