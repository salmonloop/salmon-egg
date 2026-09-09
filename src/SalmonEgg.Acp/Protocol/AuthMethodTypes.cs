using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using SalmonEgg.Acp.Serialization;

namespace SalmonEgg.Acp.Protocol
{
    /// <summary>
    /// Authentication method advertised by the agent during initialization.
    /// Custom authentication metadata is carried through the ACP <c>_meta</c> field.
    /// </summary>
    [JsonConverter(typeof(AuthMethodDefinitionJsonConverter))]
    public sealed record AuthMethodDefinition : AcpProtocolObject
    {
        public string Id { get; init; } = string.Empty;

        [JsonIgnore]
        public string MethodId
        {
            get => Id;
            init => Id = value;
        }

        public string Name { get; init; } = string.Empty;

        /// <summary>
        /// Discriminator for the ACP <c>AuthMethod</c> union. Absent means <see cref="AgentType"/>.
        /// </summary>
        public string? Type { get; init; }

        /// <summary>
        /// Discriminator value for methods the agent handles itself through <c>authenticate</c>.
        /// </summary>
        public const string AgentType = "agent";

        /// <summary>
        /// Discriminator value for methods the client must run as a separate interactive process.
        /// </summary>
        public const string TerminalType = "terminal";

        /// <summary>
        /// The effective discriminator, applying the ACP default: a method with no <c>type</c>
        /// is treated as <see cref="AgentType"/>.
        /// </summary>
        public string ResolvedType => Type ?? AgentType;

        /// <summary>
        /// Whether this method may be passed to <c>authenticate</c>.
        /// </summary>
        /// <remarks>
        /// Only <see cref="AgentType"/> (explicit or defaulted) qualifies. The ACP schema states that a
        /// client MUST NOT pass an <c>AuthMethodTerminal</c> to <c>authenticate</c>, and every other
        /// discriminator denotes a flow whose semantics this client does not implement; both are refused
        /// so that an unrecognized or non-compliant advertisement cannot reach the wire. The comparison is
        /// ordinal because the discriminator is a fixed wire literal, so any variant spelling is unknown.
        /// </remarks>
        public bool SupportsAuthenticateRequest
            => string.Equals(ResolvedType, AgentType, StringComparison.Ordinal);

        [JsonPropertyName("description")]
        public string? Description { get; init; }

        /// <summary>Arguments appended to the configured agent invocation for terminal sign-in.</summary>
        public IReadOnlyList<string>? Args { get; init; }

        /// <summary>Environment overrides for terminal sign-in, keyed by variable name.</summary>
        public IReadOnlyDictionary<string, string>? Env { get; init; }

        // Unimplemented variants must survive initialization replay without interpretation.
        // Known properties remain owned by the typed model so edits do not replay stale values.
        internal JsonElement? RawPayload { get; init; }
    }

    internal sealed class AuthMethodDefinitionJsonConverter : JsonConverter<AuthMethodDefinition>
    {
        public override AuthMethodDefinition? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            var protocolVersion = AcpWireFormat.NegotiatedVersion(options);
            var discriminator = ReadDiscriminator(root, protocolVersion);
            var isTerminal = discriminator == AuthMethodDefinition.TerminalType;

            return new AuthMethodDefinition
            {
                Id = ReadString(root, GetIdPropertyName(protocolVersion)) ?? string.Empty,
                Name = ReadString(root, "name") ?? string.Empty,
                Type = discriminator,
                Description = ReadString(root, "description"),
                Args = isTerminal ? ReadArguments(root) : null,
                Env = isTerminal ? ReadEnvironment(root, protocolVersion) : null,
                Meta = AcpMetaJson.Read(root),
                RawPayload = root.Clone()
            };
        }

        public override void Write(Utf8JsonWriter writer, AuthMethodDefinition value, JsonSerializerOptions options)
            => WriteAuthMethod(writer, value, AcpWireFormat.NegotiatedVersion(options));

        internal static void WriteAuthMethod(Utf8JsonWriter writer, AuthMethodDefinition value, int protocolVersion)
        {
            writer.WriteStartObject();
            writer.WriteString(GetIdPropertyName(protocolVersion), value.Id);
            writer.WriteString("name", value.Name);

            if (protocolVersion != AcpProtocolVersion.V1 || value.Type is not null)
            {
                writer.WriteString("type", value.ResolvedType);
            }

            if (value.Description is not null)
            {
                writer.WriteString("description", value.Description);
            }

            AcpMetaJson.Write(writer, value.Meta);
            if (value.ResolvedType == AuthMethodDefinition.TerminalType)
            {
                WriteTerminalInvocation(writer, value, protocolVersion);
            }

            WriteAdditionalProperties(writer, value, protocolVersion);
            writer.WriteEndObject();
        }

