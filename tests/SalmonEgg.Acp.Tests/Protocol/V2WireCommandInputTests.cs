using System.Text.Json;
using SalmonEgg.Acp.Protocol;

namespace SalmonEgg.Acp.Tests.Protocol;

public sealed class V2WireCommandInputTests
{
    [Theory]
    [InlineData("{}")]
    [InlineData("false")]
    [InlineData("{\"hint\":\"value\"}")]
    [InlineData("{\"type\":null,\"hint\":\"value\"}")]
    [InlineData("{\"type\":42,\"hint\":\"value\"}")]
    [InlineData("{\"type\":\"text\"}")]
    [InlineData("{\"type\":\"text\",\"hint\":false}")]
    public void AvailableCommandInputV2_InvalidInput_RejectsTheRootAndDefaultsTheOptionalParent(string inputJson)
    {
        // Arrange
        var commandJson = $$"""{"name":"review","description":"Review","input":{{inputJson}}}""";

        // Act
        var command = Assert.IsType<AvailableCommand>(JsonSerializer.Deserialize(commandJson, Wire.V2<AvailableCommand>()));
        var list = JsonSerializer.Deserialize($"[{commandJson}]", Wire.V2<List<AvailableCommand>>());
        var update = Assert.IsType<SessionUpdateParams>(JsonSerializer.Deserialize(
            $$$"""{"sessionId":"session","update":{"sessionUpdate":"available_commands_update","availableCommands":[{{{commandJson}}}]}}""",
            Wire.V2<SessionUpdateParams>()));

        // Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(inputJson, Wire.V2<AvailableCommandInput>()));
        Assert.Null(command.Input);
        Assert.Null(Assert.Single(Assert.IsType<List<AvailableCommand>>(list)).Input);
        Assert.Null(Assert.Single(Assert.IsType<AvailableCommandsUpdate>(update.Update).AvailableCommands).Input);
    }

    [Theory]
    [InlineData("\"future\"")]
    [InlineData("false")]
    [InlineData("null")]
    public void AvailableCommandInputV1_UnknownTypeProperty_RemainsMetadata(string typeJson)
    {
        // Arrange
        var json = $$$"""{"type":{{{typeJson}}},"hint":"branch","vendor":{"format":1.20e+02}}""";

        // Act
        var input = Assert.IsType<AvailableCommandInput>(JsonSerializer.Deserialize(json, Wire.V1<AvailableCommandInput>()));
        using var replay = JsonDocument.Parse(JsonSerializer.Serialize(input, Wire.V1<AvailableCommandInput>()));

        // Assert
        Assert.Equal("branch", input.Hint);
        using var expected = JsonDocument.Parse(json);
        Assert.True(JsonElement.DeepEquals(expected.RootElement, replay.RootElement));
    }

    [Fact]
    public void AvailableCommandInputV2_UnknownDiscriminator_PreservesEveryParentBoundary()
    {
        // Arrange
        const string inputJson = """{"type":"_future","schema":{"fields":[1.20e+02]},"_meta":{"vendor":false}}""";
        var commandJson = $$"""{"name":"review","description":"Review","input":{{inputJson}}}""";
        var updateJson = $$$"""{"sessionId":"session","update":{"sessionUpdate":"available_commands_update","availableCommands":[{{{commandJson}}}]}}""";

        // Act
        var direct = Assert.IsType<AvailableCommandInput>(JsonSerializer.Deserialize(inputJson, Wire.V2<AvailableCommandInput>()));
        var parent = Assert.IsType<SessionUpdateParams>(JsonSerializer.Deserialize(updateJson, Wire.V2<SessionUpdateParams>()));
        using var replay = JsonDocument.Parse(JsonSerializer.Serialize(parent, Wire.V2<SessionUpdateParams>()));

        // Assert
        Assert.Equal(inputJson, JsonSerializer.Serialize(direct, Wire.V2<AvailableCommandInput>()));
        Assert.Equal(inputJson, replay.RootElement.GetProperty("update").GetProperty("availableCommands")[0].GetProperty("input").GetRawText());
    }

    [Fact]
    public void AvailableCommandInput_ArbitraryHint_RoundTripsOnBothVersions()
        => FsCheckPropertyRunner.Run(this, nameof(CommandHintRoundTripProperty));

    private void CommandHintRoundTripProperty(string? hint)
    {
        // Arrange
        var input = new AvailableCommandInput { Hint = hint ?? string.Empty };
        var command = new AvailableCommand { Name = "review", Description = "Review", Input = input };

        foreach (var version in new[] { AcpProtocolVersion.V1, AcpProtocolVersion.V2 })
        {
            // Act
            var json = JsonSerializer.Serialize(command, Wire.Of<AvailableCommand>(version));
            var restored = Assert.IsType<AvailableCommand>(JsonSerializer.Deserialize(json, Wire.Of<AvailableCommand>(version)));

            // Assert
            Assert.Equal(input.Hint, Assert.IsType<AvailableCommandInput>(restored.Input).Hint);
        }
    }
}
