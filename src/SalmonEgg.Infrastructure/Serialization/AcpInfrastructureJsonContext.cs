using System.Text.Json;
using System.Text.Json.Serialization;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.Diagnostics;

namespace SalmonEgg.Infrastructure.Serialization;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    AllowOutOfOrderMetadataProperties = true)]
[JsonSerializable(typeof(AcpMessage))]
[JsonSerializable(typeof(AcpError))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(PermissionOutcomeResult))]
[JsonSerializable(typeof(ReadTextFileResult))]
[JsonSerializable(typeof(DiagnosticsSnapshot))]
internal partial class AcpInfrastructureJsonContext : JsonSerializerContext
{
}

internal sealed class PermissionOutcomeResult
{
    [JsonPropertyName("outcome")]
    public PermissionOutcome Outcome { get; set; } = new();
}

internal sealed class PermissionOutcome
{
    [JsonPropertyName("outcome")]
    public string Outcome { get; set; } = string.Empty;

    [JsonPropertyName("optionId")]
    public string? OptionId { get; set; }
}

internal sealed class ReadTextFileResult
{
    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;
}
