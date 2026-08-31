using System.Text.Json;
using System.Text.Json.Serialization;
using SalmonEgg.Acp.JsonRpc;

namespace SalmonEgg.Acp.Serialization;

/// <summary>
/// Source-generated context for internal JSON-RPC envelope types.
/// Kept internal so public <see cref="AcpJsonContext"/> does not expose envelope type infos.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    AllowOutOfOrderMetadataProperties = true)]
[JsonSerializable(typeof(JsonRpcRequest))]
[JsonSerializable(typeof(JsonRpcResponse))]
[JsonSerializable(typeof(JsonRpcNotification))]
[JsonSerializable(typeof(JsonRpcError))]
[JsonSerializable(typeof(JsonElement))]
internal partial class AcpJsonRpcContext : JsonSerializerContext
{
}
