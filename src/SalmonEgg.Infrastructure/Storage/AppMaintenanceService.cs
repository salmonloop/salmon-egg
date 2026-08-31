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
        // Surface top-level failures so settings owners can report success/failure honestly.
        // Per-entry cleanup remains best-effort inside CleanupCacheAsync.
        if (Directory.Exists(_paths.CacheRootPath))
        {
            Directory.Delete(_paths.CacheRootPath, recursive: true);
        }

        return Task.CompletedTask;
    }

    public Task CleanupCacheAsync(int retentionDays)
    {
        // Best-effort retention sweep used by boot cleanup: individual file/dir failures must
        // not abort the sweep or crash launch. Outer IO failures are also swallowed here.
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
                    // Best-effort: locked/in-use cache files should not stop retention cleanup.
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
                    // Best-effort empty-dir reclamation.
                }
            }
        }
        catch
        {
            // Boot-time cleanup is opportunistic; callers do not surface this to users.
        }

        return Task.CompletedTask;
    }

    public Task ClearAllLocalDataAsync()
    {
        // Surface top-level failures so settings owners can report success/failure honestly.
        if (Directory.Exists(_paths.AppDataRootPath))
        {
            Directory.Delete(_paths.AppDataRootPath, recursive: true);
        }

        return Task.CompletedTask;
    }
}
