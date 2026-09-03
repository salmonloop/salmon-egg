using System.Text.Json;
using SalmonEgg.Acp.Content;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Acp.Serialization;
using SalmonEgg.Acp.Tool;
using Xunit;

namespace SalmonEgg.Acp.Tests.Protocol;

public sealed class ToolCallContentChunkUpdateTests
{
    private static SessionUpdateParams? Parse(string updateJson) =>
        JsonSerializer.Deserialize(
            "{\"sessionId\":\"session-1\",\"update\":" + updateJson + "}",
            Wire.V2<SessionUpdateParams>());

    private static SessionUpdateParams? ParseV1(string updateJson) =>
        JsonSerializer.Deserialize(
            "{\"sessionId\":\"session-1\",\"update\":" + updateJson + "}",
            Wire.V1<SessionUpdateParams>());

    [Fact]
    public void ToolCallContentChunk_MapsToolCallIdAndSingleContentItem()
    {
        var update = Assert.IsType<ToolCallContentChunkUpdate>(Parse(
            "{\"sessionUpdate\":\"tool_call_content_chunk\",\"toolCallId\":\"tc-1\","
            + "\"content\":{\"type\":\"content\",\"content\":{\"type\":\"text\",\"text\":\"frag\"}}}")?.Update);

        Assert.Equal("tc-1", update.ToolCallId);
        var content = Assert.IsType<ContentToolCallContent>(update.Content);
        Assert.Equal("frag", Assert.IsType<TextContentBlock>(content.Content).Text);
    }

    // The chunk carries a single content item and appends it, whereas tool_call_update's content is an
    // array that replaces. Streaming through the replacing form would mean resending everything
    // produced so far on every fragment.
    [Fact]
    public void ToolCallContentChunk_CarriesOneItem_WhileToolCallUpdateCarriesAReplacingArray()
    {
        var chunk = Assert.IsType<ToolCallContentChunkUpdate>(Parse(
            "{\"sessionUpdate\":\"tool_call_content_chunk\",\"toolCallId\":\"tc-1\","
            + "\"content\":{\"type\":\"content\",\"content\":{\"type\":\"text\",\"text\":\"a\"}}}")?.Update);
        // tool_call is the v1 way to open a tool call, and v2 removed it - the first tool_call_update
        // for an id creates it there. So the comparison has to cross versions: parsing the v1 variant
        // through the v2 contract is exactly the mirror of the defect this pair of surfaces fixes.
        const string toolCallJson =
            "{\"sessionUpdate\":\"tool_call\",\"toolCallId\":\"tc-1\","
            + "\"content\":[{\"type\":\"content\",\"content\":{\"type\":\"text\",\"text\":\"a\"}}]}";
        var replacing = Assert.IsType<ToolCallUpdate>(ParseV1(toolCallJson)?.Update);
        Assert.Equal("tool_call", Assert.IsType<SessionUpdate>(Parse(toolCallJson)?.Update).UnknownUpdateKind);

        Assert.IsType<ContentToolCallContent>(chunk.Content);
        Assert.Single(replacing.Content!);
    }

    [Fact]
    public void ToolCallContentChunk_CanCarryAStructuredDiffInV2()
    {
        var update = Assert.IsType<ToolCallContentChunkUpdate>(Parse(
            "{\"sessionUpdate\":\"tool_call_content_chunk\",\"toolCallId\":\"tc-2\","
            + "\"content\":{\"type\":\"diff\",\"changes\":[{\"operation\":\"add\",\"path\":\"/a\"}]}}")?.Update);

        var diff = Assert.IsType<StructuredDiff>(update.Content);
        Assert.Equal(DiffOperationKind.Add, Assert.Single(diff.Changes).Operation);
    }

    [Fact]
    public void ToolCallContentChunk_RoundTripsThroughTheSessionUpdateContract()
    {
        const string UpdateJson =
            "{\"sessionUpdate\":\"tool_call_content_chunk\",\"toolCallId\":\"tc-3\","
            + "\"content\":{\"type\":\"terminal\",\"terminalId\":\"t-1\"}}";

        var parsed = Parse(UpdateJson);
        var json = JsonSerializer.Serialize(parsed!, Wire.V2<SessionUpdateParams>());
        var reparsed = JsonSerializer.Deserialize(json, Wire.V2<SessionUpdateParams>());

        var update = Assert.IsType<ToolCallContentChunkUpdate>(reparsed?.Update);
        Assert.Equal("tc-3", update.ToolCallId);
        Assert.Equal("t-1", Assert.IsType<TerminalToolCallContent>(update.Content).TerminalId);
    }
}
