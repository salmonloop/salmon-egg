using System;
using System.IO;
using System.Threading.Tasks;
using SalmonEgg.Infrastructure.Storage;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.Storage;

public sealed class AppFileStoreSecureStorageTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly string _secretsDirectory;

    public AppFileStoreSecureStorageTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "SalmonEggSecureStorageTests", Guid.NewGuid().ToString("N"));
        _secretsDirectory = Path.Combine(_testDirectory, "SecureStorage");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDirectory))
                Directory.Delete(_testDirectory, recursive: true);
        }
        catch { }
    }

    [Fact]
    public void Constructor_DoesNotCreateStorageDirectory()
    {
        _ = CreateStorage();

        Assert.False(Directory.Exists(_secretsDirectory));
    }

    [Fact]
    public async Task SaveThenLoad_ReturnsSavedValue()
    {
        var storage = CreateStorage();

        await storage.SaveAsync("my-key", "my-secret");
        var loaded = await storage.LoadAsync("my-key");

        Assert.Equal("my-secret", loaded);
    }

    [Fact]
    public async Task Load_AfterDelete_ReturnsNull()
    {
        var storage = CreateStorage();
        await storage.SaveAsync("key-to-delete", "value");

        await storage.DeleteAsync("key-to-delete");

        Assert.Null(await storage.LoadAsync("key-to-delete"));
    }

    [Fact]
    public async Task Load_WhenKeyNeverSaved_ReturnsNull()
    {
        var storage = CreateStorage();

        Assert.Null(await storage.LoadAsync("does-not-exist"));
    }

    [Fact]
    public async Task Save_PersistsAcrossNewInstance()
    {
        var first = CreateStorage();
        await first.SaveAsync("persistent-key", "persistent-value");

        var second = CreateStorage();
        var loaded = await second.LoadAsync("persistent-key");

        Assert.Equal("persistent-value", loaded);
    }

    [Fact]
    public async Task Save_DoesNotStoreSecretAsPlainText()
    {
        var storage = CreateStorage();
        const string secret = "super-secret-api-key";

        await storage.SaveAsync("api-key", secret);

        foreach (var file in Directory.EnumerateFiles(_secretsDirectory, "*", SearchOption.AllDirectories))
        {
            var contents = await File.ReadAllTextAsync(file);
            Assert.DoesNotContain(secret, contents, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Delete_WhenKeyDoesNotExist_DoesNotThrow()
    {
        var storage = CreateStorage();
        await storage.DeleteAsync("nonexistent-key");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task NullOrEmptyKey_ThrowsArgumentNullException(string? key)
    {
        var storage = CreateStorage();

        await Assert.ThrowsAsync<ArgumentNullException>(() => storage.SaveAsync(key!, "value"));
        await Assert.ThrowsAsync<ArgumentNullException>(() => storage.LoadAsync(key!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => storage.DeleteAsync(key!));
    }

    private AppFileStoreSecureStorage CreateStorage()
        => new AppFileStoreSecureStorage(new FileSystemAppFileStore(), _secretsDirectory);
}
