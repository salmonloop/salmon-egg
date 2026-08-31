using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using SalmonEgg.Cli.Commands.Config;
using SalmonEgg.Cli.Output;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Infrastructure.Storage;

namespace SalmonEgg.Cli.Tests.Commands.Config;

/// <summary>
/// Tests for the <c>config validate/export/import</c> handler over the real configuration stack.
/// </summary>
public sealed class ConfigPackageHandlerTests
{
    [Fact]
    public async Task ValidateAsync_WithHealthyConfigs_ReportsOkAndSucceeds()
    {
        using var fixture = new PackageFixture();
        await fixture.SeedServerAsync("alpha", "Alpha", "https://alpha.example");
        fixture.WriteAppYaml("schema_version: 3\ntheme: Dark\n");

        var exitCode = await fixture.Handler.ValidateAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Contains(fixture.Output.Lines, line => line.StartsWith("app.yaml:", StringComparison.Ordinal) && line.Contains("ok", StringComparison.Ordinal));
        // The fixture seeds through the production writer, so the reported version is a function of
        // the writer's constant. Asserting a literal here goes stale on every schema bump.
        Assert.Contains(
            fixture.Output.Lines,
            line => line.Contains("servers/alpha.yaml", StringComparison.Ordinal)
                && line.Contains(
                    $"schema_version {ConfigurationManager.CurrentServerConfigurationSchemaVersion}",
                    StringComparison.Ordinal));
        Assert.Empty(fixture.Output.Errors);
    }

    [Fact]
    public async Task ValidateAsync_WithoutAnyConfig_ReportsAbsentDefaults()
    {
        using var fixture = new PackageFixture();

        var exitCode = await fixture.Handler.ValidateAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Contains("app.yaml: not present (defaults apply).", fixture.Output.Lines, StringComparer.Ordinal);
    }

