using System;
using System.Collections.Generic;
using System.Linq;
using SalmonEgg.Domain.Models.AcpSetup;
using SalmonEgg.Domain.Services.AcpSetup;

namespace SalmonEgg.Infrastructure.AcpSetup;

/// <summary>
/// Serves the curated ACP agent catalog. Immutable and I/O-free, so it is safe as a singleton on every
/// platform including WASM.
/// </summary>
public sealed class AcpAgentCatalog : IAcpAgentCatalog
{
    private readonly Dictionary<string, AcpAgentDescriptor> _agentsById;

    public AcpAgentCatalog()
    {
        Agents = AcpAgentCatalogEntries.Create();
        _agentsById = Agents.ToDictionary(agent => agent.Id, StringComparer.Ordinal);
    }

    public IReadOnlyList<AcpAgentDescriptor> Agents { get; }

    public AcpAgentDescriptor? FindAgent(string? agentId)
        => string.IsNullOrWhiteSpace(agentId)
            ? null
            : _agentsById.TryGetValue(agentId, out var agent) ? agent : null;
}
