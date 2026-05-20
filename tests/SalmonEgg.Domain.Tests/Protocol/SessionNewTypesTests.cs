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
    public void SessionNewParams_StdioMcpServers_Should_Serialize_StableProtocolShape()
    {
        var sessionParams = new SessionNewParams
        {
            Cwd = "/home/user/project",
            McpServers =
            [
                new StdioMcpServer(
                    "test-server",
                    "/usr/local/bin/node",
                    ["server.js"],
                    [new McpEnvVariable("API_KEY", "secret")])
            ]
        };

        var json = JsonSerializer.Serialize(sessionParams);
        var parsed = JsonDocument.Parse(json);

        Assert.That(parsed.RootElement.TryGetProperty("mcpServers", out var mcpServers), Is.True);
        Assert.That(mcpServers.ValueKind, Is.EqualTo(JsonValueKind.Array));
        Assert.That(mcpServers[0].TryGetProperty("type", out _), Is.False);
        Assert.That(mcpServers[0].GetProperty("name").GetString(), Is.EqualTo("test-server"));
        Assert.That(mcpServers[0].GetProperty("command").GetString(), Is.EqualTo("/usr/local/bin/node"));
        Assert.That(mcpServers[0].GetProperty("args")[0].GetString(), Is.EqualTo("server.js"));
        Assert.That(mcpServers[0].GetProperty("env")[0].GetProperty("name").GetString(), Is.EqualTo("API_KEY"));
        Assert.That(mcpServers[0].GetProperty("env")[0].GetProperty("value").GetString(), Is.EqualTo("secret"));
    }

    [Test]
    public void SessionNewParams_HttpAndSseMcpServers_Should_Serialize_With_TransportType()
    {
        var sessionParams = new SessionNewParams
        {
            Cwd = "/home/user/project",
            McpServers =
            [
                new HttpMcpServer("http-api", "https://api.example.com/mcp", [new McpHttpHeader("Authorization", "Bearer token")]),
                new SseMcpServer("events", "https://events.example.com/mcp")
            ]
        };

        var json = JsonSerializer.Serialize(sessionParams);
        var parsed = JsonDocument.Parse(json);
        var mcpServers = parsed.RootElement.GetProperty("mcpServers");

        Assert.That(mcpServers[0].GetProperty("type").GetString(), Is.EqualTo("http"));
        Assert.That(mcpServers[0].GetProperty("headers")[0].GetProperty("name").GetString(), Is.EqualTo("Authorization"));
        Assert.That(mcpServers[1].GetProperty("type").GetString(), Is.EqualTo("sse"));
    }

    [Test]
    public void SessionNewParams_McpServers_Should_Serialize_Meta_With_UnderscoreMeta()
    {
        var sessionParams = new SessionNewParams
        {
            Cwd = "/home/user/project",
            McpServers =
            [
                new StdioMcpServer(
                    "filesystem",
                    "/usr/bin/mcp-filesystem",
                    [],
                    [
                        new McpEnvVariable("ROOT", "/repo")
                        {
                            Meta = new Dictionary<string, object?>
                            {
                                ["scope"] = "workspace"
                            }
                        }
                    ])
                {
                    Meta = new Dictionary<string, object?>
                    {
                        ["source"] = "profile",
                        ["enabled"] = true
                    }
                },
                new HttpMcpServer(
                    "api",
                    "api.example.com/mcp",
                    [
                        new McpHttpHeader("Authorization", "Bearer token")
                        {
                            Meta = new Dictionary<string, object?>
                            {
                                ["secretRef"] = "header-auth"
                            }
                        }
                    ])
                {
                    Meta = new Dictionary<string, object?>
                    {
                        ["transport"] = "remote"
                    }
                }
            ]
        };

        var json = JsonSerializer.Serialize(sessionParams);
        var parsed = JsonDocument.Parse(json);
        var mcpServers = parsed.RootElement.GetProperty("mcpServers");

        Assert.That(mcpServers[0].GetProperty("_meta").GetProperty("source").GetString(), Is.EqualTo("profile"));
        Assert.That(mcpServers[0].GetProperty("_meta").GetProperty("enabled").ValueKind, Is.EqualTo(JsonValueKind.True));
        Assert.That(mcpServers[0].GetProperty("env")[0].GetProperty("_meta").GetProperty("scope").GetString(), Is.EqualTo("workspace"));
        Assert.That(mcpServers[1].GetProperty("_meta").GetProperty("transport").GetString(), Is.EqualTo("remote"));
        Assert.That(mcpServers[1].GetProperty("headers")[0].GetProperty("_meta").GetProperty("secretRef").GetString(), Is.EqualTo("header-auth"));
    }

    [Test]
    public void McpServer_Meta_Should_Deserialize_And_Clone_As_ProtocolObjects()
    {
        var json = """
        {
          "name": "filesystem",
          "command": "/usr/bin/mcp-filesystem",
          "args": [],
          "env": [
            {
              "name": "ROOT",
              "value": "/repo",
              "_meta": { "scope": "workspace" }
            }
          ],
          "_meta": {
            "source": "profile",
            "nested": { "value": 1 }
          }
        }
        """;

        var server = JsonSerializer.Deserialize<McpServer>(json);

        Assert.That(server, Is.TypeOf<StdioMcpServer>());
        var stdio = (StdioMcpServer)server!;
        Assert.That(stdio.Meta, Is.Not.Null);
        Assert.That(((JsonElement)stdio.Meta!["source"]!).GetString(), Is.EqualTo("profile"));
        Assert.That(((JsonElement)stdio.Meta["nested"]!).GetProperty("value").GetInt32(), Is.EqualTo(1));
        Assert.That(stdio.Env, Has.Count.EqualTo(1));
        Assert.That(((JsonElement)stdio.Env![0].Meta!["scope"]!).GetString(), Is.EqualTo("workspace"));

        var clonedServer = McpServerJsonConverter.CloneServer(stdio);
        Assert.That(clonedServer, Is.TypeOf<StdioMcpServer>());
        var clone = (StdioMcpServer)clonedServer;
        Assert.That(((JsonElement)clone.Meta!["source"]!).GetString(), Is.EqualTo("profile"));
        Assert.That(clone.Env, Has.Count.EqualTo(1));
        Assert.That(((JsonElement)clone.Env![0].Meta!["scope"]!).GetString(), Is.EqualTo("workspace"));
    }

    [Test]
    public void McpServer_WhenMetaIsNotObjectOrNull_Should_NotDeserialize()
    {
        var json = """
        {
          "name": "filesystem",
          "command": "/usr/bin/mcp-filesystem",
          "args": [],
          "env": [],
          "_meta": "invalid"
        }
        """;

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<McpServer>(json));
    }

    [Test]
    public void McpServer_WithStdioTypeDiscriminator_Should_NotDeserialize()
    {
        var json = """
        {
          "type": "stdio",
          "name": "test-server",
          "command": "/usr/local/bin/node",
          "args": [],
          "env": []
        }
        """;

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<McpServer>(json));
    }

    [Test]
    public void McpServer_WhenRequiredArrayIsMissing_Should_NotDeserialize()
    {
        var json = """
        {
          "name": "test-server",
          "command": "/usr/local/bin/node",
          "env": []
        }
        """;

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<McpServer>(json));
    }

    [Test]
    public void McpServer_WhenHeaderEntryValueIsMissing_Should_NotDeserialize()
    {
        var json = """
        {
          "type": "http",
          "name": "http-api",
          "url": "https://api.example.com/mcp",
          "headers": [
            {
              "name": "Authorization"
            }
          ]
        }
        """;

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<McpServer>(json));
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

    [Test]
    public void SessionNewResponse_Modes_Should_Deserialize_Standard_State_Object()
    {
        var json = """
        {
          "sessionId": "session-1",
          "modes": {
            "currentModeId": "default",
            "availableModes": [
              {
                "id": "default",
                "name": "Default",
                "description": "General work"
              }
            ]
          }
        }
        """;

        var response = JsonSerializer.Deserialize<SessionNewResponse>(json);

        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Modes, Is.Not.Null);
        Assert.That(response.Modes!.CurrentModeId, Is.EqualTo("default"));
        Assert.That(response.Modes.AvailableModes, Has.Count.EqualTo(1));
        Assert.That(response.Modes.AvailableModes[0].Id, Is.EqualTo("default"));
    }

    [Test]
    public void SessionNewResponse_Modes_Should_Reject_Legacy_Array()
    {
        var json = """
        {
          "sessionId": "session-1",
          "modes": [
            {
              "id": "default",
              "name": "Default"
            }
          ]
        }
        """;

        Assert.That(
            () => JsonSerializer.Deserialize<SessionNewResponse>(json),
            Throws.TypeOf<JsonException>());
    }
}
