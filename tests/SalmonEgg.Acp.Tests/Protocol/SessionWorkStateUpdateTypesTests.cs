using System.Text.Json;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Acp.Serialization;
using Xunit;

namespace SalmonEgg.Acp.Tests.Protocol;

public sealed class SessionWorkStateUpdateTypesTests
{
    private static string SerializeV2(SessionUpdateParams value)
    {
        return JsonSerializer.Serialize(value, Wire.V2<SessionUpdateParams>());
    }

    // The v2 wire form is doubly flattened: the inner "state" discriminator and its payload are
    // siblings of the outer "sessionUpdate" discriminator, with no nested envelope at either level.
    [Fact]
    public void StateSessionUpdate_Idle_SerializesStateAndStopReasonFlatBesideDiscriminator()
    {
        var json = SerializeV2(new SessionUpdateParams(
            "session-1",
            new StateSessionUpdate(new IdleSessionWorkState { StopReason = StopReason.EndTurn })));

        using var document = JsonDocument.Parse(json);
        var update = document.RootElement.GetProperty("update");

        Assert.Equal("state_update", update.GetProperty("sessionUpdate").GetString());
        Assert.Equal("idle", update.GetProperty("state").GetString());
        Assert.Equal("end_turn", update.GetProperty("stopReason").GetString());
        Assert.False(update.TryGetProperty("update", out _));
        Assert.False(update.TryGetProperty("State", out _));
    }

    [Fact]
    public void StateSessionUpdate_Running_SerializesWithoutStopReason()
    {
        var json = SerializeV2(new SessionUpdateParams(
            "session-1",
            new StateSessionUpdate(new RunningSessionWorkState())));

        using var document = JsonDocument.Parse(json);
        var update = document.RootElement.GetProperty("update");

        Assert.Equal("state_update", update.GetProperty("sessionUpdate").GetString());
        Assert.Equal("running", update.GetProperty("state").GetString());
        Assert.False(update.TryGetProperty("stopReason", out _));
    }

    [Fact]
    public void StateSessionUpdate_RequiresAction_SerializesWithoutStopReason()
    {
        var json = SerializeV2(new SessionUpdateParams(
            "session-1",
            new StateSessionUpdate(new RequiresActionSessionWorkState())));

        using var document = JsonDocument.Parse(json);
        var update = document.RootElement.GetProperty("update");

        Assert.Equal("requires_action", update.GetProperty("state").GetString());
        Assert.False(update.TryGetProperty("stopReason", out _));
    }

    // Omitted and null both mean "the Agent is not reporting a stop reason", so neither may be
    // invented into a concrete reason on read - end_turn in particular would fabricate a completed
    // turn out of a bare idle transition.
    [Theory]
    [InlineData("{\"sessionUpdate\":\"state_update\",\"state\":\"idle\"}")]
    [InlineData("{\"sessionUpdate\":\"state_update\",\"state\":\"idle\",\"stopReason\":null}")]
    public void StateSessionUpdate_IdleWithoutStopReason_DeserializesAsNoReasonReported(string updateJson)
    {
        var parsed = JsonSerializer.Deserialize(
            "{\"sessionId\":\"session-1\",\"update\":" + updateJson + "}",
            Wire.V2<SessionUpdateParams>());

        var update = Assert.IsType<StateSessionUpdate>(parsed?.Update);
        var idle = Assert.IsType<IdleSessionWorkState>(update.State);
        Assert.Null(idle.StopReason);
    }

    [Fact]
    public void StateSessionUpdate_IdleWithStopReason_RoundTripsOnADraftConnection()
    {
        var parsed = JsonSerializer.Deserialize(
            "{\"sessionId\":\"session-1\",\"update\":{\"sessionUpdate\":\"state_update\","
            + "\"state\":\"idle\",\"stopReason\":\"cancelled\"}}",
            Wire.V2<SessionUpdateParams>());

        var update = Assert.IsType<StateSessionUpdate>(parsed?.Update);
        var idle = Assert.IsType<IdleSessionWorkState>(update.State);
        Assert.Equal(StopReason.Cancelled, idle.StopReason);

        var json = SerializeV2(parsed!);
        using var document = JsonDocument.Parse(json);
        var reserialized = document.RootElement.GetProperty("update");

        Assert.Equal("idle", reserialized.GetProperty("state").GetString());
        Assert.Equal("cancelled", reserialized.GetProperty("stopReason").GetString());
    }

