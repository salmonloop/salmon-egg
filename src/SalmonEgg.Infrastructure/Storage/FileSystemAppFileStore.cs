using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Infrastructure.Storage;

public sealed class FileSystemAppFileStore : IConfigurationFileStore
{
    private readonly IFileSystemPersistence _persistence;
    private readonly IConfigChangeSignal? _configChangeSignal;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private bool _isLoaded;

    public FileSystemAppFileStore()
        : this(new NoOpFileSystemPersistence())
    {
    }

    public FileSystemAppFileStore(IFileSystemPersistence persistence)
        : this(persistence, null)
    {
    }

    public FileSystemAppFileStore(IFileSystemPersistence persistence, IConfigChangeSignal? configChangeSignal)
    {
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        _configChangeSignal = configChangeSignal;
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
        await using var transaction = await BeginWriteAsync(path, content, cancellationToken).ConfigureAwait(false);
        await transaction.ApplyAndFlushAsync(cancellationToken).ConfigureAwait(false);
        transaction.Complete();
    }

    public async Task DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var transaction = await BeginDeleteAsync(path, cancellationToken).ConfigureAwait(false);
        await transaction.ApplyAndFlushAsync(cancellationToken).ConfigureAwait(false);
        transaction.Complete();
    }

    public async Task<IConfigurationFileTransaction> BeginWriteAsync(
        string path,
        string content,
        CancellationToken cancellationToken = default)
    {
        if (path is null) throw new ArgumentNullException(nameof(path));
        if (content is null) throw new ArgumentNullException(nameof(content));

        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        return await ConfigurationFileTransaction.CreateWriteAsync(
                path,
                content,
                _persistence,
                _configChangeSignal,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IConfigurationFileTransaction> BeginDeleteAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (path is null) throw new ArgumentNullException(nameof(path));

        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        return ConfigurationFileTransaction.CreateDelete(path, _persistence, _configChangeSignal);
    }

    public async IAsyncEnumerable<string> EnumerateFilesAsync(
        string directory,
        string searchPattern,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

        string[] paths;
        try
        {
            // Materialize here so a genuinely missing directory is the only case translated to
            // an empty collection. Enumeration access failures remain observable to the caller.
            paths = Directory.GetFiles(directory, searchPattern, SearchOption.TopDirectoryOnly);
        }
        catch (DirectoryNotFoundException)
        {
            yield break;
        }

        foreach (var path in paths)
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

    private sealed class ConfigurationFileTransaction : IConfigurationFileTransaction
    {
        private readonly string _path;
        private readonly string? _tempPath;
        private readonly string _backupPath;
        private readonly bool _isDelete;
        private readonly IFileSystemPersistence _persistence;
        private readonly IConfigChangeSignal? _configChangeSignal;
        private bool _targetExisted;
        private bool _applied;
        private bool _rollbackAttempted;
        private bool _completed;
        private bool _disposed;

        private ConfigurationFileTransaction(
            string path,
            string? tempPath,
            bool isDelete,
            IFileSystemPersistence persistence,
            IConfigChangeSignal? configChangeSignal)
        {
            _path = path;
            _tempPath = tempPath;
            _isDelete = isDelete;
            _persistence = persistence;
            _configChangeSignal = configChangeSignal;
            _backupPath = path + ConfigurationFileTransactionArtifacts.RollbackSuffix + Guid.NewGuid().ToString("N");
        }

        public static async Task<ConfigurationFileTransaction> CreateWriteAsync(
            string path,
            string content,
            IFileSystemPersistence persistence,
            IConfigChangeSignal? configChangeSignal,
            CancellationToken cancellationToken)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tempPath = path + ConfigurationFileTransactionArtifacts.PendingSuffix + Guid.NewGuid().ToString("N");
            try
            {
                await using (var stream = new FileStream(
                                 tempPath,
                                 FileMode.CreateNew,
                                 FileAccess.Write,
                                 FileShare.None,
                                 bufferSize: 16 * 1024,
                                 options: FileOptions.WriteThrough))
                await using (var writer = new StreamWriter(
                                 stream,
                                 new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
                {
                    await writer.WriteAsync(content.AsMemory(), cancellationToken).ConfigureAwait(false);
                    await writer.FlushAsync().ConfigureAwait(false);
                    stream.Flush(flushToDisk: true);
                }

                return new ConfigurationFileTransaction(path, tempPath, isDelete: false, persistence, configChangeSignal);
            }
            catch
            {
                TryDelete(tempPath);
                throw;
            }
        }

        public static ConfigurationFileTransaction CreateDelete(
            string path,
            IFileSystemPersistence persistence,
            IConfigChangeSignal? configChangeSignal)
            => new(path, tempPath: null, isDelete: true, persistence, configChangeSignal);

        public async Task ApplyAndFlushAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (_applied)
            {
                throw new InvalidOperationException("The configuration-file transaction was already applied.");
            }

            try
            {
                _targetExisted = File.Exists(_path);
                if (_isDelete)
                {
                    if (_targetExisted)
                    {
                        File.Move(_path, _backupPath);
                    }
                }
                else
                {
                    ApplyWrite();
                }

                _applied = true;
                await _persistence.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception operationException)
            {
                // File.Replace/File.Move can report an exception after the filesystem has changed.
                // A retained backup (or a consumed temp for a first write) is recovery evidence, not
                // proof that the candidate did not become visible, so restore conservatively.
                if (!_applied && MayHaveApplied())
                {
                    _applied = true;
                }

                if (_applied)
                {
                    try
                    {
                        await RestoreAndFlushAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception rollbackException)
                    {
                        throw new ConfigurationFileRollbackException(operationException, rollbackException);
                    }
                }

                throw;
            }
        }

        public async Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (!_applied)
            {
                return;
            }

            await RestoreAndFlushAsync(cancellationToken).ConfigureAwait(false);
        }

        public void Complete()
        {
            ThrowIfDisposed();
            if (!_applied)
            {
                throw new InvalidOperationException("The configuration-file transaction must be applied before completion.");
            }

            _completed = true;
            TryDelete(_tempPath);
            TryDelete(_backupPath);
            if ((!_isDelete || _targetExisted) && !ConfigurationFileTransactionArtifacts.IsArtifact(_path))
            {
                // A delete of an absent file changed nothing; keep the historical no-signal behavior.
                NotifyChanged(_isDelete ? ConfigChangeKind.Deleted : ConfigChangeKind.Written);
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (!_completed && _applied && !_rollbackAttempted)
            {
                await RestoreAndFlushAsync(CancellationToken.None).ConfigureAwait(false);
            }

            TryDelete(_tempPath);
            if (_completed)
            {
                TryDelete(_backupPath);
            }
        }

        private bool MayHaveApplied()
        {
            if (_isDelete)
            {
                return _targetExisted && File.Exists(_backupPath);
            }

            if (_targetExisted)
            {
                return File.Exists(_backupPath);
            }

            return _tempPath is not null && !File.Exists(_tempPath) && File.Exists(_path);
        }

        private void ApplyWrite()
        {
            if (_tempPath is null)
            {
                throw new InvalidOperationException("A write transaction has no staged candidate file.");
            }

            if (!_targetExisted)
            {
                File.Move(_tempPath, _path);
                return;
            }

            try
            {
                File.Replace(_tempPath, _path, _backupPath, ignoreMetadataErrors: true);
            }
            catch (PlatformNotSupportedException)
            {
                BackupAndOverwrite();
            }
            catch (IOException) when (!File.Exists(_backupPath))
            {
                // Some filesystems do not implement File.Replace. Preserve the original first;
                // the same-directory overwrite move keeps recovery possible even there.
                BackupAndOverwrite();
            }
        }

        private void BackupAndOverwrite()
        {
            File.Copy(_path, _backupPath, overwrite: false);
            File.Move(_tempPath!, _path, overwrite: true);
        }

        private async Task RestoreAndFlushAsync(CancellationToken cancellationToken)
        {
            _rollbackAttempted = true;
            if (_targetExisted)
            {
                if (!File.Exists(_backupPath))
                {
                    throw new IOException("The original configuration-file backup is unavailable for rollback.");
                }

                // Copy rather than move: a flush failure after the restore must retain the backup
                // for explicit operator recovery instead of consuming the only rollback material.
                File.Copy(_backupPath, _path, overwrite: true);
            }
            else
            {
                // A swallowed delete here would report a successful rollback while leaving a phantom
                // profile whose credentials have already been restored to absent.
                File.Delete(_path);
            }

            await _persistence.FlushAsync(cancellationToken).ConfigureAwait(false);
            _applied = false;
            TryDelete(_tempPath);
            TryDelete(_backupPath);

            // Restoring the previous state is not an observable configuration change, and the
            // whole-root Restored signal owns package restore. Emitting it per profile would make
            // one failed save reload unrelated settings panes.
        }

        private void NotifyChanged(ConfigChangeKind kind)
        {
            try
            {
                _configChangeSignal?.NotifyChanged(_path, kind);
            }
            catch
            {
                // Change observers must not split a completed file/credential transaction.
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ConfigurationFileTransaction));
            }
        }

        private static void TryDelete(string? path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Retained rollback artifacts are safer than masking the original persistence result.
            }
        }
    }
}
