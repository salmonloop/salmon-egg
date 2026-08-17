using System;
using System.IO;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using SalmonEgg.Infrastructure.Storage;

namespace SalmonEgg.Infrastructure.Tests.Storage;

public sealed class WindowsDpapiSecureStorageTests : IDisposable
{
    private readonly string _testDirectory;

    public WindowsDpapiSecureStorageTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "SalmonEggTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDirectory);
        Environment.SetEnvironmentVariable("SALMONEGG_APPDATA_ROOT", _testDirectory, EnvironmentVariableTarget.Process);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("SALMONEGG_APPDATA_ROOT", null, EnvironmentVariableTarget.Process);
        try
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup failures.
        }
    }

    [Fact]
    public Task SaveLoadAndDeleteAsync_RoundTripsCurrentUserCredential()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("DPAPI is supported on Windows only.");
            return Task.CompletedTask;
        }

        return SaveLoadAndDeleteAsyncOnWindows();
    }

    [SupportedOSPlatform("windows")]
    private static async Task SaveLoadAndDeleteAsyncOnWindows()
    {
        var storage = new WindowsDpapiSecureStorage();

        await storage.SaveAsync("salmonegg/config/dpapi-test/token", "secret-token");

        Assert.Equal("secret-token", await storage.LoadAsync("salmonegg/config/dpapi-test/token"));

        await storage.DeleteAsync("salmonegg/config/dpapi-test/token");

        Assert.Null(await storage.LoadAsync("salmonegg/config/dpapi-test/token"));
    }
}
