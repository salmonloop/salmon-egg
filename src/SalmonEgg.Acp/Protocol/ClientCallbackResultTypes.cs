using System.Text.Json.Serialization;

namespace SalmonEgg.Acp.Protocol
{
    public sealed record PermissionOutcomeResult : AcpProtocolObject
    {
        [JsonPropertyName("outcome")]
        public PermissionOutcome Outcome { get; set; } = new();
    }

    public sealed record PermissionOutcome : AcpProtocolObject
    {
        [JsonPropertyName("outcome")]
        public string Outcome { get; set; } = string.Empty;

        [JsonPropertyName("optionId")]
        public string? OptionId { get; set; }
    }

    public sealed record ReadTextFileResult : AcpProtocolObject
    {
        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
    }
}
