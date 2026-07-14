using SalmonEgg.Infrastructure.Services;

namespace SalmonEgg.Infrastructure.Tests.Services;

public sealed class UnsupportedAppStartupServiceTests
{
    [Fact]
    public async Task UnsupportedService_NeverReportsLaunchOnStartupSupport()
    {
        var service = new UnsupportedAppStartupService();

        Assert.False(service.IsSupported);
        Assert.Null(await service.GetLaunchOnStartupAsync());
        Assert.False(await service.SetLaunchOnStartupAsync(enabled: true));
    }
}
