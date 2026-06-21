using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
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

        var content = await store.ReadAllTextAsync(path);

        Assert.Equal("theme: Dark", content);
        Assert.Equal(1, persistence.LoadCount);
    }

    [Fact]
    public async Task WriteAllTextAsync_WhenFirstAccess_LoadsBeforeWritingAndFlushesAfterWriting()
    {
        var persistence = new RecordingFileSystemPersistence();
        var store = new FileSystemAppFileStore(persistence);
        var path = Path.Combine(_testDirectory, "config", "app.yaml");

        await store.WriteAllTextAsync(path, "theme: Dark");

        Assert.Equal(new[] { "load", "flush" }, persistence.Operations);
    }

    [Fact]
    public async Task WriteAllTextAsync_WhenWriteSucceeds_FlushesFileSystemPersistence()
    {
        var persistence = new RecordingFileSystemPersistence();
        var store = new FileSystemAppFileStore(persistence);
        var path = Path.Combine(_testDirectory, "config", "app.yaml");

        await store.WriteAllTextAsync(path, "theme: Dark");

        Assert.Equal(1, persistence.FlushCount);
    }

    [Fact]
    public async Task DeleteAsync_WhenFileExists_FlushesFileSystemPersistence()
    {
        var persistence = new RecordingFileSystemPersistence();
        var store = new FileSystemAppFileStore(persistence);
        var path = Path.Combine(_testDirectory, "config", "app.yaml");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "theme: Dark");

        await store.DeleteAsync(path);

        Assert.Equal(1, persistence.FlushCount);
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
