using System.Collections.Generic;
using System.IO;
using System.Linq;
using SalmonEgg.Domain.Models.Mcp;
using SalmonEgg.Infrastructure.Storage.YamlModels;

namespace SalmonEgg.Infrastructure.Storage;

internal static class McpServerYamlMapper
{
    internal const string StdioTransport = McpCatalogTransports.Stdio;
    internal const string HttpTransport = McpCatalogTransports.Http;
    internal const string SseTransport = McpCatalogTransports.Sse;

    internal static List<McpServerYamlV1> ToYamlServers(IEnumerable<McpServerCatalogEntry>? servers)
    {
        if (servers == null)
        {
            return new List<McpServerYamlV1>();
        }

        var yamlServers = new List<McpServerYamlV1>();
        foreach (var entry in servers)
        {
            var transport = (entry.Transport ?? string.Empty).Trim().ToLowerInvariant();
            switch (transport)
            {
                case StdioTransport:
                    yamlServers.Add(new McpServerYamlV1
                    {
                        Transport = StdioTransport,
                        Name = entry.Name ?? string.Empty,
                        Enabled = entry.Enabled,
                        Meta = McpCatalogSnapshots.CloneMeta(entry.Meta),
                        Command = entry.Command ?? string.Empty,
                        Args = McpCatalogSnapshots.CloneArgs(entry.Args),
                        Env = ToYamlNameValues(entry.Env)
                    });
                    break;
                case HttpTransport:
                    yamlServers.Add(new McpServerYamlV1
                    {
                        Transport = HttpTransport,
                        Name = entry.Name ?? string.Empty,
                        Enabled = entry.Enabled,
                        Meta = McpCatalogSnapshots.CloneMeta(entry.Meta),
                        Url = entry.Url ?? string.Empty,
                        Headers = ToYamlNameValues(entry.Headers)
                    });
                    break;
                case SseTransport:
                    yamlServers.Add(new McpServerYamlV1
                    {
                        Transport = SseTransport,
                        Name = entry.Name ?? string.Empty,
                        Enabled = entry.Enabled,
                        Meta = McpCatalogSnapshots.CloneMeta(entry.Meta),
                        Url = entry.Url ?? string.Empty,
                        Headers = ToYamlNameValues(entry.Headers)
                    });
                    break;
                default:
                    throw new InvalidDataException("MCP server transport must be one of: stdio, http, sse.");
            }
        }

        return yamlServers;
    }

    internal static List<McpServerCatalogEntry> FromYamlServers(IEnumerable<McpServerYamlV1>? yamlServers)
    {
        if (yamlServers == null)
        {
            return new List<McpServerCatalogEntry>();
        }

        var servers = new List<McpServerCatalogEntry>();
        foreach (var yamlServer in yamlServers)
        {
            if (yamlServer is null)
            {
                throw new InvalidDataException("MCP server entry cannot be null.");
            }

            var transport = (yamlServer.Transport ?? string.Empty).Trim().ToLowerInvariant();
            switch (transport)
            {
                case HttpTransport:
                    servers.Add(new McpServerCatalogEntry
                    {
                        Transport = HttpTransport,
                        Name = yamlServer.Name ?? string.Empty,
                        Enabled = yamlServer.Enabled,
                        Meta = McpCatalogSnapshots.CloneMeta(yamlServer.Meta),
                        Url = yamlServer.Url ?? string.Empty,
                        Headers = FromYamlNameValues(yamlServer.Headers)
                    });
                    break;
                case SseTransport:
                    servers.Add(new McpServerCatalogEntry
                    {
                        Transport = SseTransport,
                        Name = yamlServer.Name ?? string.Empty,
                        Enabled = yamlServer.Enabled,
                        Meta = McpCatalogSnapshots.CloneMeta(yamlServer.Meta),
                        Url = yamlServer.Url ?? string.Empty,
                        Headers = FromYamlNameValues(yamlServer.Headers)
                    });
                    break;
                case StdioTransport:
                    servers.Add(new McpServerCatalogEntry
                    {
                        Transport = StdioTransport,
                        Name = yamlServer.Name ?? string.Empty,
                        Enabled = yamlServer.Enabled,
                        Meta = McpCatalogSnapshots.CloneMeta(yamlServer.Meta),
                        Command = yamlServer.Command ?? string.Empty,
                        Args = yamlServer.Args is null ? new List<string>() : new List<string>(yamlServer.Args),
                        Env = FromYamlNameValues(yamlServer.Env)
                    });
                    break;
                default:
                    throw new InvalidDataException("MCP server transport must be one of: stdio, http, sse.");
            }
        }

        return servers;
    }

    private static List<McpNameValueYamlV1> ToYamlNameValues(IEnumerable<McpCatalogNameValue>? values)
    {
        if (values == null)
        {
            return new List<McpNameValueYamlV1>();
        }

        return values
            .Select(value => new McpNameValueYamlV1
            {
                Name = value.Name ?? string.Empty,
                Value = value.Value ?? string.Empty,
                Meta = McpCatalogSnapshots.CloneMeta(value.Meta)
            })
            .ToList();
    }

    private static List<McpCatalogNameValue> FromYamlNameValues(IEnumerable<McpNameValueYamlV1>? values)
    {
        if (values == null)
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
