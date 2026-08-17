using System;
using System.Runtime.InteropServices;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SalmonEgg.Application.Validators;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Infrastructure.Storage;

namespace SalmonEgg.Infrastructure.Desktop.DependencyInjection;

/// <summary>
/// Registers the desktop configuration stack (YAML config, app settings, secure storage,
/// credentials, config packages).
/// </summary>
/// <remarks>
/// This is the single registration point for every desktop host — the WinUI/Skia application and
/// the CLI. Hosts that duplicated these registrations would also duplicate the secure storage
/// backend selection, which is exactly the second owner this type exists to prevent.
/// Registrations are additive-safe: callers may override individual services afterwards.
/// </remarks>
public static class DesktopConfigurationServiceCollectionExtensions
{
    /// <summary>
    /// Adds the configuration services shared by all desktop hosts.
    /// </summary>
    /// <param name="services">The service collection to add registrations to.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddSalmonEggDesktopConfiguration(this IServiceCollection services)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));

        // File system persistence must be registered before IAppFileStore and ISecureStorage.
        // Desktop hosts read and write the real file system directly, so no platform sync is needed.
        services.AddSingleton<IFileSystemPersistence, NoOpFileSystemPersistence>();

        services.AddSingleton<IAppDataService, AppDataService>();
        services.AddSingleton<IConfigChangeSignal, ConfigChangeSignal>();
        services.AddSingleton<IAppFileStore>(sp => new FileSystemAppFileStore(
            sp.GetRequiredService<IFileSystemPersistence>(),
            sp.GetRequiredService<IConfigChangeSignal>()));

        services.AddSingleton<PlainTextFileSecureStorage>();
        services.AddSingleton<ISecureStorage>(CreateSecureStorage);

        services.AddSingleton<IAppSettingsService, AppSettingsService>();
        services.AddSingleton<IConfigurationService, ConfigurationManager>();
        services.AddSingleton<IServerCredentialService, ServerCredentialService>();
        services.AddSingleton<IValidator<ServerConfiguration>, ServerConfigurationValidator>();

        services.AddSingleton<ConfigurationSecretSnapshotService>();
        services.AddSingleton<ConfigSyncPackageService>();

        return services;
    }

    /// <summary>
    /// Resolves the platform secure storage backend for the running desktop OS.
    /// </summary>
    /// <remarks>
    /// Windows: DPAPI (user-scoped encryption).
    /// Linux desktop: Secret Service via libsecret's secret-tool.
    /// macOS desktop: Keychain via Security.framework.
    /// When the platform keychain is unavailable, <see cref="FallbackSecureStorage"/> downgrades to
    /// the plaintext store. That downgrade is a supported path for this project, and it stays logged
    /// by <see cref="FallbackSecureStorage"/> so an operator can still see it happened.
    ///
    /// Dispatch is at run time rather than through compile constants: a single-TFM host such as the
    /// CLI ships one binary for all desktop platforms, so the backend can only be chosen at run time.
    /// </remarks>
    private static ISecureStorage CreateSecureStorage(IServiceProvider serviceProvider)
    {
        var fallback = serviceProvider.GetRequiredService<PlainTextFileSecureStorage>();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new WindowsDpapiSecureStorage();
        }

        var fallbackLogger = serviceProvider.GetRequiredService<ILogger<FallbackSecureStorage>>();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return new FallbackSecureStorage(new LinuxSecretServiceSecureStorage(), fallback, fallbackLogger);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return new FallbackSecureStorage(new MacOSKeychainSecureStorage(), fallback, fallbackLogger);
        }

        return fallback;
    }
}
