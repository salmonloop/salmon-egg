using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace SalmonEgg.Infrastructure.Storage;

public sealed class VolatileSecureStorage : ISecureStorage
{
    private readonly ConcurrentDictionary<string, string> _values = new(StringComparer.Ordinal);

    public Task SaveAsync(string key, string value)
    {
        ValidateKey(key);
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        _values[key] = value;
        return Task.CompletedTask;
    }

    public Task<string?> LoadAsync(string key)
    {
        ValidateKey(key);
        return Task.FromResult(_values.TryGetValue(key, out var value) ? value : null);
    }

    public Task DeleteAsync(string key)
    {
        ValidateKey(key);
        _values.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            throw new ArgumentNullException(nameof(key));
        }
    }
}
