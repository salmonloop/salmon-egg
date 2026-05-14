using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Infrastructure.Storage;

public sealed class FileSystemAppFileStore : IAppFileStore
{
    public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(File.Exists(path));
    }

    public async Task<string?> ReadAllTextAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!await ExistsAsync(path, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
    }

    public Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken = default)
        => AtomicFile.WriteUtf8AtomicAsync(path, content, cancellationToken);

    public Task DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<string> EnumerateFilesAsync(
        string directory,
        string searchPattern,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(directory))
        {
            yield break;
        }

        foreach (var path in Directory.EnumerateFiles(directory, searchPattern, SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return path;
            await Task.Yield();
        }
    }
}
