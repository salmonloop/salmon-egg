using System.Collections.Generic;
using System.Text.Json;
using NUnit.Framework;
using SalmonEgg.Domain.Models.Mcp;
using SalmonEgg.Domain.Models.Protocol;

namespace SalmonEgg.Domain.Tests.Protocol;

[TestFixture]
public sealed class SessionLoadTypesTests
{
    [Test]
    public void SessionLoadParams_StdioMcpServers_Should_Serialize_StableProtocolShape()
    {
        var sessionParams = new SessionLoadParams
        {
            SessionId = "test-session",
            Cwd = "/home/user/project",
            McpServers =
            [
                new StdioMcpServer("test-server", "/usr/local/bin/node", ["server.js"])
            ]
        };

        var json = JsonSerializer.Serialize(sessionParams);
        var parsed = JsonDocument.Parse(json);

        Assert.That(parsed.RootElement.TryGetProperty("mcpServers", out var mcpServers), Is.True);
        Assert.That(mcpServers.ValueKind, Is.EqualTo(JsonValueKind.Array));
        Assert.That(mcpServers[0].TryGetProperty("type", out _), Is.False);
        Assert.That(mcpServers[0].GetProperty("command").GetString(), Is.EqualTo("/usr/local/bin/node"));
    }

    [Test]
    public void SessionLoadParams_McpServers_Should_Serialize_As_Array()
    {
        // Given: A SessionLoadParams with MCP servers
        var sessionParams = new SessionLoadParams
        {
            SessionId = "test-session",
            Cwd = "/home/user/project",
            McpServers = new List<McpServer>
            {
                new StdioMcpServer("test-server", "/usr/local/bin/node", new List<string> { "server.js" })
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
    public void SessionLoadParams_Constructor_Should_Default_McpServers_To_Empty_Array()
    {
        // Given/When: Constructing params without explicitly supplying MCP servers
        var sessionParams = new SessionLoadParams("test-session", "/home/user/project");

        // Then: protocol-required mcpServers should still be emitted as an empty array
        Assert.That(sessionParams.McpServers, Is.Not.Null);
        Assert.That(sessionParams.McpServers, Is.Empty);

        var json = JsonSerializer.Serialize(sessionParams);
        Assert.That(json, Does.Contain("\"mcpServers\":[]"));
    }

    [Test]
    public void SessionResumeParams_Constructor_Should_Default_McpServers_To_Empty_Array()
    {
        var sessionParams = new SessionResumeParams("test-session", "/home/user/project");

        Assert.That(sessionParams.McpServers, Is.Not.Null);
        Assert.That(sessionParams.McpServers, Is.Empty);

        var json = JsonSerializer.Serialize(sessionParams);
        Assert.That(json, Does.Contain("\"mcpServers\":[]"));
    }

    [Test]
    public void SessionLoadResponse_Modes_Should_Deserialize_Standard_State_Object()
    {
        var json = """
        {
          "modes": {
            "currentModeId": "review",
            "availableModes": [
              {
                "id": "review",
                "name": "Review"
              }
            ]
          }
        }
        """;

        var response = JsonSerializer.Deserialize<SessionLoadResponse>(json);

        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Modes, Is.Not.Null);
        Assert.That(response.Modes!.CurrentModeId, Is.EqualTo("review"));
        Assert.That(response.Modes.AvailableModes, Has.Count.EqualTo(1));
    }

    [Test]
    public void SessionLoadResponse_Modes_Should_Reject_Legacy_Array()
    {
        var json = """
        {
          "modes": [
            {
              "id": "review",
              "name": "Review"
            }
          ]
        }
        """;

        Assert.That(
            () => JsonSerializer.Deserialize<SessionLoadResponse>(json),
            Throws.TypeOf<JsonException>());
    }

    [Test]
    public void SessionResumeResponse_Modes_Should_Reject_Legacy_Array()
    {
        var json = """
        {
          "modes": [
            {
              "id": "review",
              "name": "Review"
            }
          ]
        }
        """;

        Assert.That(
            () => JsonSerializer.Deserialize<SessionResumeResponse>(json),
            Throws.TypeOf<JsonException>());
    }
}
