using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SalmonEgg.Acp.Mcp;
using SalmonEgg.Domain.Models.Mcp;
using SalmonEgg.Infrastructure.Storage;
using SalmonEgg.Infrastructure.Storage.YamlModels;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.Storage;

public sealed class McpServerYamlMapperTests
{
    [Fact]
    public void ToYamlServers_WithNull_ReturnsEmpty()
    {
        Assert.Empty(McpServerYamlMapper.ToYamlServers(null));
    }

    [Fact]
    public void RoundTrip_Stdio_PreservesTransportCommandArgsAndEnv()
    {
        var entry = new McpServerCatalogEntry(
            new StdioMcpServer(
                "stdio-server",
                "node",
                new List<string> { "--flag", "value" },
                new List<McpEnvVariable> { new("API_KEY", "secret") }),
            enabled: false);

        var yaml = McpServerYamlMapper.ToYamlServers(new[] { entry });
        var yamlServer = Assert.Single(yaml);
        Assert.Equal("stdio", yamlServer.Transport);
        Assert.Equal("stdio-server", yamlServer.Name);
        Assert.False(yamlServer.Enabled);
        Assert.Equal("node", yamlServer.Command);
        Assert.Equal(new[] { "--flag", "value" }, yamlServer.Args);
        var env = Assert.Single(yamlServer.Env);
        Assert.Equal("API_KEY", env.Name);
        Assert.Equal("secret", env.Value);

        var restored = McpServerYamlMapper.FromYamlServers(yaml);
        var restoredEntry = Assert.Single(restored);
        Assert.False(restoredEntry.Enabled);
        var stdio = Assert.IsType<StdioMcpServer>(restoredEntry.Server);
        Assert.Equal("stdio-server", stdio.Name);
        Assert.Equal("node", stdio.Command);
        Assert.Equal(new[] { "--flag", "value" }, stdio.Args);
        Assert.NotNull(stdio.Env);
        var restoredEnv = Assert.Single(stdio.Env!);
        Assert.Equal("API_KEY", restoredEnv.Name);
        Assert.Equal("secret", restoredEnv.Value);
    }

    [Fact]
    public void RoundTrip_Http_PreservesUrlAndHeaders()
    {
        var entry = new McpServerCatalogEntry(
            new HttpMcpServer(
                "http-server",
                "https://example.com/mcp",
                new List<McpHttpHeader> { new("Authorization", "Bearer token") }));

        var restored = McpServerYamlMapper.FromYamlServers(
            McpServerYamlMapper.ToYamlServers(new[] { entry }));

        var restoredEntry = Assert.Single(restored);
        var http = Assert.IsType<HttpMcpServer>(restoredEntry.Server);
        Assert.Equal("http-server", http.Name);
        Assert.Equal("https://example.com/mcp", http.Url);
        Assert.NotNull(http.Headers);
        var header = Assert.Single(http.Headers!);
        Assert.Equal("Authorization", header.Name);
        Assert.Equal("Bearer token", header.Value);
    }

    [Fact]
    public void RoundTrip_Sse_PreservesUrlAndHeaders()
    {
        var entry = new McpServerCatalogEntry(
            new SseMcpServer(
                "sse-server",
                "https://example.com/sse",
                new List<McpHttpHeader> { new("X-Trace", "1") }));

        var restored = McpServerYamlMapper.FromYamlServers(
            McpServerYamlMapper.ToYamlServers(new[] { entry }));

        var restoredEntry = Assert.Single(restored);
        var sse = Assert.IsType<SseMcpServer>(restoredEntry.Server);
        Assert.Equal("sse-server", sse.Name);
        Assert.Equal("https://example.com/sse", sse.Url);
        Assert.NotNull(sse.Headers);
        var header = Assert.Single(sse.Headers!);
        Assert.Equal("X-Trace", header.Name);
        Assert.Equal("1", header.Value);
    }

    [Theory]
    [InlineData("HTTP", "http")]
    [InlineData(" Http ", "http")]
    [InlineData("Stdio", "stdio")]
    [InlineData("SSE", "sse")]
    public void FromYamlServers_NormalizesTransportCasingAndWhitespace(string raw, string expected)
    {
        var yaml = new McpServerYamlV1 { Transport = raw, Name = "n", Url = "https://x", Command = "c" };

        var restored = McpServerYamlMapper.FromYamlServers(new[] { yaml });

        var server = Assert.Single(restored).Server;
        Assert.Equal(expected, server is StdioMcpServer ? "stdio" : server is HttpMcpServer ? "http" : "sse");
    }

    [Fact]
    public void FromYamlServers_UnknownTransport_ThrowsInvalidData()
    {
        var yaml = new McpServerYamlV1 { Transport = "websocket", Name = "n" };

        Assert.Throws<InvalidDataException>(() => McpServerYamlMapper.FromYamlServers(new[] { yaml }));
    }

    [Fact]
    public void FromYamlServers_NullEntry_ThrowsInvalidData()
    {
        Assert.Throws<InvalidDataException>(() => McpServerYamlMapper.FromYamlServers(new McpServerYamlV1[] { null! }));
    }

    [Fact]
    public void FromYamlServers_WithNull_ReturnsEmpty()
    {
        Assert.Empty(McpServerYamlMapper.FromYamlServers(null));
    }

    [Fact]
    public void FromYamlServers_NullFields_CollapseToEmptyDefaults()
    {
        var yaml = new McpServerYamlV1 { Transport = "http", Name = null!, Url = null! };

        var restored = McpServerYamlMapper.FromYamlServers(new[] { yaml });

        var http = Assert.IsType<HttpMcpServer>(Assert.Single(restored).Server);
        Assert.Equal(string.Empty, http.Name);
        Assert.Equal(string.Empty, http.Url);
        Assert.NotNull(http.Headers);
        Assert.Empty(http.Headers!);
    }
}
