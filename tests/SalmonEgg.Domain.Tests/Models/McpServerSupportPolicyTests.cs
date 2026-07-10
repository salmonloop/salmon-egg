using System.Collections.Generic;
using Xunit;
using SalmonEgg.Acp.Mcp;
using SalmonEgg.Acp.Protocol;

namespace SalmonEgg.Domain.Tests.Models;

public sealed class McpServerSupportPolicyTests
{
    [Fact]
    public void Validate_StdioServer_Should_BeSupportedWithoutAgentMcpCapabilities()
    {
        var result = McpServerSupportPolicy.Validate(
            new List<McpServer> { new StdioMcpServer("filesystem", "/usr/bin/mcp", ["--stdio"]) },
            new AgentCapabilities());

        Assert.True(result.IsSupported);
    }

    [Fact]
    public void Validate_StdioServer_WhenCommandIsMissing_Should_BeUnsupported()
    {
        var result = McpServerSupportPolicy.Validate(
            new List<McpServer> { new StdioMcpServer("filesystem", string.Empty) },
            new AgentCapabilities());

        Assert.False(result.IsSupported);
        Assert.Contains("requires a command", result.ErrorMessage);
    }

    [Fact]
    public void Validate_StdioServer_WhenCommandIsRelative_Should_BeUnsupported()
    {
        var result = McpServerSupportPolicy.Validate(
            new List<McpServer> { new StdioMcpServer("filesystem", "mcp-server") },
            new AgentCapabilities());

        Assert.False(result.IsSupported);
        Assert.Contains("absolute command path", result.ErrorMessage);
    }

    [Fact]
    public void Validate_WhenServerEntryIsNull_Should_BeUnsupported()
    {
        var result = McpServerSupportPolicy.Validate(
            new McpServer?[] { null },
            McpServerSupportPolicy.SupportAllTransports);

        Assert.False(result.IsSupported);
        Assert.Contains("cannot be null", result.ErrorMessage);
    }

    [Fact]
    public void Validate_StdioServer_WhenEnvNameIsNull_Should_BeUnsupported()
    {
        var result = McpServerSupportPolicy.Validate(
            new List<McpServer>
            {
                new StdioMcpServer(
                    "filesystem",
                    "/usr/bin/mcp",
                    [],
                    [new McpEnvVariable(null!, "value")])
            },
            new AgentCapabilities());

        Assert.False(result.IsSupported);
        Assert.Contains("without a name", result.ErrorMessage);
    }

    [Fact]
    public void Validate_HttpServer_WhenAgentDoesNotAdvertiseHttp_Should_BeUnsupported()
    {
        var result = McpServerSupportPolicy.Validate(
            new List<McpServer> { new HttpMcpServer("api", "https://api.example.com/mcp") },
            new AgentCapabilities());

        Assert.False(result.IsSupported);
        Assert.Contains("mcpCapabilities.http", result.ErrorMessage);
    }

    [Fact]
    public void Validate_HttpServer_WhenAgentAdvertisesHttp_Should_BeSupported()
    {
        var result = McpServerSupportPolicy.Validate(
            new List<McpServer> { new HttpMcpServer("api", "https://api.example.com/mcp") },
            new AgentCapabilities(mcpCapabilities: new McpCapabilities(http: true)));

        Assert.True(result.IsSupported);
    }

    [Fact]
    public void Validate_HttpServer_WhenNameIsMissing_Should_BeUnsupported()
    {
        var result = McpServerSupportPolicy.Validate(
            new List<McpServer> { new HttpMcpServer(string.Empty, "https://api.example.com/mcp") },
            new AgentCapabilities(mcpCapabilities: new McpCapabilities(http: true)));

        Assert.False(result.IsSupported);
        Assert.Contains("requires a name", result.ErrorMessage);
    }

    [Fact]
    public void Validate_HttpServer_WhenUrlIsPresent_Should_BeSupported()
    {
        var result = McpServerSupportPolicy.Validate(
            new List<McpServer> { new HttpMcpServer("api", "api.example.com/mcp") },
            new AgentCapabilities(mcpCapabilities: new McpCapabilities(http: true)));

        Assert.True(result.IsSupported);
    }

    [Fact]
    public void Validate_HttpServer_WhenHeaderValueMissing_Should_BeUnsupported()
    {
        var result = McpServerSupportPolicy.Validate(
            new List<McpServer>
            {
                new HttpMcpServer(
                    "api",
                    "https://api.example.com/mcp",
                    [new McpHttpHeader("Authorization", null!)])
            },
            new AgentCapabilities(mcpCapabilities: new McpCapabilities(http: true)));

        Assert.False(result.IsSupported);
        Assert.Contains("without a value", result.ErrorMessage);
    }

    [Fact]
    public void Validate_SseServer_WhenAgentDoesNotAdvertiseSse_Should_BeUnsupported()
    {
        var result = McpServerSupportPolicy.Validate(
            new List<McpServer> { new SseMcpServer("events", "https://events.example.com/mcp") },
            new AgentCapabilities(mcpCapabilities: new McpCapabilities(http: true)));

        Assert.False(result.IsSupported);
        Assert.Contains("mcpCapabilities.sse", result.ErrorMessage);
    }
}
