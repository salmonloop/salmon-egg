using System;
using System.Security.Cryptography;
using System.Text;
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
public sealed class FallbackSecureStorage : ISecureStorage, ISecureStorageRecoveryMaterialSource
{
    public const int SecretFallbackUsedEventId = 7101;

    private const string FallbackValueAuthority = "fallback-value-v1";
    private const string DeletedAuthority = "deleted-v1";

    private readonly ISecureStorage _primary;
    private readonly ISecureStorage _fallback;
    private readonly ILogger<FallbackSecureStorage> _logger;
    private readonly SecureStorageDowngradePolicy _downgradePolicy;

    public FallbackSecureStorage(
        ISecureStorage primary,
        ISecureStorage fallback,
        ILogger<FallbackSecureStorage>? logger = null)
        : this(primary, fallback, SecureStorageDowngradePolicy.AllowPlaintextDowngrade, logger)
    {
    }

    public FallbackSecureStorage(
        ISecureStorage primary,
        ISecureStorage fallback,
        SecureStorageDowngradePolicy downgradePolicy,
        ILogger<FallbackSecureStorage>? logger = null)
    {
        _primary = primary ?? throw new ArgumentNullException(nameof(primary));
        _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
        _downgradePolicy = downgradePolicy;
        _logger = logger ?? NullLogger<FallbackSecureStorage>.Instance;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Rollback material always uses the downgrade-allowing behavior. See
    /// <see cref="ISecureStorageRecoveryMaterialSource"/> for why that cannot expose anything the caller
    /// has not already stored, and why refusing it would make a fail-closed host unable to clear secrets.
    /// </remarks>
    public ISecureStorage GetRecoveryMaterialStore()
        => _downgradePolicy == SecureStorageDowngradePolicy.AllowPlaintextDowngrade
            ? this
            : new FallbackSecureStorage(
                _primary,
                _fallback,
                SecureStorageDowngradePolicy.AllowPlaintextDowngrade,
                _logger);

    public async Task SaveAsync(string key, string value)
    {
        var storedInPrimary = false;
        try
        {
            await _primary.SaveAsync(key, value).ConfigureAwait(false);
            storedInPrimary = true;
        }
        catch (SecureStorageUnavailableException ex)
            when (_downgradePolicy == SecureStorageDowngradePolicy.FailClosed)
        {
            // Rethrown rather than downgraded: the caller asked for the secret to be protected by the
            // platform or not stored at all. Rethrowing the original keeps the transactional caller on
            // its existing SecureStorageUnavailableException path, which already restores prior state.
            _logger.LogWarning(
                ex,
                "Platform secret store unavailable and plaintext downgrade is disabled; the secret was not stored. PrimaryStore={PrimaryStore}",
                _primary.GetType().Name);
            throw;
        }
        catch (SecureStorageUnavailableException ex)
        {
            // The value still has to be stored, but it now lives in the weaker store.
            await _fallback.SaveAsync(key, value).ConfigureAwait(false);
            await _fallback.SaveAsync(GetAuthorityKey(key), FallbackValueAuthority).ConfigureAwait(false);
            _logger.LogWarning(
                new EventId(SecretFallbackUsedEventId, nameof(SecretFallbackUsedEventId)),
                ex,
                "Platform secret store unavailable; saved secret to the fallback store instead. PrimaryStore={PrimaryStore} FallbackStore={FallbackStore}",
                _primary.GetType().Name,
                _fallback.GetType().Name);
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
        await _fallback.DeleteAsync(GetAuthorityKey(key)).ConfigureAwait(false);
        await _fallback.DeleteAsync(key).ConfigureAwait(false);
    }

    public async Task<string?> LoadAsync(string key)
    {
        var authority = await _fallback.LoadAsync(GetAuthorityKey(key)).ConfigureAwait(false);
        if (string.Equals(authority, DeletedAuthority, StringComparison.Ordinal))
        {
            return null;
        }

        if (string.Equals(authority, FallbackValueAuthority, StringComparison.Ordinal))
        {
            var authoritativeValue = await _fallback.LoadAsync(key).ConfigureAwait(false);
            if (authoritativeValue is null)
            {
                throw new SecureStorageUnavailableException(
                    "The authoritative fallback secret is missing; refusing to return a stale platform value.");
            }

            LogFallbackRead();
            return authoritativeValue;
        }

        if (authority is not null)
        {
            throw new SecureStorageUnavailableException(
                "The fallback secret authority marker is invalid; refusing to choose between stores.");
        }

        // Values written by earlier versions predate authority markers. This store only received a
        // value after a platform-store write failed, so a surviving legacy fallback value is newer
        // recovery evidence than any primary value that may have remained from before the outage.
        var legacyFallbackValue = await _fallback.LoadAsync(key).ConfigureAwait(false);
        if (legacyFallbackValue is not null)
        {
            LogFallbackRead();
            return legacyFallbackValue;
        }

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

        return null;
    }

    public async Task DeleteAsync(string key)
    {
        // The tombstone is written before either store is changed. If the platform store is
        // unavailable, it remains authoritative and prevents an older platform value from
        // reappearing when that store recovers.
        await _fallback.SaveAsync(GetAuthorityKey(key), DeletedAuthority).ConfigureAwait(false);
        var primaryAvailable = true;
        try
        {
            await _primary.DeleteAsync(key).ConfigureAwait(false);
        }
        catch (SecureStorageUnavailableException ex)
        {
            primaryAvailable = false;
            _logger.LogWarning(
                ex,
                "Platform secret store unavailable while deleting a secret; the fallback store is still cleared. PrimaryStore={PrimaryStore} FallbackStore={FallbackStore}",
                _primary.GetType().Name,
                _fallback.GetType().Name);
        }

        // Always clear the fallback too: a secret that was downgraded earlier must not survive a
        // delete just because the platform store is reachable again.
        await _fallback.DeleteAsync(key).ConfigureAwait(false);
        if (primaryAvailable)
        {
            await _fallback.DeleteAsync(GetAuthorityKey(key)).ConfigureAwait(false);
        }
    }

    private void LogFallbackRead()
    {
        _logger.LogWarning(
            new EventId(SecretFallbackUsedEventId, nameof(SecretFallbackUsedEventId)),
            "Reading a secret from fallback storage because the platform store has no authoritative value. PrimaryStore={PrimaryStore} FallbackStore={FallbackStore}",
            _primary.GetType().Name,
            _fallback.GetType().Name);
    }

    private static string GetAuthorityKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return $"salmonegg/fallback-authority/{Convert.ToHexString(digest).ToLowerInvariant()}";
    }
}
