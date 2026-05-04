using System;
using Microsoft.Extensions.Logging;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public static class AcpConnectionEvictionOptionsLoader
{
    private const string EnableEnv = "SALMONEGG_ACP_EVICTION_ENABLED";
    private const string IdleTtlMinutesEnv = "SALMONEGG_ACP_EVICTION_IDLE_TTL_MINUTES";
    private const string MaxWarmProfilesEnv = "SALMONEGG_ACP_EVICTION_MAX_WARM_PROFILES";
    private const string MaxPinnedProfilesEnv = "SALMONEGG_ACP_EVICTION_MAX_PINNED_PROFILES";

    public static AcpConnectionEvictionOptions Load(
        IAppSettingsService appSettingsService,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(appSettingsService);

        AppSettings? settings = null;
        try
        {
            // TODO: Make this method async and await LoadAsync once callers are async-capable.
            settings = appSettingsService.LoadAsync().ConfigureAwait(false).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to load ACP eviction options from app settings. Falling back to defaults/env.");
        }

        var enable = ParseBoolEnv(EnableEnv) ?? settings?.AcpEnableConnectionEviction ?? false;
        var idleMinutes = ParseIntEnv(IdleTtlMinutesEnv) ?? settings?.AcpConnectionIdleTtlMinutes;
        var maxWarm = ParseIntEnv(MaxWarmProfilesEnv) ?? settings?.AcpMaxWarmProfiles;
        var maxPinned = ParseIntEnv(MaxPinnedProfilesEnv) ?? settings?.AcpMaxPinnedProfiles;

        var options = new AcpConnectionEvictionOptions
        {
            EnablePolicyEviction = enable,
            IdleTtl = idleMinutes is > 0 ? TimeSpan.FromMinutes(idleMinutes.Value) : null,
            MaxWarmProfiles = maxWarm is >= 0 ? maxWarm : null,
            MaxPinnedProfiles = maxPinned is >= 0 ? maxPinned : null
        };

        logger?.LogInformation(
            "ACP eviction options loaded. enabled={Enabled} idleTtlMinutes={IdleTtlMinutes} maxWarmProfiles={MaxWarmProfiles} maxPinnedProfiles={MaxPinnedProfiles}",
            options.EnablePolicyEviction,
            idleMinutes,
            options.MaxWarmProfiles,
            options.MaxPinnedProfiles);

        return options;
    }

    private static bool? ParseBoolEnv(string name)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        return bool.TryParse(raw, out var parsed) ? parsed : null;
    }

    private static int? ParseIntEnv(string name)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        return int.TryParse(raw, out var parsed) ? parsed : null;
    }
}

