using System.Globalization;
using System.Runtime.InteropServices;
using SalmonEgg.Infrastructure.Services;

namespace SalmonEgg.Infrastructure.Tests.Services;

public sealed class AppLanguageServiceTests
{
    [Fact]
    public async Task ApplyLanguageOverrideAsync_UpdatesCurrentAndDefaultDotNetCultures()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        var previousDefaultCulture = CultureInfo.DefaultThreadCurrentCulture;
        var previousDefaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;

        try
        {
            var service = new AppLanguageService();

            await service.ApplyLanguageOverrideAsync("en-US");

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Assert.Equal("en-US", CultureInfo.CurrentCulture.Name);
                Assert.Equal("en-US", CultureInfo.CurrentUICulture.Name);
                Assert.Equal("en-US", CultureInfo.DefaultThreadCurrentCulture?.Name);
                Assert.Equal("en-US", CultureInfo.DefaultThreadCurrentUICulture?.Name);
            }
            else
            {
                Assert.Equal(previousCulture, CultureInfo.CurrentCulture);
                Assert.Equal(previousUiCulture, CultureInfo.CurrentUICulture);
                Assert.Equal(previousDefaultCulture, CultureInfo.DefaultThreadCurrentCulture);
                Assert.Equal(previousDefaultUiCulture, CultureInfo.DefaultThreadCurrentUICulture);
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
            CultureInfo.DefaultThreadCurrentCulture = previousDefaultCulture;
            CultureInfo.DefaultThreadCurrentUICulture = previousDefaultUiCulture;
        }
    }
}
