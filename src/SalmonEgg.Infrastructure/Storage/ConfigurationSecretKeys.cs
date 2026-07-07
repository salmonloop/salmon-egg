namespace SalmonEgg.Infrastructure.Storage;

internal static class ConfigurationSecretKeys
{
    public static string GetTokenKey(string serverId) => $"salmonegg/config/{serverId}/token";

    public static string GetApiKeyKey(string serverId) => $"salmonegg/config/{serverId}/apiKey";
}
