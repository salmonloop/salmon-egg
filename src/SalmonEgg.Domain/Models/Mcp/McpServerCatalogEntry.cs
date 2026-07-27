using System;
using SalmonEgg.Acp.Mcp;

namespace SalmonEgg.Domain.Models.Mcp;

/// <summary>
/// Local MCP catalog entry. The server is the ACP protocol payload; Enabled is app-local state.
/// </summary>
public sealed class McpServerCatalogEntry
{
    public McpServerCatalogEntry()
    {
    }

    public McpServerCatalogEntry(McpServer server, bool enabled = true)
    {
        Server = McpServerSnapshots.CloneServer(server ?? throw new ArgumentNullException(nameof(server)));
        Enabled = enabled;
    }

    public McpServer Server { get; set; } = new StdioMcpServer();

    public bool Enabled { get; set; } = true;

    public string Name => Server.Name;

    public McpServerCatalogEntry Clone()
        => new(Server, Enabled);

    public static implicit operator McpServerCatalogEntry(McpServer server)
        => new(server);
}
