using System.Text.Json;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Acp.Tool;

namespace SalmonEgg.Acp.Tests.Tool;

public sealed class StructuredDiffWireContractTests
{
    [Theory]
    [InlineData("")]
    [InlineData(",\"changes\":null")]
    [InlineData(",\"changes\":42")]
    [InlineData(",\"changes\":\"invalid\"")]
    public void DiffV2_MissingOrInvalidChanges_RejectsEveryRoot(string changes)
    {
        // Arrange
        var json = $$"""{"type":"diff","path":"/a","newText":"text"{{changes}}}""";

        // Act / Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(json, Wire.V2<ToolCallContent>()));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(json, Wire.V2<StructuredDiff>()));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize($"[{json}]", Wire.V2<List<ToolCallContent>>()));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(Envelope(json), Wire.V2<SessionUpdateParams>()));
    }

    [Fact]
    public void DiffV2_DirectRoot_UsesTheSameRecoveryAndPreservationContract()
    {
        // Arrange
        const string json = """{"type":"diff","changes":[{"operation":"modify"},{"operation":"_custom","extra":42}],"patch":{"format":"git_patch"}}""";

        // Act
        var direct = JsonSerializer.Deserialize(json, Wire.V2<StructuredDiff>());
        var parent = Assert.IsType<StructuredDiff>(JsonSerializer.Deserialize(json, Wire.V2<ToolCallContent>()));

        // Assert
        Assert.NotNull(direct);
        Assert.Single(direct.Changes);
        Assert.Null(direct.Patch);
        Assert.Equal(JsonSerializer.Serialize(parent, Wire.V2<ToolCallContent>()),
            JsonSerializer.Serialize(direct, Wire.V2<StructuredDiff>()));
    }

    [Fact]
    public void DiffV2_MetadataAndInvalidOptionalMetadata_FollowTheSchema()
    {
        // Arrange
        const string json = """{"type":"diff","changes":[{"operation":"add","path":"/a","_meta":{"vendor":{"id":7}}}],"_meta":{"trace":"t"}}""";
        const string invalidMetadata = """{"type":"diff","changes":[{"operation":"add","path":"/a","_meta":42}],"_meta":42}""";

        // Act
        var parsed = JsonSerializer.Deserialize(json, Wire.V2<StructuredDiff>());
        Assert.NotNull(parsed);
        using var written = JsonDocument.Parse(JsonSerializer.Serialize(parsed, Wire.V2<StructuredDiff>()));
        var recovered = JsonSerializer.Deserialize(invalidMetadata, Wire.V2<StructuredDiff>());

        // Assert
        Assert.Equal(7, written.RootElement.GetProperty("changes")[0].GetProperty("_meta").GetProperty("vendor").GetProperty("id").GetInt32());
        Assert.Equal("t", written.RootElement.GetProperty("_meta").GetProperty("trace").GetString());
        Assert.NotNull(recovered);
        Assert.Single(recovered.Changes);
        Assert.Null(recovered.Meta);
    }

    [Fact]
    public void DiffV2_StandaloneChange_PreservesUnknownFieldsAndRejectsMissingRequiredFields()
    {
        // Arrange
        const string json = """{"operation":"_future","extra":{"value":42}}""";

        // Act
        var change = JsonSerializer.Deserialize(json, Wire.V2<DiffChange>());

        // Assert
        Assert.NotNull(change);
        Assert.Equal(json, JsonSerializer.Serialize(change, Wire.V2<DiffChange>()));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize("""{"operation":"move","path":"/b"}""", Wire.V2<DiffChange>()));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize("""{"text":"patch"}""", Wire.V2<DiffPatch>()));
    }

    private static string Envelope(string content)
        => $$$"""{"sessionId":"s","update":{"sessionUpdate":"tool_call_content_chunk","toolCallId":"t","content":{{{content}}}}}""";
}
