using System;
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
}

