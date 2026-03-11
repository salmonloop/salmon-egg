using System.Collections.Generic;
using System.Text.Json;
using NUnit.Framework;
using SalmonEgg.Domain.Models.Mcp;
using SalmonEgg.Domain.Models.Protocol;

namespace SalmonEgg.Domain.Tests.Protocol;

[TestFixture]
public sealed class SessionNewTypesTests
{
    [Test]
    public void SessionNewParams_McpServers_ShouldBe_ListOfMcpServer()
    {
        // Given: A SessionNewParams type
        var property = typeof(SessionNewParams).GetProperty("McpServers");

        // Then: Property type should be List<McpServer>
        Assert.That(property, Is.Not.Null);
        Assert.That(property?.PropertyType, Is.EqualTo(typeof(List<McpServer>)));
    }

    [Test]
    public void SessionNewParams_McpServers_Should_Serialize_As_Array()
    {
        // Given: A SessionNewParams with MCP servers
        var sessionParams = new SessionNewParams
        {
            Cwd = "/home/user/project",
            McpServers = new List<McpServer>
            {
                new StdioMcpServer("test-server", "node", new List<string> { "server.js" })
            }
        };

        // When: Serialize to JSON
        var json = JsonSerializer.Serialize(sessionParams);
        var parsed = JsonDocument.Parse(json);

        // Then: mcpServers should be an array in JSON
        Assert.That(parsed.RootElement.TryGetProperty("mcpServers", out var mcpServers), Is.True);
        Assert.That(mcpServers.ValueKind, Is.EqualTo(JsonValueKind.Array));
    }

    [Test]
    public void SessionNewParams_McpServers_Should_NotBe_Object()
    {
        // Given: A SessionNewParams with MCP servers
        var sessionParams = new SessionNewParams
        {
            Cwd = "/home/user/project",
            McpServers = new List<McpServer>()
        };

        // When: Serialize to JSON
        var json = JsonSerializer.Serialize(sessionParams);

        // Then: JSON should not contain "object" representation
        Assert.That(json, Does.Not.Contain("\"mcpServers\":{}"));
        Assert.That(json, Does.Contain("\"mcpServers\":[]"));
    }
}
