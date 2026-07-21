using System;
using System.IO;
using System.Threading.Tasks;
using SalmonEgg.Domain.Services;
using SalmonEgg.Infrastructure.Storage;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.Storage;

public sealed class AppMaintenanceServiceTests
{
    [Fact]
    public async Task ClearCacheAsync_WhenCacheExists_DeletesCacheRoot()
    {
        using var root = new TempAppDataRoot();
        Directory.CreateDirectory(root.CacheRootPath);
        await File.WriteAllTextAsync(Path.Combine(root.CacheRootPath, "stale.bin"), "cache", TestContext.Current.CancellationToken);

        var service = new AppMaintenanceService(root);

        await service.ClearCacheAsync();

        Assert.False(Directory.Exists(root.CacheRootPath));
    }

    [Fact]
    public async Task ClearCacheAsync_WhenCacheMissing_CompletesWithoutThrowing()
    {
        using var root = new TempAppDataRoot();
        var service = new AppMaintenanceService(root);

        await service.ClearCacheAsync();

        Assert.False(Directory.Exists(root.CacheRootPath));
    }

    [Fact]
    public async Task ClearAllLocalDataAsync_WhenAppDataExists_DeletesAppDataRoot()
    {
        using var root = new TempAppDataRoot();
        Directory.CreateDirectory(root.ConfigRootPath);
        await File.WriteAllTextAsync(Path.Combine(root.ConfigRootPath, "settings.json"), "{}", TestContext.Current.CancellationToken);
        Directory.CreateDirectory(root.CacheRootPath);

        var service = new AppMaintenanceService(root);

        await service.ClearAllLocalDataAsync();

        Assert.False(Directory.Exists(root.AppDataRootPath));
    }

    [Fact]
    public async Task CleanupCacheAsync_RemovesOnlyFilesOlderThanRetention()
    {
        using var root = new TempAppDataRoot();
        Directory.CreateDirectory(root.CacheRootPath);
        var keepPath = Path.Combine(root.CacheRootPath, "keep.bin");
        var dropPath = Path.Combine(root.CacheRootPath, "drop.bin");
        await File.WriteAllTextAsync(keepPath, "keep", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(dropPath, "drop", TestContext.Current.CancellationToken);
        File.SetLastWriteTimeUtc(dropPath, DateTime.UtcNow.AddDays(-10));
        File.SetLastWriteTimeUtc(keepPath, DateTime.UtcNow);

        var service = new AppMaintenanceService(root);

        await service.CleanupCacheAsync(retentionDays: 3);

        Assert.True(File.Exists(keepPath));
        Assert.False(File.Exists(dropPath));
    }

    private sealed class TempAppDataRoot : IAppDataService, IDisposable
    {
        public TempAppDataRoot()
        {
            AppDataRootPath = Path.Combine(Path.GetTempPath(), "salmon-egg-maintenance-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(AppDataRootPath);
        }

        public string AppDataRootPath { get; }
        public string ConfigRootPath => Path.Combine(AppDataRootPath, "config");
        public string LogsDirectoryPath => Path.Combine(AppDataRootPath, "logs");
        public string CacheRootPath => Path.Combine(AppDataRootPath, "cache");
        public string ExportsDirectoryPath => Path.Combine(AppDataRootPath, "exports");

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(AppDataRootPath))
                {
                    Directory.Delete(AppDataRootPath, recursive: true);
                }
            }
            catch
            {
            }
        }
    }
}