    // A malformed stopReason is marked x-deserialize-default-on-error in the schema: degrade to
    // "no reason reported" rather than failing the notification, because losing the reason is
    // recoverable while dropping the end-of-turn signal is not.
    [Theory]
    [InlineData("123")]
    [InlineData("{\"nested\":true}")]
    [InlineData("[\"end_turn\"]")]
    public void StateSessionUpdate_MalformedStopReason_DegradesInsteadOfFailingTheNotification(string stopReasonJson)
    {
        var parsed = JsonSerializer.Deserialize(
            "{\"sessionId\":\"session-1\",\"update\":{\"sessionUpdate\":\"state_update\","
            + "\"state\":\"idle\",\"stopReason\":" + stopReasonJson + "}}",
            Wire.V2<SessionUpdateParams>());

        var update = Assert.IsType<StateSessionUpdate>(parsed?.Update);
        var idle = Assert.IsType<IdleSessionWorkState>(update.State);
        Assert.Null(idle.StopReason);
    }

    [Fact]
    public void StateSessionUpdate_UnknownStopReason_IsPreservedRatherThanRejected()
    {
        var parsed = JsonSerializer.Deserialize(
            "{\"sessionId\":\"session-1\",\"update\":{\"sessionUpdate\":\"state_update\","
            + "\"state\":\"idle\",\"stopReason\":\"_vendor_halted\"}}",
            Wire.V2<SessionUpdateParams>());

        var update = Assert.IsType<StateSessionUpdate>(parsed?.Update);
        var idle = Assert.IsType<IdleSessionWorkState>(update.State);
        Assert.Equal(new StopReason("_vendor_halted"), idle.StopReason);
    }

    // The schema's trailing unconstrained member makes any state string valid, so an unmodeled state
    // must round-trip verbatim instead of being downgraded by the client.
    [Fact]
    public void StateSessionUpdate_UnknownState_RoundTripsVerbatim()
    {
        const string UpdateJson =
            "{\"sessionUpdate\":\"state_update\",\"state\":\"_vendor_paused\","
            + "\"detail\":{\"b\":2,\"a\":[1,2,3]},\"_meta\":{\"k\":\"v\"}}";

        var parsed = JsonSerializer.Deserialize(
            "{\"sessionId\":\"session-1\",\"update\":" + UpdateJson + "}",
            Wire.V2<SessionUpdateParams>());

        var update = Assert.IsType<StateSessionUpdate>(parsed?.Update);
        var custom = Assert.IsType<CustomSessionWorkState>(update.State);
        Assert.Equal("_vendor_paused", custom.State);

        var json = SerializeV2(parsed!);
        Assert.Contains("\"detail\":{\"b\":2,\"a\":[1,2,3]}", json, StringComparison.Ordinal);
        Assert.Contains("\"state\":\"_vendor_paused\"", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("running")]
    [InlineData("requires_action")]
    [InlineData("idle")]
    [InlineData("_vendor_paused")]
    public void StateSessionUpdate_MetadataRoundTrip_EmitsEachFlattenedFieldOnce(string state)
    {
        // Arrange
        var json = "{\"sessionId\":\"session-1\",\"update\":{\"sessionUpdate\":\"state_update\","
            + "\"state\":\"" + state + "\",\"_meta\":{\"nested\":{\"owned\":[true,42,null]}}}}";
        using var original = JsonDocument.Parse(json);

        // Act
        var restored = Assert.IsType<SessionUpdateParams>(JsonSerializer.Deserialize(json, Wire.V2<SessionUpdateParams>()));
        using var replay = JsonDocument.Parse(SerializeV2(restored));

        // Assert
        var update = replay.RootElement.GetProperty("update");
        Assert.Single(update.EnumerateObject(), property => property.NameEquals("sessionUpdate"));
        Assert.Single(update.EnumerateObject(), property => property.NameEquals("_meta"));
        Assert.True(JsonElement.DeepEquals(original.RootElement, replay.RootElement));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("[]")]
    [InlineData("\"unknown\"")]
    [InlineData("{\"nested\":[true,42,null]}")]
    public void StateSessionUpdate_CustomMetadata_RoundTripsTheUnknownPayload(string metadata)
    {
        // Arrange
        var json = "{\"sessionId\":\"session-1\",\"update\":{\"sessionUpdate\":\"state_update\","
            + "\"state\":\"_vendor_paused\",\"_meta\":" + metadata + "}}";
        using var original = JsonDocument.Parse(json);

        // Act
        var restored = Assert.IsType<SessionUpdateParams>(JsonSerializer.Deserialize(json, Wire.V2<SessionUpdateParams>()));
        using var replay = JsonDocument.Parse(SerializeV2(restored));

        // Assert
        Assert.IsType<CustomSessionWorkState>(Assert.IsType<StateSessionUpdate>(restored.Update).State);
        Assert.True(JsonElement.DeepEquals(original.RootElement, replay.RootElement));
    }

    [Fact]
    public void StateSessionUpdate_ArbitraryMetadataValue_SurvivesTheParentRoundTrip()
        => FsCheckPropertyRunner.Run(this, nameof(StateMetadataRoundTripProperty));

    [Theory]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("[]")]
    [InlineData("\"invalid\"")]
    [InlineData("{\"outer\":true}")]
    public void StateSessionUpdate_ParentMetadata_DefaultsWithoutDiscardingTheState(string metadata)
    {
        // Arrange
        var json = "{\"sessionId\":\"session-1\",\"update\":{\"sessionUpdate\":\"state_update\","
            + "\"state\":\"idle\",\"stopReason\":\"cancelled\"},\"_meta\":" + metadata + "}";

        // Act
        var restored = Assert.IsType<SessionUpdateParams>(JsonSerializer.Deserialize(json, Wire.V2<SessionUpdateParams>()));

        // Assert
        var update = Assert.IsType<StateSessionUpdate>(restored.Update);
        Assert.Equal(StopReason.Cancelled, Assert.IsType<IdleSessionWorkState>(update.State).StopReason);
        Assert.Null(update.Meta);
        if (metadata == "{\"outer\":true}")
        {
            Assert.True(Assert.IsType<JsonElement>(restored.Meta!["outer"]).GetBoolean());
        }
        else
        {
            Assert.Null(restored.Meta);
        }
    }

