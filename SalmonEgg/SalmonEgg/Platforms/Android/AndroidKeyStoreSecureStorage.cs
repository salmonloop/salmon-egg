#if __ANDROID__
using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Android.Content;
using Android.OS;
using Android.Security.Keystore;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;
using SalmonEgg.Infrastructure.Storage;

namespace SalmonEgg.Infrastructure.Storage;

public sealed class AndroidKeyStoreSecureStorage : ISecureStorage
{
    private const int GcmTagBits = 128;
    private const int IvLength = 12;
    private const string AndroidKeyStoreName = "AndroidKeyStore";
    private const string CipherTransformation = "AES/GCM/NoPadding";
    private const string KeyAlias = "SalmonEggSecureStorage";
    private const string PreferencesName = "SalmonEgg.SecureStorage";

    public Task SaveAsync(string key, string value)
    {
        ValidateKey(key);
        ArgumentNullException.ThrowIfNull(value);
        EnsureSupportedPlatform();

        var cipher = Cipher.GetInstance(CipherTransformation)
            ?? throw new SecureStorageUnavailableException("Android secure storage cipher is unavailable.");
        cipher.Init(Javax.Crypto.CipherMode.EncryptMode, GetOrCreateSecretKey());

        var iv = cipher.GetIV()
            ?? throw new SecureStorageUnavailableException("Android secure storage cipher did not produce an IV.");
        var ciphertext = cipher.DoFinal(Encoding.UTF8.GetBytes(value))
            ?? throw new SecureStorageUnavailableException("Android secure storage encryption failed.");

        Preferences.Edit()
            ?.PutString(GetKeyHash(key), Convert.ToBase64String(Combine(iv, ciphertext)))
            ?.Apply();
        return Task.CompletedTask;
    }

    public Task<string?> LoadAsync(string key)
    {
        ValidateKey(key);
        EnsureSupportedPlatform();

        var encoded = Preferences.GetString(GetKeyHash(key), null);
        if (string.IsNullOrEmpty(encoded))
        {
            return Task.FromResult<string?>(null);
        }

        try
        {
            var payload = Convert.FromBase64String(encoded);
            if (payload.Length <= IvLength)
            {
                throw new SecureStorageUnavailableException("Android secure storage payload is malformed.");
            }

            var iv = payload.AsSpan(0, IvLength).ToArray();
            var ciphertext = payload.AsSpan(IvLength).ToArray();
            var cipher = Cipher.GetInstance(CipherTransformation)
                ?? throw new SecureStorageUnavailableException("Android secure storage cipher is unavailable.");
            cipher.Init(Javax.Crypto.CipherMode.DecryptMode, GetOrCreateSecretKey(), new GCMParameterSpec(GcmTagBits, iv));

            var plaintext = cipher.DoFinal(ciphertext)
                ?? throw new SecureStorageUnavailableException("Android secure storage decryption failed.");
            return Task.FromResult<string?>(Encoding.UTF8.GetString(plaintext));
        }
        catch (FormatException ex)
        {
            throw new SecureStorageUnavailableException($"Android secure storage payload is not valid Base64: {ex.Message}");
        }
        catch (GeneralSecurityException ex)
        {
            throw new SecureStorageUnavailableException($"Android secure storage decryption failed: {ex.Message}");
        }
    }

    public Task DeleteAsync(string key)
    {
        ValidateKey(key);
        Preferences.Edit()?.Remove(GetKeyHash(key))?.Apply();
        return Task.CompletedTask;
    }

    private static ISharedPreferences Preferences =>
        global::Android.App.Application.Context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)
        ?? throw new SecureStorageUnavailableException("Android secure storage preferences are unavailable.");

    private static byte[] Combine(byte[] iv, byte[] ciphertext)
    {
        var payload = new byte[iv.Length + ciphertext.Length];
        Buffer.BlockCopy(iv, 0, payload, 0, iv.Length);
        Buffer.BlockCopy(ciphertext, 0, payload, iv.Length, ciphertext.Length);
        return payload;
    }

    private static void EnsureSupportedPlatform()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.M)
        {
            throw new SecureStorageUnavailableException("Android secure storage requires Android 6.0 (API 23) or newer.");
        }
    }

    private static string GetKeyHash(string key)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(key));
        return Convert.ToBase64String(hash)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static IKey GetOrCreateSecretKey()
    {
        var keyStore = KeyStore.GetInstance(AndroidKeyStoreName)
            ?? throw new SecureStorageUnavailableException("AndroidKeyStore is unavailable.");
        keyStore.Load(null);

        if (keyStore.ContainsAlias(KeyAlias))
        {
            return keyStore.GetKey(KeyAlias, null)
                ?? throw new SecureStorageUnavailableException("AndroidKeyStore key is unavailable.");
        }

        var keyGenerator = KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes, AndroidKeyStoreName)
            ?? throw new SecureStorageUnavailableException("AndroidKeyStore AES generator is unavailable.");
        var spec = new KeyGenParameterSpec.Builder(
                KeyAlias,
                KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)
            .SetBlockModes(KeyProperties.BlockModeGcm)
            .SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone)
            .SetRandomizedEncryptionRequired(true)
            .Build();

        keyGenerator.Init(spec);
        return keyGenerator.GenerateKey()
            ?? throw new SecureStorageUnavailableException("AndroidKeyStore key generation failed.");
    }

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            throw new ArgumentNullException(nameof(key));
        }
    }
}
#endif
