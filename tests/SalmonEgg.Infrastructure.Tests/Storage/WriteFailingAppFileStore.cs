using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Services;
using SalmonEgg.Infrastructure.Storage;

namespace SalmonEgg.Infrastructure.Tests.Storage;

internal sealed class WriteFailingAppFileStore : IConfigurationFileStore
{
    private readonly FileSystemAppFileStore _inner = new();
    private int _writeFailuresRemaining = 1;

    public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default)
        => _inner.ExistsAsync(path, cancellationToken);

    public Task<string?> ReadAllTextAsync(string path, CancellationToken cancellationToken = default)
        => _inner.ReadAllTextAsync(path, cancellationToken);

    public Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken = default)
        => _inner.WriteAllTextAsync(path, content, cancellationToken);

    public Task DeleteAsync(string path, CancellationToken cancellationToken = default)
        => _inner.DeleteAsync(path, cancellationToken);

    public async IAsyncEnumerable<string> EnumerateFilesAsync(
        string directory,
        string searchPattern,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var path in _inner.EnumerateFilesAsync(directory, searchPattern, cancellationToken))
        {
            yield return path;
        }
    }

    public Task<IConfigurationFileTransaction> BeginWriteAsync(
        string path,
        string content,
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _writeFailuresRemaining, 0) == 1)
        {
            return Task.FromResult<IConfigurationFileTransaction>(
                new ThrowingConfigurationFileTransaction(new IOException("write failed")));
        }

        return _inner.BeginWriteAsync(path, content, cancellationToken);
    }

    public Task<IConfigurationFileTransaction> BeginDeleteAsync(
        string path,
        CancellationToken cancellationToken = default)
        => _inner.BeginDeleteAsync(path, cancellationToken);
}
