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
    [InlineData("false")]
    [InlineData("17")]
    [InlineData("[]")]
    [InlineData("{}")]
    public void ReplaySession_InvalidOptionalMediaFields_PreservesImageAndResourcePayloads(string invalid)
    {
        // Arrange
        var image = "{\"type\":\"image\",\"data\":\"YQ==\",\"mimeType\":\"image/png\",\"uri\":" + invalid + "}";
        var link = "{\"type\":\"resource_link\",\"uri\":\"file:///kept\",\"name\":\"kept\",\"title\":" + invalid
            + ",\"description\":" + invalid + ",\"mimeType\":" + invalid + ",\"size\":false}";
        var textResource = "{\"type\":\"resource\",\"resource\":{\"uri\":\"file:///kept\",\"text\":\"kept\",\"mimeType\":" + invalid + "}}";
        var blobResource = "{\"type\":\"resource\",\"resource\":{\"uri\":\"file:///kept\",\"blob\":\"YQ==\",\"mimeType\":" + invalid + "}}";

        // Act
        var blocks = AcpSessionDraftExtensions.ReplaySession("session",
            [Update(image, false), Update(link, true), Update(textResource, true), Update(blobResource, true)]).Messages[0].Content;

        // Assert
        Assert.Equal(4, blocks.Length);
        var recoveredImage = Assert.IsType<ImageContentBlock>(blocks[0]);
        Assert.Equal("YQ==", recoveredImage.Data);
        Assert.Equal("image/png", recoveredImage.MimeType);
        Assert.Null(recoveredImage.Uri);
        var recoveredLink = Assert.IsType<ResourceLinkContentBlock>(blocks[1]);
        Assert.Equal("file:///kept", recoveredLink.Uri);
        Assert.Equal("kept", recoveredLink.Name);
        Assert.Null(recoveredLink.Title);
        Assert.Null(recoveredLink.Description);
        Assert.Null(recoveredLink.MimeType);
        Assert.Null(recoveredLink.Size);
        Assert.Equal("kept", Assert.IsType<ResourceContentBlock>(blocks[2]).Resource.Text);
        Assert.Equal("YQ==", Assert.IsType<ResourceContentBlock>(blocks[3]).Resource.Blob);
        Assert.Null(Assert.IsType<ResourceContentBlock>(blocks[2]).Resource.MimeType);
        Assert.Null(Assert.IsType<ResourceContentBlock>(blocks[3]).Resource.MimeType);
        foreach (var content in new[] { image, link, textResource, blobResource })
        {
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(content, Wire.V1<ContentBlock>()));
        }
    }

    [Theory]
    [InlineData("false")]
    [InlineData("\"large\"")]
    [InlineData("1.25")]
    [InlineData("9223372036854775808")]
    public void ResourceLink_InvalidOptionalSize_DefaultsWithoutDiscardingValidFields(string size)
    {
        // Arrange
        var content = "{\"type\":\"resource_link\",\"uri\":\"file:///kept\",\"name\":\"kept\",\"title\":\"title\",\"size\":" + size + "}";

        // Act
        var link = JsonSerializer.Deserialize(content, Wire.V2<ResourceLinkContentBlock>())!;

        // Assert
        Assert.Equal("title", link.Title);
        Assert.Null(link.Size);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(content, Wire.V1<ResourceLinkContentBlock>()));
    }

    [Theory]
    [InlineData("{\"type\":\"image\",\"mimeType\":\"image/png\"}")]
    [InlineData("{\"type\":\"image\",\"data\":42,\"mimeType\":\"image/png\"}")]
    [InlineData("{\"type\":\"audio\",\"data\":\"YQ==\",\"mimeType\":false}")]
    [InlineData("{\"type\":\"resource_link\",\"name\":\"name\",\"uri\":false}")]
    [InlineData("{\"type\":\"resource_link\",\"uri\":\"file:///kept\",\"name\":false}")]
    [InlineData("{\"type\":\"resource\",\"resource\":{\"uri\":\"file:///kept\"}}")]
    [InlineData("{\"type\":\"resource\",\"resource\":{\"uri\":\"file:///kept\",\"text\":null,\"blob\":null}}")]
    [InlineData("{\"type\":\"resource\",\"resource\":{\"uri\":\"file:///kept\",\"text\":17,\"blob\":false}}")]
    public void ReplaySession_InvalidRequiredMediaFields_RejectsChunksAndSkipsInvalidWholeItems(string content)
    {
        // Arrange / Act / Assert
        Assert.Throws<JsonException>(() => AcpSessionDraftExtensions.ReplaySession("session", [Update(content, true)]));
        Assert.Empty(AcpSessionDraftExtensions.ReplaySession("session", [Update(content, false)]).Messages[0].Content);
    }

    [Theory]
    [InlineData("\"text\":\"kept\",\"blob\":17", "kept", null)]
    [InlineData("\"text\":17,\"blob\":\"YQ==\"", null, "YQ==")]
    [InlineData("\"text\":\"kept\",\"blob\":\"YQ==\"", "kept", null)]
    [InlineData("\"text\":\"\"", "", null)]
    [InlineData("\"blob\":\"\"", null, "")]
    public void ReplaySession_EmbeddedResourceUnion_UsesTheFirstValidSchemaBranch(string fields, string? text, string? blob)
    {
        // Arrange
        var content = "{\"type\":\"resource\",\"resource\":{\"uri\":\"file:///kept\"," + fields + ",\"mimeType\":false}}";

        // Act
        var resource = Assert.IsType<ResourceContentBlock>(AcpSessionDraftExtensions.ReplaySession("session", [Update(content, true)]).Messages[0].Content[0]).Resource;

        // Assert
        Assert.Equal(text, resource.Text);
        Assert.Equal(blob, resource.Blob);
        Assert.Null(resource.MimeType);
    }

    [Fact]
    public void ResourceLink_OptionalFieldRecovery_PreservesArbitraryValidSiblings()
        => FsCheckPropertyRunner.Run(this, nameof(OptionalFieldRecoveryProperty));

    private void OptionalFieldRecoveryProperty(string? title, long size, byte seed)
    {
        // Arrange
        var invalid = new[] { "false", "17", "{}", "[]", "null" }[seed % 5];
        var fields = "{\"type\":\"resource_link\",\"uri\":\"file:///kept\",\"name\":\"kept\",\"title\":"
            + JsonSerializer.Serialize(title, AcpJsonContext.Default.String) + ",\"description\":" + invalid
            + ",\"size\":" + size.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}";

        // Act
        var link = Assert.IsType<ResourceLinkContentBlock>(AcpSessionDraftExtensions.ReplaySession("session", [Update(fields, false)]).Messages[0].Content[0]);

        // Assert
        Assert.Equal(title, link.Title);
        Assert.Equal(size, link.Size);
        Assert.Null(link.Description);
        Assert.Equal("file:///kept", link.Uri);
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
