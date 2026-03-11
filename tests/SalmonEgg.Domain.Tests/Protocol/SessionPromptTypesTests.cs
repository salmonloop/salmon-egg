using System.Collections.Generic;
using System.Text.Json;
using NUnit.Framework;
using SalmonEgg.Domain.Models.Content;
using SalmonEgg.Domain.Models.Protocol;

namespace SalmonEgg.Domain.Tests.Protocol;

[TestFixture]
public sealed class SessionPromptTypesTests
{
    [Test]
    public void SessionPromptParams_Prompt_ShouldBe_ListOfContentBlock()
    {
        // Given: A SessionPromptParams type
        var property = typeof(SessionPromptParams).GetProperty("Prompt");

        // Then: Property type should be List<ContentBlock>
        Assert.That(property, Is.Not.Null);
        Assert.That(property?.PropertyType, Is.EqualTo(typeof(List<ContentBlock>)));
    }

    [Test]
    public void SessionPromptParams_Prompt_Should_Serialize_As_Array()
    {
        // Given: A SessionPromptParams with content blocks
        var sessionParams = new SessionPromptParams
        {
            SessionId = "test-session",
            Prompt = new List<ContentBlock>
            {
                new TextContentBlock { Text = "Hello, world!" }
            }
        };

        // When: Serialize to JSON
        var json = JsonSerializer.Serialize(sessionParams);
        var parsed = JsonDocument.Parse(json);

        // Then: prompt should be an array in JSON
        Assert.That(parsed.RootElement.TryGetProperty("prompt", out var prompt), Is.True);
        Assert.That(prompt.ValueKind, Is.EqualTo(JsonValueKind.Array));
    }
}
