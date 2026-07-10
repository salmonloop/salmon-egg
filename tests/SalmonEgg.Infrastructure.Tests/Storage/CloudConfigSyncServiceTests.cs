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

        var result = await service.SyncNowAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CloudConfigSyncStatus.Uploaded, result.Status);
        Assert.NotNull(provider.UploadedContent);
        AssertZipContains(provider.UploadedContent!, "secrets.json");
        AssertZipContains(provider.UploadedContent!, "files/config/app.yaml");
    }

    [Fact]
    public async Task SyncNowAsync_WhenRemoteExistsWithoutState_RestoresRemoteAndBacksUpLocalConfig()
    {
        await SaveEnabledSettingsAsync();
        await File.WriteAllTextAsync(Path.Combine(_appData.ConfigRootPath, "local-only.yaml"), "value: local", TestContext.Current.CancellationToken);
        var remotePackage = CreateRemotePackage("theme: Dark");
        var provider = new FakeProvider
        {
            Remote = new CloudConfigRemoteFile(remotePackage, "remote-etag", DateTimeOffset.UtcNow)
        };
        var service = CreateService(provider);

        var result = await service.SyncNowAsync(TestContext.Current.CancellationToken);
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
        }, TestContext.Current.CancellationToken);
        var remotePackage = CreateRemotePackage("theme: Dark");
        var provider = new FakeProvider
        {
            Remote = new CloudConfigRemoteFile(remotePackage, "old-etag", DateTimeOffset.UtcNow),
            UploadResult = CloudConfigUploadResult.PreconditionFailed()
        };
        var service = CreateService(provider);

        var result = await service.SyncNowAsync(TestContext.Current.CancellationToken);
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

        await Assert.ThrowsAsync<InvalidDataException>(() => _packageService.RestorePackageAsync(stream.ToArray(), TestContext.Current.CancellationToken));
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

        await packageService.RestorePackageAsync(CreateRemotePackage("theme: Dark"), TestContext.Current.CancellationToken);

        Assert.Equal(1, persistence.LoadCount);
        Assert.Equal(1, persistence.FlushCount);
    }

    [Fact]
    public async Task ConfigureProviderAsync_WhenProviderIsConfigurable_PersistsOnlySanitizedOptions()
    {
        var provider = new ConfigurableFakeProvider();
        var service = CreateService(provider);

        var result = await service.ConfigureProviderAsync(
            "webdav",
            new Dictionary<string, string>
            {
                [" file_url "] = " https://dav.example.test/salmonegg-config.zip ",
                ["username"] = " alice "
            },
            new Dictionary<string, string>
            {
                ["password"] = "app-password"
            }, TestContext.Current.CancellationToken);
        var settings = await _appSettings.LoadAsync();

        Assert.Equal(CloudConfigSyncStatus.Disabled, result.Status);
        Assert.Equal("webdav", result.ProviderId);
        Assert.Equal("https://dav.example.test/salmonegg-config.zip", settings.CloudConfigSync.ProviderOptions["webdav"]["file_url"]);
        Assert.Equal("alice", settings.CloudConfigSync.ProviderOptions["webdav"]["username"]);
        Assert.Equal("app-password", provider.Secrets["password"]);
        Assert.DoesNotContain("password", settings.CloudConfigSync.ProviderOptions["webdav"].Keys);
    }

    [Fact]
    public async Task ConfigureProviderAsync_WhenS3ProviderIsConfigurable_DoesNotPersistAccessSecrets()
    {
        var provider = new ConfigurableFakeProvider("s3", "S3 compatible");
        var service = CreateService(provider);

        var result = await service.ConfigureProviderAsync(
            "s3",
            new Dictionary<string, string>
            {
                ["endpoint"] = " https://s3.example.test ",
                ["bucket"] = " salmonegg ",
                ["region"] = " auto ",
                ["object_key"] = " config-sync/salmonegg-config.zip ",
                ["force_path_style"] = " true "
            },
            new Dictionary<string, string>
            {
                ["access_key_id"] = "access-key",
                ["secret_access_key"] = "secret-key"
            }, TestContext.Current.CancellationToken);
        var settings = await _appSettings.LoadAsync();

        Assert.Equal(CloudConfigSyncStatus.Disabled, result.Status);
        Assert.Equal("s3", result.ProviderId);
        Assert.Equal("https://s3.example.test", settings.CloudConfigSync.ProviderOptions["s3"]["endpoint"]);
        Assert.Equal("salmonegg", settings.CloudConfigSync.ProviderOptions["s3"]["bucket"]);
        Assert.Equal("access-key", provider.Secrets["access_key_id"]);
        Assert.Equal("secret-key", provider.Secrets["secret_access_key"]);
        Assert.DoesNotContain("access_key_id", settings.CloudConfigSync.ProviderOptions["s3"].Keys);
        Assert.DoesNotContain("secret_access_key", settings.CloudConfigSync.ProviderOptions["s3"].Keys);
    }

    [Fact]
    public async Task ConfigureProviderAsync_WhenOptionsAndSecretsAreNull_PassesEmptyDictionaries()
    {
        var provider = new ConfigurableFakeProvider("s3", "S3 compatible");
        var service = CreateService(provider);

        var result = await service.ConfigureProviderAsync("s3", null!, null!, TestContext.Current.CancellationToken);
        var settings = await _appSettings.LoadAsync();

        Assert.Equal(CloudConfigSyncStatus.Disabled, result.Status);
        Assert.Empty(provider.Secrets);
        Assert.Empty(settings.CloudConfigSync.ProviderOptions["s3"]);
    }

    [Fact]
    public async Task AuthorizeAndSyncAsync_WhenProviderChanges_SignsOutPreviousProvider()
    {
        await SaveEnabledSettingsAsync();
        var previousProvider = new ConfigurableFakeProvider("onedrive", "OneDrive");
        var nextProvider = new ConfigurableFakeProvider("s3", "S3 compatible");
        var service = CreateService(previousProvider, nextProvider);

        var result = await service.AuthorizeAndSyncAsync("s3", TestContext.Current.CancellationToken);
        var settings = await _appSettings.LoadAsync();

        Assert.Equal(CloudConfigSyncStatus.Uploaded, result.Status);
        Assert.Equal("s3", settings.CloudConfigSync.ProviderId);
        Assert.Equal(1, previousProvider.SignOutCount);
        Assert.Equal(0, nextProvider.SignOutCount);
    }

    [Fact]
    public async Task AuthorizeAndSyncAsync_WhenProviderDoesNotChange_DoesNotSignOutCurrentProvider()
    {
        await SaveEnabledSettingsAsync();
        var provider = new ConfigurableFakeProvider("onedrive", "OneDrive");
        var service = CreateService(provider);

        await service.AuthorizeAndSyncAsync("onedrive", TestContext.Current.CancellationToken);

        Assert.Equal(0, provider.SignOutCount);
    }

    private CloudConfigSyncService CreateService(ICloudConfigStorageProvider provider)
        => CreateService([provider]);

    private CloudConfigSyncService CreateService(params ICloudConfigStorageProvider[] providers)
        => new(
            _appSettings,
            providers,
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

    private sealed class ConfigurableFakeProvider : IConfigurableCloudConfigStorageProvider
    {
        public ConfigurableFakeProvider(string providerId = "webdav", string displayName = "WebDAV")
        {
            Descriptor = new CloudConfigProviderDescriptor(providerId, displayName, true);
        }

        public Dictionary<string, string> Secrets { get; } = new(StringComparer.OrdinalIgnoreCase);

        public int SignOutCount { get; private set; }

        public CloudConfigProviderDescriptor Descriptor { get; }

        public Task<CloudConfigProviderConfigurationResult> ConfigureAsync(
            IReadOnlyDictionary<string, string> options,
            IReadOnlyDictionary<string, string> secrets,
            CancellationToken cancellationToken = default)
        {
            foreach (var secret in secrets)
            {
                Secrets[secret.Key] = secret.Value;
            }

            return Task.FromResult(CloudConfigProviderConfigurationResult.Success());
        }

        public Task<CloudConfigProviderConfigurationStatus> GetConfigurationStatusAsync(
            IReadOnlyDictionary<string, string> options,
            CancellationToken cancellationToken = default)
            => Task.FromResult(CloudConfigProviderConfigurationStatus.NotRequired());

        public Task<CloudConfigAuthorizationResult> EnsureAuthorizedAsync(bool interactive, CancellationToken cancellationToken = default)
            => Task.FromResult(CloudConfigAuthorizationResult.Success());

        public Task SignOutAsync(CancellationToken cancellationToken = default)
        {
            SignOutCount++;
            return Task.CompletedTask;
        }

        public Task<CloudConfigRemoteFile?> TryDownloadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<CloudConfigRemoteFile?>(null);

        public Task<CloudConfigUploadResult> UploadAsync(byte[] content, string? expectedETag, CancellationToken cancellationToken = default)
            => Task.FromResult(CloudConfigUploadResult.Uploaded("etag"));
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
