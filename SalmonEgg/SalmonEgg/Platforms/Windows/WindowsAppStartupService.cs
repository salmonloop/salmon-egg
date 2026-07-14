#if WINDOWS
using System.Threading.Tasks;
using SalmonEgg.Domain.Services;
using Windows.ApplicationModel;

namespace SalmonEgg.Platforms.Windows;

public sealed class WindowsAppStartupService : IAppStartupService
{
    private const string StartupTaskId = "SalmonEggStartup";

    public bool IsSupported => true;

    public async Task<bool?> GetLaunchOnStartupAsync()
    {
        try
        {
            var task = await StartupTask.GetAsync(StartupTaskId).AsTask().ConfigureAwait(false);
            return task.State == StartupTaskState.Enabled;
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> SetLaunchOnStartupAsync(bool enabled)
    {
        try
        {
            var task = await StartupTask.GetAsync(StartupTaskId).AsTask().ConfigureAwait(false);
            if (enabled)
            {
                var result = await task.RequestEnableAsync().AsTask().ConfigureAwait(false);
                return result == StartupTaskState.Enabled;
            }

            task.Disable();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
#endif
