using System;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Infrastructure.Storage;

/// <summary>
/// Coordinates one profile mutation across processes and instances.
/// </summary>
/// <remarks>
/// The in-process gate prevents multiple <see cref="FileStream"/> attempts from the same process
/// from racing. The lock file handle is the cross-process owner; its lifetime covers the complete
/// YAML and secure-storage compensation transaction. The file itself is intentionally outside the
/// portable configuration tree.
/// </remarks>
internal sealed class ConfigurationProfileLockProvider
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ProcessGates = new(StringComparer.Ordinal);
    private static readonly TimeSpan AcquisitionTimeout = TimeSpan.FromSeconds(10);

    private readonly string _lockDirectory;

    public ConfigurationProfileLockProvider(IAppDataService appData)
    {
        ArgumentNullException.ThrowIfNull(appData);
        _lockDirectory = Path.Combine(appData.AppDataRootPath, "config-locks");
    }

    public async Task<IAsyncDisposable> AcquireAsync(string profileId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            throw new ArgumentException("Profile ID cannot be empty.", nameof(profileId));
        }

        var lockPath = GetLockPath(profileId);
        var gate = ProcessGates.GetOrAdd(lockPath, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            try
            {
                Directory.CreateDirectory(_lockDirectory);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new ConfigurationLockUnavailableException(lockPath, exception);
            }

            var deadline = DateTimeOffset.UtcNow + AcquisitionTimeout;
            while (true)
            {
                try
                {
                    var stream = new FileStream(
                        lockPath,
                        FileMode.OpenOrCreate,
                        FileAccess.ReadWrite,
                        FileShare.None,
                        bufferSize: 1,
                        options: FileOptions.None);
                    return new Lease(stream, gate);
                }
                catch (IOException) when (DateTimeOffset.UtcNow < deadline)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
                }
                catch (IOException exception)
                {
                    throw new ConfigurationLockUnavailableException(lockPath, exception);
                }
                catch (UnauthorizedAccessException exception)
                {
                    throw new ConfigurationLockUnavailableException(lockPath, exception);
                }
            }
        }
        catch
        {
            gate.Release();
            throw;
        }
    }

    internal string GetLockPath(string profileId)
    {
        using var sha256 = SHA256.Create();
        var digest = sha256.ComputeHash(Encoding.UTF8.GetBytes(profileId));
        return Path.Combine(_lockDirectory, Convert.ToHexString(digest).ToLowerInvariant() + ".lock");
    }

    private sealed class Lease : IAsyncDisposable
    {
        private readonly FileStream _stream;
        private readonly SemaphoreSlim _gate;
        private int _disposed;

        public Lease(FileStream stream, SemaphoreSlim gate)
        {
            _stream = stream;
            _gate = gate;
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _stream.Dispose();
                _gate.Release();
            }

            return ValueTask.CompletedTask;
        }
    }
}

internal sealed class ConfigurationLockUnavailableException : IOException
{
    public ConfigurationLockUnavailableException(string lockPath, Exception innerException)
        : base($"Configuration profile lock could not be acquired: {lockPath}", innerException)
    {
        LockPath = lockPath;
    }

    public string LockPath { get; }
}
