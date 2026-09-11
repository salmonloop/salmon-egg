using System.Text.Json;
using SalmonEgg.Acp.Client;
using SalmonEgg.Acp.Content;
using SalmonEgg.Acp.Serialization;
using SalmonEgg.Acp.Tool;

namespace SalmonEgg.Acp.Tests.Client;

public sealed class AcpSessionContentRecoveryTests
{
    public static TheoryData<string, string> ContentCases => new()
    {
        { "text", "\"text\":\"kept\"" },
        { "image", "\"data\":\"YQ==\",\"mimeType\":\"image/png\"" },
        { "audio", "\"data\":\"YQ==\",\"mimeType\":\"audio/wav\"" },
        { "resource_link", "\"name\":\"kept\",\"uri\":\"file:///kept\"" },
        { "resource", "\"resource\":{\"uri\":\"file:///kept\",\"text\":\"kept\"}" }
    };

    [Theory]
    [MemberData(nameof(ContentCases))]
    public void ReplaySession_InvalidContentAnnotationsAndMetadata_PreservesWholeAndChunkContent(string type, string fields)
    {
        // Arrange
        var content = "{\"type\":\"" + type + "\"," + fields + ",\"annotations\":false,\"_meta\":false}";
        var whole = AcpSessionDraftExtensions.ReplaySession("session", [Update(content, chunk: false)]);

        // Act
        var snapshot = AcpSessionDraftExtensions.ReplaySession("session", [Update(content, chunk: false), Update(content, chunk: true)]);

        // Assert
        var message = Assert.Single(snapshot.Messages);
        Assert.Single(whole.Messages[0].Content);
        Assert.Equal(2, message.Content.Length);
        foreach (var block in message.Content)
        {
            Assert.Equal(type, block.Type);
            Assert.Null(block.Annotations);
            Assert.Null(block.Meta);
            AssertPayloadPreserved(block);
        }
    }

    [Fact]
    public void ReplaySession_InvalidAnnotationFields_DefaultsOnlyTheInvalidFields()
    {
        // Arrange
        var content = """{"type":"text","text":"kept","annotations":{"audience":["user",17,"assistant"],"priority":false,"lastModified":"2026-09-11T00:00:00Z","_meta":false},"_meta":{"source":"agent"}}""";

        // Act
        var message = Assert.Single(AcpSessionDraftExtensions.ReplaySession("session", [Update(content, chunk: true)]).Messages);

        // Assert
        var text = Assert.IsType<TextContentBlock>(Assert.Single(message.Content));
        Assert.Equal("kept", text.Text);
        Assert.Equal(["user", "assistant"], text.Annotations!.Audience);
        Assert.Null(text.Annotations.Priority);
        Assert.Equal("2026-09-11T00:00:00Z", text.Annotations.LastModified);
        Assert.Null(text.Annotations.Meta);
        Assert.Equal("agent", Assert.IsType<JsonElement>(text.Meta!["source"]).GetString());
    }

    [Fact]
    public void ReplaySession_InvalidAnnotationCollectionAndTimestamp_PreservesValidPriority()
    {
        // Arrange
        var content = """{"type":"text","text":"kept","annotations":{"audience":false,"priority":0.5,"lastModified":17,"_meta":{"source":"annotations"}}}""";

        // Act
        var message = Assert.Single(AcpSessionDraftExtensions.ReplaySession("session", [Update(content, chunk: false)]).Messages);

        // Assert
        var text = Assert.IsType<TextContentBlock>(Assert.Single(message.Content));
        Assert.Null(text.Annotations!.Audience);
        Assert.Equal(0.5, text.Annotations.Priority);
        Assert.Null(text.Annotations.LastModified);
        Assert.Equal("annotations", Assert.IsType<JsonElement>(text.Annotations.Meta!["source"]).GetString());
    }

    [Theory]
    [InlineData("")]
    [InlineData(",\"text\":null")]
    [InlineData(",\"text\":42")]
    public void ReplaySession_InvalidRequiredText_SkipsOnlyInvalidWholeItemsAndRejectsChunks(string field)
    {
        // Arrange
        var invalid = "{\"type\":\"text\"" + field + ",\"annotations\":{}}";
        var whole = Json("{\"sessionUpdate\":\"agent_message\",\"messageId\":\"m\",\"content\":["
            + invalid + ",{\"type\":\"text\",\"text\":\"valid sibling\"}]}");

        // Act / Assert
        var snapshot = AcpSessionDraftExtensions.ReplaySession("session", [whole]);
        Assert.Equal("valid sibling", Assert.IsType<TextContentBlock>(Assert.Single(snapshot.Messages[0].Content)).Text);
        Assert.Throws<JsonException>(() => AcpSessionDraftExtensions.ReplaySession("session", [Update(invalid, chunk: true)]));
    }

