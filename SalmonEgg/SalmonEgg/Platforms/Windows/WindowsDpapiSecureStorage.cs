#if WINDOWS
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using SalmonEgg.Infrastructure.Storage;

namespace SalmonEgg.Platforms.Windows;

public sealed class WindowsDpapiSecureStorage : ISecureStorage
{
    private readonly string _storageDirectory;

    public WindowsDpapiSecureStorage()
    {
        _storageDirectory = Path.Combine(SalmonEggPaths.GetAppDataRootPath(), "SecureStorage");
        Directory.CreateDirectory(_storageDirectory);
    }

    public async Task SaveAsync(string key, string value)
    {
        ValidateKey(key);
        ArgumentNullException.ThrowIfNull(value);

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
        catch (CryptographicException)
        {
            if (TryDecodeLegacyPlainText(bytes, out var legacyValue))
            {
                await SaveAsync(key, legacyValue).ConfigureAwait(false);
                return legacyValue;
            }

            throw new InvalidOperationException($"Stored secure data for key '{key}' could not be decrypted.");
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

    private static bool TryDecodeLegacyPlainText(byte[] bytes, out string value)
    {
        value = string.Empty;
        try
        {
            var decoder = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            var decoded = decoder.GetString(bytes);
            if (!IsPlausibleLegacySecret(decoded))
            {
                return false;
            }

            value = decoded;
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static bool IsPlausibleLegacySecret(string value)
    {
        foreach (var character in value)
        {
            if (char.IsControl(character)
                && character is not '\r'
                && character is not '\n'
                && character is not '\t')
            {
                return false;
            }
        }

        return true;
    }
}
#endif
