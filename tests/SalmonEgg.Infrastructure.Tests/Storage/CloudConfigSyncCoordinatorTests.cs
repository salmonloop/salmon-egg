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

    private readonly ConfigContentFingerprint _fingerprint;

    public CloudConfigSyncCoordinatorTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "SalmonEggCloudSyncTests", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("SALMONEGG_APPDATA_ROOT", Path.Combine(_testDirectory, "SalmonEgg"), EnvironmentVariableTarget.Process);
        _appData = new AppDataService();
        _configChangeSignal = new ConfigChangeSignal();
        _fileStore = new FileSystemAppFileStore(new NoOpFileSystemPersistence(), _configChangeSignal);
        _appSettings = new AppSettingsService(_fileStore, _appData, NullLogger<AppSettingsService>.Instance);
        _secureStorage = new PlainTextFileSecureStorage(_fileStore, _appData);
        var secrets = new ConfigurationSecretSnapshotService(_secureStorage, _fileStore, _appData);
        _packageService = new ConfigSyncPackageService(
            _appData,
            secrets,
            _configChangeSignal,
            new NoOpFileSystemPersistence());
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
    public async Task SyncNowAsync_WhenRemoteExistsWithoutBaseline_FailsClosedAndKeepsLocal()
    {
        // SyncNow 默认 RequireManual：基线未知且内容不同 → fail-closed，不覆盖本地。
        await SaveEnabledSettingsAsync();
        await File.WriteAllTextAsync(Path.Combine(_appData.ConfigRootPath, "local-only.yaml"), "value: local", TestContext.Current.CancellationToken);
        var settings = await _appSettings.LoadAsync();
        settings.Theme = "Light";
        await _appSettings.SaveAsync(settings);

        var provider = new FakeProvider
        {
            Session =
            {
                Remote = new CloudConfigRemoteFile(CreateRemotePackage("theme: Dark"), "remote-etag", DateTimeOffset.UtcNow)
            }
        };
        using var coordinator = CreateCoordinator(provider);

        await coordinator.SyncNowAsync(TestContext.Current.CancellationToken);
        var local = await _appSettings.LoadAsync();

        Assert.Equal(CloudTransferPhase.Failed, coordinator.Current.Transfer.Phase);
        Assert.Equal(CloudSyncFailureKind.RemoteConflict, coordinator.Current.Transfer.Failure?.Kind);
        Assert.Equal("Light", local.Theme);
        Assert.True(File.Exists(Path.Combine(_appData.ConfigRootPath, "local-only.yaml")));
        Assert.False(string.IsNullOrWhiteSpace(coordinator.Current.Transfer.Failure?.ArtifactPath));
        Assert.True(File.Exists(Path.Combine(coordinator.Current.Transfer.Failure!.ArtifactPath!, "remote.package.zip")));
        Assert.True(File.Exists(Path.Combine(coordinator.Current.Transfer.Failure.ArtifactPath!, "local", "local-only.yaml")));
        Assert.Null(provider.Session.UploadedContent);
    }

    [Fact]
    public async Task ApplyAndActivateAsync_WhenRemoteExistsWithoutBaseline_PrefersRemoteAndPublishesConfiguration()
    {
        // ApplyAndActivate 显式 PreferRemote：连接已有云配置时 restore。
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
            Session =
            {
                Remote = new CloudConfigRemoteFile(CreateRemotePackage(remoteAppYaml), "remote-etag", DateTimeOffset.UtcNow)
            }
        };
        using var coordinator = CreateCoordinator(provider);

        await coordinator.ApplyAndActivateAsync(
            new CloudProviderDraft(
                "webdav",
                new Dictionary<string, string> { ["file_url"] = "https://dav.example.test/old.zip" },
                new Dictionary<string, CloudSecretUpdate>()),
            TestContext.Current.CancellationToken);

        // PreferRemote 先 restore 远端；Activate 提交阶段再叠 draft 配置（revision 单调递增）。
        Assert.Equal(CloudTransferOutcome.Restored, coordinator.Current.Transfer.LastSuccess?.Outcome);
        Assert.Equal("webdav", coordinator.Current.Configuration.ProviderId);
        Assert.True(coordinator.Current.Configuration.Revision >= 42);
        Assert.Equal("https://dav.example.test/old.zip", coordinator.Current.Configuration.Options["file_url"]);
        Assert.True((await _appSettings.LoadAsync()).CloudConfigSync.Enabled);
    }

    [Fact]
    public async Task SyncNowAsync_WhenUploadPreconditionFails_ReResolvesByContentAndFailsClosedOnConflict()
    {
        await SaveEnabledSettingsAsync();
        // 建立基线：synced == 当前远端包指纹；本地随后改脏 → 仅本地 dirty → 上传。
        var baselinePackage = CreateRemotePackage("theme: System");
        var baselineFingerprint = _fingerprint.ComputeFromPackage(baselinePackage, includeSecrets: true);
        await new CloudConfigSyncStateStore(_fileStore, _appData).SaveAsync(new CloudConfigSyncState
        {
            ProviderId = "onedrive",
            RemoteETag = "old-etag",
            SyncedFingerprint = baselineFingerprint,
            LastSyncUtc = DateTimeOffset.UtcNow.AddHours(-2).ToString("O")
        }, TestContext.Current.CancellationToken);

        // 本地改脏。
        var settings = await _appSettings.LoadAsync();
        settings.Theme = "Light";
        await _appSettings.SaveAsync(settings);

        // 上传 If-Match 失败后，重下拿到并发远端（相对基线也变）→ 真冲突 fail-closed。
        var concurrentRemote = CreateRemotePackage("theme: Dark");
        var provider = new FakeProvider
        {
            Session =
            {
                Remote = new CloudConfigRemoteFile(baselinePackage, "old-etag", DateTimeOffset.UtcNow.AddHours(-2)),
                RemoteAfterPrecondition = new CloudConfigRemoteFile(concurrentRemote, "new-etag", DateTimeOffset.UtcNow),
                UploadResult = CloudConfigUploadResult.PreconditionFailed("Remote changed.")
            }
        };
        using var coordinator = CreateCoordinator(provider);

        await coordinator.SyncNowAsync(TestContext.Current.CancellationToken);
        var local = await _appSettings.LoadAsync();

        Assert.Equal(CloudTransferPhase.Failed, coordinator.Current.Transfer.Phase);
        Assert.Equal(CloudSyncFailureKind.RemoteConflict, coordinator.Current.Transfer.Failure?.Kind);
        Assert.Equal("Light", local.Theme);
        Assert.False(string.IsNullOrWhiteSpace(coordinator.Current.Transfer.Failure?.ArtifactPath));
    }

    [Fact]
    public async Task SyncNowAsync_WhenLocalDirtyAndRemoteUnchanged_UploadsWithoutRestore()
    {
        await SaveEnabledSettingsAsync();
        var baselinePackage = await _packageService.CreatePackageAsync(includeSecrets: true, TestContext.Current.CancellationToken);
        var baselineFingerprint = _fingerprint.ComputeFromPackage(baselinePackage, includeSecrets: true);
        await new CloudConfigSyncStateStore(_fileStore, _appData).SaveAsync(new CloudConfigSyncState
        {
            ProviderId = "onedrive",
            RemoteETag = "etag-1",
            SyncedFingerprint = baselineFingerprint,
            LastSyncUtc = DateTimeOffset.UtcNow.AddHours(-1).ToString("O")
        }, TestContext.Current.CancellationToken);

        var settings = await _appSettings.LoadAsync();
        settings.Theme = "Dark";
        await _appSettings.SaveAsync(settings);

        var provider = new FakeProvider
        {
            Session =
            {
                Remote = new CloudConfigRemoteFile(baselinePackage, "etag-1", DateTimeOffset.UtcNow.AddHours(-1))
            }
        };
        using var coordinator = CreateCoordinator(provider);

        await coordinator.SyncNowAsync(TestContext.Current.CancellationToken);
        var local = await _appSettings.LoadAsync();

        Assert.Equal(CloudTransferPhase.Succeeded, coordinator.Current.Transfer.Phase);
        Assert.Equal(CloudTransferOutcome.Uploaded, coordinator.Current.Transfer.LastSuccess?.Outcome);
        Assert.Equal("Dark", local.Theme);
        Assert.NotNull(provider.Session.UploadedContent);
    }

    [Fact]
    public async Task SyncNowAsync_WhenUploadReturnsEmptyETag_SecondSyncDoesNotRestore()
    {
        await SaveEnabledSettingsAsync();
        var provider = new FakeProvider
        {
            Session = { UploadResult = CloudConfigUploadResult.Uploaded(etag: null) }
        };
        using var coordinator = CreateCoordinator(provider);

        await coordinator.SyncNowAsync(TestContext.Current.CancellationToken);
        Assert.Equal(CloudTransferOutcome.Uploaded, coordinator.Current.Transfer.LastSuccess?.Outcome);
        Assert.NotNull(provider.Session.UploadedContent);

        // 远端现在持有刚上传的内容，但 ETag 仍为空（WebDAV 常见）。
        provider.Session.Remote = new CloudConfigRemoteFile(
            provider.Session.UploadedContent!,
            ETag: null,
            LastModifiedUtc: DateTimeOffset.UtcNow);
        provider.Session.UploadedContent = null;

        await coordinator.SyncNowAsync(TestContext.Current.CancellationToken);
        var local = await _appSettings.LoadAsync();

        // 内容收敛 → no-op / 刷新基线，不得 restore 回退。
        Assert.Equal(CloudTransferPhase.Succeeded, coordinator.Current.Transfer.Phase);
        Assert.Equal(CloudTransferOutcome.None, coordinator.Current.Transfer.LastSuccess?.Outcome);
        Assert.Null(provider.Session.UploadedContent);
        Assert.NotNull(local);
    }

    [Fact]
    public async Task SyncNowAsync_WhenOnlyRemoteChanged_Restores()
    {
        await SaveEnabledSettingsAsync();
        var baselinePackage = await _packageService.CreatePackageAsync(includeSecrets: true, TestContext.Current.CancellationToken);
        var baselineFingerprint = _fingerprint.ComputeFromPackage(baselinePackage, includeSecrets: true);
        await new CloudConfigSyncStateStore(_fileStore, _appData).SaveAsync(new CloudConfigSyncState
        {
            ProviderId = "onedrive",
            RemoteETag = "etag-1",
            SyncedFingerprint = baselineFingerprint,
            LastSyncUtc = DateTimeOffset.UtcNow.AddHours(-1).ToString("O")
        }, TestContext.Current.CancellationToken);

        var remotePackage = CreateRemotePackage("theme: Dark");
        var provider = new FakeProvider
        {
            Session = { Remote = new CloudConfigRemoteFile(remotePackage, "etag-2", DateTimeOffset.UtcNow) }
        };
        using var coordinator = CreateCoordinator(provider);

        await coordinator.SyncNowAsync(TestContext.Current.CancellationToken);
        var restored = await _appSettings.LoadAsync();

        Assert.Equal(CloudTransferOutcome.Restored, coordinator.Current.Transfer.LastSuccess?.Outcome);
        Assert.Equal("Dark", restored.Theme);
    }

    [Fact]
    public async Task SyncNowAsync_WhenBothSidesConverged_IsNoOp()
    {
        await SaveEnabledSettingsAsync();
        var package = await _packageService.CreatePackageAsync(includeSecrets: true, TestContext.Current.CancellationToken);
        var fingerprint = _fingerprint.ComputeFromPackage(package, includeSecrets: true);
        await new CloudConfigSyncStateStore(_fileStore, _appData).SaveAsync(new CloudConfigSyncState
        {
            ProviderId = "onedrive",
            RemoteETag = "etag-1",
            SyncedFingerprint = fingerprint,
            LastSyncUtc = DateTimeOffset.UtcNow.AddHours(-1).ToString("O")
        }, TestContext.Current.CancellationToken);

        var provider = new FakeProvider
        {
            Session = { Remote = new CloudConfigRemoteFile(package, "etag-1", DateTimeOffset.UtcNow.AddHours(-1)) }
        };
        using var coordinator = CreateCoordinator(provider);

        await coordinator.SyncNowAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CloudTransferPhase.Succeeded, coordinator.Current.Transfer.Phase);
        Assert.Equal(CloudTransferOutcome.None, coordinator.Current.Transfer.LastSuccess?.Outcome);
        Assert.Null(provider.Session.UploadedContent);
    }

    [Fact]
    public async Task SyncNowAsync_WhenTrueConflict_FailsClosedAndPreservesLocal()
    {
        await SaveEnabledSettingsAsync();
        var baselinePackage = CreateRemotePackage("theme: System");
        var baselineFingerprint = _fingerprint.ComputeFromPackage(baselinePackage, includeSecrets: true);
        await new CloudConfigSyncStateStore(_fileStore, _appData).SaveAsync(new CloudConfigSyncState
        {
            ProviderId = "onedrive",
            RemoteETag = "etag-1",
            SyncedFingerprint = baselineFingerprint,
            LastSyncUtc = DateTimeOffset.UtcNow.AddHours(-3).ToString("O")
        }, TestContext.Current.CancellationToken);

        var settings = await _appSettings.LoadAsync();
        settings.Theme = "Light";
        await _appSettings.SaveAsync(settings);

        var remotePackage = CreateRemotePackage("theme: Dark");
        var provider = new FakeProvider
        {
            Session = { Remote = new CloudConfigRemoteFile(remotePackage, "etag-2", DateTimeOffset.UtcNow) }
        };
        using var coordinator = CreateCoordinator(provider);

        await coordinator.SyncNowAsync(TestContext.Current.CancellationToken);
        var local = await _appSettings.LoadAsync();
        var state = await new CloudConfigSyncStateStore(_fileStore, _appData).LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CloudTransferPhase.Failed, coordinator.Current.Transfer.Phase);
        Assert.Equal(CloudSyncFailureKind.RemoteConflict, coordinator.Current.Transfer.Failure?.Kind);
        Assert.Equal("Light", local.Theme);
        Assert.Equal(baselineFingerprint, state.SyncedFingerprint);
        Assert.Null(provider.Session.UploadedContent);
        Assert.True(Directory.Exists(coordinator.Current.Transfer.Failure?.ArtifactPath));
    }

    [Fact]
    public async Task SyncNowAsync_WhenLegacyStateLacksFingerprint_FailsClosedWithoutDroppingLocal()
    {
        // 老 sync state：无 SyncedFingerprint。SyncNow RequireManual → fail-closed，不静默吞本地。
        await SaveEnabledSettingsAsync();
        var settings = await _appSettings.LoadAsync();
        settings.Theme = "Light";
        await _appSettings.SaveAsync(settings);

        await new CloudConfigSyncStateStore(_fileStore, _appData).SaveAsync(new CloudConfigSyncState
        {
            ProviderId = "onedrive",
            RemoteETag = "legacy-etag",
            SyncedFingerprint = string.Empty,
            LastSyncUtc = DateTimeOffset.UtcNow.AddDays(-1).ToString("O")
        }, TestContext.Current.CancellationToken);

        var remotePackage = CreateRemotePackage("theme: Dark");
        var provider = new FakeProvider
        {
            Session = { Remote = new CloudConfigRemoteFile(remotePackage, "legacy-etag", DateTimeOffset.UtcNow.AddDays(-1)) }
        };
        using var coordinator = CreateCoordinator(provider);

        await coordinator.SyncNowAsync(TestContext.Current.CancellationToken);
        var local = await _appSettings.LoadAsync();

        Assert.Equal(CloudTransferPhase.Failed, coordinator.Current.Transfer.Phase);
        Assert.Equal(CloudSyncFailureKind.RemoteConflict, coordinator.Current.Transfer.Failure?.Kind);
        Assert.Equal(CloudProviderReadiness.Ready, coordinator.Current.Readiness);
        Assert.Equal("Light", local.Theme);
        Assert.Null(provider.Session.UploadedContent);
    }

    [Fact]
    public async Task ResolveConflictAsync_KeepLocal_UploadsLocalAndClearsConflict()
    {
        await SaveEnabledSettingsAsync();
        var baselinePackage = CreateRemotePackage("theme: System");
        var baselineFingerprint = _fingerprint.ComputeFromPackage(baselinePackage, includeSecrets: true);
        await new CloudConfigSyncStateStore(_fileStore, _appData).SaveAsync(new CloudConfigSyncState
        {
            ProviderId = "onedrive",
            RemoteETag = "etag-1",
            SyncedFingerprint = baselineFingerprint,
            SyncedIncludeSecrets = true,
            LastSyncUtc = DateTimeOffset.UtcNow.AddHours(-3).ToString("O")
        }, TestContext.Current.CancellationToken);

        var settings = await _appSettings.LoadAsync();
        settings.Theme = "Light";
        await _appSettings.SaveAsync(settings);

        var provider = new FakeProvider
        {
            Session = { Remote = new CloudConfigRemoteFile(CreateRemotePackage("theme: Dark"), "etag-2", DateTimeOffset.UtcNow) }
        };
        using var coordinator = CreateCoordinator(provider);

        await coordinator.SyncNowAsync(TestContext.Current.CancellationToken);
        Assert.Equal(CloudSyncFailureKind.RemoteConflict, coordinator.Current.LastFailure?.Kind);

        await coordinator.ResolveConflictAsync(CloudSyncConflictResolution.KeepLocal, TestContext.Current.CancellationToken);
        var local = await _appSettings.LoadAsync();
        var state = await new CloudConfigSyncStateStore(_fileStore, _appData).LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CloudTransferPhase.Succeeded, coordinator.Current.Transfer.Phase);
        Assert.Equal(CloudTransferOutcome.Uploaded, coordinator.Current.Transfer.LastSuccess?.Outcome);
        Assert.Null(coordinator.Current.LastFailure);
        Assert.Equal("Light", local.Theme);
        Assert.NotNull(provider.Session.UploadedContent);
        Assert.False(string.IsNullOrWhiteSpace(state.SyncedFingerprint));
        Assert.True(state.SyncedIncludeSecrets);
    }

    [Fact]
    public async Task ResolveConflictAsync_ApplyRemote_RestoresRemoteAndClearsConflict()
    {
        await SaveEnabledSettingsAsync();
        var baselinePackage = CreateRemotePackage("theme: System");
        var baselineFingerprint = _fingerprint.ComputeFromPackage(baselinePackage, includeSecrets: true);
        await new CloudConfigSyncStateStore(_fileStore, _appData).SaveAsync(new CloudConfigSyncState
        {
            ProviderId = "onedrive",
            RemoteETag = "etag-1",
            SyncedFingerprint = baselineFingerprint,
            SyncedIncludeSecrets = true,
            LastSyncUtc = DateTimeOffset.UtcNow.AddHours(-3).ToString("O")
        }, TestContext.Current.CancellationToken);

        var settings = await _appSettings.LoadAsync();
        settings.Theme = "Light";
        await _appSettings.SaveAsync(settings);

        var remotePackage = CreateRemotePackage("theme: Dark");
        var provider = new FakeProvider
        {
            Session = { Remote = new CloudConfigRemoteFile(remotePackage, "etag-2", DateTimeOffset.UtcNow) }
        };
        using var coordinator = CreateCoordinator(provider);

        await coordinator.SyncNowAsync(TestContext.Current.CancellationToken);
        Assert.Equal(CloudSyncFailureKind.RemoteConflict, coordinator.Current.LastFailure?.Kind);

        await coordinator.ResolveConflictAsync(CloudSyncConflictResolution.ApplyRemote, TestContext.Current.CancellationToken);
        var restored = await _appSettings.LoadAsync();

        Assert.Equal(CloudTransferPhase.Succeeded, coordinator.Current.Transfer.Phase);
        Assert.Equal(CloudTransferOutcome.Restored, coordinator.Current.Transfer.LastSuccess?.Outcome);
        Assert.Null(coordinator.Current.LastFailure);
        Assert.Equal("Dark", restored.Theme);
        Assert.NotNull(coordinator.Current.Transfer.LastSuccess?.BackupPath);
    }

    [Fact]
    public async Task SyncNowAsync_WhenIncludeSecretsPolicyFlips_TreatsBaselineAsUnknown()
    {
        // 基线在 includeSecrets=true 下写入；当前改为 false → 旧指纹不可比 → SyncNow fail-closed。
        await SaveEnabledSettingsAsync();
        var baselinePackage = await _packageService.CreatePackageAsync(includeSecrets: true, TestContext.Current.CancellationToken);
        var baselineFingerprint = _fingerprint.ComputeFromPackage(baselinePackage, includeSecrets: true);
        await new CloudConfigSyncStateStore(_fileStore, _appData).SaveAsync(new CloudConfigSyncState
        {
            ProviderId = "onedrive",
            RemoteETag = "etag-1",
            SyncedFingerprint = baselineFingerprint,
            SyncedIncludeSecrets = true,
            LastSyncUtc = DateTimeOffset.UtcNow.AddHours(-1).ToString("O")
        }, TestContext.Current.CancellationToken);

        var settings = await _appSettings.LoadAsync();
        settings.CloudConfigSync.IncludeSecrets = false;
        settings.Theme = "Light";
        await _appSettings.SaveAsync(settings);

        var provider = new FakeProvider
        {
            Session = { Remote = new CloudConfigRemoteFile(CreateRemotePackage("theme: Dark"), "etag-2", DateTimeOffset.UtcNow) }
        };
        using var coordinator = CreateCoordinator(provider);

        await coordinator.SyncNowAsync(TestContext.Current.CancellationToken);
        var local = await _appSettings.LoadAsync();

        Assert.Equal(CloudTransferPhase.Failed, coordinator.Current.Transfer.Phase);
        Assert.Equal(CloudSyncFailureKind.RemoteConflict, coordinator.Current.Transfer.Failure?.Kind);
        Assert.Equal("Light", local.Theme);
    }

    [Fact]
    public async Task SyncNowAsync_WhenUploadReturnsEmptyETagButHeadHasMatchingContent_ReconcilesETag()
    {
        await SaveEnabledSettingsAsync();
        var provider = new FakeProvider
        {
            Session =
            {
                UploadResult = CloudConfigUploadResult.Uploaded(etag: null),
                // 上传后 reconcile 用第二次下载补 ETag；内容与刚上传包一致。
                ProvideRemoteFromLastUpload = true,
                RemoteETagAfterUpload = "reconciled-etag"
            }
        };
        using var coordinator = CreateCoordinator(provider);

        await coordinator.SyncNowAsync(TestContext.Current.CancellationToken);
        var state = await new CloudConfigSyncStateStore(_fileStore, _appData).LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CloudTransferOutcome.Uploaded, coordinator.Current.Transfer.LastSuccess?.Outcome);
        Assert.Equal("reconciled-etag", state.RemoteETag);
        Assert.Equal("reconciled-etag", coordinator.Current.Transfer.LastSuccess?.RemoteETag);
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
        // ApplyAndActivate 使用 PreferRemote：基线未知时 restore 远端。
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
        _fingerprint,
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
            WriteEntry(
                archive,
                "manifest.json",
                """{"schemaVersion":1,"appId":"SalmonEgg","createdAtUtc":"2026-01-01T00:00:00.0000000Z","files":["app.yaml"]}""");
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
        private int _downloadCount;

        public TaskCompletionSource DownloadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool BlockDownload { get; set; }

        public bool IgnoreCancellation { get; set; }

        public TaskCompletionSource ReleaseDownload { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CloudConfigRemoteFile? Remote { get; set; }

        /// <summary>
        /// 第一次下载返回 Remote；若设置了 RemoteAfterPrecondition，后续下载返回它
        /// （模拟 If-Match 失败后重拉到的并发远端）。
        /// </summary>
        public CloudConfigRemoteFile? RemoteAfterPrecondition { get; set; }

        /// <summary>
        /// 上传后 reconcile：用刚上传内容 + RemoteETagAfterUpload 构造下载结果。
        /// </summary>
        public bool ProvideRemoteFromLastUpload { get; set; }

        public string? RemoteETagAfterUpload { get; set; }

        public byte[]? UploadedContent { get; set; }

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

            var count = Interlocked.Increment(ref _downloadCount);
            if (ProvideRemoteFromLastUpload && UploadedContent is not null && count > 0 && Remote is null)
            {
                return new CloudConfigRemoteFile(
                    UploadedContent,
                    RemoteETagAfterUpload,
                    DateTimeOffset.UtcNow);
            }

            if (count > 1 && RemoteAfterPrecondition is not null)
            {
                return RemoteAfterPrecondition;
            }

            if (ProvideRemoteFromLastUpload && UploadedContent is not null && count > 1)
            {
                return new CloudConfigRemoteFile(
                    UploadedContent,
                    RemoteETagAfterUpload,
                    DateTimeOffset.UtcNow);
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
