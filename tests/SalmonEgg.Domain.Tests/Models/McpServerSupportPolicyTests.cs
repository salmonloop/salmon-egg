using System.Collections.Generic;
using NUnit.Framework;
using SalmonEgg.Domain.Models.Mcp;
using SalmonEgg.Domain.Models.Protocol;

namespace SalmonEgg.Domain.Tests.Models;

[TestFixture]
public sealed class McpServerSupportPolicyTests
{
    [Test]
    public void Validate_StdioServer_Should_BeSupportedWithoutAgentMcpCapabilities()
    {
        var result = McpServerSupportPolicy.Validate(
            new List<McpServer> { new StdioMcpServer("filesystem", "/usr/bin/mcp", ["--stdio"]) },
            new AgentCapabilities());

        Assert.That(result.IsSupported, Is.True);
    }

    [Test]
    public void Validate_StdioServer_WhenCommandIsMissing_Should_BeUnsupported()
    {
        var result = McpServerSupportPolicy.Validate(
            new List<McpServer> { new StdioMcpServer("filesystem", string.Empty) },
            new AgentCapabilities());

        Assert.That(result.IsSupported, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("requires a command"));
    }

    [Test]
    public void Validate_StdioServer_WhenCommandIsRelative_Should_BeUnsupported()
    {
        var result = McpServerSupportPolicy.Validate(
            new List<McpServer> { new StdioMcpServer("filesystem", "mcp-server") },
            new AgentCapabilities());

        Assert.That(result.IsSupported, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("absolute command path"));
    }

    [Test]
    public void Validate_HttpServer_WhenAgentDoesNotAdvertiseHttp_Should_BeUnsupported()
    {
        var result = McpServerSupportPolicy.Validate(
            new List<McpServer> { new HttpMcpServer("api", "https://api.example.com/mcp") },
            new AgentCapabilities());

        Assert.That(result.IsSupported, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("mcpCapabilities.http"));
    }

    [Test]
    public void Validate_HttpServer_WhenAgentAdvertisesHttp_Should_BeSupported()
    {
        var result = McpServerSupportPolicy.Validate(
            new List<McpServer> { new HttpMcpServer("api", "https://api.example.com/mcp") },
            new AgentCapabilities(mcpCapabilities: new McpCapabilities(http: true)));

        Assert.That(result.IsSupported, Is.True);
    }

    [Test]
    public void Validate_HttpServer_WhenNameIsMissing_Should_BeUnsupported()
    {
        var result = McpServerSupportPolicy.Validate(
            new List<McpServer> { new HttpMcpServer(string.Empty, "https://api.example.com/mcp") },
            new AgentCapabilities(mcpCapabilities: new McpCapabilities(http: true)));

        Assert.That(result.IsSupported, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("requires a name"));
    }

    [Test]
    public void Validate_HttpServer_WhenUrlIsNotHttp_Should_BeUnsupported()
    {
        var result = McpServerSupportPolicy.Validate(
            new List<McpServer> { new HttpMcpServer("api", "ftp://api.example.com/mcp") },
            new AgentCapabilities(mcpCapabilities: new McpCapabilities(http: true)));

        Assert.That(result.IsSupported, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("HTTP URL"));
    }

    [Test]
    public void Validate_SseServer_WhenAgentDoesNotAdvertiseSse_Should_BeUnsupported()
    {
        var result = McpServerSupportPolicy.Validate(
            new List<McpServer> { new SseMcpServer("events", "https://events.example.com/mcp") },
            new AgentCapabilities(mcpCapabilities: new McpCapabilities(http: true)));

        Assert.That(result.IsSupported, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("mcpCapabilities.sse"));
    }
}
