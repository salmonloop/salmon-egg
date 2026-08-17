using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Infrastructure.Tests.Storage;

internal sealed class WriteFailingAppFileStore : IAppFileStore
{
    public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default)
        => Task.FromResult(false);

    public Task<string?> ReadAllTextAsync(string path, CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(null);

    public Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken = default)
        => throw new IOException("write failed");

    public Task DeleteAsync(string path, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public async IAsyncEnumerable<string> EnumerateFilesAsync(
        string directory,
        string searchPattern,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        yield break;
    }
}
