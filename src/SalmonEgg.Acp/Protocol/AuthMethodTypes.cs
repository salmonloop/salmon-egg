using System;
using System.Text.Json;
using System.Text.Json.Serialization;

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
        public string ResolvedType => string.IsNullOrWhiteSpace(Type) ? AgentType : Type;

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
    }

    internal sealed class AuthMethodDefinitionJsonConverter : JsonConverter<AuthMethodDefinition>
    {
        public override AuthMethodDefinition? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;

            return new AuthMethodDefinition
            {
                Id = ReadString(root, "methodId") ?? ReadString(root, "id") ?? string.Empty,
                Name = ReadString(root, "name") ?? string.Empty,
                Type = ReadString(root, "type"),
                Description = ReadString(root, "description"),
                Meta = AcpMetaJson.Read(root)
            };
        }

        public override void Write(Utf8JsonWriter writer, AuthMethodDefinition value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("id", value.Id);
            writer.WriteString("name", value.Name);

            if (!string.IsNullOrWhiteSpace(value.Type))
            {
                writer.WriteString("type", value.Type);
            }

            if (!string.IsNullOrWhiteSpace(value.Description))
            {
                writer.WriteString("description", value.Description);
            }

            AcpMetaJson.Write(writer, value.Meta);
            writer.WriteEndObject();
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
