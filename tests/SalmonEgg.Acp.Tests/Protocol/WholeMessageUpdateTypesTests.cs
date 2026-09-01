using System.Text.Json;
using SalmonEgg.Acp.Content;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Acp.Serialization;
using Xunit;

namespace SalmonEgg.Acp.Tests.Protocol;

public sealed class WholeMessageUpdateTypesTests
{
    private static SessionUpdateParams? Parse(string updateJson) =>
        JsonSerializer.Deserialize(
            "{\"sessionId\":\"session-1\",\"update\":" + updateJson + "}",
            AcpJsonContext.Default.SessionUpdateParams);

    [Theory]
    [InlineData("agent_message", typeof(AgentWholeMessageUpdate))]
    [InlineData("user_message", typeof(UserWholeMessageUpdate))]
    [InlineData("agent_thought", typeof(AgentWholeThoughtUpdate))]
    public void WholeMessageUpdate_KnownDiscriminators_MapToTheirVariant(string discriminator, Type expected)
    {
        var parsed = Parse(
            "{\"sessionUpdate\":\"" + discriminator + "\",\"messageId\":\"m-1\","
            + "\"content\":[{\"type\":\"text\",\"text\":\"hi\"}]}");

        var update = Assert.IsAssignableFrom<WholeMessageUpdate>(parsed?.Update);
        Assert.IsType(expected, update);
        Assert.Equal("m-1", update.MessageId);
        Assert.True(update.HasContent);
        var block = Assert.Single(update.Content!);
        Assert.Equal("hi", Assert.IsType<TextContentBlock>(block).Text);
    }

    // content is three-state and each state is a different instruction. Absent means leave the message
    // unchanged; null means clear it. Collapsing them would turn "no change" into "erase".
    [Fact]
    public void WholeMessageUpdate_AbsentContent_IsDistinctFromNullContent()
    {
        var absent = Assert.IsType<AgentWholeMessageUpdate>(
            Parse("{\"sessionUpdate\":\"agent_message\",\"messageId\":\"m-1\"}")?.Update);
        var cleared = Assert.IsType<AgentWholeMessageUpdate>(
            Parse("{\"sessionUpdate\":\"agent_message\",\"messageId\":\"m-1\",\"content\":null}")?.Update);

        Assert.False(absent.HasContent);
        Assert.Null(absent.Content);

        Assert.True(cleared.HasContent);
        Assert.Null(cleared.Content);
    }

    [Fact]
    public void WholeMessageUpdate_EmptyContentArray_IsPresentAndEmptyRatherThanAbsent()
    {
        var update = Assert.IsType<AgentWholeMessageUpdate>(
            Parse("{\"sessionUpdate\":\"agent_message\",\"messageId\":\"m-1\",\"content\":[]}")?.Update);

        Assert.True(update.HasContent);
        Assert.NotNull(update.Content);
        Assert.Empty(update.Content!);
    }

    // v2 keeps both families, so the whole-message variants must not be confused with the streaming
    // chunk variants that share their message id space.
    [Fact]
    public void WholeMessageUpdate_DoesNotCollideWithTheChunkVariants()
    {
        var chunk = Parse(
            "{\"sessionUpdate\":\"agent_message_chunk\",\"messageId\":\"m-1\","
            + "\"content\":{\"type\":\"text\",\"text\":\"frag\"}}")?.Update;
        var whole = Parse(
            "{\"sessionUpdate\":\"agent_message\",\"messageId\":\"m-1\","
            + "\"content\":[{\"type\":\"text\",\"text\":\"frag\"}]}")?.Update;

        Assert.IsType<AgentMessageUpdate>(chunk);
        Assert.IsType<AgentWholeMessageUpdate>(whole);
        Assert.False(chunk is WholeMessageUpdate);
    }

    [Fact]
    public void WholeMessageUpdate_SerializesMessageIdAndContentArrayWithoutLeakingPresenceFlag()
    {
        var value = new SessionUpdateParams(
            "session-1",
            new AgentWholeMessageUpdate
            {
                MessageId = "m-1",
                Content = new List<ContentBlock> { new TextContentBlock("hi") },
                HasContent = true
            });

        var json = JsonSerializer.Serialize(value, AcpJsonContext.Default.SessionUpdateParams);

        using var document = JsonDocument.Parse(json);
        var update = document.RootElement.GetProperty("update");

        Assert.Equal("agent_message", update.GetProperty("sessionUpdate").GetString());
        Assert.Equal("m-1", update.GetProperty("messageId").GetString());
        Assert.Equal(JsonValueKind.Array, update.GetProperty("content").ValueKind);
        Assert.False(update.TryGetProperty("hasContent", out _));
        Assert.False(update.TryGetProperty("HasContent", out _));
    }

    [Fact]
    public void WholeMessageUpdate_RoundTripsMessageIdAndContentPresence()
    {
        const string UpdateJson =
            "{\"sessionUpdate\":\"user_message\",\"messageId\":\"m-7\","
            + "\"content\":[{\"type\":\"text\",\"text\":\"prompt\"}]}";

        var parsed = Parse(UpdateJson);
        var json = JsonSerializer.Serialize(parsed!, AcpJsonContext.Default.SessionUpdateParams);
        var reparsed = JsonSerializer.Deserialize(json, AcpJsonContext.Default.SessionUpdateParams);

        var update = Assert.IsType<UserWholeMessageUpdate>(reparsed?.Update);
        Assert.Equal("m-7", update.MessageId);
        Assert.True(update.HasContent);
        Assert.Single(update.Content!);
    }
}
