using System;
using System.Collections.Generic;
using SalmonEgg.Domain.Models.Protocol;

namespace SalmonEgg.Domain.Models.Mcp
{
    public sealed class McpServerSupportResult
    {
        private McpServerSupportResult(bool isSupported, string? errorMessage)
        {
            IsSupported = isSupported;
            ErrorMessage = errorMessage;
        }

        public bool IsSupported { get; }

        public string? ErrorMessage { get; }

        public static McpServerSupportResult Supported { get; } = new(true, null);

        public static McpServerSupportResult Unsupported(string errorMessage)
        {
            return new McpServerSupportResult(false, errorMessage);
        }
    }

    public static class McpServerSupportPolicy
    {
        public static AgentCapabilities SupportAllTransports { get; } = new(
            mcpCapabilities: new McpCapabilities(http: true, sse: true));

        public static McpServerSupportResult Validate(
            IEnumerable<McpServer?>? servers,
            AgentCapabilities? agentCapabilities)
        {
            if (servers == null)
            {
                return McpServerSupportResult.Supported;
            }

            foreach (var server in servers)
            {
                var result = Validate(server, agentCapabilities);
                if (!result.IsSupported)
                {
                    return result;
                }
            }

            return McpServerSupportResult.Supported;
        }

        public static McpServerSupportResult Validate(
            McpServer? server,
            AgentCapabilities? agentCapabilities)
        {
            return server switch
            {
                null => McpServerSupportResult.Unsupported("MCP server entry cannot be null."),
                StdioMcpServer stdio => ValidateStdio(stdio),
                HttpMcpServer http when agentCapabilities?.SupportsHttp == true => ValidateHttp(http),
                HttpMcpServer http => McpServerSupportResult.Unsupported(
                    $"Agent does not advertise mcpCapabilities.http for MCP server '{ResolveName(http)}'."),
                SseMcpServer sse when agentCapabilities?.SupportsSse == true => ValidateSse(sse),
                SseMcpServer sse => McpServerSupportResult.Unsupported(
                    $"Agent does not advertise mcpCapabilities.sse for MCP server '{ResolveName(sse)}'."),
                _ => McpServerSupportResult.Unsupported(
                    $"Unsupported MCP server type '{server.GetType().Name}'.")
            };
        }

        public static McpServerTransport GetTransport(McpServer server)
        {
            if (server == null)
            {
                throw new ArgumentNullException(nameof(server));
            }

            return server switch
            {
                StdioMcpServer => McpServerTransport.Stdio,
                HttpMcpServer => McpServerTransport.Http,
                SseMcpServer => McpServerTransport.Sse,
                _ => throw new ArgumentException("Unsupported MCP server type.", nameof(server))
            };
        }

        private static McpServerSupportResult ValidateStdio(StdioMcpServer server)
        {
            if (string.IsNullOrWhiteSpace(server.Name))
            {
                return McpServerSupportResult.Unsupported("Stdio MCP server requires a name.");
            }

            if (string.IsNullOrWhiteSpace(server.Command))
            {
                return McpServerSupportResult.Unsupported($"Stdio MCP server '{ResolveName(server)}' requires a command.");
            }

            return ProtocolPathRules.IsAbsolutePath(server.Command)
                ? ValidateArgsAndEnv(server)
                : McpServerSupportResult.Unsupported($"Stdio MCP server '{ResolveName(server)}' requires an absolute command path.");
        }

        private static McpServerSupportResult ValidateHttp(HttpMcpServer server)
        {
            if (string.IsNullOrWhiteSpace(server.Name))
            {
                return McpServerSupportResult.Unsupported("HTTP MCP server requires a name.");
            }

            if (string.IsNullOrWhiteSpace(server.Url))
            {
                return McpServerSupportResult.Unsupported($"HTTP MCP server '{ResolveName(server)}' requires a URL.");
            }

            return ValidateHeaders(server);
        }

        private static McpServerSupportResult ValidateSse(SseMcpServer server)
        {
            if (string.IsNullOrWhiteSpace(server.Name))
            {
                return McpServerSupportResult.Unsupported("SSE MCP server requires a name.");
            }

            if (string.IsNullOrWhiteSpace(server.Url))
            {
                return McpServerSupportResult.Unsupported($"SSE MCP server '{ResolveName(server)}' requires a URL.");
            }

            return ValidateHeaders(server);
        }

        private static string ResolveName(McpServer server)
            => string.IsNullOrWhiteSpace(server.Name) ? "<unnamed>" : server.Name;

        private static McpServerSupportResult ValidateArgsAndEnv(StdioMcpServer server)
        {
            var args = server.Args ?? new List<string>();
            for (var index = 0; index < args.Count; index++)
            {
                if (args[index] == null)
                {
                    return McpServerSupportResult.Unsupported(
                        $"Stdio MCP server '{ResolveName(server)}' has null args[{index}].");
                }
            }

            var env = server.Env ?? new List<McpEnvVariable>();
            for (var index = 0; index < env.Count; index++)
            {
                var variable = env[index];
                if (variable == null)
                {
                    return McpServerSupportResult.Unsupported(
                        $"Stdio MCP server '{ResolveName(server)}' has null env[{index}].");
                }

                if (variable.Name == null)
                {
                    return McpServerSupportResult.Unsupported(
                        $"Stdio MCP server '{ResolveName(server)}' has env[{index}] without a name.");
                }

                if (variable.Value == null)
                {
                    return McpServerSupportResult.Unsupported(
                        $"Stdio MCP server '{ResolveName(server)}' has env[{index}] without a value.");
                }
            }

            return McpServerSupportResult.Supported;
        }

        private static McpServerSupportResult ValidateHeaders(HttpMcpServer server)
            => ValidateHeaders((McpServer)server, server.Headers);

        private static McpServerSupportResult ValidateHeaders(SseMcpServer server)
            => ValidateHeaders((McpServer)server, server.Headers);

        private static McpServerSupportResult ValidateHeaders(McpServer server, IList<McpHttpHeader>? headers)
        {
            headers ??= new List<McpHttpHeader>();
            for (var index = 0; index < headers.Count; index++)
            {
                var header = headers[index];
                if (header == null)
                {
                    return McpServerSupportResult.Unsupported(
                        $"MCP server '{ResolveName(server)}' has null headers[{index}].");
                }

                if (header.Name == null)
                {
                    return McpServerSupportResult.Unsupported(
                        $"MCP server '{ResolveName(server)}' has headers[{index}] without a name.");
                }

                if (header.Value == null)
                {
                    return McpServerSupportResult.Unsupported(
                        $"MCP server '{ResolveName(server)}' has headers[{index}] without a value.");
                }
            }

            return McpServerSupportResult.Supported;
        }
    }
}
