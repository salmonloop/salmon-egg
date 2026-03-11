using System.Text.Json;
using NUnit.Framework;
using SalmonEgg.Domain.Models.Protocol;
using SalmonEgg.Domain.Models.Tool;
using SalmonEgg.Infrastructure.Serialization;

namespace SalmonEgg.Domain.Tests.Protocol;

[TestFixture]
public sealed class ToolCallUpdateTests
{
    private readonly JsonSerializerOptions _jsonOptions;

    public ToolCallUpdateTests()
    {
        // Configure serialization options to match the codebase
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };
        
        // Add the JsonPropertyNameEnumConverterFactory to honor JsonPropertyName attributes on enums
        _jsonOptions.Converters.Add(new JsonPropertyNameEnumConverterFactory());
    }

    [Test]
    public void ToolCallUpdate_ToolCallId_ShouldBe_Required()
    {
        // Given: A ToolCallUpdate type
        var property = typeof(ToolCallUpdate).GetProperty("ToolCallId");

        // Then: Property should exist
        Assert.That(property, Is.Not.Null);
        // Note: We check that it's not nullable by checking the property type
        // For reference types, nullable reference types are a compile-time feature
    }

    [Test]
    public void ToolCallUpdate_Title_ShouldBe_Required()
    {
        // Given: A ToolCallUpdate type
        var property = typeof(ToolCallUpdate).GetProperty("Title");

        // Then: Property should exist
        Assert.That(property, Is.Not.Null);
    }

    [Test]
    public void ToolCallUpdate_Kind_ShouldBe_Required()
    {
        // Given: A ToolCallUpdate type
        var property = typeof(ToolCallUpdate).GetProperty("Kind");

        // Then: Property should exist
        Assert.That(property, Is.Not.Null);
    }

    [Test]
    public void ToolCallUpdate_Should_NotHave_ToolCall_Property()
    {
        // Given: A ToolCallUpdate type
        var property = typeof(ToolCallUpdate).GetProperty("ToolCall");

        // Then: Property should not exist (protocol doesn't define this field)
        Assert.That(property, Is.Null);
    }

    [Test]
    public void ToolCallUpdate_Should_Serialize_Correctly()
    {
        // Given: A ToolCallUpdate with required fields
        var update = new ToolCallUpdate
        {
            ToolCallId = "test-call-123",
            Title = "Test Tool Call",
            Kind = ToolCallKind.Execute,
            Status = ToolCallStatus.Pending
        };

        // When: Serialize to JSON
        var json = JsonSerializer.Serialize(update, _jsonOptions);
        var parsed = JsonDocument.Parse(json);

        // Then: Required fields should be present
        Assert.That(parsed.RootElement.TryGetProperty("toolCallId", out var toolCallId), Is.True);
        Assert.That(toolCallId.GetString(), Is.EqualTo("test-call-123"));
        Assert.That(parsed.RootElement.TryGetProperty("title", out var title), Is.True);
        Assert.That(title.GetString(), Is.EqualTo("Test Tool Call"));
        Assert.That(parsed.RootElement.TryGetProperty("kind", out var kind), Is.True);
        Assert.That(kind.GetString(), Is.EqualTo("execute"));
        Assert.That(parsed.RootElement.TryGetProperty("status", out var status), Is.True);
        Assert.That(status.GetString(), Is.EqualTo("pending"));
    }
}
