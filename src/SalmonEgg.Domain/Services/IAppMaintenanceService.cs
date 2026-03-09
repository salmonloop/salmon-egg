using System.Threading.Tasks;

namespace SalmonEgg.Domain.Services;

public interface IAppMaintenanceService
{
    /// <summary>
    /// Removes cache entries older than retentionDays and cleans empty folders.
    /// </summary>
    Task CleanupCacheAsync(int retentionDays);

    Task ClearCacheAsync();

    /// <summary>
    /// Clears all local app data, including configs and logs.
    /// </summary>
    Task ClearAllLocalDataAsync();
}
