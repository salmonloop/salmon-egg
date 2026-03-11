using System.Text.Json;
using NUnit.Framework;
using SalmonEgg.Domain.Models.Protocol;

namespace SalmonEgg.Domain.Tests.Protocol;

[TestFixture]
public sealed class InitializeTypesTests
{
    [Test]
    public void InitializeParams_ProtocolVersion_ShouldBe_Integer()
    {
        // Given: An InitializeParams with protocol version
        var initializeParams = new InitializeParams
        {
            ProtocolVersion = 1
        };

        // When: Serialize to JSON
        var json = JsonSerializer.Serialize(initializeParams);
        var parsed = JsonDocument.Parse(json);

        // Then: ProtocolVersion should be an integer in JSON
        Assert.That(parsed.RootElement.GetProperty("protocolVersion").ValueKind, Is.EqualTo(JsonValueKind.Number));
    }

    [Test]
    public void InitializeParams_ProtocolVersion_ShouldNotBe_String()
    {
        // Given: An InitializeParams with protocol version
        var initializeParams = new InitializeParams
        {
            ProtocolVersion = 1
        };

        // When: Serialize to JSON
        var json = JsonSerializer.Serialize(initializeParams);

        // Then: JSON should not contain string version
        Assert.That(json, Does.Not.Contain("\"2024-11-05\""));
        Assert.That(json, Does.Contain("\"protocolVersion\":1"));
    }

    [Test]
    public void InitializeResponse_ProtocolVersion_ShouldBe_Integer_Type()
    {
        // Given: An InitializeResponse type
        var property = typeof(InitializeResponse).GetProperty("ProtocolVersion");

        // Then: Property type should be int, not object
        Assert.That(property?.PropertyType, Is.EqualTo(typeof(int)));
    }

    [Test]
    public void InitializeResponse_ProtocolVersion_Should_Serialize_As_Integer()
    {
        // Given: An InitializeResponse with protocol version
        var response = new InitializeResponse
        {
            ProtocolVersion = 1
        };

        // When: Serialize to JSON
        var json = JsonSerializer.Serialize(response);
        var parsed = JsonDocument.Parse(json);

        // Then: ProtocolVersion should be an integer in JSON
        Assert.That(parsed.RootElement.GetProperty("protocolVersion").ValueKind, Is.EqualTo(JsonValueKind.Number));
    }

    [Test]
    public void InitializeResponse_ProtocolVersion_ShouldNotBe_String()
    {
        // Given: An InitializeResponse with protocol version
        var response = new InitializeResponse
        {
            ProtocolVersion = 1
        };

        // When: Serialize to JSON
        var json = JsonSerializer.Serialize(response);

        // Then: JSON should not contain string version
        Assert.That(json, Does.Not.Contain("\"2024-11-05\""));
        Assert.That(json, Does.Contain("\"protocolVersion\":1"));
    }

    [Test]
    public void AgentCapabilities_Should_Have_SessionCapabilities_Property()
    {
        // Given: An AgentCapabilities type
        var property = typeof(AgentCapabilities).GetProperty("SessionCapabilities");

        // Then: Property should exist and be of correct type
        Assert.That(property, Is.Not.Null);
        Assert.That(property?.PropertyType, Is.EqualTo(typeof(SessionCapabilities)));
    }

    [Test]
    public void AgentCapabilities_SessionCapabilities_Should_Serialize_Correctly()
    {
        // Given: An AgentCapabilities with session capabilities
        var capabilities = new AgentCapabilities
        {
            SessionCapabilities = new SessionCapabilities
            {
                List = new SessionListCapabilities()
            }
        };

        // When: Serialize to JSON
        var json = JsonSerializer.Serialize(capabilities);
        var parsed = JsonDocument.Parse(json);

        // Then: sessionCapabilities should be present in JSON
        Assert.That(parsed.RootElement.TryGetProperty("sessionCapabilities", out var sessionCaps), Is.True);
        Assert.That(sessionCaps.TryGetProperty("list", out _), Is.True);
    }
}