    [Theory]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("[]")]
    [InlineData("{}")]
    public void StateSessionUpdate_InvalidStateDiscriminator_RemainsRejected(string state)
    {
        // Arrange
        var json = "{\"sessionId\":\"session-1\",\"update\":{\"sessionUpdate\":\"state_update\","
            + "\"state\":" + state + ",\"_meta\":42}}";

        // Act / Assert
        var error = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(json, Wire.V2<SessionUpdateParams>()));
        Assert.Equal(SessionWorkStateJsonConverter.MissingStateMessage, error.Message);
    }

    [Theory]
    [InlineData("42")]
    [InlineData("[]")]
    [InlineData("\"invalid\"")]
    public void MetadataWithoutSchemaDefaultOptIn_RemainsStrict(string metadata)
    {
        // Arrange
        using var document = JsonDocument.Parse("{\"_meta\":" + metadata + "}");

        // Act / Assert
        Assert.Throws<JsonException>(() => AcpMetaJson.Read(document.RootElement));
        Assert.Throws<JsonException>(() => ReadMetadataValue(metadata));
    }

    private static void ReadMetadataValue(string metadata)
    {
        var reader = new Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(metadata));
        Assert.True(reader.Read());
        AcpMetaJson.ReadValue(ref reader);
    }

    private void StateMetadataRoundTripProperty(string? content, long number, bool flag)
    {
        // Arrange
        var original = new SessionUpdateParams("session-1", new StateSessionUpdate(new IdleSessionWorkState
        {
            StopReason = StopReason.EndTurn,
            Meta = new Dictionary<string, object?>
            {
                ["_vendor"] = new object?[] { content, number, flag, null }
            }
        }));
        using var first = JsonDocument.Parse(SerializeV2(original));

        // Act
        var restored = Assert.IsType<SessionUpdateParams>(
            JsonSerializer.Deserialize(first.RootElement, Wire.V2<SessionUpdateParams>()));
        using var replay = JsonDocument.Parse(SerializeV2(restored));

        // Assert
        Assert.True(JsonElement.DeepEquals(first.RootElement, replay.RootElement));
        Assert.Single(replay.RootElement.GetProperty("update").EnumerateObject(), property => property.NameEquals("_meta"));
    }

    [Fact]
    public void StateSessionUpdate_MissingState_IsRejected()
    {
        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            "{\"sessionId\":\"session-1\",\"update\":{\"sessionUpdate\":\"state_update\"}}",
            Wire.V2<SessionUpdateParams>()));

        Assert.Equal(SessionWorkStateJsonConverter.MissingStateMessage, exception.Message);
    }

    // state_update does not exist in v1. Emitting one under a v1 write context would put a field on
    // the wire that a v1 Agent has no contract for, so writing fails closed rather than degrading.
    [Fact]
    public void StateSessionUpdate_OnAStableConnection_RefusesToSerialize()
    {
        var value = new SessionUpdateParams(
            "session-1",
            new StateSessionUpdate(new IdleSessionWorkState { StopReason = StopReason.EndTurn }));

        var exception = Assert.Throws<JsonException>(
            () => JsonSerializer.Serialize(value, Wire.V1<SessionUpdateParams>()));

        // The container guard fires first now, and names both the discriminator and the version - the
        // inner converter's message is still reachable when a bare SessionWorkState is serialized.
        Assert.Contains("state_update", exception.Message, StringComparison.Ordinal);
        Assert.Contains("protocolVersion 1", exception.Message, StringComparison.Ordinal);
    }

}
