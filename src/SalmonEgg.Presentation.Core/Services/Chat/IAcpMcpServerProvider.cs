using System;
using System.Collections.Generic;
using System.Threading;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.Mcp;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public interface IAcpMcpServerProvider
{
    IReadOnlyList<McpServer> GetMcpServers(
        ServerConfiguration? profile,
        CancellationToken cancellationToken = default);
}

public sealed class AcpProfileMcpServerProvider : IAcpMcpServerProvider
{
    public static AcpProfileMcpServerProvider Instance { get; } = new();

    public IReadOnlyList<McpServer> GetMcpServers(
        ServerConfiguration? profile,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (profile?.McpServers == null)
        {
            return Array.Empty<McpServer>();
        }

        return profile.McpServers;
    }
}
