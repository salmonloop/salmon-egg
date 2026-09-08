using System;
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

        // Unimplemented variants must survive initialization replay, including terminal args/env.
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

            return new AuthMethodDefinition
            {
                Id = ReadString(root, GetIdPropertyName(protocolVersion)) ?? string.Empty,
                Name = ReadString(root, "name") ?? string.Empty,
                Type = ReadDiscriminator(root, protocolVersion),
                Description = ReadString(root, "description"),
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
            WriteAdditionalProperties(writer, value.RawPayload, protocolVersion);
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

        private static void WriteAdditionalProperties(Utf8JsonWriter writer, JsonElement? rawPayload, int protocolVersion)
        {
            if (rawPayload is not { ValueKind: JsonValueKind.Object } root)
            {
                return;
            }

            foreach (var property in root.EnumerateObject())
            {
                if (property.Name == GetIdPropertyName(protocolVersion)
                    || property.Name is "name" or "type" or "description" or "_meta")
                {
                    continue;
                }

                writer.WritePropertyName(property.Name);
                writer.WriteRawValue(property.Value.GetRawText());
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
