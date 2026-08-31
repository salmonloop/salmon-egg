using System;
using System.Collections.Generic;
using System.Linq;
using SalmonEgg.Acp.Mcp;
using SalmonEgg.Domain.Models.Mcp;

namespace SalmonEgg.Application.Services.Mcp;

/// <summary>
/// Projects Domain-owned MCP catalog entries to ACP wire servers and back.
/// </summary>
public static class McpServerCatalogMapper
{
    public static McpServer ToAcpServer(McpServerCatalogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var transport = (entry.Transport ?? string.Empty).Trim().ToLowerInvariant();
        return transport switch
        {
            McpCatalogTransports.Http => new HttpMcpServer(
                entry.Name ?? string.Empty,
                entry.Url ?? string.Empty,
                ToHeaders(entry.Headers))
            {
                Meta = McpServerSnapshots.CloneMeta(entry.Meta)
            },
            McpCatalogTransports.Sse => new SseMcpServer(
                entry.Name ?? string.Empty,
                entry.Url ?? string.Empty,
                ToHeaders(entry.Headers))
            {
                Meta = McpServerSnapshots.CloneMeta(entry.Meta)
            },
            McpCatalogTransports.Stdio => new StdioMcpServer(
                entry.Name ?? string.Empty,
                entry.Command ?? string.Empty,
                entry.Args is null ? null : new List<string>(entry.Args),
                ToEnv(entry.Env))
            {
                Meta = McpServerSnapshots.CloneMeta(entry.Meta)
            },
            _ => throw new NotSupportedException(
                $"Unsupported MCP catalog transport '{entry.Transport}'.")
        };
    }

    public static IReadOnlyList<McpServer> ToAcpServers(
        IEnumerable<McpServerCatalogEntry>? entries,
        bool enabledOnly = false)
    {
        if (entries is null)
        {
            return Array.Empty<McpServer>();
        }

        var servers = new List<McpServer>();
        foreach (var entry in entries)
        {
            if (entry is null)
            {
                continue;
            }

            if (enabledOnly && !entry.Enabled)
            {
                continue;
            }

            servers.Add(ToAcpServer(entry));
        }

        return servers;
    }

    public static McpServerCatalogEntry FromAcpServer(McpServer server, bool enabled = true)
    {
        ArgumentNullException.ThrowIfNull(server);

        return server switch
        {
            StdioMcpServer stdio => new McpServerCatalogEntry
            {
                Transport = McpCatalogTransports.Stdio,
                Name = stdio.Name ?? string.Empty,
                Enabled = enabled,
                Meta = McpServerSnapshots.CloneMeta(stdio.Meta),
                Command = stdio.Command ?? string.Empty,
                Args = stdio.Args is null ? new List<string>() : new List<string>(stdio.Args),
                Env = FromEnv(stdio.Env)
            },
            HttpMcpServer http => new McpServerCatalogEntry
            {
                Transport = McpCatalogTransports.Http,
                Name = http.Name ?? string.Empty,
                Enabled = enabled,
                Meta = McpServerSnapshots.CloneMeta(http.Meta),
                Url = http.Url ?? string.Empty,
                Headers = FromHeaders(http.Headers)
            },
            SseMcpServer sse => new McpServerCatalogEntry
            {
                Transport = McpCatalogTransports.Sse,
                Name = sse.Name ?? string.Empty,
                Enabled = enabled,
                Meta = McpServerSnapshots.CloneMeta(sse.Meta),
                Url = sse.Url ?? string.Empty,
                Headers = FromHeaders(sse.Headers)
            },
            _ => throw new NotSupportedException(
                $"Unsupported MCP server type '{server.GetType().Name}'.")
        };
    }

    private static List<McpEnvVariable>? ToEnv(IEnumerable<McpCatalogNameValue>? values)
    {
        if (values is null)
        {
            return null;
        }

        return values
            .Select(value => new McpEnvVariable(value.Name ?? string.Empty, value.Value ?? string.Empty)
            {
                Meta = McpServerSnapshots.CloneMeta(value.Meta)
            })
            .ToList();
    }

    private static List<McpHttpHeader>? ToHeaders(IEnumerable<McpCatalogNameValue>? values)
    {
        if (values is null)
        {
            return null;
        }

        return values
            .Select(value => new McpHttpHeader(value.Name ?? string.Empty, value.Value ?? string.Empty)
            {
                Meta = McpServerSnapshots.CloneMeta(value.Meta)
            })
            .ToList();
    }

    private static List<McpCatalogNameValue> FromEnv(IEnumerable<McpEnvVariable>? values)
    {
        if (values is null)
        {
            return new List<McpCatalogNameValue>();
        }

        return values
            .Select(value => new McpCatalogNameValue(
                value.Name ?? string.Empty,
                value.Value ?? string.Empty,
                value.Meta))
            .ToList();
    }

    private static List<McpCatalogNameValue> FromHeaders(IEnumerable<McpHttpHeader>? values)
    {
        if (values is null)
        {
            return new List<McpCatalogNameValue>();
        }

        return values
            .Select(value => new McpCatalogNameValue(
                value.Name ?? string.Empty,
                value.Value ?? string.Empty,
                value.Meta))
            .ToList();
    }
}
