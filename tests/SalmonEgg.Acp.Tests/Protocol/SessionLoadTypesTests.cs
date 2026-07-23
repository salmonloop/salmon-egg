using System;
using System.Collections.Generic;
using System.Text.Json;
using Xunit;
using SalmonEgg.Acp.Mcp;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Acp.Serialization;

namespace SalmonEgg.Acp.Tests.Protocol;

public sealed class SessionLoadTypesTests
{
    [Fact]
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

        Assert.True(parsed.RootElement.TryGetProperty("mcpServers", out var mcpServers));
        Assert.Equal(JsonValueKind.Array, mcpServers.ValueKind);
        Assert.False(mcpServers[0].TryGetProperty("type", out _));
        Assert.Equal("/usr/local/bin/node", mcpServers[0].GetProperty("command").GetString());
    }

    [Fact]
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
        Assert.True(parsed.RootElement.TryGetProperty("mcpServers", out var mcpServers));
        Assert.Equal(JsonValueKind.Array, mcpServers.ValueKind);
    }

    [Fact]
    public void SessionLoadParams_Constructor_Should_Default_McpServers_To_Empty_Array()
    {
        // Given/When: Constructing params without explicitly supplying MCP servers
        var sessionParams = new SessionLoadParams("test-session", "/home/user/project");

        // Then: protocol-required mcpServers should still be emitted as an empty array
        Assert.NotNull(sessionParams.McpServers);
        Assert.Empty(sessionParams.McpServers);

        var json = JsonSerializer.Serialize(sessionParams);
        Assert.Contains("\"mcpServers\":[]", json);
    }

    [Fact]
    public void SessionResumeParams_Constructor_Should_Default_McpServers_To_Empty_Array()
    {
        var sessionParams = new SessionResumeParams("test-session", "/home/user/project");

        Assert.NotNull(sessionParams.McpServers);
        Assert.Empty(sessionParams.McpServers);

        var json = JsonSerializer.Serialize(sessionParams);
        Assert.Contains("\"mcpServers\":[]", json);
    }

    [Fact]
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

        Assert.NotNull(response);
        Assert.NotNull(response!.Modes);
        Assert.Equal("review", response.Modes!.CurrentModeId);
        Assert.Single(response.Modes.AvailableModes);
    }

    [Fact]
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

        Assert.Throws<JsonException>((() => JsonSerializer.Deserialize<SessionLoadResponse>(json)));
    }

    [Fact]
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

        Assert.Throws<JsonException>((() => JsonSerializer.Deserialize<SessionResumeResponse>(json)));
    }

    [Fact]
    public void SessionResumeParams_WhenReplayFromIsNull_OmitsReplayFromProperty()
    {
        var sessionParams = new SessionResumeParams("test-session", "/home/user/project");

        var json = JsonSerializer.Serialize(sessionParams, AcpJsonContext.Default.SessionResumeParams);
        using var parsed = JsonDocument.Parse(json);

        Assert.False(parsed.RootElement.TryGetProperty("replayFrom", out _));
    }

    [Fact]
    public void SessionResumeParams_WhenReplayFromStart_SerializesTypeStart()
    {
        var sessionParams = new SessionResumeParams(
            "test-session",
            "/home/user/project",
            replayFrom: SessionReplayFrom.Start);

        var json = JsonSerializer.Serialize(sessionParams, AcpJsonContext.Default.SessionResumeParams);
        using var parsed = JsonDocument.Parse(json);

        Assert.True(parsed.RootElement.TryGetProperty("replayFrom", out var replayFrom));
        Assert.Equal("start", replayFrom.GetProperty("type").GetString());
    }

    [Fact]
    public void SessionReplayFrom_Start_IsTypeStart()
    {
        Assert.Equal("start", SessionReplayFrom.Start.Type);
    }

}
