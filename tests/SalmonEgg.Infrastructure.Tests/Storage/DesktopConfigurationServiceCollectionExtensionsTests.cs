using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SalmonEgg.Domain.Services;
using SalmonEgg.Infrastructure.Desktop.DependencyInjection;
using SalmonEgg.Infrastructure.Storage;

namespace SalmonEgg.Infrastructure.Tests.Storage;

public sealed class DesktopConfigurationServiceCollectionExtensionsTests
{
    [Fact]
    public void AddSalmonEggDesktopConfiguration_ResolvesSharedConfigurationStack()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        services.AddSalmonEggDesktopConfiguration();
        using var provider = services.BuildServiceProvider(validateScopes: true);

        Assert.IsType<ConfigurationManager>(provider.GetRequiredService<IConfigurationService>());
        Assert.IsType<AppSettingsService>(provider.GetRequiredService<IAppSettingsService>());
        Assert.IsType<ServerCredentialService>(provider.GetRequiredService<IServerCredentialService>());
        Assert.IsType<ConfigSyncPackageService>(provider.GetRequiredService<ConfigSyncPackageService>());
        AssertSecureStorageMatchesCurrentDesktopPlatform(provider.GetRequiredService<ISecureStorage>());
    }

    private static void AssertSecureStorageMatchesCurrentDesktopPlatform(ISecureStorage storage)
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.IsType<WindowsDpapiSecureStorage>(storage);
            return;
        }

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            Assert.IsType<FallbackSecureStorage>(storage);
            return;
        }

        Assert.IsType<PlainTextFileSecureStorage>(storage);
    }
}
