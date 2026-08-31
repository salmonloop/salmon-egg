using System.Collections.Generic;

namespace SalmonEgg.Domain.Models.Mcp;

/// <summary>
/// App-local name/value pair used by MCP catalog env vars and headers.
/// </summary>
public sealed class McpCatalogNameValue
{
    public McpCatalogNameValue()
    {
    }

    public McpCatalogNameValue(string name, string value, Dictionary<string, object?>? meta = null)
    {
        Name = name;
        Value = value;
        Meta = McpCatalogSnapshots.CloneMeta(meta);
    }

    public string Name { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public Dictionary<string, object?>? Meta { get; set; }

    public McpCatalogNameValue Clone()
        => new(Name, Value, Meta);
}
