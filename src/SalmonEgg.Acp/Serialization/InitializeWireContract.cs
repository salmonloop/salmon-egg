using System.Text.Json;
using SalmonEgg.Acp.Protocol;

namespace SalmonEgg.Acp.Serialization;

internal static class InitializeWireContract
{
    private const string InvalidInfoMessage = "ACP v2 initialize requires 'info' with string 'name' and 'version'.";

    internal static JsonElement RequireInfo(JsonElement root)
    {
        // v2's Implementation is required; capabilities is separately defaultable by schema.
        // Keeping these checks separate prevents a malformed identity being treated as an empty peer.
        if (!root.TryGetProperty("info", out var info)
            || info.ValueKind != JsonValueKind.Object
            || !info.TryGetProperty("name", out var name)
            || name.ValueKind != JsonValueKind.String
            || !info.TryGetProperty("version", out var version)
            || version.ValueKind != JsonValueKind.String)
        {
            throw new JsonException(InvalidInfoMessage);
        }

        return info;
    }

    internal static void RequireInfo(ClientInfo? info) => RequireInfo(info?.Name, info?.Version);

    internal static void RequireInfo(AgentInfo? info) => RequireInfo(info?.Name, info?.Version);

    private static void RequireInfo(string? name, string? version)
    {
        if (name is null || version is null)
        {
            throw new JsonException(InvalidInfoMessage);
        }
    }
}