    [Fact]
    public async Task ValidateAsync_WithTooNewAppYaml_FailsAndReportsUpgradePathOnStderr()
    {
        using var fixture = new PackageFixture();
        fixture.WriteAppYaml(
            """
            schema_version: 42
            theme: Dark
            """);

        var exitCode = await fixture.Handler.ValidateAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Failure, exitCode);
        var error = Assert.Single(fixture.Output.Errors);
        Assert.Contains("app.yaml", error, StringComparison.Ordinal);
        Assert.Contains("schema_version 42", error, StringComparison.Ordinal);
        Assert.Contains("upgrade", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateAsync_WithCorruptServerYaml_FailsWithoutEchoingFileContent()
    {
        using var fixture = new PackageFixture();
        Directory.CreateDirectory(Path.Combine(fixture.AppDataRoot, "config", "servers"));
        // 故意写一段含「伪凭据」的坏 YAML：诊断必须报文件与失败类别，不得回显内容。
        File.WriteAllText(
            Path.Combine(fixture.AppDataRoot, "config", "servers", "broken.yaml"),
            "schema_version: 2\nname: [unclosed\nsecret_token_value: super-secret-token\n");

        var exitCode = await fixture.Handler.ValidateAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Failure, exitCode);
        Assert.NotEmpty(fixture.Output.Errors);
        Assert.DoesNotContain(fixture.Output.Errors, error => error.Contains("super-secret-token", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExportThenImport_RoundTripsConfigurationThroughRealPackageService()
    {
        using var fixture = new PackageFixture();
        await fixture.SeedServerAsync("roundtrip", "Round Trip", "https://roundtrip.example");
        fixture.WriteAppYaml("schema_version: 3\ntheme: Dark\n");

        var exportExit = await fixture.Handler.ExportAsync(includeSecrets: false, TestContext.Current.CancellationToken);
        Assert.Equal(CliExitCodes.Success, exportExit);
        var packagePath = Assert.Single(Directory.GetFiles(fixture.ExportsDirectory, "*.zip"));

        // 清空当前配置，模拟「换一台机器导入」。
        fixture.ResetConfigRoot();
        fixture.Output.Reset();

        var importExit = await fixture.Handler.ImportAsync(packagePath, TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, importExit);
        var reloaded = await fixture.Configurations.LoadConfigurationAsync("roundtrip");
        Assert.NotNull(reloaded);
        Assert.Equal("Round Trip", reloaded!.Name);
        Assert.Equal("Dark", (await fixture.AppSettings.LoadAsync()).Theme);
        Assert.Contains(fixture.Output.Lines, line => line.Contains("backed up at:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ImportAsync_WithMissingFile_ReturnsUsage()
    {
        using var fixture = new PackageFixture();

        var exitCode = await fixture.Handler.ImportAsync(
            Path.Combine(fixture.AppDataRoot, "no-such-package.zip"), TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Usage, exitCode);
        Assert.Contains(fixture.Output.Errors, error => error.Contains("not found", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ImportAsync_WithSchemaTooNewEntry_RefusesBeforeRestore()
    {
        using var fixture = new PackageFixture();
        await fixture.SeedServerAsync("existing", "Existing", "https://existing.example");
        var packagePath = fixture.BuildForeignPackage(
        [
            ("files/config/servers/future.yaml", "schema_version: 77\nid: future\nname: Future\ntransport: websocket\nserver_url: https://future.example\n")
        ]);

        var existingYaml = Path.Combine(fixture.AppDataRoot, "config", "servers", "existing.yaml");
        Assert.True(File.Exists(existingYaml));

        var exitCode = await fixture.Handler.ImportAsync(packagePath, TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Failure, exitCode);
        Assert.Contains(fixture.Output.Errors, error => error.Contains("schema_version 77", StringComparison.Ordinal));
        Assert.Contains(fixture.Output.Errors, error => error.Contains("Refusing to import", StringComparison.Ordinal));
        // 拒绝导入后，现有配置必须原样保留，且不得混入包内的高版本文件。
        Assert.True(File.Exists(existingYaml));
        Assert.False(File.Exists(Path.Combine(fixture.AppDataRoot, "config", "servers", "future.yaml")));
    }

    [Fact]
    public async Task ImportAsync_WithMissingManifest_RefusesWithIdentifiableMessage()
    {
        using var fixture = new PackageFixture();
        var packagePath = fixture.BuildForeignPackage(
            [("files/config/app.yaml", "schema_version: 3\ntheme: Dark\n")],
            includeManifest: false);

        var exitCode = await fixture.Handler.ImportAsync(packagePath, TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Failure, exitCode);
        Assert.Contains(fixture.Output.Errors, error => error.Contains("manifest.json", StringComparison.Ordinal));
    }
}

/// <summary>
/// Builds the package handler over the real diagnostics + packaging stack in an isolated root.
/// </summary>
internal sealed class PackageFixture : IDisposable
{
    private readonly string _root;

    public PackageFixture()
    {
        _root = Path.Combine(Path.GetTempPath(), "SalmonEggCliPackageTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        Environment.SetEnvironmentVariable("SALMONEGG_APPDATA_ROOT", _root, EnvironmentVariableTarget.Process);

        Output = new RecordingCliOutput();
        AppDataService = new AppDataService();
        Configurations = new ConfigurationManager(
            new RecordingSecureStorage(),
            new FileSystemAppFileStore(),
            AppDataService,
            NullLogger<ConfigurationManager>.Instance);
        AppSettings = new AppSettingsService(
            new FileSystemAppFileStore(),
            AppDataService,
            NullLogger<AppSettingsService>.Instance,
            new RecordingSecureStorage());
        Handler = new ConfigPackageHandler(
            Output,
            new ConfigurationDiagnosticsService(AppDataService, new FileSystemAppFileStore()),
            new ConfigSyncPackageService(
                AppDataService,
                new ConfigurationSecretSnapshotService(
                    new RecordingSecureStorage(),
                    new FileSystemAppFileStore(),
                    AppDataService),
                new ConfigChangeSignal(),
                new NoOpFileSystemPersistence(),
                NullLogger<ConfigSyncPackageService>.Instance),
            AppDataService);
    }

    public RecordingCliOutput Output { get; }

    public AppDataService AppDataService { get; }

    public IConfigurationService Configurations { get; }

    public AppSettingsService AppSettings { get; }

    public ConfigPackageHandler Handler { get; }

    public string AppDataRoot => _root;

    public string ExportsDirectory => Path.Combine(_root, "exports");

    public async Task SeedServerAsync(string id, string name, string url)
    {
        await Configurations.SaveConfigurationAsync(new ServerConfiguration
        {
            Id = id,
            Name = name,
            ServerUrl = url,
            Transport = TransportType.WebSocket,
            ConnectionTimeout = AcpConnectionTimeoutPolicy.DefaultSeconds
        });
        Output.Reset();
    }

    public void WriteAppYaml(string yaml)
    {
        var path = Path.Combine(_root, "config", "app.yaml");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, yaml);
        Output.Reset();
    }

    /// <summary>Removes everything under the config root to simulate a fresh machine.</summary>
    public void ResetConfigRoot()
    {
        var configRoot = Path.Combine(_root, "config");
        if (Directory.Exists(configRoot))
        {
            Directory.Delete(configRoot, recursive: true);
        }
    }

    /// <summary>
    /// Builds a zip that looks like a config package but with arbitrary entry contents.
    /// </summary>
    public string BuildForeignPackage((string EntryName, string Content)[] entries, bool includeManifest = true)
    {
        var packagePath = Path.Combine(_root, $"{Guid.NewGuid():N}.zip");
        using var stream = File.Create(packagePath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        if (includeManifest)
        {
            var manifest = archive.CreateEntry("manifest.json");
            using (var writer = new StreamWriter(manifest.Open()))
            {
                writer.Write("""{"schemaVersion":1,"appId":"SalmonEgg","includesSecrets":false,"files":[]}""");
            }
        }

        foreach (var (entryName, content) in entries)
        {
            var entry = archive.CreateEntry(entryName);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(content);
        }

        return packagePath;
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

    private sealed class RecordingSecureStorage : ISecureStorage
    {
        private readonly ConcurrentDictionary<string, string> _values = new(StringComparer.Ordinal);

        public Task SaveAsync(string key, string value)
        {
            _values[key] = value;
            return Task.CompletedTask;
        }

        public Task<string?> LoadAsync(string key)
        {
            _values.TryGetValue(key, out var value);
            return Task.FromResult(value);
        }

        public Task DeleteAsync(string key)
        {
            _values.TryRemove(key, out _);
            return Task.CompletedTask;
        }
    }
}
