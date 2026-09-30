using System.Text.Json;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Acp.Serialization;
using Xunit;

namespace SalmonEgg.Acp.Tests.Protocol;

/// <summary>
/// The Tool Call Name RFD stabilized an optional <c>name</c> on tool calls in ACP v1 and v2. This SDK
/// already carried it losslessly inside <see cref="SessionUpdate.ExtensionData"/> as an unknown field;
/// these cases pin the part that was missing — a typed, readable, round-trippable name on both the v1
/// <c>tool_call</c> shape and the upsert shape the two versions share.
/// </summary>
public sealed class ToolCallNameTests
{
    private static SessionUpdate? Parse(int version, string updateJson) =>
        JsonSerializer.Deserialize(
            $"{{\"sessionId\":\"session-1\",\"update\":{updateJson}}}",
            AcpWireFormat.For(version).TypeInfo<SessionUpdateParams>())?.Update;

    private static string RoundTrip(int version, string updateJson)
    {
        var update = Parse(version, updateJson);
        return JsonSerializer.Serialize(
            new SessionUpdateParams { SessionId = "session-1", Update = update! },
            AcpWireFormat.For(version).TypeInfo<SessionUpdateParams>());
    }

    [Fact]
    public void V1ToolCall_ExposesTheProgrammaticName()
    {
        var update = Assert.IsType<ToolCallUpdate>(Parse(
            AcpProtocolVersion.V1,
            "{\"sessionUpdate\":\"tool_call\",\"toolCallId\":\"tc-1\",\"name\":\"read_file\","
            + "\"title\":\"Reading configuration file\",\"kind\":\"read\"}"));

        Assert.Equal("read_file", update.Name);
        // A name that stayed in ExtensionData would read as "absent" here while still round-tripping,
        // so the assertion above alone cannot tell the two apart; this one can.
        Assert.False(update.ExtensionData?.ContainsKey("name") == true);
    }

    [Theory]
    [InlineData(AcpProtocolVersion.V1)]
    [InlineData(AcpProtocolVersion.V2)]
    public void ToolCallUpdate_ExposesTheProgrammaticName(int version)
    {
        var update = Assert.IsType<ToolCallStatusUpdate>(Parse(
            version,
            "{\"sessionUpdate\":\"tool_call_update\",\"toolCallId\":\"tc-1\",\"name\":\"run_command\","
            + "\"status\":\"in_progress\"}"));

        Assert.Equal("run_command", update.Name);
        Assert.False(update.ExtensionData?.ContainsKey("name") == true);
    }

    // The name is informational, so a proxy that only forwards updates it cannot interpret still has to
    // reproduce it — that is the one capability loss the SDK itself has to rule out.
    [Theory]
    [InlineData(AcpProtocolVersion.V1, "tool_call")]
    [InlineData(AcpProtocolVersion.V1, "tool_call_update")]
    [InlineData(AcpProtocolVersion.V2, "tool_call_update")]
    public void ToolCallName_SurvivesAFullRoundTrip(int version, string discriminator)
    {
        using var replayed = JsonDocument.Parse(
            RoundTrip(version, $"{{\"sessionUpdate\":\"{discriminator}\",\"toolCallId\":\"tc-1\",\"name\":\"grep\"}}"));

        Assert.Equal("grep", replayed.RootElement.GetProperty("update").GetProperty("name").GetString());
    }

    [Fact]
    public void ToolCallUpdate_WithoutAName_ReadsAsAbsentRatherThanEmpty()
    {
        var update = Assert.IsType<ToolCallStatusUpdate>(Parse(
            AcpProtocolVersion.V1,
            "{\"sessionUpdate\":\"tool_call_update\",\"toolCallId\":\"tc-1\",\"status\":\"completed\"}"));

        Assert.Null(update.Name);
    }
}
