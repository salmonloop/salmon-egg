using System.Text.Json.Serialization;

namespace SalmonEgg.Acp.Protocol
{
    public sealed class PermissionOutcomeResult
    {
        [JsonPropertyName("outcome")]
        public PermissionOutcome Outcome { get; set; } = new();
    }

    public sealed class PermissionOutcome
    {
        [JsonPropertyName("outcome")]
        public string Outcome { get; set; } = string.Empty;

        [JsonPropertyName("optionId")]
        public string? OptionId { get; set; }
    }

    public sealed class ReadTextFileResult
    {
        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
    }
}
