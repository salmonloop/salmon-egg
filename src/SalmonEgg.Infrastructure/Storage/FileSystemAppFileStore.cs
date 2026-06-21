using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Infrastructure.Storage;

public sealed class FileSystemAppFileStore : IAppFileStore
{
    private readonly IFileSystemPersistence _persistence;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private bool _isLoaded;

    public FileSystemAppFileStore()
        : this(new NoOpFileSystemPersistence())
    {
    }

    public FileSystemAppFileStore(IFileSystemPersistence persistence)
    {
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
    }

    public async Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        return File.Exists(path);
    }

    public async Task<string?> ReadAllTextAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!await ExistsAsync(path, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
    }

    public async Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        await AtomicFile.WriteUtf8AtomicAsync(path, content, cancellationToken).ConfigureAwait(false);
        await _persistence.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        if (File.Exists(path))
        {
            File.Delete(path);
            await _persistence.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async IAsyncEnumerable<string> EnumerateFilesAsync(
        string directory,
        string searchPattern,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

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

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_isLoaded)
        {
            return;
        }

        await _loadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_isLoaded)
            {
                return;
            }

            await _persistence.LoadAsync(cancellationToken).ConfigureAwait(false);
            _isLoaded = true;
        }
        finally
        {
            _loadLock.Release();
        }
    }
}
