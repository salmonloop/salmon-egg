using System;
using System.Security.Cryptography;
using System.Text;

namespace SalmonEgg.Infrastructure.Storage;

internal static class ConfigurationSecretKeys
{
    public static string GetTokenKey(string serverId) => $"salmonegg/config/{serverId}/token";

    public static string GetApiKeyKey(string serverId) => $"salmonegg/config/{serverId}/apiKey";

    public static string GetRecoveryTokenKey(string serverId) =>
        $"salmonegg/config-recovery/{GetStableId(serverId)}/token";

    public static string GetRecoveryApiKeyKey(string serverId) =>
        $"salmonegg/config-recovery/{GetStableId(serverId)}/apiKey";

    private static string GetStableId(string serverId)
    {
        using var sha256 = SHA256.Create();
        return Convert.ToHexString(sha256.ComputeHash(Encoding.UTF8.GetBytes(serverId))).ToLowerInvariant();
    }
}
