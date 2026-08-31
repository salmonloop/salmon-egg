namespace SalmonEgg.Domain.Models.Mcp;

/// <summary>
/// Canonical transport labels for the app-local MCP catalog.
/// Values match ACP wire transport names for known kinds; unknown labels stay open.
/// </summary>
public static class McpCatalogTransports
{
    public const string Stdio = "stdio";
    public const string Http = "http";
    public const string Sse = "sse";
}
