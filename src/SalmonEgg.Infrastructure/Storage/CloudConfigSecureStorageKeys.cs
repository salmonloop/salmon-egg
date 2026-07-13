using System;
using System.Collections.Generic;

namespace SalmonEgg.Infrastructure.Storage;

public static class CloudConfigSecureStorageKeys
{
    public const string WebDavPassword = "salmonegg/cloud-sync/webdav/password";
    public const string S3AccessKeyId = "salmonegg/cloud-sync/s3/access-key-id";
    public const string S3SecretAccessKey = "salmonegg/cloud-sync/s3/secret-access-key";

    private static readonly CloudConfigSecretRegistration[] RegisteredSecrets =
    [
        new("webdav", "password", WebDavPassword),
        new("s3", "access_key_id", S3AccessKeyId),
        new("s3", "secret_access_key", S3SecretAccessKey)
    ];

    public static IReadOnlyList<CloudConfigSecretRegistration> Registrations => RegisteredSecrets;

    public static bool TryGetStorageKey(string providerId, string secretName, out string storageKey)
    {
        foreach (var registration in RegisteredSecrets)
        {
            if (string.Equals(registration.ProviderId, providerId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(registration.SecretName, secretName, StringComparison.OrdinalIgnoreCase))
            {
                storageKey = registration.StorageKey;
                return true;
            }
        }

        storageKey = string.Empty;
        return false;
    }
}

public sealed record CloudConfigSecretRegistration(string ProviderId, string SecretName, string StorageKey);
