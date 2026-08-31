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
    /// <param name="secureStorageDowngradePolicy">
    /// What to do when the platform secret store is unavailable for a write. Interactive hosts pass
    /// <see cref="SecureStorageDowngradePolicy.AllowPlaintextDowngrade"/> so the app keeps working on a
    /// desktop without a keyring; non-interactive hosts pass
    /// <see cref="SecureStorageDowngradePolicy.FailClosed"/> so credentials are never written in
    /// plaintext without the operator asking for it.
    /// </param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddSalmonEggDesktopConfiguration(
        this IServiceCollection services,
        SecureStorageDowngradePolicy secureStorageDowngradePolicy = SecureStorageDowngradePolicy.AllowPlaintextDowngrade)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));

        // File system persistence must be registered before IAppFileStore and ISecureStorage.
        // Desktop hosts read and write the real file system directly, so no platform sync is needed.
        services.AddSingleton<IFileSystemPersistence, NoOpFileSystemPersistence>();

        services.AddSingleton<IAppDataService, AppDataService>();
        services.AddSingleton<IConfigChangeSignal, ConfigChangeSignal>();
        services.AddSingleton<IConfigurationFileStore>(sp => new FileSystemAppFileStore(
            sp.GetRequiredService<IFileSystemPersistence>(),
            sp.GetRequiredService<IConfigChangeSignal>()));
        services.AddSingleton<IAppFileStore>(sp => sp.GetRequiredService<IConfigurationFileStore>());
        services.AddSingleton<IConfigurationFileTransactionStore>(sp =>
            sp.GetRequiredService<IConfigurationFileStore>());

        services.AddSingleton<PlainTextFileSecureStorage>();
        services.AddSingleton<ISecureStorage>(sp => CreateSecureStorage(sp, secureStorageDowngradePolicy));

        services.AddSingleton<IAppSettingsService, AppSettingsService>();
        services.AddSingleton<ConfigurationManager>(sp => new ConfigurationManager(
            sp.GetRequiredService<ISecureStorage>(),
            sp.GetRequiredService<IConfigurationFileStore>(),
            sp.GetRequiredService<IAppDataService>(),
            sp.GetRequiredService<ILogger<ConfigurationManager>>()));
        services.AddSingleton<IConfigurationService>(sp => sp.GetRequiredService<ConfigurationManager>());
        services.AddSingleton<IConfigurationRecoveryService>(sp => sp.GetRequiredService<ConfigurationManager>());
        services.AddSingleton<IServerCredentialService, ServerCredentialService>();
        services.AddSingleton<IValidator<ServerConfiguration>, ServerConfigurationValidator>();

        services.AddSingleton<ConfigurationSecretSnapshotService>();
        services.AddSingleton<ConfigSyncPackageService>();
        services.AddSingleton<ConfigurationDiagnosticsService>();

        return services;
    }

    /// <summary>
    /// Resolves the platform secure storage backend for the running desktop OS.
    /// </summary>
    /// <remarks>
    /// Windows: DPAPI (user-scoped encryption).
    /// Linux desktop: Secret Service via libsecret's secret-tool.
    /// macOS desktop: Keychain via Security.framework.
    /// When the platform keychain is unavailable, <paramref name="downgradePolicy"/> decides whether
    /// <see cref="FallbackSecureStorage"/> downgrades a write to the plaintext store or fails it. Either
    /// way the event stays logged by <see cref="FallbackSecureStorage"/> so an operator can see it.
    ///
    /// Dispatch is at run time rather than through compile constants: a single-TFM host such as the
    /// CLI ships one binary for all desktop platforms, so the backend can only be chosen at run time.
    /// </remarks>
    private static ISecureStorage CreateSecureStorage(
        IServiceProvider serviceProvider,
        SecureStorageDowngradePolicy downgradePolicy)
    {
        var fallback = serviceProvider.GetRequiredService<PlainTextFileSecureStorage>();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // DPAPI needs no keyring daemon, so there is no downgrade decision to make here.
            return new WindowsDpapiSecureStorage();
        }

        var fallbackLogger = serviceProvider.GetRequiredService<ILogger<FallbackSecureStorage>>();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return new FallbackSecureStorage(
                new LinuxSecretServiceSecureStorage(),
                fallback,
                downgradePolicy,
                fallbackLogger);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return new FallbackSecureStorage(
                new MacOSKeychainSecureStorage(),
                fallback,
                downgradePolicy,
                fallbackLogger);
        }

        // No platform secret store exists on this OS. Under a fail-closed policy the plaintext store is
        // still the only option, so it is wrapped to make writes fail rather than silently downgrade.
        return downgradePolicy == SecureStorageDowngradePolicy.FailClosed
            ? new FallbackSecureStorage(
                UnavailableSecureStorage.Instance,
                fallback,
                downgradePolicy,
                fallbackLogger)
            : fallback;
    }
}
