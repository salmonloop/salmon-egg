using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SalmonEgg.Infrastructure.Storage;

/// <summary>
/// Uses the platform secret store and falls back to a secondary store when it is unavailable.
/// </summary>
/// <remarks>
/// The fallback is a weaker store than the platform keychain, so every fallback is a security
/// downgrade. Downgrades are logged because the app keeps working either way: without that record an
/// operator cannot tell that credentials stopped being protected by the platform.
/// </remarks>
public sealed class FallbackSecureStorage : ISecureStorage
{
    private readonly ISecureStorage _primary;
    private readonly ISecureStorage _fallback;
    private readonly ILogger<FallbackSecureStorage> _logger;

    public FallbackSecureStorage(
        ISecureStorage primary,
        ISecureStorage fallback,
        ILogger<FallbackSecureStorage>? logger = null)
    {
        _primary = primary ?? throw new ArgumentNullException(nameof(primary));
        _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
        _logger = logger ?? NullLogger<FallbackSecureStorage>.Instance;
    }

    public async Task SaveAsync(string key, string value)
    {
        var storedInPrimary = false;
        try
        {
            await _primary.SaveAsync(key, value).ConfigureAwait(false);
            storedInPrimary = true;
        }
        catch (SecureStorageUnavailableException ex)
        {
            // The value still has to be stored, but it now lives in the weaker store.
            _logger.LogWarning(
                ex,
                "Platform secret store unavailable; saving secret to the fallback store instead. PrimaryStore={PrimaryStore} FallbackStore={FallbackStore}",
                _primary.GetType().Name,
                _fallback.GetType().Name);
            await _fallback.SaveAsync(key, value).ConfigureAwait(false);
        }

        if (!storedInPrimary)
        {
            return;
        }

        // An earlier downgrade may have left a superseded copy of this key in the weaker store.
        // Leaving it there lets the old secret come back the next time the platform store is
        // unavailable and a read falls through, so an overwrite has to retire it, exactly as a delete
        // does. Kept out of the try above so a fallback failure is never mistaken for the platform
        // store being unavailable.
        await _fallback.DeleteAsync(key).ConfigureAwait(false);
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
        catch (SecureStorageUnavailableException ex)
        {
            _logger.LogWarning(
                ex,
                "Platform secret store unavailable; reading secret from the fallback store instead. PrimaryStore={PrimaryStore} FallbackStore={FallbackStore}",
                _primary.GetType().Name,
                _fallback.GetType().Name);
        }

        return await _fallback.LoadAsync(key).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string key)
    {
        try
        {
            await _primary.DeleteAsync(key).ConfigureAwait(false);
        }
        catch (SecureStorageUnavailableException ex)
        {
            _logger.LogWarning(
                ex,
                "Platform secret store unavailable while deleting a secret; the fallback store is still cleared. PrimaryStore={PrimaryStore} FallbackStore={FallbackStore}",
                _primary.GetType().Name,
                _fallback.GetType().Name);
        }

        // Always clear the fallback too: a secret that was downgraded earlier must not survive a
        // delete just because the platform store is reachable again.
        await _fallback.DeleteAsync(key).ConfigureAwait(false);
    }
}
