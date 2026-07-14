using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.ViewModels.Settings;

namespace SalmonEgg.Presentation.Core.Services;

public sealed class ConfigProjectionReloadCoordinator : IDisposable
{
    private readonly IConfigChangeSignal _configChangeSignal;
    private readonly IAppDataService _appData;
    private readonly AppPreferencesViewModel _preferences;
    private readonly AcpProfilesViewModel _profiles;
    private readonly McpSettingsViewModel _mcpSettings;
    private readonly ILogger<ConfigProjectionReloadCoordinator> _logger;
    private readonly SemaphoreSlim _reloadGate = new(1, 1);
    private long _reloadVersion;
    private bool _disposed;

    public ConfigProjectionReloadCoordinator(
        IConfigChangeSignal configChangeSignal,
        IAppDataService appData,
        AppPreferencesViewModel preferences,
        AcpProfilesViewModel profiles,
        McpSettingsViewModel mcpSettings,
        ILogger<ConfigProjectionReloadCoordinator> logger)
    {
        _configChangeSignal = configChangeSignal ?? throw new ArgumentNullException(nameof(configChangeSignal));
        _appData = appData ?? throw new ArgumentNullException(nameof(appData));
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        _mcpSettings = mcpSettings ?? throw new ArgumentNullException(nameof(mcpSettings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _configChangeSignal.Changed += OnConfigChanged;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _configChangeSignal.Changed -= OnConfigChanged;
        _reloadGate.Dispose();
    }

    private void OnConfigChanged(object? sender, ConfigChangedEventArgs args)
    {
        if (args.Kind != ConfigChangeKind.Restored || !IsUnderConfigRoot(args.Path))
        {
            return;
        }

        var version = Interlocked.Increment(ref _reloadVersion);
        _ = ReloadProjectionsAsync(version);
    }

    private async Task ReloadProjectionsAsync(long version)
    {
        await _reloadGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!IsLatestReload(version))
            {
                return;
            }

            await _preferences.ReloadFromStoreAsync().ConfigureAwait(false);
            if (!IsLatestReload(version))
            {
                return;
            }

            await _profiles.RefreshAsync().ConfigureAwait(false);
            if (!IsLatestReload(version))
            {
                return;
            }

            await _mcpSettings.ReloadFromStoreAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reload configuration projections after config restore");
        }
        finally
        {
            _reloadGate.Release();
        }
    }

    private bool IsLatestReload(long version) => version == Volatile.Read(ref _reloadVersion);

    private bool IsUnderConfigRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var root = Path.GetFullPath(_appData.ConfigRootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var target = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return string.Equals(target, root, comparison) ||
               target.StartsWith(root + Path.DirectorySeparatorChar, comparison) ||
               target.StartsWith(root + Path.AltDirectorySeparatorChar, comparison);
    }
}
