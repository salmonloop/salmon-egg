using System.Text.Json;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Acp.Serialization;

namespace SalmonEgg.Acp.Tests.Architecture;

/// <summary>
/// Guards the mechanism that carries the negotiated protocol version, as opposed to what any one
/// version's surface contains.
/// </summary>
public sealed class AcpWireFormatTests
{
    [Fact]
    public void FastPathSerialization_IsDisabled()
    {
        // The default source-generation mode also emits a per-type SerializeHandler, and that handler
        // resolves nested contracts from the context instance instead of from the caller's options -
        // generated as JsonSerializer.Serialize(writer, value.ReplayFrom, SessionReplayFrom), where the
        // property reads AcpJsonContext.Default.Options.
        //
        // With the version carried on the options, that silently drops it one level down: serializing
        // SessionResumeParams through the v2 contract reached SessionReplayFromJsonConverter holding v1
        // options and threw "replayFrom is only available in protocolVersion 2". Nothing about the call
        // site suggested a nesting boundary had been crossed.
        //
        // Asserted on the mechanism rather than only through behavior, because a behavior test only
        // covers the nestings it happens to exercise, and the next version-dependent converter added
        // below a fast-path type would reintroduce this with no failing test to show it.
        foreach (var contract in new[]
                 {
                     (object)AcpWireFormat.For(AcpProtocolVersion.V1).TypeInfo<SessionResumeParams>(),
                     AcpWireFormat.For(AcpProtocolVersion.V2).TypeInfo<SessionResumeParams>(),
                     AcpWireFormat.For(AcpProtocolVersion.V2).TypeInfo<SessionUpdateParams>(),
                     AcpWireFormat.For(AcpProtocolVersion.V2).TypeInfo<SessionNewParams>(),
                     AcpJsonContext.Default.SessionResumeParams,
                 })
        {
            var serializeHandler = contract.GetType().GetProperty("SerializeHandler")!.GetValue(contract);
            Assert.Null(serializeHandler);
        }
    }

    [Fact]
    public void NegotiatedVersion_SurvivesANestingBoundary()
    {
        // The behavioral half of the assertion above, kept because it is the failure a reader will
        // recognize: replayFrom is written by a converter one level below the type being serialized, and
        // it refuses to write on anything but v2.
        var resume = new SessionResumeParams(
            "session-1",
            "/work",
            replayFrom: SessionReplayFrom.Start);

        var v2 = JsonSerializer.Serialize(resume, Wire.V2<SessionResumeParams>());
        Assert.Contains("\"replayFrom\"", v2, StringComparison.Ordinal);

        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(resume, Wire.V1<SessionResumeParams>()));
    }

    [Fact]
    public void WireFormatOptions_AgreeWithTheGeneratedContractSettings()
    {
        // The DTOs were authored against AcpJsonContext's [JsonSourceGenerationOptions]. A wire format
        // whose knobs disagreed would serve those contracts under different rules - camelCase drifting,
        // or nulls suddenly written - which is a wire change with no diff at the DTO.
        var generated = AcpJsonContext.Default.Options;
        foreach (var version in new[] { AcpProtocolVersion.V1, AcpProtocolVersion.V2 })
        {
            var options = AcpWireFormat.For(version).Options;
            Assert.Same(generated.PropertyNamingPolicy, options.PropertyNamingPolicy);
            Assert.Equal(generated.PropertyNameCaseInsensitive, options.PropertyNameCaseInsensitive);
            Assert.Equal(generated.DefaultIgnoreCondition, options.DefaultIgnoreCondition);
            Assert.Equal(generated.AllowOutOfOrderMetadataProperties, options.AllowOutOfOrderMetadataProperties);
            Assert.True(options.IsReadOnly);
        }
    }

    [Fact]
    public void EachModeledVersion_HasItsOwnFrozenContract()
    {
        var v1 = AcpWireFormat.For(AcpProtocolVersion.V1);
        var v2 = AcpWireFormat.For(AcpProtocolVersion.V2);

        Assert.NotSame(v1, v2);
        Assert.NotSame(v1.Options, v2.Options);
        // Cached, not rebuilt per call: JsonSerializerOptions caches contracts per instance, so a fresh
        // instance per call would rebuild every contract and give source generation nothing to do.
        Assert.Same(v1, AcpWireFormat.For(AcpProtocolVersion.V1));
        Assert.Equal(AcpProtocolVersion.V1, v1.Version);
        Assert.Equal(AcpProtocolVersion.V2, v2.Version);
    }

    [Fact]
    public void NegotiatedVersion_FallsBackToTheStableSurfaceForForeignOptions()
    {
        // Converters are reachable from a bare AcpJsonContext - contract tests, and consumers
        // serializing a single DTO. Defaulting to the stable version is the safe direction: an unknown
        // caller gets v1 shapes, never draft ones.
        Assert.Equal(AcpProtocolVersion.Default, AcpWireFormat.NegotiatedVersion(AcpJsonContext.Default.Options));
        Assert.Equal(AcpProtocolVersion.Default, AcpWireFormat.NegotiatedVersion(new JsonSerializerOptions()));
    }

    [Fact]
    public void UnmodeledVersion_HasNoContract()
    {
        // Falling back to the stable surface here would be the wrong kind of lenient: the caller asked
        // for a version this SDK cannot speak, and answering with v1's contract would put v1 wire on a
        // connection that negotiated something else.
        Assert.Throws<ArgumentOutOfRangeException>(() => AcpWireFormat.For(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => AcpWireFormat.For(0));
    }
}
