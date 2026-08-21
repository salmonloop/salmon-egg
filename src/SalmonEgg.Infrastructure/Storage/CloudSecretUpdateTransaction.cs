using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Infrastructure.Storage;

public sealed class CloudSecretUpdateTransaction : ICloudSecretUpdateTransaction
{
    // Only ever used to put previous values back, never to apply an update, so it is the restore store
    // rather than the caller's store.
    private readonly ISecureStorage? _restoreStorage;
    private readonly IReadOnlyDictionary<string, string?> _previousValues;
    private bool _completed;
    private bool _disposed;

    private CloudSecretUpdateTransaction(
        ISecureStorage? restoreStorage,
        IReadOnlyDictionary<string, string?> previousValues)
    {
        _restoreStorage = restoreStorage;
        _previousValues = previousValues;
    }

    public static ICloudSecretUpdateTransaction None() =>
        new CloudSecretUpdateTransaction(null, new Dictionary<string, string?>());

    public static async Task<ICloudSecretUpdateTransaction> BeginAsync(
        ISecureStorage secureStorage,
        IReadOnlyDictionary<string, CloudSecretUpdate> updates,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(secureStorage);
        ArgumentNullException.ThrowIfNull(updates);

        var previousValues = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var update in updates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            previousValues[update.Key] = await secureStorage.LoadAsync(update.Key).ConfigureAwait(false);
        }

        // Applying an update goes through the caller's store so a fail-closed policy can still refuse a
        // new secret. Restoring must not be refused: it puts back a value that was already stored, and a
        // blocked restore would strand the mutation half-applied. See ISecureStorageRecoveryMaterialSource.
        var restoreStorage = secureStorage is ISecureStorageRecoveryMaterialSource recoveryMaterialSource
            ? recoveryMaterialSource.GetRecoveryMaterialStore()
            : secureStorage;
        var transaction = new CloudSecretUpdateTransaction(restoreStorage, previousValues);
        try
        {
            foreach (var update in updates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (update.Value.Kind == CloudSecretUpdateKind.Clear)
                {
                    await secureStorage.DeleteAsync(update.Key).ConfigureAwait(false);
                }
                else if (update.Value.Kind == CloudSecretUpdateKind.Replace)
                {
                    await secureStorage.SaveAsync(update.Key, update.Value.Value ?? string.Empty).ConfigureAwait(false);
                }
            }

            return transaction;
        }
        catch (Exception operationException)
        {
            try
            {
                await transaction.RestoreAsync().ConfigureAwait(false);
            }
            catch (Exception rollbackException)
            {
                throw new AggregateException(operationException, rollbackException);
            }

            throw;
        }
    }

    public void Complete() => _completed = true;

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (!_completed)
        {
            await RestoreAsync().ConfigureAwait(false);
        }
    }

    private async Task RestoreAsync()
    {
        if (_restoreStorage is null)
        {
            return;
        }

        foreach (var previous in _previousValues)
        {
            if (previous.Value is null)
            {
                await _restoreStorage.DeleteAsync(previous.Key).ConfigureAwait(false);
            }
            else
            {
                await _restoreStorage.SaveAsync(previous.Key, previous.Value).ConfigureAwait(false);
            }
        }
    }
}
