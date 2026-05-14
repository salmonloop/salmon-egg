using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.ProjectAffinity;
using SalmonEgg.Domain.Services;
using SalmonEgg.Infrastructure.Storage.YamlModels;
using YamlDotNet.Core;

namespace SalmonEgg.Infrastructure.Storage;

public sealed class AppSettingsService : IAppSettingsService
{
    private const int CurrentSchemaVersion = 1;

    private readonly IAppFileStore _fileStore;
    private readonly string _appYamlPath;

    public AppSettingsService(IAppFileStore fileStore, IAppDataService appData)
    {
        _fileStore = fileStore ?? throw new ArgumentNullException(nameof(fileStore));
        if (appData is null) throw new ArgumentNullException(nameof(appData));

        _appYamlPath = System.IO.Path.Combine(appData.ConfigRootPath, "app.yaml");
    }

    public async Task<AppSettings> LoadAsync()
    {
        var yaml = await _fileStore.ReadAllTextAsync(_appYamlPath).ConfigureAwait(false);
        if (yaml is null)
        {
            return new AppSettings();
        }

        try
        {
            var model = YamlSerialization.CreateDeserializer().Deserialize<AppSettingsYamlV1>(yaml);
            if (model.SchemaVersion <= 0)
            {
                return new AppSettings();
            }

            return new AppSettings
            {
                Theme = string.IsNullOrWhiteSpace(model.Theme) ? "System" : model.Theme,
                IsAnimationEnabled = model.IsAnimationEnabled,
                LastSelectedServerId = string.IsNullOrWhiteSpace(model.LastSelectedServerId) ? null : model.LastSelectedServerId,
                LaunchOnStartup = model.LaunchOnStartup,
                MinimizeToTray = model.MinimizeToTray,
                Language = string.IsNullOrWhiteSpace(model.Language) ? "System" : model.Language,
                Backdrop = string.IsNullOrWhiteSpace(model.Backdrop) ? "System" : model.Backdrop,
                SaveLocalHistory = model.SaveLocalHistory,
                CacheRetentionDays = model.CacheRetentionDays > 0 ? model.CacheRetentionDays : 7,
                KeyBindings = model.KeyBindings ?? new(),
                Projects = model.Projects ?? new(),
                ProjectPathMappings = CloneProjectPathMappings(model.ProjectPathMappings),
                LastSelectedProjectId = string.IsNullOrWhiteSpace(model.LastSelectedProjectId) ? null : model.LastSelectedProjectId,
                AcpEnableConnectionEviction = model.AcpEnableConnectionEviction,
                AcpConnectionIdleTtlMinutes = model.AcpConnectionIdleTtlMinutes,
                AcpMaxWarmProfiles = model.AcpMaxWarmProfiles,
                AcpMaxPinnedProfiles = model.AcpMaxPinnedProfiles,
                AcpHydrationCompletionMode = string.IsNullOrWhiteSpace(model.AcpHydrationCompletionMode)
                    ? "StrictReplay"
                    : model.AcpHydrationCompletionMode.Trim()
            };
        }
        catch (YamlException)
        {
            return new AppSettings();
        }
        catch (IOException)
        {
            return new AppSettings();
        }
    }

    public async Task SaveAsync(AppSettings settings)
    {
        if (settings is null) throw new ArgumentNullException(nameof(settings));

        await EnsureWritableSchemaAsync(_appYamlPath).ConfigureAwait(false);

        var model = new AppSettingsYamlV1
        {
            SchemaVersion = CurrentSchemaVersion,
            UpdatedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
            Theme = settings.Theme ?? "System",
            IsAnimationEnabled = settings.IsAnimationEnabled,
            LastSelectedServerId = settings.LastSelectedServerId ?? string.Empty,
            LaunchOnStartup = settings.LaunchOnStartup,
            MinimizeToTray = settings.MinimizeToTray,
            Language = settings.Language ?? "System",
            Backdrop = settings.Backdrop ?? "System",
            SaveLocalHistory = settings.SaveLocalHistory,
            CacheRetentionDays = settings.CacheRetentionDays > 0 ? settings.CacheRetentionDays : 7,
            KeyBindings = settings.KeyBindings ?? new(),
            Projects = settings.Projects ?? new(),
            ProjectPathMappings = CloneProjectPathMappings(settings.ProjectPathMappings),
            LastSelectedProjectId = settings.LastSelectedProjectId ?? string.Empty,
            AcpEnableConnectionEviction = settings.AcpEnableConnectionEviction,
            AcpConnectionIdleTtlMinutes = settings.AcpConnectionIdleTtlMinutes,
            AcpMaxWarmProfiles = settings.AcpMaxWarmProfiles,
            AcpMaxPinnedProfiles = settings.AcpMaxPinnedProfiles,
            AcpHydrationCompletionMode = string.IsNullOrWhiteSpace(settings.AcpHydrationCompletionMode)
                ? "StrictReplay"
                : settings.AcpHydrationCompletionMode.Trim()
        };

        var yaml = YamlSerialization.CreateSerializer().Serialize(model);
        await _fileStore.WriteAllTextAsync(_appYamlPath, yaml).ConfigureAwait(false);
    }

    private async Task EnsureWritableSchemaAsync(string appYamlPath)
    {
        var yaml = await _fileStore.ReadAllTextAsync(appYamlPath).ConfigureAwait(false);
        if (yaml is null)
        {
            return;
        }

        try
        {
            var existing = YamlSerialization.CreateDeserializer().Deserialize<AppSettingsYamlV1>(yaml);
            if (existing.SchemaVersion > CurrentSchemaVersion)
            {
                throw new InvalidOperationException(
                    $"App settings schema_version {existing.SchemaVersion} is newer than supported version {CurrentSchemaVersion}. Refusing to overwrite.");
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch
        {
            throw new InvalidOperationException("Existing app.yaml is unreadable; refusing to overwrite.");
        }
    }

    private static List<ProjectPathMapping> CloneProjectPathMappings(IEnumerable<ProjectPathMapping>? mappings)
    {
        var clone = new List<ProjectPathMapping>();
        if (mappings is null)
        {
            return clone;
        }

        foreach (var mapping in mappings)
        {
            if (mapping is null)
            {
                continue;
            }

            clone.Add(new ProjectPathMapping
            {
                ProfileId = mapping.ProfileId?.Trim() ?? string.Empty,
                RemoteRootPath = mapping.RemoteRootPath?.Trim() ?? string.Empty,
                LocalRootPath = mapping.LocalRootPath?.Trim() ?? string.Empty
            });
        }

        return clone;
    }
}
