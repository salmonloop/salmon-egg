using System.Collections.Generic;

namespace SalmonEgg.Domain.Models.Mcp;

/// <summary>
/// App-local MCP catalog entry.
/// Transport and fields are Domain-owned open wire values; ACP DTOs are projected at host boundaries.
/// </summary>
public sealed class McpServerCatalogEntry
{
    public McpServerCatalogEntry()
    {
    }

    public string Transport { get; set; } = McpCatalogTransports.Stdio;

    public string Name { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public Dictionary<string, object?>? Meta { get; set; }

    public string Command { get; set; } = string.Empty;

    public List<string> Args { get; set; } = new();

    public List<McpCatalogNameValue> Env { get; set; } = new();

    public string Url { get; set; } = string.Empty;

    public List<McpCatalogNameValue> Headers { get; set; } = new();

    public McpServerCatalogEntry Clone()
        => new()
        {
            Transport = Transport,
            Name = Name,
            Enabled = Enabled,
            Meta = McpCatalogSnapshots.CloneMeta(Meta),
            Command = Command,
            Args = McpCatalogSnapshots.CloneArgs(Args),
            Env = McpCatalogSnapshots.CloneNameValues(Env),
            Url = Url,
            Headers = McpCatalogSnapshots.CloneNameValues(Headers)
        };
}
