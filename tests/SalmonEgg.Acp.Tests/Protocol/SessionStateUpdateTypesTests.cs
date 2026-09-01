using System.Text.Json;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Acp.Serialization;
using Xunit;

namespace SalmonEgg.Acp.Tests.Protocol;

public sealed class SessionStateUpdateTypesTests
{
    private static string SerializeV2(SessionUpdateParams value)
    {
        using var scope = AcpProtocolWriteContext.Enter(AcpProtocolVersion.V2);
        return JsonSerializer.Serialize(value, AcpJsonContext.Default.SessionUpdateParams);
    }

    // The v2 wire form is doubly flattened: the inner "state" discriminator and its payload are
    // siblings of the outer "sessionUpdate" discriminator, with no nested envelope at either level.
    [Fact]
    public void StateSessionUpdate_Idle_SerializesStateAndStopReasonFlatBesideDiscriminator()
    {
        var json = SerializeV2(new SessionUpdateParams(
            "session-1",
            new StateSessionUpdate(new IdleSessionState { StopReason = StopReason.EndTurn })));

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
            new StateSessionUpdate(new RunningSessionState())));

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
            new StateSessionUpdate(new RequiresActionSessionState())));

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
            AcpJsonContext.Default.SessionUpdateParams);

        var update = Assert.IsType<StateSessionUpdate>(parsed?.Update);
        var idle = Assert.IsType<IdleSessionState>(update.State);
        Assert.Null(idle.StopReason);
    }

    [Fact]
    public void StateSessionUpdate_IdleWithStopReason_RoundTripsThroughV2WriteContext()
    {
        var parsed = JsonSerializer.Deserialize(
            "{\"sessionId\":\"session-1\",\"update\":{\"sessionUpdate\":\"state_update\","
            + "\"state\":\"idle\",\"stopReason\":\"cancelled\"}}",
            AcpJsonContext.Default.SessionUpdateParams);

        var update = Assert.IsType<StateSessionUpdate>(parsed?.Update);
        var idle = Assert.IsType<IdleSessionState>(update.State);
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
            AcpJsonContext.Default.SessionUpdateParams);

        var update = Assert.IsType<StateSessionUpdate>(parsed?.Update);
        var idle = Assert.IsType<IdleSessionState>(update.State);
        Assert.Null(idle.StopReason);
    }

    [Fact]
    public void StateSessionUpdate_UnknownStopReason_IsPreservedRatherThanRejected()
    {
        var parsed = JsonSerializer.Deserialize(
            "{\"sessionId\":\"session-1\",\"update\":{\"sessionUpdate\":\"state_update\","
            + "\"state\":\"idle\",\"stopReason\":\"_vendor_halted\"}}",
            AcpJsonContext.Default.SessionUpdateParams);

        var update = Assert.IsType<StateSessionUpdate>(parsed?.Update);
        var idle = Assert.IsType<IdleSessionState>(update.State);
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
            AcpJsonContext.Default.SessionUpdateParams);

        var update = Assert.IsType<StateSessionUpdate>(parsed?.Update);
        var custom = Assert.IsType<CustomSessionState>(update.State);
        Assert.Equal("_vendor_paused", custom.State);

        var json = SerializeV2(parsed!);
        Assert.Contains("\"detail\":{\"b\":2,\"a\":[1,2,3]}", json, StringComparison.Ordinal);
        Assert.Contains("\"state\":\"_vendor_paused\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void StateSessionUpdate_MissingState_IsRejected()
    {
        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            "{\"sessionId\":\"session-1\",\"update\":{\"sessionUpdate\":\"state_update\"}}",
            AcpJsonContext.Default.SessionUpdateParams));

        Assert.Equal(SessionStateJsonConverter.MissingStateMessage, exception.Message);
    }

    // state_update does not exist in v1. Emitting one under a v1 write context would put a field on
    // the wire that a v1 Agent has no contract for, so writing fails closed rather than degrading.
    [Fact]
    public void StateSessionUpdate_UnderV1WriteContext_RefusesToSerialize()
    {
        var value = new SessionUpdateParams(
            "session-1",
            new StateSessionUpdate(new IdleSessionState { StopReason = StopReason.EndTurn }));

        var exception = Assert.Throws<JsonException>(
            () => JsonSerializer.Serialize(value, AcpJsonContext.Default.SessionUpdateParams));

        Assert.Equal(SessionStateJsonConverter.V2OnlyMessage, exception.Message);
    }

    // Reading must stay version-agnostic: a parser has to keep accepting whatever the peer sends, and
    // gating reads on the negotiated version would make the client the arbiter of the Agent's
    // semantics. Only writes are version-gated.
    [Fact]
    public void StateSessionUpdate_ReadIsNotVersionGated()
    {
        var parsed = JsonSerializer.Deserialize(
            "{\"sessionId\":\"session-1\",\"update\":{\"sessionUpdate\":\"state_update\","
            + "\"state\":\"idle\",\"stopReason\":\"end_turn\"}}",
            AcpJsonContext.Default.SessionUpdateParams);

        Assert.Equal(AcpProtocolVersion.V1, AcpProtocolWriteContext.Current);
        var update = Assert.IsType<StateSessionUpdate>(parsed?.Update);
        Assert.IsType<IdleSessionState>(update.State);
    }
}
