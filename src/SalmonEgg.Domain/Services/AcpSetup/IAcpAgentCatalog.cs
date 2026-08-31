using System.Collections.Generic;
using SalmonEgg.Domain.Models.AcpSetup;

namespace SalmonEgg.Domain.Services.AcpSetup;

/// <summary>
/// The set of agents the ACP wizard can configure. Backed by a curated snapshot of the ACP registry
/// so the wizard works offline; the catalog never performs I/O.
/// </summary>
public interface IAcpAgentCatalog
{
    /// <summary>Agents in display order, recommended entries first.</summary>
    IReadOnlyList<AcpAgentDescriptor> Agents { get; }

    /// <summary>Returns the agent with the given id, or null when it is not in the catalog.</summary>
    AcpAgentDescriptor? FindAgent(string? agentId);
}