        private static string GetIdPropertyName(int protocolVersion)
            => protocolVersion == AcpProtocolVersion.V1 ? "id" : "methodId";

        private static string? ReadDiscriminator(JsonElement root, int protocolVersion)
        {
            if (!root.TryGetProperty("type", out var property))
            {
                if (protocolVersion != AcpProtocolVersion.V1)
                {
                    throw new JsonException("ACP v2 authentication method requires 'type'.");
                }

                return null;
            }

            // ACP defaults only an absent discriminator; an invalid JSON type must never gain
            // the privileges of the default agent method. Unknown strings stay uninterpreted.
            if (property.ValueKind != JsonValueKind.String)
            {
                throw new JsonException("ACP authentication method 'type' must be a string when provided.");
            }

            return property.GetString();
        }

        private static void WriteAdditionalProperties(Utf8JsonWriter writer, AuthMethodDefinition value, int protocolVersion)
        {
            if (value.RawPayload is not { ValueKind: JsonValueKind.Object } root)
            {
                return;
            }

            foreach (var property in root.EnumerateObject())
            {
                if (property.Name == GetIdPropertyName(protocolVersion)
                    || property.Name is "name" or "type" or "description" or "_meta"
                    || (value.ResolvedType == AuthMethodDefinition.TerminalType && property.Name is "args" or "env"))
                {
                    continue;
                }

                writer.WritePropertyName(property.Name);
                writer.WriteRawValue(property.Value.GetRawText());
            }
        }

        private static IReadOnlyList<string>? ReadArguments(JsonElement root)
        {
            if (!root.TryGetProperty("args", out var args)) return null;
            var result = new List<string>();
            // The schema explicitly defaults malformed args and skips malformed items.
            if (args.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in args.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String) result.Add(item.GetString()!);
                }
            }

            return result;
        }

        private static IReadOnlyDictionary<string, string>? ReadEnvironment(JsonElement root, int protocolVersion)
        {
            if (!root.TryGetProperty("env", out var env)) return null;
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (protocolVersion == AcpProtocolVersion.V1)
            {
                if (env.ValueKind != JsonValueKind.Object) return result;
                foreach (var property in env.EnumerateObject())
                {
                    // V1 defaults the whole field on error; it does not permit per-entry recovery.
                    if (property.Value.ValueKind != JsonValueKind.String) return new Dictionary<string, string>();
                    result[property.Name] = property.Value.GetString()!;
                }
            }
            else if (env.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in env.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object
                        && ReadString(item, "name") is { } name && ReadString(item, "value") is { } value)
                    {
                        result.TryAdd(name, value);
                    }
                }
            }

            return result;
        }

        private static void WriteTerminalInvocation(Utf8JsonWriter writer, AuthMethodDefinition value, int protocolVersion)
        {
            if (value.Args is { } arguments)
            {
                writer.WriteStartArray("args");
                foreach (var argument in arguments) writer.WriteStringValue(argument);
                writer.WriteEndArray();
            }

            if (value.Env is not { } environment) return;
            if (protocolVersion == AcpProtocolVersion.V1)
            {
                writer.WriteStartObject("env");
                foreach (var entry in environment) writer.WriteString(entry.Key, entry.Value);
                writer.WriteEndObject();
                return;
            }

            writer.WriteStartArray("env");
            foreach (var entry in environment)
            {
                writer.WriteStartObject();
                writer.WriteString("name", entry.Key);
                writer.WriteString("value", entry.Value);
                WriteEnvironmentExtensions(writer, value.RawPayload, entry.Key);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        private static void WriteEnvironmentExtensions(Utf8JsonWriter writer, JsonElement? raw, string name)
        {
            if (raw is not { ValueKind: JsonValueKind.Object } root
                || !root.TryGetProperty("env", out var env) || env.ValueKind != JsonValueKind.Array) return;
            foreach (var item in env.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || ReadString(item, "name") != name
                    || ReadString(item, "value") is null) continue;
                foreach (var property in item.EnumerateObject())
                {
                    if (property.Name is not ("name" or "value")) property.WriteTo(writer);
                }

                return;
            }
        }

        private static string? ReadString(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            return property.GetString();
        }
    }
}
