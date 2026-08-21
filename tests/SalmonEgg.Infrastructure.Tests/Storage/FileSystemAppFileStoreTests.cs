using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Services;
using SalmonEgg.Infrastructure.Storage;

namespace SalmonEgg.Infrastructure.Tests.Storage;

public sealed class FileSystemAppFileStoreTests : IDisposable
{
    private readonly string _testDirectory;

    public FileSystemAppFileStoreTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "SalmonEggFileStoreTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, recursive: true);
            }
        }
        catch
        {
        }
    }

    [Fact]
    public async Task ReadAllTextAsync_WhenFirstAccess_LoadsFileSystemPersistenceBeforeReading()
    {
        var path = Path.Combine(_testDirectory, "config", "app.yaml");
        var persistence = new RecordingFileSystemPersistence
        {
            OnLoad = () =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, "theme: Dark");
            }
        };
        var store = new FileSystemAppFileStore(persistence);

        var content = await store.ReadAllTextAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal("theme: Dark", content);
        Assert.Equal(1, persistence.LoadCount);
    }

    [Fact]
    public async Task WriteAllTextAsync_WhenFirstAccess_LoadsBeforeWritingAndFlushesAfterWriting()
    {
        var persistence = new RecordingFileSystemPersistence();
        var store = new FileSystemAppFileStore(persistence);
        var path = Path.Combine(_testDirectory, "config", "app.yaml");

        await store.WriteAllTextAsync(path, "theme: Dark", TestContext.Current.CancellationToken);

        Assert.Equal(new[] { "load", "flush" }, persistence.Operations);
    }

    [Fact]
    public async Task WriteAllTextAsync_WhenWriteSucceeds_FlushesFileSystemPersistence()
    {
        var persistence = new RecordingFileSystemPersistence();
        var store = new FileSystemAppFileStore(persistence);
        var path = Path.Combine(_testDirectory, "config", "app.yaml");

        await store.WriteAllTextAsync(path, "theme: Dark", TestContext.Current.CancellationToken);

        Assert.Equal(1, persistence.FlushCount);
    }

    [Fact]
    public async Task DeleteAsync_WhenFileExists_FlushesFileSystemPersistence()
    {
        var persistence = new RecordingFileSystemPersistence();
        var store = new FileSystemAppFileStore(persistence);
        var path = Path.Combine(_testDirectory, "config", "app.yaml");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "theme: Dark", TestContext.Current.CancellationToken);

        await store.DeleteAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(1, persistence.FlushCount);
    }

    [Fact]
    public async Task DeleteAsync_WhenDeletingTransactionArtifact_DoesNotNotifyConfigurationObservers()
    {
        var signal = new ConfigChangeSignal();
        var store = new FileSystemAppFileStore(new NoOpFileSystemPersistence(), signal);
        var path = Path.Combine(
            _testDirectory,
            "config",
            "server.yaml" + ConfigurationFileTransactionArtifacts.RollbackSuffix + "crash");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "orphan", TestContext.Current.CancellationToken);
        var notifications = new List<ConfigChangedEventArgs>();
        signal.Changed += (_, args) => notifications.Add(args);

        await store.DeleteAsync(path, TestContext.Current.CancellationToken);

        Assert.Empty(notifications);
    }

    [Fact]
    public async Task WriteAllTextAsync_WhenFlushFailsAfterReplacement_RestoresOriginalFile()
    {
        var persistence = new ThrowOnFirstFlushPersistence();
        var store = new FileSystemAppFileStore(persistence);
        var path = Path.Combine(_testDirectory, "config", "app.yaml");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "theme: Light", TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<IOException>(
            () => store.WriteAllTextAsync(path, "theme: Dark", TestContext.Current.CancellationToken));

        Assert.Equal("theme: Light", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        Assert.Equal(2, persistence.FlushCount);
        Assert.DoesNotContain(
            Directory.GetFiles(Path.GetDirectoryName(path)!),
            ConfigurationFileTransactionArtifacts.IsArtifact);
    }

    [Fact]
    public async Task WriteAllTextAsync_WhenFirstSaveFlushFails_RemovesCandidateFile()
    {
        var persistence = new ThrowOnFirstFlushPersistence();
        var store = new FileSystemAppFileStore(persistence);
        var path = Path.Combine(_testDirectory, "config", "new.yaml");

        await Assert.ThrowsAsync<IOException>(
            () => store.WriteAllTextAsync(path, "theme: Dark", TestContext.Current.CancellationToken));

        Assert.False(File.Exists(path));
        Assert.Equal(2, persistence.FlushCount);
    }

    [Fact]
    public async Task DeleteAsync_WhenFlushFailsAfterDeletion_RestoresOriginalFile()
    {
        var persistence = new ThrowOnFirstFlushPersistence();
        var store = new FileSystemAppFileStore(persistence);
        var path = Path.Combine(_testDirectory, "config", "delete.yaml");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "theme: Light", TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<IOException>(
            () => store.DeleteAsync(path, TestContext.Current.CancellationToken));

        Assert.Equal("theme: Light", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        Assert.Equal(2, persistence.FlushCount);
    }

    private sealed class ThrowOnFirstFlushPersistence : IFileSystemPersistence
    {
        public int FlushCount { get; private set; }

        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task FlushAsync(CancellationToken cancellationToken = default)
        {
            FlushCount++;
            if (FlushCount == 1)
            {
                throw new IOException("flush failed after candidate mutation");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class RecordingFileSystemPersistence : IFileSystemPersistence
    {
        private readonly List<string> _operations = new();

        public Action? OnLoad { get; init; }

        public int LoadCount { get; private set; }

        public int FlushCount { get; private set; }

        public IReadOnlyList<string> Operations => _operations;

        public Task LoadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LoadCount++;
            _operations.Add("load");
            OnLoad?.Invoke();
            return Task.CompletedTask;
        }

        public Task FlushAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FlushCount++;
            _operations.Add("flush");
            return Task.CompletedTask;
        }
    }
}
