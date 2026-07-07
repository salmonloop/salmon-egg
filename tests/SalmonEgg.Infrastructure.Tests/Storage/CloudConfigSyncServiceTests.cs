using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Infrastructure.Storage;

namespace SalmonEgg.Infrastructure.Tests.Storage;

public sealed class CloudConfigSyncServiceTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly AppDataService _appData;
    private readonly IAppFileStore _fileStore;
    private readonly ConfigChangeSignal _configChangeSignal;
    private readonly AppSettingsService _appSettings;
    private readonly PlainTextFileSecureStorage _secureStorage;
    private readonly ConfigSyncPackageService _packageService;

    public CloudConfigSyncServiceTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "SalmonEggCloudSyncTests", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("SALMONEGG_APPDATA_ROOT", Path.Combine(_testDirectory, "SalmonEgg"), EnvironmentVariableTarget.Process);
        _appData = new AppDataService();
        _configChangeSignal = new ConfigChangeSignal();
        _fileStore = new FileSystemAppFileStore(new NoOpFileSystemPersistence(), _configChangeSignal);
        _appSettings = new AppSettingsService(_fileStore, _appData, NullLogger<AppSettingsService>.Instance);
        _secureStorage = new PlainTextFileSecureStorage(_fileStore, _appData);
        _packageService = new ConfigSyncPackageService(
            _appData,
            new ConfigurationSecretSnapshotService(_secureStorage, _fileStore, _appData),
            _configChangeSignal,
            new NoOpFileSystemPersistence());
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
    public async Task SyncNowAsync_WhenRemoteMissing_UploadsLocalPackageWithSecrets()
    {
        await SaveEnabledSettingsAsync();
        await SeedServerProfileAsync("profile-a", "secret-token");
        var provider = new FakeProvider { Remote = null };
        var service = CreateService(provider);

        var result = await service.SyncNowAsync();

        Assert.Equal(CloudConfigSyncStatus.Uploaded, result.Status);
        Assert.NotNull(provider.UploadedContent);
        AssertZipContains(provider.UploadedContent!, "secrets.json");
        AssertZipContains(provider.UploadedContent!, "files/config/app.yaml");
    }

    [Fact]
    public async Task SyncNowAsync_WhenRemoteExistsWithoutState_RestoresRemoteAndBacksUpLocalConfig()
    {
        await SaveEnabledSettingsAsync();
        await File.WriteAllTextAsync(Path.Combine(_appData.ConfigRootPath, "local-only.yaml"), "value: local");
        var remotePackage = CreateRemotePackage("theme: Dark");
        var provider = new FakeProvider
        {
            Remote = new CloudConfigRemoteFile(remotePackage, "remote-etag", DateTimeOffset.UtcNow)
        };
        var service = CreateService(provider);

        var result = await service.SyncNowAsync();
        var restored = await _appSettings.LoadAsync();

        Assert.Equal(CloudConfigSyncStatus.Restored, result.Status);
        Assert.Equal("Dark", restored.Theme);
        Assert.NotNull(result.BackupPath);
        Assert.True(File.Exists(Path.Combine(result.BackupPath!, "local-only.yaml")));
    }

    [Fact]
    public async Task SyncNowAsync_WhenUploadPreconditionFails_AppliesRemotePackage()
    {
        await SaveEnabledSettingsAsync();
        await new CloudConfigSyncStateStore(_fileStore, _appData).SaveAsync(new CloudConfigSyncState
        {
            ProviderId = "onedrive",
            RemoteETag = "old-etag"
        });
        var remotePackage = CreateRemotePackage("theme: Dark");
        var provider = new FakeProvider
        {
            Remote = new CloudConfigRemoteFile(remotePackage, "old-etag", DateTimeOffset.UtcNow),
            UploadResult = CloudConfigUploadResult.PreconditionFailed()
        };
        var service = CreateService(provider);

        var result = await service.SyncNowAsync();
        var restored = await _appSettings.LoadAsync();

        Assert.Equal(CloudConfigSyncStatus.ConflictRemoteApplied, result.Status);
        Assert.Equal("Dark", restored.Theme);
    }

    [Fact]
    public async Task RestorePackageAsync_RejectsTraversalEntries()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            archive.CreateEntry("manifest.json");
            archive.CreateEntry("files/config/../escape.yaml");
        }

        await Assert.ThrowsAsync<InvalidDataException>(() => _packageService.RestorePackageAsync(stream.ToArray()));
    }

    [Fact]
    public async Task RestorePackageAsync_LoadsAndFlushesFileSystemPersistence()
    {
        var persistence = new RecordingFileSystemPersistence();
        var packageService = new ConfigSyncPackageService(
            _appData,
            new ConfigurationSecretSnapshotService(_secureStorage, _fileStore, _appData),
            _configChangeSignal,
            persistence);

        await packageService.RestorePackageAsync(CreateRemotePackage("theme: Dark"));

        Assert.Equal(1, persistence.LoadCount);
        Assert.Equal(1, persistence.FlushCount);
    }

    private CloudConfigSyncService CreateService(FakeProvider provider)
        => new(
            _appSettings,
            new[] { provider },
            _packageService,
            new CloudConfigSyncStateStore(_fileStore, _appData),
            _configChangeSignal,
            _appData,
            NullLogger<CloudConfigSyncService>.Instance);

    private async Task SaveEnabledSettingsAsync()
    {
        await _appSettings.SaveAsync(new AppSettings
        {
            CloudConfigSync = new CloudConfigSyncSettings
            {
                Enabled = true,
                ProviderId = "onedrive",
                IncludeSecrets = true
            }
        });
    }

    private async Task SeedServerProfileAsync(string profileId, string token)
    {
        var configuration = new ConfigurationManager(
            _secureStorage,
            _fileStore,
            _appData,
            NullLogger<ConfigurationManager>.Instance);
        await configuration.SaveConfigurationAsync(new ServerConfiguration
        {
            Id = profileId,
            Name = profileId,
            ServerUrl = "wss://example.test/acp",
            Authentication = new AuthenticationConfig { Token = token }
        });
    }

    private static byte[] CreateRemotePackage(string appYaml)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "manifest.json", """{"schemaVersion":1,"appId":"SalmonEgg","files":["app.yaml"]}""");
            WriteEntry(archive, "files/config/app.yaml", $"schema_version: 2{Environment.NewLine}{appYaml}{Environment.NewLine}");
        }

        return stream.ToArray();
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    private static void AssertZipContains(byte[] content, string entryName)
    {
        using var stream = new MemoryStream(content, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        Assert.NotNull(archive.GetEntry(entryName));
    }

    private sealed class FakeProvider : ICloudConfigStorageProvider
    {
        public CloudConfigRemoteFile? Remote { get; set; }

        public byte[]? UploadedContent { get; private set; }

        public CloudConfigUploadResult UploadResult { get; set; } = CloudConfigUploadResult.Uploaded("new-etag");

        public CloudConfigProviderDescriptor Descriptor { get; } = new("onedrive", "OneDrive", true);

        public Task<CloudConfigAuthorizationResult> EnsureAuthorizedAsync(bool interactive, CancellationToken cancellationToken = default)
            => Task.FromResult(CloudConfigAuthorizationResult.Success());

        public Task SignOutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<CloudConfigRemoteFile?> TryDownloadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Remote);

        public Task<CloudConfigUploadResult> UploadAsync(byte[] content, string? expectedETag, CancellationToken cancellationToken = default)
        {
            UploadedContent = content;
            return Task.FromResult(UploadResult);
        }
    }

    private sealed class RecordingFileSystemPersistence : IFileSystemPersistence
    {
        public int LoadCount { get; private set; }

        public int FlushCount { get; private set; }

        public Task LoadAsync(CancellationToken cancellationToken = default)
        {
            LoadCount++;
            return Task.CompletedTask;
        }

        public Task FlushAsync(CancellationToken cancellationToken = default)
        {
            FlushCount++;
            return Task.CompletedTask;
        }
    }
}
