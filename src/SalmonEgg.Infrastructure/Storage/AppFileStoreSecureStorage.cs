using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Infrastructure.Storage;

/// <summary>
/// File-backed ISecureStorage test/compatibility implementation.
/// Values are base-64 encoded, not encrypted, so production platforms must prefer
/// OS-backed secure storage or a fail-closed volatile implementation.
/// The file name is derived from a SHA-256 hash of the key so the key is never exposed on disk.
/// </summary>
public sealed class AppFileStoreSecureStorage : ISecureStorage
{
    private readonly IAppFileStore _fileStore;
    private readonly string _secretsDirectory;

    public AppFileStoreSecureStorage(IAppFileStore fileStore, string secretsDirectory)
    {
        _fileStore = fileStore ?? throw new ArgumentNullException(nameof(fileStore));
        if (string.IsNullOrWhiteSpace(secretsDirectory))
            throw new ArgumentNullException(nameof(secretsDirectory));
        _secretsDirectory = secretsDirectory;
    }

    public async Task SaveAsync(string key, string value)
    {
        ValidateKey(key);
        if (value is null) throw new ArgumentNullException(nameof(value));

        var path = GetFilePath(key);
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        await _fileStore.WriteAllTextAsync(path, encoded).ConfigureAwait(false);
    }

    public async Task<string?> LoadAsync(string key)
    {
        ValidateKey(key);

        var path = GetFilePath(key);
        var encoded = await _fileStore.ReadAllTextAsync(path).ConfigureAwait(false);
        if (encoded is null)
            return null;

        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("Stored secure data for key is not valid Base64.", ex);
        }
    }

    public async Task DeleteAsync(string key)
    {
        ValidateKey(key);
        var path = GetFilePath(key);
        await _fileStore.DeleteAsync(path).ConfigureAwait(false);
    }

    private string GetFilePath(string key)
    {
        using var sha = SHA256.Create();
        var keyHash = sha.ComputeHash(Encoding.UTF8.GetBytes(key));
        var fileName = Convert.ToBase64String(keyHash)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_')
            + ".dat";
        return System.IO.Path.Combine(_secretsDirectory, fileName);
    }

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentNullException(nameof(key));
    }
}
