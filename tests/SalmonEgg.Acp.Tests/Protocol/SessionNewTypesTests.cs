using System;
using System.Collections.Generic;
using System.Text.Json;
using Xunit;
using SalmonEgg.Acp.Mcp;
using SalmonEgg.Acp.Protocol;

namespace SalmonEgg.Acp.Tests.Protocol;

public sealed class SessionNewTypesTests
{
    [Fact]
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

        Assert.True(parsed.RootElement.TryGetProperty("mcpServers", out var mcpServers));
        Assert.Equal(JsonValueKind.Array, mcpServers.ValueKind);
        // 默认写入上下文为 V2 主线：stdio 显式携带 type 判别式（V2 schema 以 type 区分三种 transport）。
        Assert.Equal("stdio", mcpServers[0].GetProperty("type").GetString());
        Assert.Equal("test-server", mcpServers[0].GetProperty("name").GetString());
        Assert.Equal("/usr/local/bin/node", mcpServers[0].GetProperty("command").GetString());
        Assert.Equal("server.js", mcpServers[0].GetProperty("args")[0].GetString());
        Assert.Equal("API_KEY", mcpServers[0].GetProperty("env")[0].GetProperty("name").GetString());
        Assert.Equal("secret", mcpServers[0].GetProperty("env")[0].GetProperty("value").GetString());
    }

    [Fact]
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

        Assert.Equal("http", mcpServers[0].GetProperty("type").GetString());
        Assert.Equal("Authorization", mcpServers[0].GetProperty("headers")[0].GetProperty("name").GetString());
        Assert.Equal("sse", mcpServers[1].GetProperty("type").GetString());
    }

    [Fact]
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

        Assert.Equal("profile", mcpServers[0].GetProperty("_meta").GetProperty("source").GetString());
        Assert.Equal(JsonValueKind.True, mcpServers[0].GetProperty("_meta").GetProperty("enabled").ValueKind);
        Assert.Equal("workspace", mcpServers[0].GetProperty("env")[0].GetProperty("_meta").GetProperty("scope").GetString());
        Assert.Equal("remote", mcpServers[1].GetProperty("_meta").GetProperty("transport").GetString());
        Assert.Equal("header-auth", mcpServers[1].GetProperty("headers")[0].GetProperty("_meta").GetProperty("secretRef").GetString());
    }

    [Fact]
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

        Assert.IsType<StdioMcpServer>(server);
        var stdio = (StdioMcpServer)server!;
        Assert.NotNull(stdio.Meta);
        Assert.Equal("profile", ((JsonElement)stdio.Meta!["source"]!).GetString());
        Assert.Equal(1, ((JsonElement)stdio.Meta["nested"]!).GetProperty("value").GetInt32());
        var env = Assert.Single(stdio.Env!);
        Assert.Equal("workspace", ((JsonElement)env.Meta!["scope"]!).GetString());

        var clonedServer = McpServerJsonConverter.CloneServer(stdio);
        Assert.IsType<StdioMcpServer>(clonedServer);
        var clone = (StdioMcpServer)clonedServer;
        Assert.Equal("profile", ((JsonElement)clone.Meta!["source"]!).GetString());
        var clonedEnv = Assert.Single(clone.Env!);
        Assert.Equal("workspace", ((JsonElement)clonedEnv.Meta!["scope"]!).GetString());
    }

    [Fact]
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

        Assert.Throws<JsonException>((Action)(() => JsonSerializer.Deserialize<McpServer>(json)));
    }

    [Fact]
    public void McpServer_StdioWithoutEnv_Should_Deserialize_WithEmptyEnvironment()
    {
        var json = """
        {
          "name": "test-server",
          "command": "/usr/local/bin/node",
          "args": []
        }
        """;

        var server = JsonSerializer.Deserialize<McpServer>(json);

        Assert.IsType<StdioMcpServer>(server);
        var stdio = (StdioMcpServer)server!;
        Assert.NotNull(stdio.Env);
        Assert.Empty(stdio.Env);
    }

    [Fact]
    public void McpServer_StdioWithNullEnv_Should_NotDeserialize()
    {
        var json = """
        {
          "name": "test-server",
          "command": "/usr/local/bin/node",
          "args": [],
          "env": null
        }
        """;

        Assert.Throws<JsonException>((Action)(() => JsonSerializer.Deserialize<McpServer>(json)));
    }

    [Fact]
    public void McpServer_WithStdioTypeDiscriminator_Should_Deserialize()
    {
        // V2 schema 以 `type` 判别式显式标注 stdio；读路径对两版本一视同仁地接受
        // 带或不带 type 的 stdio 形态（V1 无 type、V2 有 type 均合法）。
        var json = """
        {
          "type": "stdio",
          "name": "test-server",
          "command": "/usr/local/bin/node",
          "args": [],
          "env": []
        }
        """;

        var server = JsonSerializer.Deserialize<McpServer>(json);

        Assert.IsType<StdioMcpServer>(server);
        var stdio = (StdioMcpServer)server!;
        Assert.Equal("test-server", stdio.Name);
        Assert.Equal("/usr/local/bin/node", stdio.Command);
    }

    [Fact]
    public void McpServer_StdioWithoutArgs_Should_Deserialize_WithNullArgs()
    {
        // V2 将 stdio 的 args 放宽为可选；缺省时忠实还原为 null（区别于「显式空数组」），
        // client 不再反向收紧为必填。
        var json = """
        {
          "name": "test-server",
          "command": "/usr/local/bin/node",
          "env": []
        }
        """;

        var server = JsonSerializer.Deserialize<McpServer>(json);

        Assert.IsType<StdioMcpServer>(server);
        var stdio = (StdioMcpServer)server!;
        Assert.Null(stdio.Args);
    }

    [Fact]
    public void McpServer_StdioWithNonArrayArgs_Should_NotDeserialize()
    {
        // 类型契约不放宽：args 一旦提供却非数组，仍视为协议违规抛出，
        // 不做反向的过度容忍。
        var json = """
        {
          "name": "test-server",
          "command": "/usr/local/bin/node",
          "args": "server.js",
          "env": []
        }
        """;

        Assert.Throws<JsonException>((Action)(() => JsonSerializer.Deserialize<McpServer>(json)));
    }

    [Fact]
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

        Assert.Throws<JsonException>((Action)(() => JsonSerializer.Deserialize<McpServer>(json)));
    }

    [Fact]
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
        Assert.True(parsed.RootElement.TryGetProperty("mcpServers", out var mcpServers));
        Assert.Equal(JsonValueKind.Array, mcpServers.ValueKind);
    }

    [Fact]
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
        Assert.DoesNotContain("\"mcpServers\":{}", json);
        Assert.Contains("\"mcpServers\":[]", json);
    }

    [Fact]
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

        Assert.NotNull(response);
        Assert.NotNull(response!.Modes);
        Assert.Equal("default", response.Modes!.CurrentModeId);
        var mode = Assert.Single(response.Modes.AvailableModes);
        Assert.Equal("default", mode.Id);
    }

    [Fact]
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

        Assert.Throws<JsonException>((() => JsonSerializer.Deserialize<SessionNewResponse>(json)));
    }

    [Fact]
    public void McpServer_WithUnknownTransport_Should_Deserialize_As_CustomAndPreserveRawPayload()
    {
        // V2 schema "other" 分支：非 stdio/http/sse 的 transport，client 不认识也不收紧，
        // 按 spec「preserve the raw payload」原样保留，交由 Agent 决定接受或拒绝。
        var json = """
        {
          "type": "_experimental-grpc",
          "name": "future-server",
          "endpoint": "grpc://future.example.com",
          "customField": { "nested": [1, 2, 3] }
        }
        """;

        var server = JsonSerializer.Deserialize<McpServer>(json);

        var custom = Assert.IsType<CustomMcpServer>(server);
        Assert.Equal("_experimental-grpc", custom.Transport);
        Assert.Equal("future-server", custom.Name);
        Assert.Equal(JsonValueKind.Object, custom.RawPayload.ValueKind);
        Assert.Equal("grpc://future.example.com", custom.RawPayload.GetProperty("endpoint").GetString());
    }

    [Fact]
    public void McpServer_WithUnknownTransport_Should_RoundTrip_UnknownFieldsIntact()
    {
        var json = """
        {
          "type": "_experimental-grpc",
          "name": "future-server",
          "endpoint": "grpc://future.example.com",
          "customField": { "nested": [1, 2, 3] }
        }
        """;

        var server = JsonSerializer.Deserialize<McpServer>(json);
        var reserialized = JsonSerializer.Serialize(server);
        var parsed = JsonDocument.Parse(reserialized);

        // 前向兼容透传：重新序列化必须原样带回全部未知字段，不丢弃、不重排语义。
        Assert.Equal("_experimental-grpc", parsed.RootElement.GetProperty("type").GetString());
        Assert.Equal("future-server", parsed.RootElement.GetProperty("name").GetString());
        Assert.Equal("grpc://future.example.com", parsed.RootElement.GetProperty("endpoint").GetString());
        Assert.Equal(1, parsed.RootElement.GetProperty("customField").GetProperty("nested")[0].GetInt32());
    }

    [Fact]
    public void CustomMcpServer_Should_Clone_RawPayload_Independently()
    {
        var json = """
        {
          "type": "_experimental-grpc",
          "name": "future-server",
          "endpoint": "grpc://future.example.com"
        }
        """;

        var custom = (CustomMcpServer)JsonSerializer.Deserialize<McpServer>(json)!;
        var clone = (CustomMcpServer)McpServerJsonConverter.CloneServer(custom);

        Assert.Equal(custom.Transport, clone.Transport);
        Assert.Equal(custom.Name, clone.Name);
        Assert.Equal("grpc://future.example.com", clone.RawPayload.GetProperty("endpoint").GetString());
    }
}
