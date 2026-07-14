using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Infrastructure.Storage;

namespace SalmonEgg.Infrastructure.Tests.Storage;

public sealed class CloudConfigSyncCoordinatorTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly AppDataService _appData;
    private readonly IAppFileStore _fileStore;
    private readonly ConfigChangeSignal _configChangeSignal;
    private readonly AppSettingsService _appSettings;
    private readonly PlainTextFileSecureStorage _secureStorage;
    private readonly ConfigSyncPackageService _packageService;

    public CloudConfigSyncCoordinatorTests()
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
    public async Task InitializeAsync_WithStoredCredential_ReportsAvailableAndReady()
    {
        await SaveEnabledSettingsAsync("webdav", new Dictionary<string, string> { ["file_url"] = "https://dav.example.test/config.zip" });
        var provider = new FakeProvider("webdav")
        {
            CredentialInspection = new CloudCredentialInspection(CloudCredentialState.Available)
        };
        using var coordinator = CreateCoordinator(provider);

        await coordinator.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CloudSyncInitializationState.Ready, coordinator.Current.Initialization);
        Assert.Equal(CloudCredentialState.Available, coordinator.Current.Credential);
        Assert.Equal(CloudProviderReadiness.Ready, coordinator.Current.Readiness);
        Assert.False(provider.LastSessionWasInteractive);
    }

    [Fact]
    public async Task InitializeAsync_WithMissingCredential_ReportsAuthenticationRequiredWithoutInteractivePrompt()
    {
        await SaveEnabledSettingsAsync("webdav", new Dictionary<string, string> { ["file_url"] = "https://dav.example.test/config.zip" });
        var provider = new FakeProvider("webdav")
        {
            CredentialInspection = new CloudCredentialInspection(CloudCredentialState.Missing)
        };
        using var coordinator = CreateCoordinator(provider);

        await coordinator.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CloudCredentialState.Missing, coordinator.Current.Credential);
        Assert.Equal(CloudProviderReadiness.AuthenticationRequired, coordinator.Current.Readiness);
        Assert.Equal(0, provider.CreateSessionCount);
    }

    [Fact]
    public async Task InitializeAsync_WhenCredentialInspectionFaults_PreservesFaultedCredentialState()
    {
        await SaveEnabledSettingsAsync("webdav", new Dictionary<string, string> { ["file_url"] = "https://dav.example.test/config.zip" });
        var provider = new FakeProvider("webdav")
        {
            CredentialInspection = new CloudCredentialInspection(
                CloudCredentialState.Faulted,
                new CloudSyncFailure(CloudSyncFailureKind.Unknown, "Inspection failed."))
        };
        using var coordinator = CreateCoordinator(provider);

        await coordinator.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CloudCredentialState.Faulted, coordinator.Current.Credential);
        Assert.Equal(CloudProviderReadiness.Faulted, coordinator.Current.Readiness);
    }

    [Fact]
    public async Task InitializeAsync_WhenTransferHistoryBelongsToAnotherProvider_DoesNotProjectHistory()
    {
        await SaveEnabledSettingsAsync(
            "webdav",
            new Dictionary<string, string> { ["file_url"] = "https://dav.example.test/config.zip" });
        await new CloudConfigSyncStateStore(_fileStore, _appData).SaveAsync(new CloudConfigSyncState
        {
            ProviderId = "s3",
            RemoteETag = "s3-etag",
            LastSyncUtc = DateTimeOffset.UtcNow.ToString("O")
        }, TestContext.Current.CancellationToken);
        var provider = new FakeProvider("webdav");
        using var coordinator = CreateCoordinator(provider);

        await coordinator.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal("webdav", coordinator.Current.Configuration.ProviderId);
        Assert.Null(coordinator.Current.Transfer.LastSuccess);
    }

    [Fact]
    public async Task InitializeAsync_WithoutConfiguredProvider_DoesNotProjectOwnerlessHistory()
    {
        await new CloudConfigSyncStateStore(_fileStore, _appData).SaveAsync(new CloudConfigSyncState
        {
            ProviderId = string.Empty,
            RemoteETag = "legacy-etag",
            LastSyncUtc = DateTimeOffset.UtcNow.ToString("O")
        }, TestContext.Current.CancellationToken);
        using var coordinator = CreateCoordinator(new FakeProvider("webdav"));

        await coordinator.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Empty(coordinator.Current.Configuration.ProviderId);
        Assert.Null(coordinator.Current.Transfer.LastSuccess);
    }

    [Fact]
    public async Task InitializeAsync_WhenNewProviderConfigurationFails_DropsPreviousProviderHistory()
    {
        await SaveEnabledSettingsAsync(
            "webdav",
            new Dictionary<string, string> { ["file_url"] = "https://dav.example.test/config.zip" });
        await new CloudConfigSyncStateStore(_fileStore, _appData).SaveAsync(new CloudConfigSyncState
        {
            ProviderId = "webdav",
            RemoteETag = "webdav-etag",
            LastSyncUtc = DateTimeOffset.UtcNow.ToString("O")
        }, TestContext.Current.CancellationToken);
        var webDav = new FakeProvider("webdav");
        var s3 = new FakeProvider("s3")
        {
            Validation = CloudProviderValidationResult.Failed("Invalid S3 configuration.")
        };
        using var coordinator = CreateCoordinator(webDav, s3);
        await coordinator.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(coordinator.Current.Transfer.LastSuccess);
        var settings = await _appSettings.LoadAsync();
        settings.CloudConfigSync.ProviderId = "s3";
        settings.CloudConfigSync.ProviderOptions["s3"] = new Dictionary<string, string>();
        await _appSettings.SaveAsync(settings);

        await coordinator.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal("s3", coordinator.Current.Configuration.ProviderId);
        Assert.Equal(CloudProviderReadiness.NeedsConfiguration, coordinator.Current.Readiness);
        Assert.Null(coordinator.Current.Transfer.LastSuccess);
    }

    [Fact]
    public async Task SyncNowAsync_WhenRemoteMissing_UploadsLocalPackageWithSecrets()
    {
        await SaveEnabledSettingsAsync();
        await SeedServerProfileAsync("profile-a", "secret-token");
        var provider = new FakeProvider();
        using var coordinator = CreateCoordinator(provider);

        await coordinator.SyncNowAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CloudTransferPhase.Succeeded, coordinator.Current.Transfer.Phase);
        Assert.Equal(CloudTransferOutcome.Uploaded, coordinator.Current.Transfer.LastSuccess?.Outcome);
        Assert.NotNull(provider.Session.UploadedContent);
        AssertZipContains(provider.Session.UploadedContent!, "secrets.json");
        AssertZipContains(provider.Session.UploadedContent!, "files/config/app.yaml");
    }

    [Fact]
    public async Task SyncNowAsync_WhenRemoteExistsWithoutState_RestoresRemoteAndBacksUpLocalConfig()
    {
        await SaveEnabledSettingsAsync();
        await File.WriteAllTextAsync(Path.Combine(_appData.ConfigRootPath, "local-only.yaml"), "value: local", TestContext.Current.CancellationToken);
        var provider = new FakeProvider
        {
            Session = { Remote = new CloudConfigRemoteFile(CreateRemotePackage("theme: Dark"), "remote-etag", DateTimeOffset.UtcNow) }
        };
        using var coordinator = CreateCoordinator(provider);

        await coordinator.SyncNowAsync(TestContext.Current.CancellationToken);
        var restored = await _appSettings.LoadAsync();

        Assert.Equal(CloudTransferOutcome.Restored, coordinator.Current.Transfer.LastSuccess?.Outcome);
        Assert.Equal("Dark", restored.Theme);
        var backupPath = coordinator.Current.Transfer.LastSuccess?.BackupPath;
        Assert.NotNull(backupPath);
        Assert.True(File.Exists(Path.Combine(backupPath!, "local-only.yaml")));
    }

    [Fact]
    public async Task SyncNowAsync_WhenRemoteRestoresCloudConfiguration_PublishesRestoredConfiguration()
    {
        await SaveEnabledSettingsAsync(
            "webdav",
            new Dictionary<string, string> { ["file_url"] = "https://dav.example.test/old.zip" });
        var remoteAppYaml = string.Join(
            Environment.NewLine,
            "cloud_config_sync:",
            "  enabled: true",
            "  provider_id: webdav",
            "  revision: 42",
            "  include_secrets: true",
            "  provider_options:",
            "    webdav:",
            "      file_url: https://dav.example.test/restored.zip");
        var provider = new FakeProvider("webdav")
        {
            Session = { Remote = new CloudConfigRemoteFile(CreateRemotePackage(remoteAppYaml), "remote-etag", DateTimeOffset.UtcNow) }
        };
        using var coordinator = CreateCoordinator(provider);

        await coordinator.SyncNowAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CloudTransferOutcome.Restored, coordinator.Current.Transfer.LastSuccess?.Outcome);
        Assert.Equal("webdav", coordinator.Current.Configuration.ProviderId);
        Assert.Equal(42, coordinator.Current.Configuration.Revision);
        Assert.Equal("https://dav.example.test/restored.zip", coordinator.Current.Configuration.Options["file_url"]);
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
        var provider = new FakeProvider
        {
            Session =
            {
                Remote = new CloudConfigRemoteFile(CreateRemotePackage("theme: Dark"), "old-etag", DateTimeOffset.UtcNow),
                UploadResult = CloudConfigUploadResult.PreconditionFailed("Remote changed.")
            }
        };
        using var coordinator = CreateCoordinator(provider);

        await coordinator.SyncNowAsync(TestContext.Current.CancellationToken);
        var restored = await _appSettings.LoadAsync();

        Assert.Equal(CloudTransferOutcome.ConflictRemoteApplied, coordinator.Current.Transfer.LastSuccess?.Outcome);
        Assert.Equal("Dark", restored.Theme);
    }

    [Fact]
    public async Task ApplyAndActivateAsync_CommitsSecretsOnlyAfterRemoteValidationSucceeds()
    {
        var provider = new FakeProvider("webdav");
        using var coordinator = CreateCoordinator(provider);
        var draft = new CloudProviderDraft(
            "webdav",
            new Dictionary<string, string> { [" file_url "] = " https://dav.example.test/config.zip " },
            new Dictionary<string, CloudSecretUpdate> { ["password"] = CloudSecretUpdate.Replace("app-password") });

        await coordinator.ApplyAndActivateAsync(draft, TestContext.Current.CancellationToken);
        var settings = await _appSettings.LoadAsync();

        Assert.Equal(1, provider.CommitSecretsCount);
        Assert.Equal("app-password", provider.CommittedSecrets["password"].Value);
        Assert.Equal("https://dav.example.test/config.zip", settings.CloudConfigSync.ProviderOptions["webdav"]["file_url"]);
        Assert.DoesNotContain("password", settings.CloudConfigSync.ProviderOptions["webdav"].Keys);
        Assert.True(settings.CloudConfigSync.Enabled);
    }

    [Fact]
    public async Task ApplyAndActivateAsync_WhenRemoteMissing_UploadsCandidateConfigurationAndSecret()
    {
        var provider = new FakeProvider("webdav");
        using var coordinator = CreateCoordinator(provider);
        var draft = new CloudProviderDraft(
            "webdav",
            new Dictionary<string, string> { ["file_url"] = "https://dav.example.test/new-folder/" },
            new Dictionary<string, CloudSecretUpdate> { ["password"] = CloudSecretUpdate.Replace("new-password") });

        await coordinator.ApplyAndActivateAsync(draft, TestContext.Current.CancellationToken);

        Assert.NotNull(provider.Session.UploadedContent);
        var appYaml = ReadZipEntry(provider.Session.UploadedContent!, "files/config/app.yaml");
        Assert.Contains("https://dav.example.test/new-folder/", appYaml, StringComparison.Ordinal);
        using var secrets = JsonDocument.Parse(ReadZipEntry(provider.Session.UploadedContent!, "secrets.json"));
        var cloudCredential = Assert.Single(secrets.RootElement.GetProperty("entries").EnumerateArray(), entry =>
            entry.GetProperty("profileId").GetString() == "cloud-provider/webdav" &&
            entry.GetProperty("kind").GetString() == "password");
        Assert.Equal("new-password", cloudCredential.GetProperty("value").GetString());
    }

    [Fact]
    public async Task ApplyAndActivateAsync_WhenRemoteSettingsAreRestored_PreservesRestoredValuesDuringCommit()
    {
        var provider = new FakeProvider("webdav")
        {
            Session =
            {
                Remote = new CloudConfigRemoteFile(CreateRemotePackage("theme: Dark"), "remote-etag", DateTimeOffset.UtcNow)
            }
        };
        using var coordinator = CreateCoordinator(provider);

        await coordinator.ApplyAndActivateAsync(
            new CloudProviderDraft(
                "webdav",
                new Dictionary<string, string> { ["file_url"] = "https://dav.example.test/config.zip" },
                new Dictionary<string, CloudSecretUpdate>()),
            TestContext.Current.CancellationToken);
        var settings = await _appSettings.LoadAsync();

        Assert.Equal("Dark", settings.Theme);
        Assert.True(settings.CloudConfigSync.Enabled);
        Assert.Equal("webdav", settings.CloudConfigSync.ProviderId);
    }

    [Fact]
    public async Task ApplyAndActivateAsync_WhenRemoteRestoreCanReplaceSecrets_CommitsFrozenCandidateCredential()
    {
        var provider = new FakeProvider("webdav")
        {
            ResolvedSecrets = new Dictionary<string, CloudSecretUpdate>(StringComparer.OrdinalIgnoreCase)
            {
                ["password"] = CloudSecretUpdate.Replace("verified-local-password")
            },
            Session =
            {
                Remote = new CloudConfigRemoteFile(CreateRemotePackage("theme: Dark"), "remote-etag", DateTimeOffset.UtcNow)
            }
        };
        using var coordinator = CreateCoordinator(provider);

        await coordinator.ApplyAndActivateAsync(
            new CloudProviderDraft(
                "webdav",
                new Dictionary<string, string> { ["file_url"] = "https://dav.example.test/config.zip" },
                new Dictionary<string, CloudSecretUpdate> { ["password"] = CloudSecretUpdate.KeepExisting() }),
            TestContext.Current.CancellationToken);

        Assert.Equal(1, provider.ResolveSecretsCount);
        Assert.Equal(CloudSecretUpdateKind.Replace, provider.CommittedSecrets["password"].Kind);
        Assert.Equal("verified-local-password", provider.CommittedSecrets["password"].Value);
    }

    [Fact]
    public async Task ApplyAndActivateAsync_WhenRemoteValidationFails_DoesNotPersistConfigurationOrSecrets()
    {
        var provider = new FakeProvider("webdav")
        {
            SessionResult = CloudProviderSessionResult.Failed(
                CloudCredentialState.Missing,
                new CloudSyncFailure(CloudSyncFailureKind.Authentication, "Rejected."))
        };
        using var coordinator = CreateCoordinator(provider);

        await coordinator.ApplyAndActivateAsync(
            new CloudProviderDraft(
                "webdav",
                new Dictionary<string, string> { ["file_url"] = "https://dav.example.test/config.zip" },
                new Dictionary<string, CloudSecretUpdate> { ["password"] = CloudSecretUpdate.Replace("wrong") }),
            TestContext.Current.CancellationToken);
        var settings = await _appSettings.LoadAsync();

        Assert.Equal(0, provider.CommitSecretsCount);
        Assert.False(settings.CloudConfigSync.Enabled);
        Assert.Equal(CloudProviderReadiness.AuthenticationRequired, coordinator.Current.Readiness);
    }

    [Fact]
    public async Task DisableAsync_KeepsProviderOptionsAndCredentials()
    {
        await SaveEnabledSettingsAsync("webdav", new Dictionary<string, string> { ["file_url"] = "https://dav.example.test/config.zip" });
        var provider = new FakeProvider("webdav");
        using var coordinator = CreateCoordinator(provider);

        await coordinator.DisableAsync(TestContext.Current.CancellationToken);
        var settings = await _appSettings.LoadAsync();

        Assert.False(settings.CloudConfigSync.Enabled);
        Assert.True(settings.CloudConfigSync.ProviderOptions.ContainsKey("webdav"));
        Assert.Equal(0, provider.ForgetCredentialsCount);
    }

    [Fact]
    public async Task ForgetProviderAsync_RemovesOnlyRequestedProviderConfigurationAndCredential()
    {
        await SaveEnabledSettingsAsync("webdav", new Dictionary<string, string> { ["file_url"] = "https://dav.example.test/config.zip" });
        var settings = await _appSettings.LoadAsync();
        settings.CloudConfigSync.ProviderOptions["s3"] = new Dictionary<string, string> { ["bucket"] = "other" };
        await _appSettings.SaveAsync(settings);
        var webDav = new FakeProvider("webdav");
        var s3 = new FakeProvider("s3");
        using var coordinator = CreateCoordinator(webDav, s3);

        await coordinator.ForgetProviderAsync("webdav", TestContext.Current.CancellationToken);
        settings = await _appSettings.LoadAsync();

        Assert.Equal(1, webDav.ForgetCredentialsCount);
        Assert.Equal(0, s3.ForgetCredentialsCount);
        Assert.False(settings.CloudConfigSync.ProviderOptions.ContainsKey("webdav"));
        Assert.True(settings.CloudConfigSync.ProviderOptions.ContainsKey("s3"));
        Assert.False(settings.CloudConfigSync.Enabled);
    }

    [Fact]
    public async Task ForgetProviderAsync_WhenProviderIsInactive_PreservesActiveSnapshot()
    {
        await SaveEnabledSettingsAsync();
        var active = new FakeProvider("onedrive");
        var inactive = new FakeProvider("s3");
        using var coordinator = CreateCoordinator(active, inactive);
        await coordinator.InitializeAsync(TestContext.Current.CancellationToken);

        await coordinator.ForgetProviderAsync("s3", TestContext.Current.CancellationToken);

        Assert.Equal("onedrive", coordinator.Current.Configuration.ProviderId);
        Assert.Equal(CloudCredentialState.Available, coordinator.Current.Credential);
        Assert.Equal(CloudProviderReadiness.Ready, coordinator.Current.Readiness);
    }

    [Fact]
    public async Task ForgetProviderAsync_WhenActiveProviderHasHistory_ClearsPublishedTransfer()
    {
        await SaveEnabledSettingsAsync(
            "webdav",
            new Dictionary<string, string> { ["file_url"] = "https://dav.example.test/config.zip" });
        await new CloudConfigSyncStateStore(_fileStore, _appData).SaveAsync(new CloudConfigSyncState
        {
            ProviderId = "webdav",
            RemoteETag = "webdav-etag",
            LastSyncUtc = DateTimeOffset.UtcNow.ToString("O")
        }, TestContext.Current.CancellationToken);
        var provider = new FakeProvider("webdav");
        using var coordinator = CreateCoordinator(provider);
        await coordinator.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(coordinator.Current.Transfer.LastSuccess);

        await coordinator.ForgetProviderAsync("webdav", TestContext.Current.CancellationToken);

        Assert.Equal(CloudTransferPhase.Idle, coordinator.Current.Transfer.Phase);
        Assert.Null(coordinator.Current.Transfer.LastSuccess);
    }

    [Fact]
    public async Task DisableAsync_CancelsInFlightSyncBeforeItCanUpload()
    {
        await SaveEnabledSettingsAsync();
        var provider = new FakeProvider();
        provider.Session.BlockDownload = true;
        using var coordinator = CreateCoordinator(provider);

        var syncTask = coordinator.SyncNowAsync(TestContext.Current.CancellationToken);
        await provider.Session.DownloadStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        await coordinator.DisableAsync(TestContext.Current.CancellationToken);
        await syncTask;

        Assert.False(coordinator.Current.Configuration.Enabled);
        Assert.Null(provider.Session.UploadedContent);
        Assert.Equal(CloudProviderReadiness.Disabled, coordinator.Current.Readiness);
    }

    [Fact]
    public async Task DisableAsync_WhenProviderReturnsAfterCancellation_PreventsStaleApplyCommit()
    {
        var provider = new FakeProvider("webdav");
        provider.Session.BlockDownload = true;
        provider.Session.IgnoreCancellation = true;
        using var coordinator = CreateCoordinator(provider);
        var applyTask = coordinator.ApplyAndActivateAsync(
            new CloudProviderDraft(
                "webdav",
                new Dictionary<string, string> { ["file_url"] = "https://dav.example.test/config/" },
                new Dictionary<string, CloudSecretUpdate> { ["password"] = CloudSecretUpdate.Replace("stale") }),
            TestContext.Current.CancellationToken);
        await provider.Session.DownloadStarted.Task.WaitAsync(TestContext.Current.CancellationToken);

        var disableTask = coordinator.DisableAsync(TestContext.Current.CancellationToken);
        provider.Session.ReleaseDownload.TrySetResult();
        await Task.WhenAll(applyTask, disableTask);
        var settings = await _appSettings.LoadAsync();

        Assert.False(settings.CloudConfigSync.Enabled);
        Assert.Equal(0, provider.CommitSecretsCount);
        Assert.Null(provider.Session.UploadedContent);
    }

    [Fact]
    public async Task DisableAsync_CancelsPendingAutoSyncBeforeDebounceCompletes()
    {
        await SaveEnabledSettingsAsync();
        var provider = new FakeProvider();
        using var coordinator = CreateCoordinator(provider);
        await coordinator.InitializeAsync(TestContext.Current.CancellationToken);

        _configChangeSignal.NotifyChanged(
            Path.Combine(_appData.ConfigRootPath, "changed.yaml"),
            ConfigChangeKind.Written);
        await coordinator.DisableAsync(TestContext.Current.CancellationToken);
        await Task.Delay(TimeSpan.FromMilliseconds(2200), TestContext.Current.CancellationToken);

        Assert.Equal(0, provider.CreateSessionCount);
        Assert.False(coordinator.Current.Configuration.Enabled);
        Assert.Equal(CloudProviderReadiness.Disabled, coordinator.Current.Readiness);
    }

    [Fact]
    public async Task ApplyAndActivateAsync_WhenProviderChanges_DoesNotForgetPreviousProviderCredential()
    {
        await SaveEnabledSettingsAsync();
        var previous = new FakeProvider("onedrive");
        var next = new FakeProvider("s3");
        using var coordinator = CreateCoordinator(previous, next);

        await coordinator.ApplyAndActivateAsync(
            new CloudProviderDraft("s3", new Dictionary<string, string>(), new Dictionary<string, CloudSecretUpdate>()),
            TestContext.Current.CancellationToken);

        Assert.Equal(0, previous.ForgetCredentialsCount);
        Assert.Equal(0, next.ForgetCredentialsCount);
        Assert.Equal("s3", coordinator.Current.Configuration.ProviderId);
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

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            _packageService.RestorePackageAsync(stream.ToArray(), TestContext.Current.CancellationToken));
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
    public async Task RestorePackageAsync_PublishesSingleRestoredChangeAfterFlush()
    {
        var events = new List<ConfigChangedEventArgs>();
        _configChangeSignal.Changed += (_, args) => events.Add(args);

        await _packageService.RestorePackageAsync(CreateRemotePackage("theme: Dark"), TestContext.Current.CancellationToken);

        var change = Assert.Single(events);
        Assert.Equal(ConfigChangeKind.Restored, change.Kind);
        Assert.Equal(_appData.ConfigRootPath, change.Path);
    }

    [Fact]
    public async Task CloudSecretUpdateTransaction_WhenSecondWriteFails_RestoresAllPreviousValues()
    {
        var storage = new FailingSecureStorage(
            new Dictionary<string, string>
            {
                ["access-key"] = "old-access",
                ["secret-key"] = "old-secret"
            },
            failOnceOnSaveKey: "secret-key");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CloudSecretUpdateTransaction.BeginAsync(
                storage,
                new Dictionary<string, CloudSecretUpdate>
                {
                    ["access-key"] = CloudSecretUpdate.Replace("new-access"),
                    ["secret-key"] = CloudSecretUpdate.Replace("new-secret")
                },
                TestContext.Current.CancellationToken));

        Assert.Equal("old-access", await storage.LoadAsync("access-key"));
        Assert.Equal("old-secret", await storage.LoadAsync("secret-key"));
    }

    private CloudConfigSyncCoordinator CreateCoordinator(params ICloudConfigStorageProvider[] providers) => new(
        _appSettings,
        providers,
        _packageService,
        new CloudConfigSyncStateStore(_fileStore, _appData),
        _configChangeSignal,
        _appData,
        NullLogger<CloudConfigSyncCoordinator>.Instance);

    private async Task SaveEnabledSettingsAsync(
        string providerId = "onedrive",
        IReadOnlyDictionary<string, string>? options = null)
    {
        await _appSettings.SaveAsync(new AppSettings
        {
            CloudConfigSync = new CloudConfigSyncSettings
            {
                Enabled = true,
                ProviderId = providerId,
                Revision = 1,
                IncludeSecrets = true,
                ProviderOptions = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
                {
                    [providerId] = options is null
                        ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        : new Dictionary<string, string>(options, StringComparer.OrdinalIgnoreCase)
                }
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

    private static string ReadZipEntry(byte[] content, string entryName)
    {
        using var stream = new MemoryStream(content, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = archive.GetEntry(entryName);
        Assert.NotNull(entry);
        using var reader = new StreamReader(entry!.Open());
        return reader.ReadToEnd();
    }

    private sealed class FakeProvider : ICloudConfigStorageProvider
    {
        public FakeProvider(string providerId = "onedrive")
        {
            Descriptor = new CloudConfigProviderDescriptor(providerId, providerId, true);
            SessionResult = CloudProviderSessionResult.Success(Session, CloudCredentialState.Available);
        }

        public CloudConfigProviderDescriptor Descriptor { get; }

        public FakeSession Session { get; } = new();

        public CloudCredentialInspection CredentialInspection { get; set; } = new(CloudCredentialState.Available);

        public CloudProviderValidationResult Validation { get; set; } = CloudProviderValidationResult.Success();

        public CloudProviderSessionResult SessionResult { get; set; }

        public int CreateSessionCount { get; private set; }

        public bool LastSessionWasInteractive { get; private set; }

        public int CommitSecretsCount { get; private set; }

        public int ResolveSecretsCount { get; private set; }

        public int ForgetCredentialsCount { get; private set; }

        public Dictionary<string, CloudSecretUpdate> CommittedSecrets { get; } = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyDictionary<string, CloudSecretUpdate>? ResolvedSecrets { get; set; }

        public CloudProviderValidationResult Validate(IReadOnlyDictionary<string, string> options) => Validation;

        public Task<CloudCredentialInspection> InspectCredentialAsync(
            IReadOnlyDictionary<string, string> options,
            CancellationToken cancellationToken = default) => Task.FromResult(CredentialInspection);

        public Task<CloudProviderSessionResult> CreateSessionAsync(
            IReadOnlyDictionary<string, string> options,
            IReadOnlyDictionary<string, CloudSecretUpdate> secrets,
            bool interactive,
            CancellationToken cancellationToken = default)
        {
            CreateSessionCount++;
            LastSessionWasInteractive = interactive;
            return Task.FromResult(SessionResult);
        }

        public Task<IReadOnlyDictionary<string, CloudSecretUpdate>> ResolveSecretUpdatesAsync(
            IReadOnlyDictionary<string, CloudSecretUpdate> secrets,
            CancellationToken cancellationToken = default)
        {
            ResolveSecretsCount++;
            return Task.FromResult(
                ResolvedSecrets ?? new Dictionary<string, CloudSecretUpdate>(secrets, StringComparer.OrdinalIgnoreCase));
        }

        public Task<ICloudSecretUpdateTransaction> BeginSecretUpdateAsync(
            IReadOnlyDictionary<string, CloudSecretUpdate> secrets,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<ICloudSecretUpdateTransaction>(new FakeSecretUpdateTransaction(() =>
            {
                CommitSecretsCount++;
                foreach (var secret in secrets)
                {
                    CommittedSecrets[secret.Key] = secret.Value;
                }
            }));
        }

        public Task ForgetCredentialsAsync(CancellationToken cancellationToken = default)
        {
            ForgetCredentialsCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSecretUpdateTransaction : ICloudSecretUpdateTransaction
    {
        private readonly Action _complete;

        public FakeSecretUpdateTransaction(Action complete)
        {
            _complete = complete;
        }

        public void Complete() => _complete();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeSession : ICloudConfigStorageSession
    {
        public TaskCompletionSource DownloadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool BlockDownload { get; set; }

        public bool IgnoreCancellation { get; set; }

        public TaskCompletionSource ReleaseDownload { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CloudConfigRemoteFile? Remote { get; set; }

        public byte[]? UploadedContent { get; private set; }

        public CloudConfigUploadResult UploadResult { get; set; } = CloudConfigUploadResult.Uploaded("new-etag");

        public async Task<CloudConfigRemoteFile?> TryDownloadAsync(CancellationToken cancellationToken = default)
        {
            DownloadStarted.TrySetResult();
            if (BlockDownload)
            {
                if (IgnoreCancellation)
                {
                    await ReleaseDownload.Task;
                }
                else
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
            }

            return Remote;
        }

        public Task<CloudConfigUploadResult> UploadAsync(
            byte[] content,
            string? expectedETag,
            CancellationToken cancellationToken = default)
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

    private sealed class FailingSecureStorage : ISecureStorage
    {
        private readonly Dictionary<string, string> _values;
        private readonly string _failOnceOnSaveKey;
        private bool _hasFailed;

        public FailingSecureStorage(Dictionary<string, string> values, string failOnceOnSaveKey)
        {
            _values = values;
            _failOnceOnSaveKey = failOnceOnSaveKey;
        }

        public Task SaveAsync(string key, string value)
        {
            if (!_hasFailed && string.Equals(key, _failOnceOnSaveKey, StringComparison.Ordinal))
            {
                _hasFailed = true;
                throw new InvalidOperationException("Simulated secure storage failure.");
            }

            _values[key] = value;
            return Task.CompletedTask;
        }

        public Task<string?> LoadAsync(string key) =>
            Task.FromResult(_values.TryGetValue(key, out var value) ? value : null);

        public Task DeleteAsync(string key)
        {
            _values.Remove(key);
            return Task.CompletedTask;
        }
    }
}
