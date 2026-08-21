namespace SalmonEgg.Infrastructure.Storage;

internal static class ConfigurationPackagePaths
{
    internal static string Normalize(string relativePath) => relativePath.Replace('\\', '/');
}
