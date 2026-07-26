using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Infrastructure.Storage;

namespace SalmonEgg.Infrastructure.Tests.Storage;

public sealed class ConfigContentFingerprintTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly AppDataService _appData;
    private readonly IAppFileStore _fileStore;
    private readonly AppSettingsService _appSettings;
    private readonly ConfigSyncPackageService _packageService;
    private readonly ConfigContentFingerprint _fingerprint;

    public ConfigContentFingerprintTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "SalmonEggFingerprintTests", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("SALMONEGG_APPDATA_ROOT", Path.Combine(_testDirectory, "SalmonEgg"), EnvironmentVariableTarget.Process);
        _appData = new AppDataService();
        var signal = new ConfigChangeSignal();
        _fileStore = new FileSystemAppFileStore(new NoOpFileSystemPersistence(), signal);
        _appSettings = new AppSettingsService(_fileStore, _appData, NullLogger<AppSettingsService>.Instance);
        var secureStorage = new PlainTextFileSecureStorage(_fileStore, _appData);
        var secrets = new ConfigurationSecretSnapshotService(secureStorage, _fileStore, _appData);
        _packageService = new ConfigSyncPackageService(
            _appData,
            secrets,
            signal,
            new NoOpFileSystemPersistence(),
            NullLogger<ConfigSyncPackageService>.Instance);
        _fingerprint = new ConfigContentFingerprint(_packageService);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("SALMONEGG_APPDATA_ROOT", null, EnvironmentVariableTarget.Process);
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task ComputeLocalAsync_StripsUpdatedAtUtc_SoTimestampOnlyChangeIsStable()
    {
        await _appSettings.SaveAsync(new AppSettings { Theme = "Dark" });
        var first = await _fingerprint.ComputeLocalAsync(includeSecrets: false, cancellationToken: TestContext.Current.CancellationToken);

        // 再次保存仅刷新 UpdatedAtUtc，业务内容不变。
        await Task.Delay(10, TestContext.Current.CancellationToken);
        await _appSettings.SaveAsync(new AppSettings { Theme = "Dark" });
        var second = await _fingerprint.ComputeLocalAsync(includeSecrets: false, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task ComputeLocalAsync_ChangesWhenBusinessContentChanges()
    {
        await _appSettings.SaveAsync(new AppSettings { Theme = "Dark" });
        var first = await _fingerprint.ComputeLocalAsync(includeSecrets: false, cancellationToken: TestContext.Current.CancellationToken);

        await _appSettings.SaveAsync(new AppSettings { Theme = "Light" });
        var second = await _fingerprint.ComputeLocalAsync(includeSecrets: false, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task ComputeFromPackage_MatchesComputeLocal_ForSameContent()
    {
        await _appSettings.SaveAsync(new AppSettings { Theme = "Dark", CloudConfigSync = new CloudConfigSyncSettings { Enabled = true, ProviderId = "webdav" } });
        var package = await _packageService.CreatePackageAsync(includeSecrets: true, TestContext.Current.CancellationToken);

        var fromPackage = _fingerprint.ComputeFromPackage(package, includeSecrets: true);
        var fromLocal = await _fingerprint.ComputeLocalAsync(includeSecrets: true, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(fromPackage, fromLocal);
    }

    [Fact]
    public void ComputeFromPackage_IgnoresManifestCreatedAtUtc()
    {
        var early = CreatePackageWithManifestTime("theme: Dark", DateTimeOffset.UtcNow.AddHours(-1));
        var late = CreatePackageWithManifestTime("theme: Dark", DateTimeOffset.UtcNow.AddHours(1));

        Assert.Equal(
            _fingerprint.ComputeFromPackage(early, includeSecrets: false),
            _fingerprint.ComputeFromPackage(late, includeSecrets: false));
    }

    private static byte[] CreatePackageWithManifestTime(string appYamlBody, DateTimeOffset createdAtUtc)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var manifest = $$"""{"schemaVersion":1,"appId":"SalmonEgg","createdAtUtc":"{{createdAtUtc:O}}","files":["app.yaml"]}""";
            WriteEntry(archive, "manifest.json", manifest);
            WriteEntry(archive, "files/config/app.yaml", $"schema_version: 2{Environment.NewLine}{appYamlBody}{Environment.NewLine}");
        }

        return stream.ToArray();
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }
}
