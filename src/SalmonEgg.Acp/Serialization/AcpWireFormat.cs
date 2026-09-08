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

            if (info?.Kind == JsonTypeInfoKind.Object)
            {
                ApplyObjectContract(info);
            }

            return info;
        }

        private void ApplyObjectContract(JsonTypeInfo info)
        {
            if (Version == AcpProtocolVersion.V2
                && (typeof(ContentChunkUpdate).IsAssignableFrom(info.Type) || typeof(WholeMessageUpdate).IsAssignableFrom(info.Type)))
            {
                var messageId = FindProperty(info, "messageId");
                messageId.IsRequired = true;
                messageId.CustomConverter = null;
                info.OnDeserialized = static value => RequireMessageId(value);
                info.OnSerializing = static value => RequireMessageId(value);
            }

            if (Version != AcpProtocolVersion.V2)
            {
                return;
            }

            if (info.Type == typeof(SessionNewResponse) || info.Type == typeof(SessionResumeResponse))
            {
                IgnoreProperty(info, "modes", new IgnoredProtocolPropertyJsonConverter<SessionModesState>());
                FindProperty(info, "configOptions").CustomConverter = new DefaultableConfigOptionsJsonConverter();
                info.OnDeserialized = static value => NormalizeConfigOptions(value);
            }
            else if (info.Type == typeof(ConfigOptionUpdate) || info.Type == typeof(SessionSetConfigOptionResponse))
            {
                var configOptions = FindProperty(info, "configOptions");
                configOptions.IsRequired = true;
                configOptions.CustomConverter = new DefaultableConfigOptionsJsonConverter();
                info.OnSerializing = static value => RequireConfigOptions(value);
            }
            else if (info.Type == typeof(AgentAuthCapabilities))
            {
                IgnoreProperty(info, "logout", new IgnoredProtocolPropertyJsonConverter<LogoutCapabilities>());
            }
            else if (info.Type == typeof(ClientCapabilities))
            {
                info.OnSerializing = static value => InitializeClientProtocolPolicy.Validate(AcpProtocolVersion.V2, (ClientCapabilities)value);
                IgnoreProperty(info, "fs", new IgnoredProtocolPropertyJsonConverter<FsCapability>());
                IgnoreProperty(info, "terminal", new IgnoredProtocolPropertyJsonConverter<bool?>());
                IgnoreProperty(info, "session", new IgnoredProtocolPropertyJsonConverter<ClientSessionCapabilities>());
            }
        }

        private static void RequireMessageId(object value)
        {
            var id = value is ContentChunkUpdate chunk ? chunk.MessageId : ((WholeMessageUpdate)value).MessageId;
            if (id is null)
            {
                throw new JsonException("ACP v2 message update requires string 'messageId'.");
            }
        }

        private static void NormalizeConfigOptions(object value)
        {
            // The v2 schema uses a defaultable array here, rather than v1's nullable snapshot.
            if (value is SessionNewResponse created && created.ConfigOptions is null)
            {
                created.SetDefaultConfigOptions();
            }
            else if (value is SessionResumeResponse resumed && resumed.ConfigOptions is null)
            {
                resumed.SetDefaultConfigOptions();
            }
        }

        private static void RequireConfigOptions(object value)
        {
            var options = value is ConfigOptionUpdate update
                ? update.ConfigOptions
                : ((SessionSetConfigOptionResponse)value).ConfigOptions;
            if (options is null)
            {
                throw new JsonException("ACP v2 configuration update requires 'configOptions'.");
            }
        }

        private static JsonPropertyInfo FindProperty(JsonTypeInfo info, string name)
        {
            foreach (var property in info.Properties)
            {
                if (property.Name == name)
                {
                    return property;
                }
            }

            throw new InvalidOperationException($"The {info.Type.Name} contract has no '{name}' property.");
        }

        private static void IgnoreProperty(JsonTypeInfo info, string name, JsonConverter converter)
        {
            // Source-generated record constructors bind parameters to these properties. Retain that
            // metadata while removing the older wire behavior, rather than breaking constructor binding.
            var property = FindProperty(info, name);
            property.CustomConverter = converter;
            property.ShouldSerialize = static (_, _) => false;
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
