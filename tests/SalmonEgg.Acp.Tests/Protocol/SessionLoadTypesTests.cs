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
    public void SessionLoadParams_StdioMcpServers_DefaultWriteContext_SerializesV1Shape()
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

        var json = JsonSerializer.Serialize(sessionParams, AcpJsonContext.Default.SessionLoadParams);
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
    public void SessionResumeParams_WhenReplayFromStart_OnAStableConnectionRejectsV2Field()
    {
        var sessionParams = new SessionResumeParams(
            "test-session",
            "/home/user/project",
            replayFrom: SessionReplayFrom.Start);

        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Serialize(
            sessionParams,
            AcpJsonContext.Default.SessionResumeParams));

        Assert.Equal(SessionReplayFromJsonConverter.V2OnlyMessage, exception.Message);
    }

    [Fact]
    public void SessionResumeParams_WhenReplayFromStart_OnADraftConnectionSerializesTypeStart()
    {
        var sessionParams = new SessionResumeParams(
            "test-session",
            "/home/user/project",
            replayFrom: SessionReplayFrom.Start);

        var json = JsonSerializer.Serialize(sessionParams, Wire.V2<SessionResumeParams>());

        using var parsed = JsonDocument.Parse(json);

        Assert.True(parsed.RootElement.TryGetProperty("replayFrom", out var replayFrom));
        Assert.Equal("start", replayFrom.GetProperty("type").GetString());
    }

    [Fact]
    public void SessionResumeParams_UnknownReplayCursor_RoundTripsOnADraftConnection()
    {
        const string CursorJson =
            "{\"type\":\"_vendor_cursor\",\"messageId\":\"first\",\"messageId\":\"second\"," +
            "\"label\":\"\\u4f60\\u597d\",\"offset\":1.2300e+02," +
            "\"checkpoint\":{\"b\":2,\"a\":[1,2,3]},\"_meta\":{\"cursor\":\"opaque\"}}";
        var sessionParams = JsonSerializer.Deserialize(
            "{\"sessionId\":\"test-session\",\"cwd\":\"/home/user/project\"," +
            "\"mcpServers\":[],\"replayFrom\":" + CursorJson + "}",
            AcpJsonContext.Default.SessionResumeParams);

        var replayFrom = Assert.IsType<SessionReplayFrom>(sessionParams?.ReplayFrom);
        Assert.Equal("_vendor_cursor", replayFrom.Type);
        var cursor = Assert.IsType<JsonElement>(replayFrom.Meta?["cursor"]);
        Assert.Equal("opaque", cursor.GetString());
        Assert.Equal(CursorJson, replayFrom.RawPayload.GetRawText());

        var json = JsonSerializer.Serialize(sessionParams, Wire.V2<SessionResumeParams>());

        Assert.Contains("\"replayFrom\":" + CursorJson, json, StringComparison.Ordinal);
        Assert.Contains("\"messageId\":\"first\",\"messageId\":\"second\"", json, StringComparison.Ordinal);
        Assert.Contains("\"label\":\"\\u4f60\\u597d\"", json, StringComparison.Ordinal);
        Assert.Contains("\"offset\":1.2300e+02", json, StringComparison.Ordinal);
    }

    [Fact]
    public void SessionResumeParams_WhenReplayCursorTypeIsMissing_ThrowsJsonException()
    {
        const string Json = """
            {
              "sessionId": "test-session",
              "cwd": "/home/user/project",
              "mcpServers": [],
              "replayFrom": {}
            }
            """;

        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            Json,
            AcpJsonContext.Default.SessionResumeParams));

        Assert.Contains("required string property 'type'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SessionResumeParams_WhenReplayCursorTypeIsNull_ThrowsJsonException()
    {
        const string Json = """
            {
              "sessionId": "test-session",
              "cwd": "/home/user/project",
              "mcpServers": [],
              "replayFrom": { "type": null }
            }
            """;

        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            Json,
            AcpJsonContext.Default.SessionResumeParams));

        Assert.Contains("replayFrom.type", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SessionResumeParams_WhenReplayFromIsExplicitNull_DeserializesAsNull()
    {
        const string Json = """
            {
              "sessionId": "test-session",
              "cwd": "/home/user/project",
              "mcpServers": [],
              "replayFrom": null
            }
            """;

        var sessionParams = JsonSerializer.Deserialize(
            Json,
            AcpJsonContext.Default.SessionResumeParams);

        Assert.NotNull(sessionParams);
        Assert.Null(sessionParams!.ReplayFrom);
    }

    [Fact]
    public void SessionResumeParams_WhenReplayCursorTypeHasWrongWireType_ThrowsJsonException()
    {
        const string Json = """
            {
              "sessionId": "test-session",
              "cwd": "/home/user/project",
              "mcpServers": [],
              "replayFrom": { "type": 42 }
            }
            """;

        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            Json,
            AcpJsonContext.Default.SessionResumeParams));

        Assert.Contains("replayFrom.type", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SessionResumeParams_WhenReplayCursorTypeIsNullInMemory_OnADraftConnectionThrowsJsonException()
    {
        var sessionParams = new SessionResumeParams(
            "test-session",
            "/home/user/project",
            replayFrom: new SessionReplayFrom(null!));

        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Serialize(
            sessionParams,
            Wire.V2<SessionResumeParams>()));

        Assert.Contains("replayFrom.type", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SessionReplayFrom_Start_IsTypeStart()
    {
        Assert.Equal("start", SessionReplayFrom.Start.Type);
    }

}
