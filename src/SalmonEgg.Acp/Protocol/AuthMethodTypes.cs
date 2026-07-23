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
    public sealed class AuthMethodDefinition : AcpProtocolObject
    {
        public string Id { get; set; } = string.Empty;

        [JsonIgnore]
        public string MethodId
        {
            get => Id;
            set => Id = value;
        }

        public string Name { get; set; } = string.Empty;

        public string? Type { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }
    }

    public sealed class AuthMethodDefinitionJsonConverter : JsonConverter<AuthMethodDefinition>
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
