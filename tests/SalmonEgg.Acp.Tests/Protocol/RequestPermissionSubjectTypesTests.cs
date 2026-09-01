using System.Text.Json;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Acp.Serialization;
using Xunit;

namespace SalmonEgg.Acp.Tests.Protocol;

public sealed class RequestPermissionSubjectTypesTests
{
    [Fact]
    public void CommandSubject_RoundTripsAllKnownFields()
    {
        var subject = JsonSerializer.Deserialize<RequestPermissionSubject>(
            "{\"type\":\"command\",\"command\":\"git status\",\"cwd\":\"/work\",\"toolCallId\":\"tc\",\"terminalId\":\"t\"}",
            AcpJsonContext.Default.RequestPermissionSubject);
        var command = Assert.IsType<CommandPermissionSubject>(subject);
        Assert.Equal("git status", command.Command);
        Assert.Equal("/work", command.Cwd);
        Assert.Equal("tc", command.ToolCallId);
        var json = JsonSerializer.Serialize(subject, AcpJsonContext.Default.RequestPermissionSubject);
        Assert.Contains("\"type\":\"command\"", json, StringComparison.Ordinal);
        Assert.Contains("\"cwd\":\"/work\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ToolCallSubject_DeserializesNestedUpsert()
    {
        var subject = JsonSerializer.Deserialize<RequestPermissionSubject>(
            "{\"type\":\"tool_call\",\"toolCall\":{\"sessionUpdate\":\"tool_call\",\"toolCallId\":\"tc-1\",\"title\":\"Read\"}}",
            AcpJsonContext.Default.RequestPermissionSubject);
        var tool = Assert.IsType<ToolCallPermissionSubject>(subject);
        Assert.Equal("tc-1", tool.ToolCall.ToolCallId);
        Assert.Equal("Read", tool.ToolCall.Title);
    }

    [Fact]
    public void UnknownSubject_RoundTripsRawPayload()
    {
        const string Json = "{\"type\":\"_vendor_action\",\"payload\":{\"a\":1}}";
        var subject = JsonSerializer.Deserialize<RequestPermissionSubject>(Json, AcpJsonContext.Default.RequestPermissionSubject);
        var custom = Assert.IsType<CustomRequestPermissionSubject>(subject);
        Assert.Equal("_vendor_action", custom.Type);
        Assert.Equal(Json, JsonSerializer.Serialize(subject, AcpJsonContext.Default.RequestPermissionSubject));
    }
}
