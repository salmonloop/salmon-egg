using System.Text.Json.Serialization;

namespace SalmonEgg.Acp.Protocol
{
    /// <summary>
    /// Authentication method advertised by the agent during initialization.
    /// Custom authentication metadata is carried through the ACP <c>_meta</c> field.
    /// </summary>
    public sealed class AuthMethodDefinition : AcpProtocolObject
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string? Description { get; set; }

    }
}
