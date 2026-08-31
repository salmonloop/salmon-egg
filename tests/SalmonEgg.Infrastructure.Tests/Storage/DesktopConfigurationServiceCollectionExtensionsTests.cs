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
        Assert.Same(
            provider.GetRequiredService<IConfigurationService>(),
            provider.GetRequiredService<IConfigurationRecoveryService>());
        Assert.IsType<AppSettingsService>(provider.GetRequiredService<IAppSettingsService>());
        Assert.IsType<ServerCredentialService>(provider.GetRequiredService<IServerCredentialService>());
        Assert.IsType<ConfigSyncPackageService>(provider.GetRequiredService<ConfigSyncPackageService>());
        AssertSecureStorageMatchesCurrentDesktopPlatform(provider.GetRequiredService<ISecureStorage>());
    }

    [Fact]
    public void AddSalmonEggDesktopConfiguration_SharesOneFileStoreInstanceAcrossBothContracts()
    {
        // A second instance would split the load lock and the change signal, so the transactional
        // configuration path and the generic file path must resolve to the same store.
        var services = new ServiceCollection();
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        services.AddSalmonEggDesktopConfiguration();
        using var provider = services.BuildServiceProvider(validateScopes: true);

        var fileStore = provider.GetRequiredService<IAppFileStore>();
        var transactionStore = provider.GetRequiredService<IConfigurationFileTransactionStore>();
        var configurationStore = provider.GetRequiredService<IConfigurationFileStore>();

        Assert.Same(fileStore, transactionStore);
        Assert.Same(fileStore, configurationStore);
    }

    [Fact]
    public async Task AddSalmonEggDesktopConfiguration_WithFailClosedPolicy_RefusesToStoreSecretsUnprotected()
    {
        // Non-interactive hosts opt into fail-closed. On a machine with a working platform store this
        // write succeeds; on one without it must throw rather than land in plaintext. Both outcomes are
        // acceptable here — what must never happen is a silent plaintext write.
        var services = new ServiceCollection();
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        services.AddSalmonEggDesktopConfiguration(SecureStorageDowngradePolicy.FailClosed);
        using var provider = services.BuildServiceProvider(validateScopes: true);

        var storage = provider.GetRequiredService<ISecureStorage>();
        var plaintextStore = provider.GetRequiredService<PlainTextFileSecureStorage>();
        var key = $"salmonegg-tests/fail-closed/{Guid.NewGuid():N}";

        try
        {
            await storage.SaveAsync(key, "secret-value");
        }
        catch (SecureStorageUnavailableException)
        {
            Assert.Null(await plaintextStore.LoadAsync(key));
            return;
        }

        try
        {
            Assert.Null(await plaintextStore.LoadAsync(key));
        }
        finally
        {
            await storage.DeleteAsync(key);
        }
    }

    [Fact]
    public void AddSalmonEggDesktopConfiguration_DefaultPolicy_KeepsTheInteractiveDowngrade()
    {
        // The GUI depends on the downgrade to stay usable without a desktop keyring, so the default
        // overload must not inherit the CLI's fail-closed choice.
        var services = new ServiceCollection();
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        services.AddSalmonEggDesktopConfiguration();
        using var provider = services.BuildServiceProvider(validateScopes: true);

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