    [Theory]
    [InlineData("_vendor")]
    [InlineData("future_kind")]
    public void ReplaySession_UnknownContentWithUnrecognizedMetadata_PreservesRawPayload(string type)
    {
        // Arrange
        var content = "{\"type\":\"" + type + "\",\"annotations\":false,\"_meta\":17,\"payload\":{\"number\":1.20e+02}}";

        // Act
        var block = Assert.Single(AcpSessionDraftExtensions.ReplaySession("session", [Update(content, chunk: true)]).Messages[0].Content);
        var replay = JsonSerializer.Serialize(block, Wire.V2<ContentBlock>());

        // Assert
        Assert.Equal(type, block.Type);
        Assert.Equal(content, replay);
    }

    [Fact]
    public void ReplaySession_ToolAndEmbeddedResourceMetadata_RecoversAtTheContentBoundary()
    {
        // Arrange
        var resource = """{"type":"resource","resource":{"uri":"file:///kept","text":"kept","_meta":false}}""";
        var tool = Json("""{"sessionUpdate":"tool_call_content_chunk","toolCallId":"tool","content":{"type":"content","content":{"type":"text","text":"tool text","annotations":false,"_meta":false}}}""");

        // Act
        var snapshot = AcpSessionDraftExtensions.ReplaySession("session", [Update(resource, chunk: true), tool]);

        // Assert
        var embedded = Assert.IsType<ResourceContentBlock>(Assert.Single(snapshot.Messages[0].Content)).Resource;
        Assert.Equal("kept", embedded.Text);
        Assert.Null(embedded.Meta);
        var toolText = Assert.IsType<TextContentBlock>(Assert.IsType<ContentToolCallContent>(Assert.Single(snapshot.ToolCalls[0].Content)).Content);
        Assert.Equal("tool text", toolText.Text);
        Assert.Null(toolText.Annotations);
        Assert.Null(toolText.Meta);
    }

    [Fact]
    public void ResourceLink_DirectDraftReader_UsesTheSameContentRecovery()
    {
        // Arrange
        const string content = """{"type":"resource_link","uri":"file:///kept","name":"kept","annotations":false,"_meta":false}""";

        // Act
        var link = JsonSerializer.Deserialize(content, Wire.V2<ResourceLinkContentBlock>());

        // Assert
        Assert.Equal("kept", link!.Name);
        Assert.Equal("file:///kept", link.Uri);
        Assert.Null(link.Annotations);
        Assert.Null(link.Meta);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(content, Wire.V1<ResourceLinkContentBlock>()));
    }

    [Theory]
    [InlineData("\"annotations\":false")]
    [InlineData("\"_meta\":false")]
    [InlineData("\"annotations\":{\"priority\":false}")]
    [InlineData("\"annotations\":{\"audience\":[\"user\",17]}")]
    public void StableContentReader_InvalidOptionalFields_RetainsItsExistingStrictContract(string field)
    {
        // Arrange
        var content = "{\"type\":\"text\",\"text\":\"kept\"," + field + "}";

        // Act / Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(content, AcpJsonContext.Default.ContentBlock));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(content, Wire.V1<ContentBlock>()));
    }

    private static void AssertPayloadPreserved(ContentBlock block)
    {
        switch (block)
        {
            case TextContentBlock text:
                Assert.Equal("kept", text.Text);
                break;
            case ImageContentBlock image:
                Assert.Equal("YQ==", image.Data);
                Assert.Equal("image/png", image.MimeType);
                break;
            case AudioContentBlock audio:
                Assert.Equal("YQ==", audio.Data);
                Assert.Equal("audio/wav", audio.MimeType);
                break;
            case ResourceLinkContentBlock link:
                Assert.Equal("kept", link.Name);
                Assert.Equal("file:///kept", link.Uri);
                break;
            case ResourceContentBlock resource:
                Assert.Equal("kept", resource.Resource.Text);
                Assert.Equal("file:///kept", resource.Resource.Uri);
                break;
            default:
                Assert.Fail("Expected a known content type.");
                break;
        }
    }

    private static JsonElement Update(string content, bool chunk)
        => Json("{\"sessionUpdate\":\"" + (chunk ? "agent_message_chunk" : "agent_message")
            + "\",\"messageId\":\"m\",\"content\":" + (chunk ? content : "[" + content + "]") + "}");

    private static JsonElement Json(string text)
    {
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }
}
