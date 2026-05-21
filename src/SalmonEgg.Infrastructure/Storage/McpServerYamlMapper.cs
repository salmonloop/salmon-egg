using System.Collections.Generic;
using System.IO;
using System.Linq;
using SalmonEgg.Domain.Models.Mcp;
using SalmonEgg.Infrastructure.Storage.YamlModels;

namespace SalmonEgg.Infrastructure.Storage;

internal static class McpServerYamlMapper
{
    internal const string StdioTransport = "stdio";
    internal const string HttpTransport = "http";
    internal const string SseTransport = "sse";

    internal static List<McpServerYamlV1> ToYamlServers(IEnumerable<McpServer>? servers)
    {
        if (servers == null)
        {
            return new List<McpServerYamlV1>();
        }

        var yamlServers = new List<McpServerYamlV1>();
        foreach (var server in servers)
        {
            switch (server)
            {
                case StdioMcpServer stdio:
                    yamlServers.Add(new McpServerYamlV1
                    {
                        Transport = StdioTransport,
                        Name = stdio.Name ?? string.Empty,
                        Enabled = stdio.Enabled,
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
                        Enabled = http.Enabled,
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
                        Enabled = sse.Enabled,
                        Meta = McpServerJsonConverter.CloneMeta(sse.Meta),
                        Url = sse.Url ?? string.Empty,
                        Headers = ToYamlNameValues(sse.Headers)
                    });
                    break;
            }
        }

        return yamlServers;
    }

    internal static List<McpServer> FromYamlServers(IEnumerable<McpServerYamlV1>? yamlServers)
    {
        if (yamlServers == null)
        {
            return new List<McpServer>();
        }

        var servers = new List<McpServer>();
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
                    servers.Add(new HttpMcpServer(
                        yamlServer.Name ?? string.Empty,
                        yamlServer.Url ?? string.Empty,
                        FromYamlHeaders(yamlServer.Headers))
                    {
                        Enabled = yamlServer.Enabled,
                        Meta = McpServerJsonConverter.CloneMeta(yamlServer.Meta)
                    });
                    break;
                case SseTransport:
                    servers.Add(new SseMcpServer(
                        yamlServer.Name ?? string.Empty,
                        yamlServer.Url ?? string.Empty,
                        FromYamlHeaders(yamlServer.Headers))
                    {
                        Enabled = yamlServer.Enabled,
                        Meta = McpServerJsonConverter.CloneMeta(yamlServer.Meta)
                    });
                    break;
                case StdioTransport:
                    servers.Add(new StdioMcpServer(
                        yamlServer.Name ?? string.Empty,
                        yamlServer.Command ?? string.Empty,
                        yamlServer.Args ?? new List<string>(),
                        FromYamlEnv(yamlServer.Env))
                    {
                        Enabled = yamlServer.Enabled,
                        Meta = McpServerJsonConverter.CloneMeta(yamlServer.Meta)
                    });
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
