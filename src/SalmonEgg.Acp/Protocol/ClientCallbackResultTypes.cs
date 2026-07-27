using System.Text.Json.Serialization;

namespace SalmonEgg.Acp.Protocol
{
    public sealed record PermissionOutcomeResult : AcpProtocolObject
    {
        [JsonPropertyName("outcome")]
        public PermissionOutcome Outcome { get; init; } = new();
    }

    public sealed record PermissionOutcome : AcpProtocolObject
    {
        [JsonPropertyName("outcome")]
        public string Outcome { get; init; } = string.Empty;

        [JsonPropertyName("optionId")]
        public string? OptionId { get; init; }
    }

    public sealed record ReadTextFileResult : AcpProtocolObject
    {
        [JsonPropertyName("content")]
        public string Content { get; init; } = string.Empty;
    }
}
