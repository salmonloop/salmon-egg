using System.Text.Json;
using Xunit;
using SalmonEgg.Acp.Tool;

namespace SalmonEgg.Acp.Tests.Tool;

public sealed class ToolCallContentPolymorphismTests
{
    [Fact]
    public void Deserialize_ContentToolCallContent_Works()
    {
        var json = """
        {
          "type": "content",
          "content": { "type": "text", "text": "hello" }
        }
        """;

        var parsed = JsonSerializer.Deserialize<ToolCallContent>(json, CreateJsonOptions());

        var content = Assert.IsType<ContentToolCallContent>(parsed);
        Assert.NotNull(content.Content);
    }

    [Fact]
    public void Deserialize_UnknownToolCallContent_FallsBackToBaseAndPreservesPayloadForRoundTrip()
    {
        // 协议未来可能新增 tool_call content 类型。client 不得因未知判别值抛异常丢掉整条
        // tool_call 更新,必须回落基类、原样保留 payload 并可 round-trip(由 Agent 决定语义)。
        var json = """
        {
          "type": "audio_stream",
          "streamId": "abc-123",
          "mimeType": "audio/wav"
        }
        """;

        var parsed = JsonSerializer.Deserialize<ToolCallContent>(json, CreateJsonOptions());

        var content = Assert.IsType<CustomToolCallContent>(parsed);
        Assert.Equal("audio_stream", content.Type);

        var serialized = JsonSerializer.Serialize<ToolCallContent>(content, CreateJsonOptions());
        Assert.Contains("audio_stream", serialized, StringComparison.Ordinal);
        Assert.Contains("streamId", serialized, StringComparison.Ordinal);
        Assert.Contains("abc-123", serialized, StringComparison.Ordinal);
        Assert.Contains("audio/wav", serialized, StringComparison.Ordinal);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
    }
}
