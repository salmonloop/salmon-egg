using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.ProjectAffinity;
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
        var service = new AppSettingsService();
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
    public async Task SaveThenLoad_RoundTripsProjectPathMappings()
    {
        var service = new AppSettingsService();
        var settings = new AppSettings
        {
            ProjectPathMappings = new List<ProjectPathMapping>
            {
                new()
                {
                    ProfileId = "profile-one",
                    RemoteRootPath = "/remote/one",
                    LocalRootPath = "C:\\Project\\One"
                },
                new()
                {
                    ProfileId = " profile-two ",
                    RemoteRootPath = " /remote/two ",
                    LocalRootPath = " C:\\Project\\Two "
                }
            }
        };

        await service.SaveAsync(settings);

        var loaded = await service.LoadAsync();
        Assert.Equal(2, loaded.ProjectPathMappings.Count);
        Assert.Collection(
            loaded.ProjectPathMappings,
            first =>
            {
                Assert.Equal("profile-one", first.ProfileId);
                Assert.Equal("/remote/one", first.RemoteRootPath);
                Assert.Equal("C:\\Project\\One", first.LocalRootPath);
            },
            second =>
            {
                Assert.Equal("profile-two", second.ProfileId);
                Assert.Equal("/remote/two", second.RemoteRootPath);
                Assert.Equal("C:\\Project\\Two", second.LocalRootPath);
            });
    }
}

