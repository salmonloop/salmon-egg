using System;
using System.ComponentModel;
using Microsoft.Extensions.Logging;
using SalmonEgg.Presentation.ViewModels.Settings;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public sealed class AcpConnectionEvictionOptionsBridge : IDisposable
{
    private readonly AcpConnectionEvictionOptions _options;
    private readonly AppPreferencesViewModel _preferences;
    private readonly ILogger<AcpConnectionEvictionOptionsBridge> _logger;

    public AcpConnectionEvictionOptionsBridge(
        AcpConnectionEvictionOptions options,
        AppPreferencesViewModel preferences,
        ILogger<AcpConnectionEvictionOptionsBridge> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _preferences.PropertyChanged += OnPreferencesPropertyChanged;
        if (_preferences.IsLoaded)
        {
            ApplyFromPreferences();
        }
    }

    private void OnPreferencesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppPreferencesViewModel.AcpEnableConnectionEviction)
            or nameof(AppPreferencesViewModel.AcpConnectionIdleTtlMinutes)
            or nameof(AppPreferencesViewModel.AcpMaxWarmProfiles)
            or nameof(AppPreferencesViewModel.AcpMaxPinnedProfiles)
            or nameof(AppPreferencesViewModel.IsLoaded))
        {
            if (!_preferences.IsLoaded)
            {
                return;
            }

            ApplyFromPreferences();
        }
    }

    private void ApplyFromPreferences()
    {
        var idleMinutes = _preferences.AcpConnectionIdleTtlMinutes;
        _options.EnablePolicyEviction = _preferences.AcpEnableConnectionEviction;
        _options.IdleTtl = idleMinutes is > 0 ? TimeSpan.FromMinutes(idleMinutes.Value) : null;
        _options.MaxWarmProfiles = _preferences.AcpMaxWarmProfiles is >= 0 ? _preferences.AcpMaxWarmProfiles : null;
        _options.MaxPinnedProfiles = _preferences.AcpMaxPinnedProfiles is >= 0 ? _preferences.AcpMaxPinnedProfiles : null;

        _logger.LogInformation(
            "ACP eviction options refreshed from preferences. enabled={Enabled} idleTtlMinutes={IdleTtlMinutes} maxWarmProfiles={MaxWarmProfiles} maxPinnedProfiles={MaxPinnedProfiles}",
            _options.EnablePolicyEviction,
            idleMinutes,
            _options.MaxWarmProfiles,
            _options.MaxPinnedProfiles);
    }

    public void Dispose()
    {
        _preferences.PropertyChanged -= OnPreferencesPropertyChanged;
    }
}
