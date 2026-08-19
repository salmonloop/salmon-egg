using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Infrastructure.Storage;

public sealed class CloudSecretUpdateTransaction : ICloudSecretUpdateTransaction
{
    private readonly ISecureStorage? _secureStorage;
    private readonly IReadOnlyDictionary<string, string?> _previousValues;
    private bool _completed;
    private bool _disposed;

    private CloudSecretUpdateTransaction(
        ISecureStorage? secureStorage,
        IReadOnlyDictionary<string, string?> previousValues)
    {
        _secureStorage = secureStorage;
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

        var transaction = new CloudSecretUpdateTransaction(secureStorage, previousValues);
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
        if (_secureStorage is null)
        {
            return;
        }

        foreach (var previous in _previousValues)
        {
            if (previous.Value is null)
            {
                await _secureStorage.DeleteAsync(previous.Key).ConfigureAwait(false);
            }
            else
            {
                await _secureStorage.SaveAsync(previous.Key, previous.Value).ConfigureAwait(false);
            }
        }
    }
}
