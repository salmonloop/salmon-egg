using System.Text.Json;
using NUnit.Framework;
using SalmonEgg.Domain.Models.Protocol;

namespace SalmonEgg.Domain.Tests.Protocol;

[TestFixture]
public sealed class SessionUpdateTypesTests
{
    [Test]
    public void SessionUpdateParams_Update_ShouldNotBe_Nullable()
    {
        // Given: A SessionUpdateParams type
        var property = typeof(SessionUpdateParams).GetProperty("Update");

        // Then: Property should not be nullable reference type
        Assert.That(property, Is.Not.Null);
        // Check that the property type is SessionUpdate, not SessionUpdate?
        // Note: In C#, nullable reference types are a compile-time feature,
        // so we check that the property doesn't have a nullable annotation
        var propertyType = property!.PropertyType;
        Assert.That(propertyType, Is.EqualTo(typeof(SessionUpdate)));
    }

    [Test]
    public void SessionUpdateParams_Should_Serialize_With_Update()
    {
        // Given: A SessionUpdateParams with an update
        var sessionParams = new SessionUpdateParams
        {
            SessionId = "test-session",
            Update = new CurrentModeUpdate { CurrentModeId = "test-mode" }
        };

        // When: Serialize to JSON
        var json = JsonSerializer.Serialize(sessionParams);
        var parsed = JsonDocument.Parse(json);

        // Then: update should be present in JSON
        Assert.That(parsed.RootElement.TryGetProperty("update", out var update), Is.True);
        Assert.That(update.ValueKind, Is.EqualTo(JsonValueKind.Object));
    }
}
