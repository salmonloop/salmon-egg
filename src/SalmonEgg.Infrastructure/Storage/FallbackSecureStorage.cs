using System;
using System.Threading.Tasks;

namespace SalmonEgg.Infrastructure.Storage;

public sealed class FallbackSecureStorage : ISecureStorage
{
    private readonly ISecureStorage _primary;
    private readonly ISecureStorage _fallback;

    public FallbackSecureStorage(ISecureStorage primary, ISecureStorage fallback)
    {
        _primary = primary ?? throw new ArgumentNullException(nameof(primary));
        _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
    }

    public async Task SaveAsync(string key, string value)
    {
        try
        {
            await _primary.SaveAsync(key, value).ConfigureAwait(false);
        }
        catch (SecureStorageUnavailableException)
        {
            await _fallback.SaveAsync(key, value).ConfigureAwait(false);
        }
    }

    public async Task<string?> LoadAsync(string key)
    {
        try
        {
            var value = await _primary.LoadAsync(key).ConfigureAwait(false);
            if (value is not null)
            {
                return value;
            }
        }
        catch (SecureStorageUnavailableException)
        {
        }

        return await _fallback.LoadAsync(key).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string key)
    {
        try
        {
            await _primary.DeleteAsync(key).ConfigureAwait(false);
        }
        catch (SecureStorageUnavailableException)
        {
        }

        await _fallback.DeleteAsync(key).ConfigureAwait(false);
    }
}
