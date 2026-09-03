using System.Reflection;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Acp.Serialization;

namespace SalmonEgg.Acp.Tests.Architecture;

/// <summary>
/// Guards the per-version <c>sessionUpdate</c> surfaces against the upstream schema and against each
/// other.
/// </summary>
/// <remarks>
/// ACP negotiates one version per connection and each side then serves that version's surface. The two
/// surfaces are different sets, not one plus an increment: v2 adds eight discriminators and removes
/// three that v1 defines. Everything here exists because a single table serving both would be wrong in
/// both directions at once - and was.
/// </remarks>
public sealed class SessionUpdateSurfaceTests
{
    // Counts read from the upstream schema as JSON, not from the rendered docs page (which truncates
    // before SessionUpdate):
    //   schema/v1/schema.json - SessionUpdate is a closed oneOf with a discriminator keyword,
    //                           11 named variants, no fallback variant.
    //   schema/v2/schema.json - SessionUpdate is an anyOf with 16 named variants plus one open
    //                           "custom or future session update" fallback.
    // Pinning the counts is what anchors the table to the specification rather than to itself.
    private const int V1NamedVariantsInSchema = 11;
    private const int V2NamedVariantsInSchema = 16;

    [Fact]
    public void EachSurface_HasAsManyDiscriminatorsAsTheUpstreamSchema()
    {
        Assert.Equal(V1NamedVariantsInSchema, SessionUpdateWireSurface.DiscriminatorsFor(AcpProtocolVersion.V1).Count);
        Assert.Equal(V2NamedVariantsInSchema, SessionUpdateWireSurface.DiscriminatorsFor(AcpProtocolVersion.V2).Count);
    }

    [Fact]
    public void EveryTableEntry_IsClassifiedAndUnique()
    {
        var unclassified = SessionUpdateWireSurface.Entries
            .Where(static entry => entry.Surface == SessionUpdateWireSurface.Surfaces.None)
            .Select(static entry => entry.Discriminator)
            .ToArray();
        Assert.True(
            unclassified.Length == 0,
            "A discriminator belonging to no version can never be read or written; it is an unfinished "
            + "edit, not a surface: " + string.Join(", ", unclassified));

        var duplicates = SessionUpdateWireSurface.Entries
            .GroupBy(static entry => entry.Discriminator, StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .ToArray();
        Assert.True(duplicates.Length == 0, "Duplicate discriminators: " + string.Join(", ", duplicates));
    }

    [Fact]
    public void AttributeRegistrations_AreExactlyTheStableSurface()
    {
        // Attribute metadata is static, so whatever it declares is what every unversioned caller sees -
        // including the public AcpJsonContext.Default. Declaring the union would make that default a
        // version nobody negotiates; declaring v1 makes it merely stable. This is the assertion that
        // keeps it that way, and it also catches the reverse mistake: adding a [JsonDerivedType] without
        // classifying it in the table.
        var declared = typeof(SessionUpdate)
            .GetCustomAttributes<JsonDerivedTypeAttribute>(inherit: false)
            .Select(static attribute => attribute.TypeDiscriminator?.ToString() ?? "<none>")
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        var expected = SessionUpdateWireSurface.RegistrationsFor(AcpProtocolVersion.V1)
            .Select(static entry => entry.Discriminator)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, declared);
    }

    [Fact]
    public void ConverterDispatchedVariants_AreNeverRegistered()
    {
        // state_update flattens a second discriminator alongside the first, which STJ polymorphism
        // cannot express. Registering it would hand STJ a shape it cannot write, and the converter that
        // completes the contract would be fighting it.
        var converterDispatched = SessionUpdateWireSurface.Entries
            .Where(static entry => entry.DispatchedByConverter)
            .Select(static entry => entry.Discriminator)
            .ToArray();
        Assert.NotEmpty(converterDispatched);

        foreach (var version in new[] { AcpProtocolVersion.V1, AcpProtocolVersion.V2 })
        {
            var registered = SessionUpdateWireSurface.RegistrationsFor(version)
                .Select(static entry => entry.Discriminator)
                .ToArray();
            Assert.DoesNotContain(converterDispatched[0], registered, StringComparer.Ordinal);
        }
    }

    [Fact]
    public void ResolvedContract_CarriesExactlyTheNegotiatedSurface()
    {
        // The table being right is not the same as the mechanism applying it. This asserts the contract
        // a real connection resolves, which is the only thing the wire actually sees.
        foreach (var version in new[] { AcpProtocolVersion.V1, AcpProtocolVersion.V2 })
        {
            var contract = AcpWireFormat.For(version).TypeInfo<SessionUpdate>();
            Assert.NotNull(contract.PolymorphismOptions);

            var resolved = contract.PolymorphismOptions!.DerivedTypes
                .Select(static derived => derived.TypeDiscriminator?.ToString() ?? "<none>")
                .OrderBy(static name => name, StringComparer.Ordinal)
                .ToArray();
            var expected = SessionUpdateWireSurface.RegistrationsFor(version)
                .Select(static entry => entry.Discriminator)
                .OrderBy(static name => name, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(expected, resolved);
        }
    }

    [Fact]
    public void BothSurfaces_KeepTheForwardCompatibleFallback()
    {
        // v2's schema defines an open "custom or future" variant, so the fallback is required there. v1's
        // union is closed, which means the specification permits ignoring or rejecting an unknown
        // variant instead - preserving it is this SDK's choice, and the reasons are that AGENTS.md
        // forbids reducing existing leniency, that a proxy has to forward what it cannot read, and that
        // SessionUpdate.UnknownUpdateKind is then able to attribute the violation to the peer rather
        // than swallow it. Asserted so the choice cannot be reversed by accident.
        foreach (var version in new[] { AcpProtocolVersion.V1, AcpProtocolVersion.V2 })
        {
            var polymorphism = AcpWireFormat.For(version).TypeInfo<SessionUpdate>().PolymorphismOptions!;
            Assert.Equal(JsonUnknownDerivedTypeHandling.FallBackToBaseType, polymorphism.UnknownDerivedTypeHandling);
            Assert.True(polymorphism.IgnoreUnrecognizedTypeDiscriminators);
        }
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
    public void UnmodeledVersion_HasNoContract()
    {
        // Falling back to the stable surface here would be the wrong kind of lenient: the caller asked
        // for a version this SDK cannot speak, and answering with v1's contract would put v1 wire on a
        // connection that negotiated something else.
        Assert.Throws<ArgumentOutOfRangeException>(() => AcpWireFormat.For(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => AcpWireFormat.For(0));
    }
}
