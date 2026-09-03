using System.Text.Json;
using SalmonEgg.Acp.Protocol;
using Xunit;

namespace SalmonEgg.Acp.Tests.Protocol;

/// <summary>
/// The read direction, per negotiated version: an update the connection's version does not define must
/// not bind to a contract, and must survive being read and written back.
/// </summary>
/// <remarks>
/// <para>
/// This is the direction that was missing. Every version check used to live on the write side, so a v1
/// connection materialized v2 contracts and handed them upstream while being unable to write them back.
/// The same table now serves both directions, so both are asserted here - including the mirror case
/// nobody had noticed, where v2 would bind the three variants v2 removed.
/// </para>
/// <para>
/// Round-trip is asserted field by field rather than byte for byte. Property order is not part of the
/// JSON object contract, and the discriminator is restored from polymorphic metadata rather than kept in
/// place, so a byte comparison would assert an implementation detail and fail on a harmless change.
/// </para>
/// </remarks>
public sealed class SessionUpdateVersionSurfaceTests
{
    private const string SessionId = "session-1";

    // One payload per discriminator, each carrying at least one field beyond the discriminator so
    // preservation is observable rather than vacuous.
    private static readonly (string Discriminator, string UpdateJson)[] s_v2Only =
    [
        ("agent_message", """{"sessionUpdate":"agent_message","messageId":"m-1","content":[{"type":"text","text":"hi"}]}"""),
        ("user_message", """{"sessionUpdate":"user_message","messageId":"m-2","content":[{"type":"text","text":"ask"}]}"""),
        ("agent_thought", """{"sessionUpdate":"agent_thought","messageId":"m-3","content":[{"type":"text","text":"think"}]}"""),
        ("state_update", """{"sessionUpdate":"state_update","state":"idle","stopReason":"end_turn"}"""),
        ("tool_call_content_chunk", """{"sessionUpdate":"tool_call_content_chunk","toolCallId":"tc-1","content":{"type":"content","content":{"type":"text","text":"frag"}}}"""),
        ("terminal_update", """{"sessionUpdate":"terminal_update","terminalId":"t-1","command":"ls"}"""),
        ("terminal_output_chunk", """{"sessionUpdate":"terminal_output_chunk","terminalId":"t-1","data":"YQ=="}"""),
        ("plan_update", """{"sessionUpdate":"plan_update","plan":{"type":"items","planId":"p-1","entries":[]}}"""),
    ];

    private static readonly (string Discriminator, string UpdateJson)[] s_v1Only =
    [
        ("tool_call", """{"sessionUpdate":"tool_call","toolCallId":"tc-1","title":"run"}"""),
        ("plan", """{"sessionUpdate":"plan","entries":[]}"""),
        ("current_mode_update", """{"sessionUpdate":"current_mode_update","currentModeId":"mode-1"}"""),
    ];

    private static readonly (string Discriminator, string UpdateJson)[] s_shared =
    [
        ("agent_message_chunk", """{"sessionUpdate":"agent_message_chunk","content":{"type":"text","text":"hi"}}"""),
        ("user_message_chunk", """{"sessionUpdate":"user_message_chunk","content":{"type":"text","text":"ask"}}"""),
        ("agent_thought_chunk", """{"sessionUpdate":"agent_thought_chunk","content":{"type":"text","text":"think"}}"""),
        ("tool_call_update", """{"sessionUpdate":"tool_call_update","toolCallId":"tc-1","status":"completed"}"""),
        ("available_commands_update", """{"sessionUpdate":"available_commands_update","availableCommands":[]}"""),
        ("config_option_update", """{"sessionUpdate":"config_option_update","configOptions":[]}"""),
        ("session_info_update", """{"sessionUpdate":"session_info_update","title":"t"}"""),
        ("usage_update", """{"sessionUpdate":"usage_update","used":1,"size":2}"""),
    ];

    public static TheoryData<string, string> V2OnlyUpdates() => ToTheoryData(s_v2Only);

    public static TheoryData<string, string> V1OnlyUpdates() => ToTheoryData(s_v1Only);

    public static TheoryData<string, string> SharedUpdates() => ToTheoryData(s_shared);

    [Theory]
    [MemberData(nameof(V2OnlyUpdates))]
    public void V2OnlyUpdate_OnAStableConnection_PassesThroughInsteadOfBinding(string discriminator, string updateJson)
    {
        AssertPassthrough(AcpProtocolVersion.V1, discriminator, updateJson);
    }

