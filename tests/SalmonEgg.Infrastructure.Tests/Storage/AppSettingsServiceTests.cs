using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Infrastructure.Storage;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.Storage;

public sealed class AppSettingsServiceTests : IDisposable
{
    private readonly string _testDirectory;

    public AppSettingsServiceTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "SalmonEggAppSettingsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDirectory);
        Environment.SetEnvironmentVariable("SALMONEGG_APPDATA_ROOT", Path.Combine(_testDirectory, "SalmonEgg"), EnvironmentVariableTarget.Process);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("SALMONEGG_APPDATA_ROOT", null, EnvironmentVariableTarget.Process);
        try
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, recursive: true);
            }
        }
        catch
        {
        }
    }

    [Fact]
    public async Task SaveThenLoad_RoundTripsToAppYaml()
    {
        var service = CreateService();
        var settings = new AppSettings
        {
            Theme = "Dark",
            IsAnimationEnabled = false,
            KeyboardShortcutsEnabled = false
        };

        await service.SaveAsync(settings);

        var appYamlPath = Path.Combine(_testDirectory, "SalmonEgg", "config", "app.yaml");
        Assert.True(File.Exists(appYamlPath));

        var loaded = await service.LoadAsync();
        Assert.Equal("Dark", loaded.Theme);
        Assert.False(loaded.IsAnimationEnabled);
        Assert.False(loaded.KeyboardShortcutsEnabled);
    }

    [Fact]
    public async Task SaveThenLoad_RoundTripsCloudConfigSyncSettings()
    {
        var service = CreateService();

        await service.SaveAsync(new AppSettings
        {
            CloudConfigSync = new CloudConfigSyncSettings
            {
                Enabled = true,
                ProviderId = "onedrive",
                IncludeSecrets = true,
                ProviderOptions = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["webdav"] = new(StringComparer.OrdinalIgnoreCase)
                    {
                        ["file_url"] = " https://dav.example.test/salmonegg-config.zip ",
                        ["username"] = " alice "
                    },
                    ["s3"] = new(StringComparer.OrdinalIgnoreCase)
                    {
                        ["endpoint"] = " https://s3.example.test ",
                        ["bucket"] = " salmonegg ",
                        ["region"] = " auto ",
                        ["object_key"] = " config-sync/salmonegg-config.zip ",
                        ["force_path_style"] = " true "
                    }
                }
            }
        });

        var appYamlPath = Path.Combine(_testDirectory, "SalmonEgg", "config", "app.yaml");
        var yaml = await File.ReadAllTextAsync(appYamlPath, TestContext.Current.CancellationToken);
        var loaded = await service.LoadAsync();

        // The service wrote this file, so the expected version follows its constant rather than a
        // literal that would go stale on the next app.yaml schema bump.
        Assert.Contains(
            $"schema_version: {AppSettingsService.CurrentAppSettingsSchemaVersion}",
            yaml,
            StringComparison.Ordinal);
        Assert.True(loaded.CloudConfigSync.Enabled);
        Assert.Equal("onedrive", loaded.CloudConfigSync.ProviderId);
        Assert.True(loaded.CloudConfigSync.IncludeSecrets);
        Assert.Equal("https://dav.example.test/salmonegg-config.zip", loaded.CloudConfigSync.ProviderOptions["webdav"]["file_url"]);
        Assert.Equal("alice", loaded.CloudConfigSync.ProviderOptions["webdav"]["username"]);
        Assert.Equal("https://s3.example.test", loaded.CloudConfigSync.ProviderOptions["s3"]["endpoint"]);
        Assert.Equal("salmonegg", loaded.CloudConfigSync.ProviderOptions["s3"]["bucket"]);
        Assert.Equal("auto", loaded.CloudConfigSync.ProviderOptions["s3"]["region"]);
        Assert.Equal("config-sync/salmonegg-config.zip", loaded.CloudConfigSync.ProviderOptions["s3"]["object_key"]);
        Assert.Equal("true", loaded.CloudConfigSync.ProviderOptions["s3"]["force_path_style"]);
    }

    [Fact]
    public async Task SaveThenLoad_TrimsAgentRemoteDirectories_WithoutPersistingProfileBinding()
    {
        var service = CreateService();

        await service.SaveAsync(new AppSettings
        {
            AgentRemoteDirectories = new List<AgentRemoteDirectory>
            {
                new()
                {
                    DirectoryId = " dir-a ",
                    DisplayName = " Alpha ",
                    RemotePath = " /remote/alpha "
                },
                new()
                {
                    DirectoryId = "dir-b",
                    DisplayName = "Beta",
                    RemotePath = "/remote/beta"
                }
            }
        });

        var appYamlPath = Path.Combine(_testDirectory, "SalmonEgg", "config", "app.yaml");
        var yaml = await File.ReadAllTextAsync(appYamlPath, TestContext.Current.CancellationToken);

        Assert.DoesNotContain("profile_id", yaml, StringComparison.Ordinal);

        var loaded = await service.LoadAsync();

        Assert.Collection(
            loaded.AgentRemoteDirectories,
            first =>
            {
                Assert.Equal("dir-a", first.DirectoryId);
                Assert.Equal("Alpha", first.DisplayName);
                Assert.Equal("/remote/alpha", first.RemotePath);
            },
            second =>
            {
                Assert.Equal("dir-b", second.DirectoryId);
                Assert.Equal("Beta", second.DisplayName);
                Assert.Equal("/remote/beta", second.RemotePath);
            });
    }

    [Fact]
    public async Task LoadAsync_IgnoresLegacyRemoteDirectoryProfileBindingField()
    {
        var appYamlPath = Path.Combine(_testDirectory, "SalmonEgg", "config", "app.yaml");
        Directory.CreateDirectory(Path.GetDirectoryName(appYamlPath)!);

        await File.WriteAllTextAsync(
            appYamlPath,
            """
            schema_version: 1
            agent_remote_directories:
              - profile_id: legacy-profile
                directory_id: dir-a
                display_name: Alpha
                remote_path: /remote/alpha
            """, TestContext.Current.CancellationToken);

        var service = CreateService();

        var loaded = await service.LoadAsync();

        var directory = Assert.Single(loaded.AgentRemoteDirectories);
        Assert.Equal("dir-a", directory.DirectoryId);
        Assert.Equal("Alpha", directory.DisplayName);
        Assert.Equal("/remote/alpha", directory.RemotePath);
    }

    [Fact]
    public void AgentRemoteDirectoryModel_DoesNotExposeProfileBinding()
    {
        Assert.Null(typeof(AgentRemoteDirectory).GetProperty("ProfileId"));
    }

    [Fact]
    public async Task SaveThenLoad_RoundTripsAcpConnectionGovernanceOptionsWithoutGlobalDisable()
    {
        var service = CreateService();
        var settings = new AppSettings
        {
            AcpEnableConnectionEviction = true,
            AcpConnectionIdleTtlMinutes = 15,
            AcpMaxWarmProfiles = 3,
            AcpMaxPinnedProfiles = 1
        };

        await service.SaveAsync(settings);

        var loaded = await service.LoadAsync();
        Assert.True(loaded.AcpEnableConnectionEviction);
        Assert.Equal(15, loaded.AcpConnectionIdleTtlMinutes);
        Assert.Equal(3, loaded.AcpMaxWarmProfiles);
        Assert.Equal(1, loaded.AcpMaxPinnedProfiles);

        var appYamlPath = Path.Combine(_testDirectory, "SalmonEgg", "config", "app.yaml");
        var yaml = await File.ReadAllTextAsync(appYamlPath, TestContext.Current.CancellationToken);
        Assert.DoesNotContain("acp_enabled", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaveThenLoad_RoundTripsAcpHydrationCompletionMode()
    {
        var service = CreateService();
        var settings = new AppSettings
        {
            AcpHydrationCompletionMode = "LoadResponse"
        };

        await service.SaveAsync(settings);

        var loaded = await service.LoadAsync();
        Assert.Equal("LoadResponse", loaded.AcpHydrationCompletionMode);
    }

    [Theory]
    [InlineData("zh", "zh-Hans")]
    [InlineData("zh-CN", "zh-Hans")]
    [InlineData("zh-Hans", "zh-Hans")]
    [InlineData("en", "en-US")]
    [InlineData("en-US", "en-US")]
    [InlineData("fr-FR", "System")]
    public async Task LoadAsync_NormalizesLanguageTags(string persistedTag, string expectedTag)
    {
        var appYamlPath = Path.Combine(_testDirectory, "SalmonEgg", "config", "app.yaml");
        Directory.CreateDirectory(Path.GetDirectoryName(appYamlPath)!);

        await File.WriteAllTextAsync(
            appYamlPath,
            $"""
            schema_version: 1
            language: {persistedTag}
            """, TestContext.Current.CancellationToken);

        var service = CreateService();

        var loaded = await service.LoadAsync();

        Assert.Equal(expectedTag, loaded.Language);
    }

    [Fact]
    public async Task SaveAsync_PersistsCanonicalLanguageTag()
    {
        var service = CreateService();

        await service.SaveAsync(new AppSettings { Language = "zh-CN" });

        var appYamlPath = Path.Combine(_testDirectory, "SalmonEgg", "config", "app.yaml");
        var yaml = await File.ReadAllTextAsync(appYamlPath, TestContext.Current.CancellationToken);

        Assert.Contains("language: zh-Hans", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("zh-CN", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaveThenLoad_DoesNotPersistRemovedStorageKeys_AndKeepsLastSelectedProjectId()
    {
        var service = CreateService();
        var settings = new AppSettings
        {
            Theme = "Dark",
            LastSelectedProjectId = "project-123"
        };

        await service.SaveAsync(settings);

        var appYamlPath = Path.Combine(_testDirectory, "SalmonEgg", "config", "app.yaml");
        var yaml = await File.ReadAllTextAsync(appYamlPath, TestContext.Current.CancellationToken);

        Assert.DoesNotContain("HistoryRetentionDays", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("RememberRecentProjectPaths", yaml, StringComparison.Ordinal);

        var loaded = await service.LoadAsync();
        Assert.Equal("project-123", loaded.LastSelectedProjectId);
    }

    [Fact]
    public async Task LoadAsync_IgnoresLegacyRemovedStorageKeys()
    {
        var appYamlPath = Path.Combine(_testDirectory, "SalmonEgg", "config", "app.yaml");
        Directory.CreateDirectory(Path.GetDirectoryName(appYamlPath)!);

        await File.WriteAllTextAsync(
            appYamlPath,
            """
            schema_version: 1
            theme: Dark
            history_retention_days: 45
            remember_recent_project_paths: false
            last_selected_project_id: project-123
            """, TestContext.Current.CancellationToken);

        var service = CreateService();

        var loaded = await service.LoadAsync();

        Assert.Equal("Dark", loaded.Theme);
        Assert.Equal("project-123", loaded.LastSelectedProjectId);
    }

    [Fact]
    public void RemovedStoragePreferenceProperties_AreNotInPersistedModels()
    {
        Assert.Null(typeof(AppSettings).GetProperty("HistoryRetentionDays"));
        Assert.Null(typeof(AppSettings).GetProperty("RememberRecentProjectPaths"));

        var yamlModelType = typeof(AppSettingsService).Assembly.GetType(
            "SalmonEgg.Infrastructure.Storage.YamlModels.AppSettingsYamlV1",
            throwOnError: true);

        Assert.NotNull(yamlModelType);
        Assert.Null(yamlModelType!.GetProperty("HistoryRetentionDays"));
        Assert.Null(yamlModelType.GetProperty("RememberRecentProjectPaths"));
    }

    [Fact]
    public void Constructor_DoesNotCreateConfigDirectory()
    {
        _ = CreateService();

        Assert.False(Directory.Exists(Path.Combine(_testDirectory, "SalmonEgg", "config")));
    }

    [Fact]
    public async Task SaveAsync_WhenExistingFileIsCorruptedYaml_OverwritesAndLoadsBack()
    {
        var appYamlPath = Path.Combine(_testDirectory, "SalmonEgg", "config", "app.yaml");
        Directory.CreateDirectory(Path.GetDirectoryName(appYamlPath)!);
        await File.WriteAllTextAsync(appYamlPath, ":\n  - definitely not yaml", TestContext.Current.CancellationToken);

        var service = CreateService();
        await service.SaveAsync(new AppSettings { Theme = "Dark", Language = "zh-Hans" });

        var loaded = await service.LoadAsync();
        Assert.Equal("Dark", loaded.Theme);
        Assert.Equal("zh-Hans", loaded.Language);
    }

    [Fact]
    public async Task LoadAsync_WhenSettingsFileCannotBeRead_ReturnsDefaults()
    {
        var service = new AppSettingsService(new FailingAppFileStore(), new AppDataService(), NullLogger<AppSettingsService>.Instance);

        var loaded = await service.LoadAsync();

        Assert.Equal("System", loaded.Theme);
    }

    [Fact]
    public async Task SaveThenLoad_RoundTripsTelemetrySettings()
    {
        // 用非默认值：若 YAML 映射漏了这几个字段，断言"等于默认值"会假绿。
        var secureStorage = new VolatileSecureStorage();
        var service = CreateService(secureStorage);

        await service.SaveAsync(new AppSettings
        {
            TelemetrySharingEnabled = false,
            TelemetryCustomEndpoint = "https://collector.example.com:4318",
            TelemetryAuthHeader = "api-key=abc123"
        });

        var loaded = await service.LoadAsync();

        Assert.False(loaded.TelemetrySharingEnabled);
        Assert.Equal("https://collector.example.com:4318", loaded.TelemetryCustomEndpoint);
        Assert.Equal("api-key=abc123", loaded.TelemetryAuthHeader);

        var storedHeader = await secureStorage.LoadAsync("SalmonEgg.TelemetryAuthHeader");
        Assert.Equal("api-key=abc123", storedHeader);
    }

    [Fact]
    public async Task LoadAsync_WhenTelemetryEndpointIsBlank_NormalizesToNullWithoutDroppingOtherFields()
    {
        // 空串必须还原为 null：否则会阻断 TelemetrySettings.Build 回退到部署环境变量，
        // 并把无效的空地址保留为用户自定义端点。
        //
        // 同时断言一个非空字段：只断言"为 null"无法区分「已归一化」与「压根没映射」——
        // 反向验证时删掉整段 YAML 映射，纯 null 断言依然会绿（假阳性）。
        var service = CreateService();

        await service.SaveAsync(new AppSettings
        {
            TelemetryCustomEndpoint = "   ",
            TelemetryAuthHeader = "api-key=abc123"
        });

        var loaded = await service.LoadAsync();

        Assert.Null(loaded.TelemetryCustomEndpoint);
        Assert.Equal("api-key=abc123", loaded.TelemetryAuthHeader);
    }

    [Fact]
    public async Task SaveAsync_RaisesSavedWithPersistedSnapshot()
    {
        // 运行态（遥测管线）就是靠这个事件跟随磁盘真相的；不触发则"保存后立即生效"不成立。
        var service = CreateService();
        var received = new List<AppSettings>();
        service.Saved += (_, args) => received.Add(args.Settings);

        var settings = new AppSettings { TelemetryCustomEndpoint = "https://collector.example.com:4318" };
        await service.SaveAsync(settings);

        Assert.Single(received);
        Assert.Same(settings, received[0]);
    }

    [Fact]
    public async Task SaveAsync_WhenSubscriberThrows_StillPersists()
    {
        // 订阅方（遥测重建）出错不得让"设置已保存"变成失败——磁盘此时已经写好了。
        var service = CreateService();
        service.Saved += (_, _) => throw new InvalidOperationException("subscriber failed");

        var exception = await Record.ExceptionAsync(
            () => service.SaveAsync(new AppSettings { Theme = "Dark" }));

        Assert.Null(exception);
        var loaded = await service.LoadAsync();
        Assert.Equal("Dark", loaded.Theme);
    }

    [Fact]
    public async Task SaveAsync_WhenWriteFails_DoesNotRaiseSaved()
    {
        // "收到通知 ⇒ 磁盘已是该快照"是订阅方赖以成立的前提：写失败还通知，会让遥测切到
        // 一份并不存在于磁盘上的配置。
        var service = new AppSettingsService(
            new FailingAppFileStore(),
            new AppDataService(),
            NullLogger<AppSettingsService>.Instance);
        var raised = 0;
        service.Saved += (_, _) => raised++;

        await Assert.ThrowsAnyAsync<Exception>(() => service.SaveAsync(new AppSettings()));

        Assert.Equal(0, raised);
    }

    [Fact]
    public async Task SaveAsync_WhenSchemaTooNew_ThrowsTypedRefusalAndLeavesFileUntouched()
    {
        // 高版本文件是更新的程序写的；拒绝写回必须可被宿主按类型识别（给出升级指引而非
        // 重试建议），且拒绝后原文件必须原样保留。
        var appYamlPath = Path.Combine(_testDirectory, "SalmonEgg", "config", "app.yaml");
        Directory.CreateDirectory(Path.GetDirectoryName(appYamlPath)!);
        const string foreignYaml = "schema_version: 99\ntheme: Dark\n";
        await File.WriteAllTextAsync(appYamlPath, foreignYaml, TestContext.Current.CancellationToken);
        var service = CreateService();
        var raised = 0;
        service.Saved += (_, _) => raised++;

        var exception = await Assert.ThrowsAsync<ConfigurationPersistenceException>(
            () => service.SaveAsync(new AppSettings { Theme = "Light" }));

        Assert.Equal(ConfigurationPersistenceFailureReason.SchemaVersionTooNew, exception.Reason);
        Assert.Contains("schema_version 99", exception.UserMessage, StringComparison.Ordinal);
        Assert.Contains("Refusing to overwrite", exception.UserMessage, StringComparison.Ordinal);
        Assert.Equal(0, raised);
        Assert.Equal(foreignYaml, await File.ReadAllTextAsync(appYamlPath, TestContext.Current.CancellationToken));
    }

    private AppSettingsService CreateService(ISecureStorage? secureStorage = null)
        => new(new FileSystemAppFileStore(), new AppDataService(), NullLogger<AppSettingsService>.Instance, secureStorage);
}
