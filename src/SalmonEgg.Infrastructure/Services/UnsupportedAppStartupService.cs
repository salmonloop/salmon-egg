using System.Threading.Tasks;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Infrastructure.Services;

public sealed class UnsupportedAppStartupService : IAppStartupService
{
    public bool IsSupported => false;

    public Task<bool?> GetLaunchOnStartupAsync() => Task.FromResult<bool?>(null);

    public Task<bool> SetLaunchOnStartupAsync(bool enabled) => Task.FromResult(false);
}
