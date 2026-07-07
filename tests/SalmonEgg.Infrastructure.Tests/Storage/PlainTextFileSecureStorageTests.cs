using System;
using System.IO;
using System.Threading.Tasks;
using SalmonEgg.Infrastructure.Storage;

namespace SalmonEgg.Infrastructure.Tests.Storage;

public sealed class PlainTextFileSecureStorageTests : IDisposable
{
    private readonly string _testDirectory;

    public PlainTextFileSecureStorageTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "SalmonEggPlainTextSecureStorageTests", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("SALMONEGG_APPDATA_ROOT", Path.Combine(_testDirectory, "SalmonEgg"), EnvironmentVariableTarget.Process);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("SALMONEGG_APPDATA_ROOT", null, EnvironmentVariableTarget.Process);
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task SaveLoadDeleteAsync_RoundTripsThroughAppFiles()
    {
        var storage = CreateStorage();

        await storage.SaveAsync("salmonegg/config/profile/token", "secret-token");
        var loaded = await storage.LoadAsync("salmonegg/config/profile/token");
        await storage.DeleteAsync("salmonegg/config/profile/token");
        var deleted = await storage.LoadAsync("salmonegg/config/profile/token");

        Assert.Equal("secret-token", loaded);
        Assert.Null(deleted);
        Assert.True(Directory.Exists(Path.Combine(_testDirectory, "SalmonEgg", "SecureStoragePlainText")));
    }

    [Fact]
    public void Constructor_DoesNotCreateStorageDirectory()
    {
        _ = CreateStorage();

        Assert.False(Directory.Exists(Path.Combine(_testDirectory, "SalmonEgg", "SecureStoragePlainText")));
    }

    private static PlainTextFileSecureStorage CreateStorage()
    {
        var appData = new AppDataService();
        return new PlainTextFileSecureStorage(new FileSystemAppFileStore(), appData);
    }
}
