using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Infrastructure.Storage;

public sealed class AppMaintenanceService : IAppMaintenanceService
{
    private readonly IAppDataService _paths;

    public AppMaintenanceService(IAppDataService paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public Task ClearCacheAsync()
    {
        try
        {
            if (Directory.Exists(_paths.CacheRootPath))
            {
                Directory.Delete(_paths.CacheRootPath, recursive: true);
            }
        }
        catch
        {
        }

        return Task.CompletedTask;
    }

    public Task CleanupCacheAsync(int retentionDays)
    {
        try
        {
            if (retentionDays <= 0)
            {
                retentionDays = 7;
            }

            if (!Directory.Exists(_paths.CacheRootPath))
            {
                return Task.CompletedTask;
            }

            var threshold = DateTimeOffset.UtcNow.AddDays(-retentionDays);

            foreach (var file in Directory.EnumerateFiles(_paths.CacheRootPath, "*", SearchOption.AllDirectories))
            {
                try
                {
                    var info = new FileInfo(file);
                    if (info.LastWriteTimeUtc < threshold.UtcDateTime)
                    {
                        info.Delete();
                    }
                }
                catch
                {
                }
            }

            // Remove empty directories (deep-first)
            foreach (var dir in Directory.EnumerateDirectories(_paths.CacheRootPath, "*", SearchOption.AllDirectories)
                         .OrderByDescending(d => d.Length))
            {
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(dir).Any())
                    {
                        Directory.Delete(dir, recursive: false);
                    }
                }
                catch
                {
                }
            }
        }
        catch
        {
        }

        return Task.CompletedTask;
    }

    public Task ClearAllLocalDataAsync()
    {
        try
        {
            if (Directory.Exists(_paths.AppDataRootPath))
            {
                Directory.Delete(_paths.AppDataRootPath, recursive: true);
            }
        }
        catch
        {
        }

        return Task.CompletedTask;
    }
}
