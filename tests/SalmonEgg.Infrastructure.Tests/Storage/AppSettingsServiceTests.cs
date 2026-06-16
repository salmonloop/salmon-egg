using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models;
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
            IsAnimationEnabled = false
        };

        await service.SaveAsync(settings);

        var appYamlPath = Path.Combine(_testDirectory, "SalmonEgg", "config", "app.yaml");
        Assert.True(File.Exists(appYamlPath));

        var loaded = await service.LoadAsync();
        Assert.Equal("Dark", loaded.Theme);
        Assert.False(loaded.IsAnimationEnabled);
    }

    [Fact]
    public async Task SaveThenLoad_RoundTripsAgentRemoteDirectories()
    {
        var service = CreateService();

        await service.SaveAsync(new AppSettings
        {
            AgentRemoteDirectories = new List<AgentRemoteDirectory>
            {
                new()
                {
                    ProfileId = " profile-a ",
                    DirectoryId = " dir-a ",
                    DisplayName = " Alpha ",
                    RemotePath = " /remote/alpha "
                },
                new()
                {
                    ProfileId = "profile-b",
                    DirectoryId = "dir-b",
                    DisplayName = "Beta",
                    RemotePath = "/remote/beta"
                }
            }
        });

        var loaded = await service.LoadAsync();

        Assert.Collection(
            loaded.AgentRemoteDirectories,
            first =>
            {
                Assert.Equal("profile-a", first.ProfileId);
                Assert.Equal("dir-a", first.DirectoryId);
                Assert.Equal("Alpha", first.DisplayName);
                Assert.Equal("/remote/alpha", first.RemotePath);
            },
            second =>
            {
                Assert.Equal("profile-b", second.ProfileId);
                Assert.Equal("dir-b", second.DirectoryId);
                Assert.Equal("Beta", second.DisplayName);
                Assert.Equal("/remote/beta", second.RemotePath);
            });
    }

    [Fact]
    public async Task SaveThenLoad_RoundTripsAcpConnectionGovernanceOptions()
    {
        var service = CreateService();
        var settings = new AppSettings
        {
            AcpEnabled = false,
            AcpEnableConnectionEviction = true,
            AcpConnectionIdleTtlMinutes = 15,
            AcpMaxWarmProfiles = 3,
            AcpMaxPinnedProfiles = 1
        };

        await service.SaveAsync(settings);

        var loaded = await service.LoadAsync();
        Assert.False(loaded.AcpEnabled);
        Assert.True(loaded.AcpEnableConnectionEviction);
        Assert.Equal(15, loaded.AcpConnectionIdleTtlMinutes);
        Assert.Equal(3, loaded.AcpMaxWarmProfiles);
        Assert.Equal(1, loaded.AcpMaxPinnedProfiles);

        var appYamlPath = Path.Combine(_testDirectory, "SalmonEgg", "config", "app.yaml");
        var yaml = await File.ReadAllTextAsync(appYamlPath);
        Assert.Contains("acp_enabled: false", yaml, StringComparison.Ordinal);
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
            """);

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
        var yaml = await File.ReadAllTextAsync(appYamlPath);

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
        var yaml = await File.ReadAllTextAsync(appYamlPath);

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
            """);

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
    public async Task LoadAsync_WhenSettingsFileCannotBeRead_ReturnsDefaults()
    {
        var service = new AppSettingsService(new FailingAppFileStore(), new AppDataService());

        var loaded = await service.LoadAsync();

        Assert.Equal("System", loaded.Theme);
    }

    private AppSettingsService CreateService()
        => new(new FileSystemAppFileStore(), new AppDataService());
}
