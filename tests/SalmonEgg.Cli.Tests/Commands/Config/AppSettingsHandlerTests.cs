using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using SalmonEgg.Cli.Commands.Config;
using SalmonEgg.Cli.Output;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Infrastructure.Storage;

namespace SalmonEgg.Cli.Tests.Commands.Config;

/// <summary>
/// Tests for the <c>config settings</c> handler over the real app-settings stack.
/// </summary>
public sealed class AppSettingsHandlerTests
{
    [Fact]
    public async Task GetAsync_PrintsEveryEditableKeyWithCurrentValue()
    {
        using var fixture = new SettingsFixture();
        fixture.SeedAppYaml(
            """
            schema_version: 3
            theme: Dark
            cache_retention_days: 14
            """);

        var exitCode = await fixture.Handler.GetAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, exitCode);
        var lines = fixture.Output.Lines;
        Assert.Contains("theme: Dark", lines, StringComparer.Ordinal);
        Assert.Contains("cache_retention_days: 14", lines, StringComparer.Ordinal);
        Assert.Equal(AppSettingValueCatalog.EditableKeys.Count, lines.Count);
    }

    [Fact]
    public async Task GetAsync_WithoutAppYaml_ShowsDefaults()
    {
        using var fixture = new SettingsFixture();

        var exitCode = await fixture.Handler.GetAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Contains("theme: System", fixture.Output.Lines, StringComparer.Ordinal);
        Assert.Contains("cache_retention_days: 7", fixture.Output.Lines, StringComparer.Ordinal);
    }

    [Fact]
    public async Task SetAsync_UpdatesSingleFieldAndPersistsIt()
    {
        using var fixture = new SettingsFixture();

        var exitCode = await fixture.Handler.SetAsync("theme", "Dark", TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Contains("theme: Dark", fixture.Output.Lines, StringComparer.Ordinal);
        var reloaded = await fixture.AppSettings.LoadAsync();
        Assert.Equal("Dark", reloaded.Theme);
        // 其余字段保持默认：单字段更新不得顺带改写别的设置。
        Assert.True(reloaded.IsAnimationEnabled);
        Assert.Equal(7, reloaded.CacheRetentionDays);
    }

    [Fact]
    public async Task SetAsync_NormalizesLanguageAliasToCanonicalTag()
    {
        using var fixture = new SettingsFixture();

        var exitCode = await fixture.Handler.SetAsync("language", "zh-CN", TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, exitCode);
        var reloaded = await fixture.AppSettings.LoadAsync();
        Assert.Equal(AppLanguageCatalog.SimplifiedChineseTag, reloaded.Language);
    }

    [Fact]
    public async Task SetAsync_RejectsUnknownKeyWithoutWriting()
    {
        using var fixture = new SettingsFixture();

        var exitCode = await fixture.Handler.SetAsync("not_a_setting", "true", TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Usage, exitCode);
        Assert.Empty(fixture.Output.Lines);
        Assert.NotEmpty(fixture.Output.Errors);
        Assert.False(File.Exists(fixture.AppYamlPath));
    }

    [Fact]
    public async Task SetAsync_RejectsInvalidEnumValueWithoutWriting()
    {
        using var fixture = new SettingsFixture();

        var exitCode = await fixture.Handler.SetAsync("theme", "Neon", TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Usage, exitCode);
        Assert.Contains("Allowed values", Assert.Single(fixture.Output.Errors), StringComparison.Ordinal);
        Assert.False(File.Exists(fixture.AppYamlPath));
    }

    [Fact]
    public async Task SetAsync_RejectsNonPositiveRetentionDays()
    {
        using var fixture = new SettingsFixture();

        var exitCode = await fixture.Handler.SetAsync("cache_retention_days", "0", TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Usage, exitCode);
        Assert.False(File.Exists(fixture.AppYamlPath));
    }

    /// <summary>
    /// Saved 事件语义：CLI 写入必须像 GUI 保存一样触发订阅方，且订阅方异常不阻断落盘。
    /// </summary>
    [Fact]
    public async Task SetAsync_NotifiesSavedSubscribersWithPersistedSnapshot()
    {
        using var fixture = new SettingsFixture();
        var received = new List<AppSettings>();
        fixture.AppSettings.Saved += (_, args) => received.Add(args.Settings);

        await fixture.Handler.SetAsync("backdrop", "Acrylic", TestContext.Current.CancellationToken);

        var snapshot = Assert.Single(received);
        Assert.Equal("Acrylic", snapshot.Backdrop);
        var onDisk = await fixture.AppSettings.LoadAsync();
        Assert.Equal(snapshot.Backdrop, onDisk.Backdrop);
    }

    [Fact]
    public async Task SetAsync_SubscriberThrowDoesNotFailTheSave()
    {
        using var fixture = new SettingsFixture();
        fixture.AppSettings.Saved += (_, _) => throw new InvalidOperationException("subscriber blew up");

        var exitCode = await fixture.Handler.SetAsync("animation_enabled", "false", TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, exitCode);
        var reloaded = await fixture.AppSettings.LoadAsync();
        Assert.False(reloaded.IsAnimationEnabled);
    }

    [Fact]
    public async Task SetAsync_OnSchemaTooNewFile_RefusesWriteWithUpgradeGuidance()
    {
        using var fixture = new SettingsFixture();
        fixture.SeedAppYaml(
            """
            schema_version: 99
            theme: Dark
            """);

        var exitCode = await fixture.Handler.SetAsync("theme", "Light", TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Failure, exitCode);
        var error = Assert.Single(fixture.Output.Errors);
        Assert.Contains("Refusing to overwrite", error, StringComparison.Ordinal);
        Assert.Contains("Upgrade", error, StringComparison.Ordinal);
        // 拒绝写回后磁盘上的高版本文件必须原样保留。
        var yaml = File.ReadAllText(fixture.AppYamlPath);
        Assert.Contains("schema_version: 99", yaml, StringComparison.Ordinal);
    }
}

/// <summary>
/// Builds the settings handler over the real app-settings service in an isolated app-data root.
/// </summary>
internal sealed class SettingsFixture : IDisposable
{
    private readonly string _root;

    public SettingsFixture()
    {
        _root = Path.Combine(Path.GetTempPath(), "SalmonEggCliSettingsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        Environment.SetEnvironmentVariable("SALMONEGG_APPDATA_ROOT", _root, EnvironmentVariableTarget.Process);

        Output = new RecordingCliOutput();
        AppSettings = new AppSettingsService(
            new FileSystemAppFileStore(),
            new AppDataService(),
            NullLogger<AppSettingsService>.Instance,
            new RecordingSecureStorage());
        Handler = new AppSettingsHandler(Output, AppSettings);
    }

    public RecordingCliOutput Output { get; }

    public AppSettingsService AppSettings { get; }

    public AppSettingsHandler Handler { get; }

    public string AppYamlPath => Path.Combine(_root, "config", "app.yaml");

    public void SeedAppYaml(string yaml)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(AppYamlPath)!);
        File.WriteAllText(AppYamlPath, yaml);
        Output.Reset();
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("SALMONEGG_APPDATA_ROOT", null, EnvironmentVariableTarget.Process);
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup failures.
        }
    }
}
