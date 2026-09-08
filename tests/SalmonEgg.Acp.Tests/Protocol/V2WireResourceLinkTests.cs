using System.Text.Json;
using SalmonEgg.Acp.Content;
using SalmonEgg.Acp.Protocol;

namespace SalmonEgg.Acp.Tests.Protocol;

public sealed class V2WireResourceLinkTests
{
    [Theory]
    [InlineData("")]
    [InlineData(",\"icons\":null")]
    [InlineData(",\"icons\":false")]
    [InlineData(",\"icons\":{}")]
    public void GetIcons_DefaultableMetadata_ReturnsNoIcons(string property)
    {
        // Arrange
        var json = $$"""{"type":"resource_link","uri":"https://example.test/doc","name":"Doc"{{property}}}""";
        var resource = Assert.IsType<ResourceLinkContentBlock>(JsonSerializer.Deserialize(json, Wire.V2<ContentBlock>()));

        // Act / Assert
        Assert.Empty(resource.GetIcons());
    }

    [Fact]
    public void GetIcons_InvalidEntries_KeepTheValidSuccessorAndUnknownFields()
    {
        // Arrange
        const string json = """
            {"type":"resource_link","uri":"https://example.test/doc","name":"Doc","icons":[
              {},null,42,{"src":null},{"src":false},
              {"src":"https://example.test/icon.svg","sizes":[false,"any",null,"48x48"],"mimeType":42,"theme":"future","vendor":{"size":1.20e+02}}
            ]}
            """;
        var resource = Assert.IsType<ResourceLinkContentBlock>(JsonSerializer.Deserialize(json, Wire.V2<ContentBlock>()));

        // Act
        var icon = Assert.Single(resource.GetIcons());
        using var replay = JsonDocument.Parse(JsonSerializer.Serialize(icon, Wire.V2<Icon>()));

        // Assert
        Assert.Equal("https://example.test/icon.svg", icon.Src);
        Assert.Equal(new[] { "any", "48x48" }, icon.Sizes);
        Assert.Null(icon.MimeType);
        Assert.Equal("future", icon.Theme);
        Assert.Equal("{\"size\":1.20e+02}", replay.RootElement.GetProperty("vendor").GetRawText());
    }

    [Fact]
    public void WithIcons_DraftMetadata_UsesTheSameWireAtEveryRoot()
    {
        // Arrange
        var resource = new ResourceLinkContentBlock("https://example.test/doc", "Doc");
        var updated = resource.WithIcons([new Icon { Src = "https://example.test/icon.svg", Theme = "future" }]);
        var prompt = new SessionPromptParams("session", [updated]);

        // Act
        using var direct = JsonDocument.Parse(JsonSerializer.Serialize(updated, Wire.V2<ResourceLinkContentBlock>()));
        using var content = JsonDocument.Parse(JsonSerializer.Serialize<ContentBlock>(updated, Wire.V2<ContentBlock>()));
        using var list = JsonDocument.Parse(JsonSerializer.Serialize(new List<ContentBlock> { updated }, Wire.V2<List<ContentBlock>>()));
        using var parent = JsonDocument.Parse(JsonSerializer.Serialize(prompt, Wire.V2<SessionPromptParams>()));
        var restored = Assert.IsType<SessionPromptParams>(JsonSerializer.Deserialize(parent.RootElement, Wire.V2<SessionPromptParams>()));

        // Assert
        Assert.Empty(resource.GetIcons());
        Assert.Equal("future", Assert.Single(updated.GetIcons()).Theme);
        Assert.True(JsonElement.DeepEquals(direct.RootElement, content.RootElement));
        Assert.True(JsonElement.DeepEquals(direct.RootElement, list.RootElement[0]));
        Assert.True(JsonElement.DeepEquals(direct.RootElement, parent.RootElement.GetProperty("prompt")[0]));
        Assert.Equal("future", Assert.Single(Assert.IsType<ResourceLinkContentBlock>(Assert.Single(restored.Prompt)).GetIcons()).Theme);
    }

    [Fact]
    public void WithIcons_StableWire_RejectsDraftMetadataAtEveryRoot()
    {
        // Arrange
        var resource = new ResourceLinkContentBlock("https://example.test/doc", "Doc")
            .WithIcons([new Icon { Src = "https://example.test/icon.svg" }]);

        // Act / Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(resource, Wire.V1<ResourceLinkContentBlock>()));
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize<ContentBlock>(resource, Wire.V1<ContentBlock>()));
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(new List<ContentBlock> { resource }, Wire.V1<List<ContentBlock>>()));
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(new SessionPromptParams("session", [resource]), Wire.V1<SessionPromptParams>()));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("false")]
    [InlineData("{\"src\":null}")]
    [InlineData("{\"src\":42}")]
    public void Icon_InvalidRequiredSource_RejectsTheDirectContract(string json)
    {
        // Arrange / Act / Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(json, Wire.V2<Icon>()));
    }

    [Fact]
    public void WithIcons_ArbitraryTheme_PreservesTheValueInAParentPrompt()
        => FsCheckPropertyRunner.Run(this, nameof(IconThemeRoundTripProperty));

    private void IconThemeRoundTripProperty(string? theme)
    {
        // Arrange
        var resource = new ResourceLinkContentBlock("https://example.test/doc", "Doc")
            .WithIcons([new Icon { Src = "https://example.test/icon.svg", Theme = theme }]);

        // Act
        var json = JsonSerializer.Serialize(new SessionPromptParams("session", [resource]), Wire.V2<SessionPromptParams>());
        var restored = Assert.IsType<SessionPromptParams>(JsonSerializer.Deserialize(json, Wire.V2<SessionPromptParams>()));

        // Assert
        var restoredResource = Assert.IsType<ResourceLinkContentBlock>(Assert.Single(restored.Prompt));
        Assert.Equal(theme, Assert.Single(restoredResource.GetIcons()).Theme);
    }
}
