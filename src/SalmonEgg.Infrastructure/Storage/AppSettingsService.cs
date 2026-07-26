using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Infrastructure.Storage.YamlModels;
using YamlDotNet.Core;

namespace SalmonEgg.Infrastructure.Storage;

public sealed class AppSettingsService : IAppSettingsService
{
    private const int CurrentSchemaVersion = 2;

    private readonly IAppFileStore _fileStore;
    private readonly ILogger<AppSettingsService> _logger;
    private readonly string _appYamlPath;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public AppSettingsService(IAppFileStore fileStore, IAppDataService appData, ILogger<AppSettingsService> logger)
    {
        _fileStore = fileStore ?? throw new ArgumentNullException(nameof(fileStore));
        if (appData is null) throw new ArgumentNullException(nameof(appData));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _appYamlPath = System.IO.Path.Combine(appData.ConfigRootPath, "app.yaml");
    }

    public async Task<AppSettings> LoadAsync()
    {
        try
        {
            var yaml = await _fileStore.ReadAllTextAsync(_appYamlPath).ConfigureAwait(false);
            if (yaml is null)
            {
                return new AppSettings();
            }

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
                Language = AppLanguageCatalog.NormalizeTag(model.Language),
                Backdrop = string.IsNullOrWhiteSpace(model.Backdrop) ? "System" : model.Backdrop,
                SaveLocalHistory = model.SaveLocalHistory,
                CacheRetentionDays = model.CacheRetentionDays > 0 ? model.CacheRetentionDays : 7,
                CloudConfigSync = FromYaml(model.CloudConfigSync),
                KeyboardShortcutsEnabled = model.KeyboardShortcutsEnabled,
                KeyBindings = model.KeyBindings ?? new(),
                Projects = model.Projects ?? new(),
                AgentRemoteDirectories = CloneAgentRemoteDirectories(model.AgentRemoteDirectories),
                NavigationRemoteDirectoryIds = CloneStringList(model.NavigationRemoteDirectoryIds),
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

        // SPEC-CONFIG-PERSISTENCE §5.3:写入期间持有进程内互斥,防止多写入方
        // 的 schema 检查与落盘交错撕坏 app.yaml。
        await _writeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await EnsureWritableSchemaAsync(_appYamlPath).ConfigureAwait(false);

            await _fileStore.WriteAllTextAsync(_appYamlPath, Serialize(settings)).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    internal static string Serialize(AppSettings settings)
    {
        if (settings is null) throw new ArgumentNullException(nameof(settings));

        var model = new AppSettingsYamlV1
        {
            SchemaVersion = CurrentSchemaVersion,
            UpdatedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
            Theme = settings.Theme ?? "System",
            IsAnimationEnabled = settings.IsAnimationEnabled,
            LastSelectedServerId = settings.LastSelectedServerId ?? string.Empty,
            LaunchOnStartup = settings.LaunchOnStartup,
            MinimizeToTray = settings.MinimizeToTray,
            Language = AppLanguageCatalog.NormalizeTag(settings.Language),
            Backdrop = settings.Backdrop ?? "System",
            SaveLocalHistory = settings.SaveLocalHistory,
            CacheRetentionDays = settings.CacheRetentionDays > 0 ? settings.CacheRetentionDays : 7,
            CloudConfigSync = ToYaml(settings.CloudConfigSync),
            KeyboardShortcutsEnabled = settings.KeyboardShortcutsEnabled,
            KeyBindings = settings.KeyBindings ?? new(),
            Projects = settings.Projects ?? new(),
            AgentRemoteDirectories = CloneAgentRemoteDirectories(settings.AgentRemoteDirectories),
            NavigationRemoteDirectoryIds = CloneStringList(settings.NavigationRemoteDirectoryIds),
            LastSelectedProjectId = settings.LastSelectedProjectId ?? string.Empty,
            AcpEnableConnectionEviction = settings.AcpEnableConnectionEviction,
            AcpConnectionIdleTtlMinutes = settings.AcpConnectionIdleTtlMinutes,
            AcpMaxWarmProfiles = settings.AcpMaxWarmProfiles,
            AcpMaxPinnedProfiles = settings.AcpMaxPinnedProfiles,
            AcpHydrationCompletionMode = string.IsNullOrWhiteSpace(settings.AcpHydrationCompletionMode)
                ? "StrictReplay"
                : settings.AcpHydrationCompletionMode.Trim()
        };

        return YamlSerialization.CreateSerializer().Serialize(model);
    }

    private async Task EnsureWritableSchemaAsync(string appYamlPath)
    {
        try
        {
            var yaml = await _fileStore.ReadAllTextAsync(appYamlPath).ConfigureAwait(false);
            if (yaml is null)
            {
                return;
            }

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
        catch (YamlException ex)
        {
            // 文件存在但 YAML 已损坏（例如 WASM IDBFS 在浏览器崩溃后被截断）。
            // 允许用合法数据覆写——拒绝写入会把用户锁在无法保存的死路上。
            _logger.LogWarning(ex, "Existing app settings file {Path} is corrupted; will overwrite with new data", appYamlPath);
        }
    }

    private static List<AgentRemoteDirectory> CloneAgentRemoteDirectories(IEnumerable<AgentRemoteDirectory>? directories)
    {
        var clone = new List<AgentRemoteDirectory>();
        if (directories is null)
        {
            return clone;
        }

        foreach (var directory in directories)
        {
            if (directory is null)
            {
                continue;
            }

            clone.Add(new AgentRemoteDirectory
            {
                DirectoryId = directory.DirectoryId?.Trim() ?? string.Empty,
                DisplayName = directory.DisplayName?.Trim() ?? string.Empty,
                RemotePath = directory.RemotePath?.Trim() ?? string.Empty
            });
        }

        return clone;
    }

    private static List<string> CloneStringList(IEnumerable<string?>? values)
    {
        var clone = new List<string>();
        if (values is null)
        {
            return clone;
        }

        foreach (var value in values)
        {
            var trimmed = value?.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                clone.Add(trimmed);
            }
        }

        return clone;
    }

    private static CloudConfigSyncSettings FromYaml(CloudConfigSyncYamlV1? yaml)
    {
        if (yaml is null)
        {
            return new CloudConfigSyncSettings();
        }

        return new CloudConfigSyncSettings
        {
            Enabled = yaml.Enabled,
            ProviderId = yaml.ProviderId?.Trim() ?? string.Empty,
            Revision = yaml.Revision,
            IncludeSecrets = yaml.IncludeSecrets,
            ProviderOptions = CloneProviderOptions(yaml.ProviderOptions)
        };
    }

    private static CloudConfigSyncYamlV1 ToYaml(CloudConfigSyncSettings? settings)
    {
        if (settings is null)
        {
            return new CloudConfigSyncYamlV1();
        }

        return new CloudConfigSyncYamlV1
        {
            Enabled = settings.Enabled,
            ProviderId = settings.ProviderId?.Trim() ?? string.Empty,
            Revision = settings.Revision,
            IncludeSecrets = settings.IncludeSecrets,
            ProviderOptions = CloneProviderOptions(settings.ProviderOptions)
        };
    }

    private static Dictionary<string, Dictionary<string, string>> CloneProviderOptions(
        IReadOnlyDictionary<string, Dictionary<string, string>>? options)
    {
        var clone = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        if (options is null)
        {
            return clone;
        }

        foreach (var provider in options)
        {
            if (string.IsNullOrWhiteSpace(provider.Key) || provider.Value is null)
            {
                continue;
            }

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var option in provider.Value)
            {
                if (!string.IsNullOrWhiteSpace(option.Key) && option.Value is not null)
                {
                    values[option.Key.Trim()] = option.Value.Trim();
                }
            }

            clone[provider.Key.Trim()] = values;
        }

        return clone;
    }
}
