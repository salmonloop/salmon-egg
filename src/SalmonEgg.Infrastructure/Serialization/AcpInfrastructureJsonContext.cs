using System.Text.Json;
using System.Text.Json.Serialization;
using SalmonEgg.Domain.Models.Diagnostics;

namespace SalmonEgg.Infrastructure.Serialization;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    AllowOutOfOrderMetadataProperties = true)]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(DiagnosticsSnapshot))]
internal partial class AcpInfrastructureJsonContext : JsonSerializerContext
{
}
