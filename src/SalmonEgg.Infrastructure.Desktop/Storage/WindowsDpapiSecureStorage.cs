using System;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace SalmonEgg.Infrastructure.Storage;

/// <summary>
/// Windows DPAPI-backed secure storage.
/// </summary>
/// <remarks>
/// The implementation is shared by the WinUI application and the desktop CLI. The on-disk
/// directory and file-name encoding are intentionally unchanged so existing credentials remain
/// readable after the implementation moves out of the Uno application assembly.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WindowsDpapiSecureStorage : ISecureStorage
{
    private readonly string _storageDirectory;

    public WindowsDpapiSecureStorage()
    {
        _storageDirectory = Path.Combine(SalmonEggPaths.GetAppDataRootPath(), "SecureStorage");
        // Storage directory is created lazily on first write to avoid touching the filesystem at construction time.
    }

    public async Task SaveAsync(string key, string value)
    {
        ValidateKey(key);
        ArgumentNullException.ThrowIfNull(value);
        Directory.CreateDirectory(_storageDirectory);

        var path = GetFilePath(key);
        var bytes = Encoding.UTF8.GetBytes(value);
        var protectedBytes = ProtectedData.Protect(bytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        await File.WriteAllTextAsync(path, Convert.ToBase64String(protectedBytes)).ConfigureAwait(false);
    }

    public async Task<string?> LoadAsync(string key)
    {
        ValidateKey(key);

        var path = GetFilePath(key);
        if (!File.Exists(path))
        {
            return null;
        }

        var encoded = await File.ReadAllTextAsync(path).ConfigureAwait(false);
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(encoded);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException($"Stored secure data for key '{key}' is not valid Base64.", ex);
        }

        try
        {
            var unprotectedBytes = ProtectedData.Unprotect(bytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(unprotectedBytes);
        }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException($"Stored secure data for key '{key}' could not be decrypted.", ex);
        }
    }

    public Task DeleteAsync(string key)
    {
        ValidateKey(key);

        var path = GetFilePath(key);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private string GetFilePath(string key)
    {
        var fileName = Convert.ToBase64String(Encoding.UTF8.GetBytes(key))
            .Replace("/", "_", StringComparison.Ordinal)
            .Replace("+", "-", StringComparison.Ordinal) + ".dat";
        return Path.Combine(_storageDirectory, fileName);
    }

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            throw new ArgumentNullException(nameof(key));
        }
    }
}
