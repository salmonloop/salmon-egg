using System;
using System.IO;
using System.Text;

namespace SalmonEgg.Infrastructure.Storage;

internal static class ConfigurationProfilePaths
{
    public static string GetServerYamlPath(string serversDirectory, string profileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serversDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        return Path.Combine(serversDirectory, GetServerFileName(profileId) + ".yaml");
    }

    private static string GetServerFileName(string profileId)
    {
        if (IsSafeFileName(profileId))
        {
            return profileId;
        }

        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(profileId))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return "id_" + encoded;
    }

    private static bool IsSafeFileName(string value)
    {
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character) || character is '-' or '_')
            {
                continue;
            }

            return false;
        }

        return value.Length > 0;
    }
}
