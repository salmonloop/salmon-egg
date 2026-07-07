using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Infrastructure.Storage;

public sealed class PlainTextFileSecureStorage : ISecureStorage
{
    private readonly IAppFileStore _fileStore;
    private readonly string _storageDirectory;

    public PlainTextFileSecureStorage(IAppFileStore fileStore, IAppDataService appData)
    {
        _fileStore = fileStore ?? throw new ArgumentNullException(nameof(fileStore));
        if (appData is null) throw new ArgumentNullException(nameof(appData));
        _storageDirectory = System.IO.Path.Combine(appData.AppDataRootPath, "SecureStoragePlainText");
    }

    public Task SaveAsync(string key, string value)
    {
        ValidateKey(key);
        if (value is null) throw new ArgumentNullException(nameof(value));

        return _fileStore.WriteAllTextAsync(GetPath(key), value);
    }

    public Task<string?> LoadAsync(string key)
    {
        ValidateKey(key);
        return _fileStore.ReadAllTextAsync(GetPath(key));
    }

    public Task DeleteAsync(string key)
    {
        ValidateKey(key);
        return _fileStore.DeleteAsync(GetPath(key));
    }

    private string GetPath(string key)
    {
        byte[] bytes;
        using (var sha256 = SHA256.Create())
        {
            bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(key));
        }

        var fileName = ToHex(bytes) + ".secret";
        return System.IO.Path.Combine(_storageDirectory, fileName);
    }

    private static string ToHex(byte[] bytes)
    {
        var builder = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes)
        {
            builder.Append(b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentNullException(nameof(key));
        }
    }
}