    [Theory]
    [MemberData(nameof(V1OnlyUpdates))]
    public void V1OnlyUpdate_OnADraftConnection_PassesThroughInsteadOfBinding(string discriminator, string updateJson)
    {
        // The mirror of the defect this change fixes. v2 removes tool_call, plan and current_mode_update,
        // so a v2 connection binding them would be serving a vocabulary v2 does not have.
        AssertPassthrough(AcpProtocolVersion.V2, discriminator, updateJson);
    }

    [Theory]
    [MemberData(nameof(V2OnlyUpdates))]
    public void V2OnlyUpdate_OnADraftConnection_BindsToItsContract(string discriminator, string updateJson)
    {
        var update = Parse(AcpProtocolVersion.V2, updateJson)!.Update;

        Assert.IsNotType<SessionUpdate>(update);
        Assert.Null(update.UnknownUpdateKind);
        var expected = SessionUpdateWireSurface.Entries
            .Single(entry => entry.Discriminator == discriminator)
            .UpdateType;
        Assert.IsType(expected, update);
    }

    [Theory]
    [MemberData(nameof(SharedUpdates))]
    public void SharedUpdate_BindsOnBothSurfaces(string discriminator, string updateJson)
    {
        var expected = SessionUpdateWireSurface.Entries
            .Single(entry => entry.Discriminator == discriminator)
            .UpdateType;

        foreach (var version in new[] { AcpProtocolVersion.V1, AcpProtocolVersion.V2 })
        {
            Assert.IsType(expected, Parse(version, updateJson)!.Update);
        }
    }

    [Fact]
    public void EveryClassifiedDiscriminator_IsCoveredByThesePayloads()
    {
        // Without this the theories would silently stop covering a newly classified discriminator, and a
        // suite that quietly narrows is worse than one that fails.
        var covered = s_v2Only.Concat(s_v1Only).Concat(s_shared)
            .Select(static row => row.Discriminator)
            .ToHashSet(StringComparer.Ordinal);
        var classified = SessionUpdateWireSurface.Entries
            .Select(static entry => entry.Discriminator)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(
            classified.OrderBy(static name => name, StringComparer.Ordinal),
            covered.OrderBy(static name => name, StringComparer.Ordinal));
    }

    private static TheoryData<string, string> ToTheoryData((string Discriminator, string UpdateJson)[] rows)
    {
        var data = new TheoryData<string, string>();
        foreach (var (discriminator, updateJson) in rows)
        {
            data.Add(discriminator, updateJson);
        }

        return data;
    }

    private static void AssertPassthrough(int version, string discriminator, string updateJson)
    {
        var parsed = Parse(version, updateJson);

        // Exact type, not "assignable to": binding to any derived contract is the failure being guarded.
        var update = Assert.IsType<SessionUpdate>(parsed!.Update);
        Assert.Equal(discriminator, update.UnknownUpdateKind);

        var written = JsonSerializer.Serialize(parsed, Wire.Of<SessionUpdateParams>(version));
        using var round = JsonDocument.Parse(written);
        Assert.Equal(SessionId, round.RootElement.GetProperty("sessionId").GetString());
        AssertSameFields(updateJson, round.RootElement.GetProperty("update"));
    }

    private static SessionUpdateParams? Parse(int version, string updateJson) =>
        JsonSerializer.Deserialize(
            $"{{\"sessionId\":\"{SessionId}\",\"update\":{updateJson}}}",
            Wire.Of<SessionUpdateParams>(version));

    private static void AssertSameFields(string expectedJson, JsonElement actual)
    {
        using var expected = JsonDocument.Parse(expectedJson);
        var expectedFields = expected.RootElement.EnumerateObject()
            .ToDictionary(static property => property.Name, static property => property.Value.GetRawText(), StringComparer.Ordinal);
        var actualFields = actual.EnumerateObject()
            .ToDictionary(static property => property.Name, static property => property.Value.GetRawText(), StringComparer.Ordinal);

        Assert.Equal(
            expectedFields.OrderBy(static field => field.Key, StringComparer.Ordinal),
            actualFields.OrderBy(static field => field.Key, StringComparer.Ordinal));
    }
}
