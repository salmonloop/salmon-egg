#if __IOS__
using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Foundation;
using SalmonEgg.Infrastructure.Storage;
using Security;

namespace SalmonEgg.Infrastructure.Storage;

public sealed class IosKeychainSecureStorage : ISecureStorage
{
    private const string ServiceName = "SalmonEgg";

    public Task SaveAsync(string key, string value)
    {
        ValidateKey(key);
        ArgumentNullException.ThrowIfNull(value);

        using var query = CreateQuery(key);
        var removeStatus = SecKeyChain.Remove(query);
        if (removeStatus != SecStatusCode.Success && removeStatus != SecStatusCode.ItemNotFound)
        {
            ThrowKeychainFailure(removeStatus, "replace");
        }

        using var data = NSData.FromArray(Encoding.UTF8.GetBytes(value));
        using var record = CreateQuery(key);
        record.ValueData = data;

        var addStatus = SecKeyChain.Add(record);
        ThrowIfFailed(addStatus, "save");
        return Task.CompletedTask;
    }

    public Task<string?> LoadAsync(string key)
    {
        ValidateKey(key);

        using var query = CreateQuery(key);
        using var result = SecKeyChain.QueryAsRecord(query, out var status);
        if (status == SecStatusCode.ItemNotFound)
        {
            return Task.FromResult<string?>(null);
        }

        ThrowIfFailed(status, "load");
        var valueData = result?.ValueData;
        if (valueData is null)
        {
            return Task.FromResult<string?>(null);
        }

        return Task.FromResult<string?>(Encoding.UTF8.GetString(valueData.ToArray()));
    }

    public Task DeleteAsync(string key)
    {
        ValidateKey(key);

        using var query = CreateQuery(key);
        var status = SecKeyChain.Remove(query);
        if (status == SecStatusCode.ItemNotFound)
        {
            return Task.CompletedTask;
        }

        ThrowIfFailed(status, "delete");
        return Task.CompletedTask;
    }

    private static SecRecord CreateQuery(string key)
        => new(SecKind.GenericPassword)
        {
            Service = ServiceName,
            Account = GetKeyHash(key)
        };

    private static string GetKeyHash(string key)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(key));
        return Convert.ToBase64String(hash)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static void ThrowIfFailed(SecStatusCode status, string operation)
    {
        if (status != SecStatusCode.Success)
        {
            ThrowKeychainFailure(status, operation);
        }
    }

    private static void ThrowKeychainFailure(SecStatusCode status, string operation)
        => throw new SecureStorageUnavailableException(
            $"iOS Keychain failed to {operation} SalmonEgg credentials. SecStatusCode={status}.");

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            throw new ArgumentNullException(nameof(key));
        }
    }
}
#endif
