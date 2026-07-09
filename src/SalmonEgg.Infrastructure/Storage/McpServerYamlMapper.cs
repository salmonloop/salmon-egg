using System.Collections.Generic;
using System.IO;
using System.Linq;
using SalmonEgg.Acp.Mcp;
using SalmonEgg.Domain.Models.Mcp;
using SalmonEgg.Infrastructure.Storage.YamlModels;

namespace SalmonEgg.Infrastructure.Storage;

internal static class McpServerYamlMapper
{
    internal const string StdioTransport = "stdio";
    internal const string HttpTransport = "http";
    internal const string SseTransport = "sse";

    internal static List<McpServerYamlV1> ToYamlServers(IEnumerable<McpServerCatalogEntry>? servers)
    {
        if (servers == null)
        {
            return new List<McpServerYamlV1>();
        }

        var yamlServers = new List<McpServerYamlV1>();
        foreach (var entry in servers)
        {
            var server = entry.Server;
            switch (server)
            {
                case StdioMcpServer stdio:
                    yamlServers.Add(new McpServerYamlV1
                    {
                        Transport = StdioTransport,
                        Name = stdio.Name ?? string.Empty,
                        Enabled = entry.Enabled,
                        Meta = McpServerJsonConverter.CloneMeta(stdio.Meta),
                        Command = stdio.Command ?? string.Empty,
                        Args = stdio.Args ?? new List<string>(),
                        Env = ToYamlNameValues(stdio.Env)
                    });
                    break;
                case HttpMcpServer http:
                    yamlServers.Add(new McpServerYamlV1
                    {
                        Transport = HttpTransport,
                        Name = http.Name ?? string.Empty,
                        Enabled = entry.Enabled,
                        Meta = McpServerJsonConverter.CloneMeta(http.Meta),
                        Url = http.Url ?? string.Empty,
                        Headers = ToYamlNameValues(http.Headers)
                    });
                    break;
                case SseMcpServer sse:
                    yamlServers.Add(new McpServerYamlV1
                    {
                        Transport = SseTransport,
                        Name = sse.Name ?? string.Empty,
                        Enabled = entry.Enabled,
                        Meta = McpServerJsonConverter.CloneMeta(sse.Meta),
                        Url = sse.Url ?? string.Empty,
                        Headers = ToYamlNameValues(sse.Headers)
                    });
                    break;
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
                    servers.Add(new McpServerCatalogEntry(
                        new HttpMcpServer(
                            yamlServer.Name ?? string.Empty,
                            yamlServer.Url ?? string.Empty,
                            FromYamlHeaders(yamlServer.Headers))
                        {
                            Meta = McpServerJsonConverter.CloneMeta(yamlServer.Meta)
                        },
                        yamlServer.Enabled));
                    break;
                case SseTransport:
                    servers.Add(new McpServerCatalogEntry(
                        new SseMcpServer(
                            yamlServer.Name ?? string.Empty,
                            yamlServer.Url ?? string.Empty,
                            FromYamlHeaders(yamlServer.Headers))
                        {
                            Meta = McpServerJsonConverter.CloneMeta(yamlServer.Meta)
                        },
                        yamlServer.Enabled));
                    break;
                case StdioTransport:
                    servers.Add(new McpServerCatalogEntry(
                        new StdioMcpServer(
                            yamlServer.Name ?? string.Empty,
                            yamlServer.Command ?? string.Empty,
                            yamlServer.Args ?? new List<string>(),
                            FromYamlEnv(yamlServer.Env))
                        {
                            Meta = McpServerJsonConverter.CloneMeta(yamlServer.Meta)
                        },
                        yamlServer.Enabled));
                    break;
                default:
                    throw new InvalidDataException("MCP server transport must be one of: stdio, http, sse.");
            }
        }

        return servers;
    }

    private static List<McpNameValueYamlV1> ToYamlNameValues(IEnumerable<McpEnvVariable>? values)
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
                Meta = McpServerJsonConverter.CloneMeta(value.Meta)
            })
            .ToList();
    }

    private static List<McpNameValueYamlV1> ToYamlNameValues(IEnumerable<McpHttpHeader>? values)
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
                Meta = McpServerJsonConverter.CloneMeta(value.Meta)
            })
            .ToList();
    }

    private static List<McpEnvVariable> FromYamlEnv(IEnumerable<McpNameValueYamlV1>? values)
    {
        if (values == null)
        {
            return new List<McpEnvVariable>();
        }

        return values
            .Select(value => new McpEnvVariable(value.Name ?? string.Empty, value.Value ?? string.Empty)
            {
                Meta = McpServerJsonConverter.CloneMeta(value.Meta)
            })
            .ToList();
    }

    private static List<McpHttpHeader> FromYamlHeaders(IEnumerable<McpNameValueYamlV1>? values)
    {
        if (values == null)
        {
            return new List<McpHttpHeader>();
        }

        return values
            .Select(value => new McpHttpHeader(value.Name ?? string.Empty, value.Value ?? string.Empty)
            {
                Meta = McpServerJsonConverter.CloneMeta(value.Meta)
            })
            .ToList();
    }
}
