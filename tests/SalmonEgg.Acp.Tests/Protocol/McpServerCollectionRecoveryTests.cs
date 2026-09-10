using System.Text.Json;
using SalmonEgg.Acp.Mcp;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Acp.Serialization;

namespace SalmonEgg.Acp.Tests.Protocol;

public sealed class McpServerCollectionRecoveryTests
{
    public static TheoryData<int, string> RequestContracts => new()
    {
        { AcpProtocolVersion.V1, "new" },
        { AcpProtocolVersion.V1, "load" },
        { AcpProtocolVersion.V1, "resume" },
        { AcpProtocolVersion.V2, "new" },
        { AcpProtocolVersion.V2, "resume" }
    };

    [Theory]
    [MemberData(nameof(RequestContracts))]
    public void Deserialize_InvalidItems_PreservesValidServersAndUnknownPayloads(int version, string method)
    {
        // Arrange
        const string future = """{"type":"vendor_pipe","name":"future","nested":{"number":1.20e+02,"text":"\u4f60"}}""";
        var json = $$"""
            {"sessionId":"session","cwd":"/tmp","mcpServers":[
              42,null,[],{},
              {"type":null,"name":"bad","command":"mcp"},
              {"type":"stdio","name":"bad-args","command":"mcp","args":42},
              {"type":"stdio","name":"good","command":"mcp","args":["serve"]},
              {{future}},
              {"type":"http","name":"http","url":"https://example.test/mcp","headers":[]}
            ]}
            """;

        // Act
        var servers = ReadServers(json, version, method);
        using var replay = JsonDocument.Parse(RoundTrip(json, version, method));

        // Assert
        Assert.Equal(["good", "future", "http"], servers.Select(server => server.Name));
        Assert.Equal(["serve"], Assert.IsType<StdioMcpServer>(servers[0]).Args);
        Assert.IsType<CustomMcpServer>(servers[1]);
        Assert.IsType<HttpMcpServer>(servers[2]);
        Assert.Equal(future, replay.RootElement.GetProperty("mcpServers")[1].GetRawText());
    }

    [Theory]
    [MemberData(nameof(RequestContracts))]
    public void Deserialize_InvalidCollection_UsesEmptyDefault(int version, string method)
    {
        // Arrange
        string[] invalidValues = ["null", "42", "true", "\"invalid\"", "{}"];

        foreach (var rawValue in invalidValues)
        {
            var json = $$"""{"sessionId":"session","cwd":"/tmp","mcpServers":{{rawValue}}}""";

            // Act
            var servers = ReadServers(json, version, method);

            // Assert
            Assert.Empty(servers);
        }
    }

    [Theory]
    [InlineData(AcpProtocolVersion.V1, "resume")]
    [InlineData(AcpProtocolVersion.V2, "new")]
    [InlineData(AcpProtocolVersion.V2, "resume")]
    public void Deserialize_OmittedOptionalCollection_UsesEmptyDefault(int version, string method)
    {
        // Arrange
        const string json = """{"sessionId":"session","cwd":"/tmp"}""";

        // Act
        var servers = ReadServers(json, version, method);

        // Assert
        Assert.Empty(servers);
    }

    [Theory]
    [InlineData("new")]
    [InlineData("resume")]
    public void Deserialize_MissingDiscriminator_UsesOnlyTheNegotiatedDefault(string method)
    {
        // Arrange
        const string json = """
            {"sessionId":"session","cwd":"/tmp","mcpServers":[
              {"name":"legacy","command":"mcp"},
              {"type":"stdio","name":"explicit","command":"mcp"}
            ]}
            """;

        // Act
        var stableServers = ReadServers(json, AcpProtocolVersion.V1, method);
        var draftServers = ReadServers(json, AcpProtocolVersion.V2, method);

        // Assert
        Assert.Equal(["legacy", "explicit"], stableServers.Select(server => server.Name));
        Assert.Equal("explicit", Assert.Single(draftServers).Name);
    }

    [Theory]
    [InlineData(AcpProtocolVersion.V1)]
    [InlineData(AcpProtocolVersion.V2)]
    public void Deserialize_StandaloneMalformedServerOrList_RemainsStrict(int version)
    {
        // Arrange
        const string json = """{"type":42,"name":"bad","command":"mcp"}""";

        // Act / Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(json, Wire.Of<McpServer>(version)));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize($"[{json}]", Wire.Of<List<McpServer>>(version)));
    }

    [Theory]
    [InlineData("new")]
    [InlineData("load")]
    [InlineData("resume")]
    public void Deserialize_DefaultGeneratedContext_UsesStableCollectionRecovery(string method)
    {
        // Arrange
        const string json = """
            {"sessionId":"session","cwd":"/tmp","mcpServers":[42,{"name":"good","command":"mcp"}]}
            """;

        // Act
        var replayJson = method switch
        {
            "new" => JsonSerializer.Serialize(Assert.IsType<SessionNewParams>(JsonSerializer.Deserialize(json, AcpJsonContext.Default.SessionNewParams)), AcpJsonContext.Default.SessionNewParams),
            "load" => JsonSerializer.Serialize(Assert.IsType<SessionLoadParams>(JsonSerializer.Deserialize(json, AcpJsonContext.Default.SessionLoadParams)), AcpJsonContext.Default.SessionLoadParams),
            _ => JsonSerializer.Serialize(Assert.IsType<SessionResumeParams>(JsonSerializer.Deserialize(json, AcpJsonContext.Default.SessionResumeParams)), AcpJsonContext.Default.SessionResumeParams)
        };
        using var replay = JsonDocument.Parse(replayJson);

        // Assert
        Assert.Equal("good", Assert.Single(replay.RootElement.GetProperty("mcpServers").EnumerateArray()).GetProperty("name").GetString());
        Assert.False(replay.RootElement.GetProperty("mcpServers")[0].TryGetProperty("type", out _));
    }

    private static List<McpServer> ReadServers(string json, int version, string method) => method switch
    {
        "new" => JsonSerializer.Deserialize(json, Wire.Of<SessionNewParams>(version))!.McpServers,
        "load" => JsonSerializer.Deserialize(json, Wire.Of<SessionLoadParams>(version))!.McpServers,
        "resume" => JsonSerializer.Deserialize(json, Wire.Of<SessionResumeParams>(version))!.McpServers,
        _ => throw new ArgumentOutOfRangeException(nameof(method))
    };

    private static string RoundTrip(string json, int version, string method) => method switch
    {
        "new" => JsonSerializer.Serialize(Assert.IsType<SessionNewParams>(JsonSerializer.Deserialize(json, Wire.Of<SessionNewParams>(version))), Wire.Of<SessionNewParams>(version)),
        "load" => JsonSerializer.Serialize(Assert.IsType<SessionLoadParams>(JsonSerializer.Deserialize(json, Wire.Of<SessionLoadParams>(version))), Wire.Of<SessionLoadParams>(version)),
        "resume" => JsonSerializer.Serialize(Assert.IsType<SessionResumeParams>(JsonSerializer.Deserialize(json, Wire.Of<SessionResumeParams>(version))), Wire.Of<SessionResumeParams>(version)),
        _ => throw new ArgumentOutOfRangeException(nameof(method))
    };
}
