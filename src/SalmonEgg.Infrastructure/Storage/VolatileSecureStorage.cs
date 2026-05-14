using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SalmonEgg.Infrastructure.Storage;

/// <summary>
/// Session-scoped storage for platforms where this app has no native secure store integration yet.
/// </summary>
public sealed class VolatileSecureStorage : ISecureStorage
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

    public Task SaveAsync(string key, string value)
    {
        ValidateKey(key);
        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        _values[key] = value;
        return Task.CompletedTask;
    }

    public Task<string?> LoadAsync(string key)
    {
        ValidateKey(key);

        _values.TryGetValue(key, out var value);
        return Task.FromResult<string?>(value);
    }

    public Task DeleteAsync(string key)
    {
        ValidateKey(key);

        _values.Remove(key);
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
