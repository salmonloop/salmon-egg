using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using SalmonEgg.Acp.Protocol;

namespace SalmonEgg.Acp.Serialization
{
    /// <summary>
    /// The serialization contract of one negotiated protocol version.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ACP negotiates a single major version per connection, and the specification is explicit about
    /// what follows: "a single connection always speaks exactly one negotiated version after
    /// <c>initialize</c>", and "each side selects its v1 or v2 surface per connection based on the
    /// negotiated version". Selecting a surface is therefore a serialization-contract decision, and in
    /// System.Text.Json the object that decides which contract applies is
    /// <see cref="IJsonTypeInfoResolver"/> - which is why the negotiated version lives here.
    /// </para>
    /// <para>
    /// The alternatives were both worse in the same way: they put version-dependence somewhere it
    /// cannot belong. Static <see cref="JsonDerivedTypeAttribute"/> metadata cannot vary by runtime
    /// state at all, and ambient state (an <c>AsyncLocal</c> write context) leaves the read direction
    /// with no notion of version - which is how a v1 connection ended up materializing v2 contracts
    /// while being unable to write them back.
    /// </para>
    /// <para>
    /// Because the version is a property of this object, any converter reached through these options
    /// can recover it from <see cref="JsonSerializerOptions.TypeInfoResolver"/> without ambient state.
    /// Use <see cref="NegotiatedVersion"/> rather than reading the resolver by hand.
    /// </para>
    /// </remarks>
    internal sealed class AcpWireFormat : IJsonTypeInfoResolver
    {
        // One options instance per modeled version, built once. JsonSerializerOptions caches contracts
        // per instance and freezes on first use, so a per-call instance would rebuild every contract
        // and defeat source generation's whole point.
        private static readonly Dictionary<int, AcpWireFormat> s_byVersion = BuildAll();

        private readonly IJsonTypeInfoResolver _inner;

        private AcpWireFormat(int version)
        {
            Version = version;
            _inner = JsonTypeInfoResolver.Combine(AcpJsonContext.Default, AcpJsonRpcContext.Default);

            // Copied from the generated context rather than re-declared. Every knob here is already
            // stated once in AcpJsonContext's [JsonSourceGenerationOptions] - camelCase naming,
            // case-insensitive reads, omit-nulls, out-of-order metadata - and the DTO contracts were
            // authored against those. Re-listing them would create a second place to change them, and a
            // wire format whose knobs disagreed with the contracts it serves is not a wire format.
            Options = new JsonSerializerOptions(AcpJsonContext.Default.Options)
            {
                TypeInfoResolver = this,
            };

            // Freeze now: a contract resolved later must not be able to observe different options than
            // one resolved during startup.
            Options.MakeReadOnly();
        }

        /// <summary>The negotiated major protocol version this contract speaks.</summary>
        internal int Version { get; }

        /// <summary>Serializer options bound to this version's contract.</summary>
        internal JsonSerializerOptions Options { get; }

        /// <summary>
        /// The contract for a version the SDK models. Throws for anything else, rather than silently
        /// falling back to the stable surface and writing the wrong wire shape.
        /// </summary>
        internal static AcpWireFormat For(int version) =>
            s_byVersion.TryGetValue(version, out var format)
                ? format
                : throw new ArgumentOutOfRangeException(
                    nameof(version),
                    version,
                    $"ACP protocol version {version} has no modeled wire contract.");

        /// <summary>
        /// The version whose contract produced these options, or <see cref="AcpProtocolVersion.Default"/>
        /// when the options did not come from a wire format.
        /// </summary>
        /// <remarks>
        /// The fallback keeps converters usable from a bare <c>AcpJsonContext</c> (contract tests,
        /// consumers serializing a single DTO), and defaulting to the stable version is the safe
        /// direction: an unknown caller gets v1 shapes, never draft ones.
        /// </remarks>
        internal static int NegotiatedVersion(JsonSerializerOptions options) =>
            options.TypeInfoResolver is AcpWireFormat format ? format.Version : AcpProtocolVersion.Default;

        /// <summary>The type info for <typeparamref name="T"/> under this version's contract.</summary>
        internal JsonTypeInfo<T> TypeInfo<T>() => (JsonTypeInfo<T>)Options.GetTypeInfo(typeof(T));

        /// <inheritdoc />
        public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
        {
            var info = _inner.GetTypeInfo(type, options);
            if (info is not null && info.Type == typeof(SessionUpdate) && info.PolymorphismOptions is not null)
            {
                ApplyNegotiatedSurface(info.PolymorphismOptions);
            }

            return info;
        }

        /// <summary>
        /// Replaces the polymorphic registrations with the ones the negotiated version defines.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Rebuilt from <see cref="SessionUpdateWireSurface"/> rather than patched, because neither
        /// direction is a subset of the other: v2 adds eight discriminators and removes three that v1
        /// defines. Patching would need both an add list and a remove list to stay in step with one
        /// table, and the table is the only thing that should have to be right.
        /// </para>
        /// <para>
        /// Mutation is safe because a generated context asked for a contract by a foreign
        /// <see cref="JsonSerializerOptions"/> builds a fresh <see cref="JsonTypeInfo"/> bound to those
        /// options: measured, the two versions get separate instances regardless of which is resolved
        /// first, the outer options cache the result so this runs once per version, and concurrent
        /// resolution sees the same set. Registrations added here behave exactly like declared ones -
        /// also measured, including verbatim round-trip of a variant that has no attribute.
        /// </para>
        /// <para>
        /// This changes the read direction into forward-compatible passthrough: an update the negotiated
        /// version does not define falls back to the base type with its payload preserved. It does
        /// <em>not</em> make the write direction safe - a contract written through a version that does
        /// not define it serializes as the base type, which is an empty object rather than an error. The
        /// explicit write guards are what turn that into a failure; do not read this as covering both
        /// directions.
        /// </para>
        /// </remarks>
        private void ApplyNegotiatedSurface(JsonPolymorphismOptions polymorphism)
        {
            polymorphism.DerivedTypes.Clear();
            foreach (var entry in SessionUpdateWireSurface.RegistrationsFor(Version))
            {
                polymorphism.DerivedTypes.Add(new JsonDerivedType(entry.UpdateType, entry.Discriminator));
            }
        }

        private static Dictionary<int, AcpWireFormat> BuildAll()
        {
            var formats = new Dictionary<int, AcpWireFormat>();
            foreach (var version in new[] { AcpProtocolVersion.V1, AcpProtocolVersion.V2 })
            {
                formats[version] = new AcpWireFormat(version);
            }

            return formats;
        }
    }
}
