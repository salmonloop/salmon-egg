using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Services;
using SalmonEgg.Infrastructure.Storage;

namespace SalmonEgg.Infrastructure.Tests.Storage;

internal sealed class FailingAppFileStore : IConfigurationFileStore
{
    private readonly FileSystemAppFileStore _inner = new();

    public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default)
        => IsRecoveryPath(path)
            ? _inner.ExistsAsync(path, cancellationToken)
            : throw new IOException("read failed");

    public Task<string?> ReadAllTextAsync(string path, CancellationToken cancellationToken = default)
        => IsRecoveryPath(path)
            ? _inner.ReadAllTextAsync(path, cancellationToken)
            : throw new IOException("read failed");

    public Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken = default)
        => throw new IOException("write failed");

    public Task DeleteAsync(string path, CancellationToken cancellationToken = default)
        => throw new IOException("delete failed");

    public async IAsyncEnumerable<string> EnumerateFilesAsync(
        string directory,
        string searchPattern,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (IsRecoveryPath(directory))
        {
            await foreach (var path in _inner.EnumerateFilesAsync(directory, searchPattern, cancellationToken))
            {
                yield return path;
            }

            yield break;
        }

        await Task.Yield();
        throw new IOException("enumerate failed");
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }

    public Task<IConfigurationFileTransaction> BeginWriteAsync(
        string path,
        string content,
        CancellationToken cancellationToken = default)
        => Task.FromException<IConfigurationFileTransaction>(new IOException("write failed"));

    public Task<IConfigurationFileTransaction> BeginDeleteAsync(
        string path,
        CancellationToken cancellationToken = default)
        => Task.FromException<IConfigurationFileTransaction>(new IOException("delete failed"));

    private static bool IsRecoveryPath(string path)
        => path.Contains($"{Path.DirectorySeparatorChar}recovery", StringComparison.Ordinal);
}
