using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Infrastructure.Storage;

public sealed class CloudConfigSyncCoordinator : ICloudConfigSyncCoordinator, IDisposable
{
    private static readonly TimeSpan AutoSyncDebounce = TimeSpan.FromSeconds(2);

    private readonly IAppSettingsService _appSettings;
    private readonly IReadOnlyDictionary<string, ICloudConfigStorageProvider> _providers;
    private readonly ConfigSyncPackageService _packageService;
    private readonly CloudConfigSyncStateStore _stateStore;
    private readonly ConfigContentFingerprint _fingerprint;
    private readonly IConfigChangeSignal _configChangeSignal;
    private readonly IAppDataService _appData;
    private readonly ILogger<CloudConfigSyncCoordinator> _logger;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly object _snapshotGate = new();
    private readonly object _intentGate = new();
    private readonly object _autoSyncGate = new();
    private CancellationTokenSource? _activeIntentCts;
    private CancellationTokenSource? _autoSyncCts;
    private CloudConfigSyncSnapshot _current = CloudConfigSyncSnapshot.Initial;
    private long _intentVersion;
    private bool _disposed;

    public CloudConfigSyncCoordinator(
        IAppSettingsService appSettings,
        IEnumerable<ICloudConfigStorageProvider> providers,
        ConfigSyncPackageService packageService,
        CloudConfigSyncStateStore stateStore,
        ConfigContentFingerprint fingerprint,
        IConfigChangeSignal configChangeSignal,
        IAppDataService appData,
        ILogger<CloudConfigSyncCoordinator> logger)
    {
        _appSettings = appSettings ?? throw new ArgumentNullException(nameof(appSettings));
        ArgumentNullException.ThrowIfNull(providers);
        _providers = providers.ToDictionary(provider => provider.Descriptor.ProviderId, StringComparer.OrdinalIgnoreCase);
        _packageService = packageService ?? throw new ArgumentNullException(nameof(packageService));
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _fingerprint = fingerprint ?? throw new ArgumentNullException(nameof(fingerprint));
        _configChangeSignal = configChangeSignal ?? throw new ArgumentNullException(nameof(configChangeSignal));
        _appData = appData ?? throw new ArgumentNullException(nameof(appData));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configChangeSignal.Changed += OnConfigChanged;
    }

    public IReadOnlyList<CloudConfigProviderDescriptor> Providers =>
        _providers.Values.Select(provider => provider.Descriptor).ToList();

    public CloudConfigSyncSnapshot Current
    {
        get
        {
            lock (_snapshotGate)
            {
                return _current;
            }
        }
    }

    public event EventHandler<CloudConfigSyncSnapshot>? SnapshotChanged;

    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        RunOperationAsync(CloudSyncOperationKind.Initialize, InitializeCoreAsync, cancellationToken);

    public Task ApplyAndActivateAsync(CloudProviderDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        return RunOperationAsync(
            CloudSyncOperationKind.ApplyAndActivate,
            (intentVersion, token) => ApplyAndActivateCoreAsync(draft, intentVersion, token),
            cancellationToken);
    }

    public Task SyncNowAsync(CancellationToken cancellationToken = default) =>
        RunOperationAsync(CloudSyncOperationKind.SyncNow, SyncNowCoreAsync, cancellationToken);

    public Task ResolveConflictAsync(
        CloudSyncConflictResolution resolution,
        CancellationToken cancellationToken = default) =>
        RunOperationAsync(
            CloudSyncOperationKind.SyncNow,
            (intentVersion, token) => ResolveConflictCoreAsync(resolution, intentVersion, token),
            cancellationToken);

    public Task DisableAsync(CancellationToken cancellationToken = default) =>
        RunOperationAsync(CloudSyncOperationKind.Disable, DisableCoreAsync, cancellationToken);

    public Task ForgetProviderAsync(string providerId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(providerId)) throw new ArgumentException("Provider ID is required.", nameof(providerId));
        return RunOperationAsync(
            CloudSyncOperationKind.ForgetProvider,
            (intentVersion, token) => ForgetProviderCoreAsync(providerId.Trim(), intentVersion, token),
            cancellationToken);
    }

    public async Task<CloudCredentialInspection> InspectCredentialAsync(
        string providerId,
        IReadOnlyDictionary<string, string> options,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetProvider(providerId, out var provider))
        {
            return new CloudCredentialInspection(
                CloudCredentialState.Faulted,
                new CloudSyncFailure(CloudSyncFailureKind.Validation, "Cloud provider is not available."));
        }

        var validation = provider.Validate(options);
        return validation.Succeeded
            ? await provider.InspectCredentialAsync(options, cancellationToken).ConfigureAwait(false)
            : new CloudCredentialInspection(CloudCredentialState.Unknown, validation.Failure);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _configChangeSignal.Changed -= OnConfigChanged;
        lock (_intentGate)
        {
            _activeIntentCts?.Cancel();
            _activeIntentCts?.Dispose();
            _activeIntentCts = null;
        }

        lock (_autoSyncGate)
        {
            _autoSyncCts?.Cancel();
            _autoSyncCts?.Dispose();
            _autoSyncCts = null;
        }

        _operationGate.Dispose();
    }

    private async Task RunOperationAsync(
        CloudSyncOperationKind kind,
        Func<long, CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        var intentCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        long intentVersion;
        lock (_intentGate)
        {
            intentVersion = ++_intentVersion;
            _activeIntentCts?.Cancel();
            _activeIntentCts = intentCts;
        }

        CancelPendingAutoSync();

        var operationToken = intentCts.Token;
        var gateHeld = false;
        try
        {
            await _operationGate.WaitAsync(operationToken).ConfigureAwait(false);
            gateHeld = true;
            operationToken.ThrowIfCancellationRequested();
            if (!IsLatestIntent(intentVersion))
            {
                return;
            }

            Publish(Current with
            {
                Operation = new CloudSyncOperation(intentVersion, kind, DateTimeOffset.UtcNow),
                LastFailure = null
            });
            await operation(intentVersion, operationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (operationToken.IsCancellationRequested)
        {
            if (IsLatestIntent(intentVersion))
            {
                Publish(Current with { Operation = null });
            }

            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cloud config operation {OperationKind} failed", kind);
            if (IsLatestIntent(intentVersion))
            {
                PublishFailure(intentVersion, ClassifyFailure(ex));
            }
        }
        finally
        {
            if (gateHeld)
            {
                _operationGate.Release();
            }

            lock (_intentGate)
            {
                if (ReferenceEquals(_activeIntentCts, intentCts))
                {
                    _activeIntentCts = null;
                }
            }

            intentCts.Dispose();
        }
    }

    private async Task InitializeCoreAsync(long intentVersion, CancellationToken cancellationToken)
    {
        PublishIfLatest(intentVersion, Current with
        {
            Initialization = CloudSyncInitializationState.Loading,
            Credential = CloudCredentialState.Checking,
            Readiness = CloudProviderReadiness.Checking
        });
        var settings = await _appSettings.LoadAsync().ConfigureAwait(false);
        var configuration = CreateConfiguration(settings.CloudConfigSync);
        if (!configuration.Enabled)
        {
            PublishIfLatest(intentVersion, Current with
            {
                Initialization = CloudSyncInitializationState.Ready,
                Configuration = configuration,
                Credential = CloudCredentialState.Unknown,
                Readiness = CloudProviderReadiness.Disabled,
                Transfer = await LoadIdleTransferStateAsync(configuration.ProviderId, cancellationToken).ConfigureAwait(false),
                Operation = null,
                LastFailure = null
            });
            return;
        }

        if (!TryGetProvider(configuration.ProviderId, out var provider))
        {
            PublishConfigurationFailure(intentVersion, configuration, CloudProviderReadiness.NeedsConfiguration,
                new CloudSyncFailure(CloudSyncFailureKind.Validation, "Configured cloud provider is not available."));
            return;
        }

        var validation = provider.Validate(configuration.Options);
        if (!validation.Succeeded)
        {
            PublishConfigurationFailure(
                intentVersion,
                configuration,
                CloudProviderReadiness.NeedsConfiguration,
                validation.Failure!);
            return;
        }

        var credential = await provider.InspectCredentialAsync(configuration.Options, cancellationToken).ConfigureAwait(false);
        if (credential.State is CloudCredentialState.Missing or CloudCredentialState.StoreUnavailable or CloudCredentialState.Faulted)
        {
            PublishConfigurationFailure(
                intentVersion,
                configuration,
                ToReadiness(credential.State),
                credential.Failure ?? CreateCredentialFailure(credential.State),
                credential.State);
            return;
        }

        PublishIfLatest(intentVersion, Current with
        {
            Initialization = CloudSyncInitializationState.Ready,
            Configuration = configuration,
            Credential = credential.State,
            Readiness = CloudProviderReadiness.Ready,
            Transfer = await LoadIdleTransferStateAsync(configuration.ProviderId, cancellationToken).ConfigureAwait(false),
            Operation = null,
            LastFailure = null
        });
        ScheduleAutoSync(AutoSyncDebounce);
    }

    private async Task ApplyAndActivateCoreAsync(
        CloudProviderDraft draft,
        long intentVersion,
        CancellationToken cancellationToken)
    {
        if (!TryGetProvider(draft.ProviderId, out var provider))
        {
            PublishFailure(intentVersion, new CloudSyncFailure(CloudSyncFailureKind.Validation, "Cloud provider is not available."));
            return;
        }

        var normalizedOptions = NormalizeOptions(draft.Options);
        var validation = provider.Validate(normalizedOptions);
        if (!validation.Succeeded)
        {
            PublishFailure(intentVersion, validation.Failure!);
            return;
        }

        PublishIfLatest(intentVersion, Current with
        {
            Credential = CloudCredentialState.Checking,
            Readiness = CloudProviderReadiness.Checking,
            Transfer = Current.Transfer with { Phase = CloudTransferPhase.Syncing, Failure = null }
        });
        var sessionResult = await provider.CreateSessionAsync(
            normalizedOptions,
            draft.Secrets,
            interactive: true,
            cancellationToken).ConfigureAwait(false);
        if (!sessionResult.Succeeded)
        {
            PublishFailure(intentVersion, sessionResult.Failure!, sessionResult.Credential);
            return;
        }

        var resolvedSecrets = await provider.ResolveSecretUpdatesAsync(draft.Secrets, cancellationToken)
            .ConfigureAwait(false);
        var packageSettings = await _appSettings.LoadAsync().ConfigureAwait(false);
        var candidate = CreateCandidateConfiguration(
            packageSettings.CloudConfigSync,
            provider.Descriptor.ProviderId,
            normalizedOptions);
        ApplyCandidateConfiguration(packageSettings, candidate, draft.IncludeSecrets);
        // 激活已有云配置：基线未知时显式 PreferRemote，禁止时钟 LWW。
        var transfer = await SynchronizeAsync(
            sessionResult.Session!,
            provider.Descriptor.ProviderId,
            draft.IncludeSecrets,
            packageSettings,
            resolvedSecrets,
            CloudSyncFirstAdoptPolicy.PreferRemote,
            cancellationToken).ConfigureAwait(false);
        if (transfer.Phase == CloudTransferPhase.Failed)
        {
            PublishIfLatest(intentVersion, Current with
            {
                Initialization = CloudSyncInitializationState.Ready,
                Credential = sessionResult.Credential,
                Readiness = ToReadiness(transfer.Failure!),
                Transfer = transfer,
                Operation = null,
                LastFailure = transfer.Failure
            });
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!IsLatestIntent(intentVersion))
        {
            return;
        }

        var settings = await _appSettings.LoadAsync().ConfigureAwait(false);
        var previous = settings.CloudConfigSync;
        candidate = CreateCandidateConfiguration(previous, provider.Descriptor.ProviderId, normalizedOptions);
        ApplyCandidateConfiguration(settings, candidate, draft.IncludeSecrets);
        await using var secretTransaction = await provider.BeginSecretUpdateAsync(resolvedSecrets, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsLatestIntent(intentVersion))
            {
                return;
            }

            await SaveSettingsAsync(settings).ConfigureAwait(false);
            if (!TryCompleteLatestIntent(intentVersion, secretTransaction))
            {
                settings.CloudConfigSync = previous;
                await SaveSettingsAsync(settings).ConfigureAwait(false);
                return;
            }
        }
        catch
        {
            settings.CloudConfigSync = previous;
            await SaveSettingsAsync(settings).ConfigureAwait(false);
            throw;
        }

        PublishIfLatest(intentVersion, Current with
        {
            Initialization = CloudSyncInitializationState.Ready,
            Configuration = candidate,
            Credential = sessionResult.Credential,
            Readiness = CloudProviderReadiness.Ready,
            Transfer = transfer,
            Operation = null,
            LastFailure = null
        });
    }

    private async Task SyncNowCoreAsync(long intentVersion, CancellationToken cancellationToken)
    {
        var settings = await _appSettings.LoadAsync().ConfigureAwait(false);
        var configuration = CreateConfiguration(settings.CloudConfigSync);
        if (!configuration.Enabled || !TryGetProvider(configuration.ProviderId, out var provider))
        {
            PublishConfigurationFailure(
                intentVersion,
                configuration,
                configuration.Enabled ? CloudProviderReadiness.NeedsConfiguration : CloudProviderReadiness.Disabled,
                new CloudSyncFailure(CloudSyncFailureKind.Validation, "Cloud sync is not configured."));
            return;
        }

        var sessionResult = await provider.CreateSessionAsync(
            configuration.Options,
            new Dictionary<string, CloudSecretUpdate>(StringComparer.OrdinalIgnoreCase),
            interactive: false,
            cancellationToken).ConfigureAwait(false);
        if (!sessionResult.Succeeded)
        {
            PublishConfigurationFailure(
                intentVersion,
                configuration,
                ToReadiness(sessionResult.Credential),
                sessionResult.Failure!);
            return;
        }

        PublishIfLatest(intentVersion, Current with
        {
            Configuration = configuration,
            Credential = sessionResult.Credential,
            Transfer = Current.Transfer with { Phase = CloudTransferPhase.Syncing, Failure = null }
        });
        await SynchronizeAndPublishAsync(
            intentVersion,
            configuration,
            sessionResult.Session!,
            settings.CloudConfigSync.IncludeSecrets,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task ResolveConflictCoreAsync(
        CloudSyncConflictResolution resolution,
        long intentVersion,
        CancellationToken cancellationToken)
    {
        var settings = await _appSettings.LoadAsync().ConfigureAwait(false);
        var configuration = CreateConfiguration(settings.CloudConfigSync);
        if (!configuration.Enabled || !TryGetProvider(configuration.ProviderId, out var provider))
        {
            PublishConfigurationFailure(
                intentVersion,
                configuration,
                configuration.Enabled ? CloudProviderReadiness.NeedsConfiguration : CloudProviderReadiness.Disabled,
                new CloudSyncFailure(CloudSyncFailureKind.Validation, "Cloud sync is not configured."));
            return;
        }

        // 仅允许从当前 RemoteConflict 失败态进入显式解决；禁止旁路 3-way 静默选边。
        if (Current.LastFailure?.Kind != CloudSyncFailureKind.RemoteConflict &&
            Current.Transfer.Failure?.Kind != CloudSyncFailureKind.RemoteConflict)
        {
            PublishFailure(
                intentVersion,
                new CloudSyncFailure(
                    CloudSyncFailureKind.Validation,
                    "No pending cloud configuration conflict to resolve."));
            return;
        }

        var sessionResult = await provider.CreateSessionAsync(
            configuration.Options,
            new Dictionary<string, CloudSecretUpdate>(StringComparer.OrdinalIgnoreCase),
            interactive: false,
            cancellationToken).ConfigureAwait(false);
        if (!sessionResult.Succeeded)
        {
            PublishConfigurationFailure(
                intentVersion,
                configuration,
                ToReadiness(sessionResult.Credential),
                sessionResult.Failure!);
            return;
        }

        PublishIfLatest(intentVersion, Current with
        {
            Configuration = configuration,
            Credential = sessionResult.Credential,
            Transfer = Current.Transfer with { Phase = CloudTransferPhase.Syncing, Failure = null }
        });

        CloudTransferState transfer;
        try
        {
            var state = await _stateStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            var includeSecrets = settings.CloudConfigSync.IncludeSecrets;
            transfer = resolution switch
            {
                CloudSyncConflictResolution.KeepLocal => await UploadLocalAsync(
                        sessionResult.Session!,
                        state,
                        configuration.ProviderId,
                        expectedETag: null, // 用户显式覆盖远端，不用 If-Match 拦。
                        includeSecrets,
                        settingsOverride: null,
                        secretOverrides: null,
                        CloudSyncFirstAdoptPolicy.RequireManual,
                        allowPreconditionRetry: false,
                        cancellationToken)
                    .ConfigureAwait(false),
                CloudSyncConflictResolution.ApplyRemote => await ApplyRemoteResolutionAsync(
                        sessionResult.Session!,
                        state,
                        configuration.ProviderId,
                        includeSecrets,
                        cancellationToken)
                    .ConfigureAwait(false),
                _ => new CloudTransferState(
                    CloudTransferPhase.Failed,
                    CreateLastSuccess(state),
                    new CloudSyncFailure(CloudSyncFailureKind.Validation, "Unknown conflict resolution."))
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            transfer = new CloudTransferState(
                CloudTransferPhase.Failed,
                CreateLastSuccess(await _stateStore.LoadAsync(cancellationToken).ConfigureAwait(false)),
                ClassifyFailure(ex));
        }

        var publishedConfiguration = configuration;
        if (transfer.Phase == CloudTransferPhase.Succeeded &&
            transfer.LastSuccess?.Outcome is CloudTransferOutcome.Restored)
        {
            var restoredSettings = await _appSettings.LoadAsync().ConfigureAwait(false);
            publishedConfiguration = CreateConfiguration(restoredSettings.CloudConfigSync);
        }

        PublishIfLatest(intentVersion, Current with
        {
            Initialization = CloudSyncInitializationState.Ready,
            Configuration = publishedConfiguration,
            Readiness = transfer.Phase == CloudTransferPhase.Succeeded
                ? CloudProviderReadiness.Ready
                : ToReadiness(transfer.Failure!),
            Transfer = transfer,
            Operation = null,
            LastFailure = transfer.Failure
        });
    }

    private async Task<CloudTransferState> ApplyRemoteResolutionAsync(
        ICloudConfigStorageSession session,
        CloudConfigSyncState state,
        string providerId,
        bool includeSecrets,
        CancellationToken cancellationToken)
    {
        var remote = await session.TryDownloadAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (remote is null)
        {
            return new CloudTransferState(
                CloudTransferPhase.Failed,
                CreateLastSuccess(state),
                new CloudSyncFailure(
                    CloudSyncFailureKind.RemoteConflict,
                    "Remote package is no longer available to apply."));
        }

        var remoteFingerprint = _fingerprint.ComputeFromPackage(remote.Content, includeSecrets);
        return await RestoreRemoteAsync(
                state,
                providerId,
                remote,
                remoteFingerprint,
                includeSecrets,
                CloudTransferOutcome.Restored,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task DisableCoreAsync(long intentVersion, CancellationToken cancellationToken)
    {
        var settings = await _appSettings.LoadAsync().ConfigureAwait(false);
        settings.CloudConfigSync.Enabled = false;
        await SaveSettingsAsync(settings).ConfigureAwait(false);
        var configuration = CreateConfiguration(settings.CloudConfigSync);
        PublishIfLatest(intentVersion, Current with
        {
            Initialization = CloudSyncInitializationState.Ready,
            Configuration = configuration,
            Readiness = CloudProviderReadiness.Disabled,
            Operation = null,
            LastFailure = null
        });
    }

    private async Task ForgetProviderCoreAsync(
        string providerId,
        long intentVersion,
        CancellationToken cancellationToken)
    {
        if (!TryGetProvider(providerId, out var provider))
        {
            PublishFailure(intentVersion, new CloudSyncFailure(CloudSyncFailureKind.Validation, "Cloud provider is not available."));
            return;
        }

        await provider.ForgetCredentialsAsync(cancellationToken).ConfigureAwait(false);
        var settings = await _appSettings.LoadAsync().ConfigureAwait(false);
        settings.CloudConfigSync.ProviderOptions.Remove(providerId);
        var wasActive = string.Equals(settings.CloudConfigSync.ProviderId, providerId, StringComparison.OrdinalIgnoreCase);
        if (wasActive)
        {
            settings.CloudConfigSync.Enabled = false;
            settings.CloudConfigSync.ProviderId = string.Empty;
            settings.CloudConfigSync.Revision++;
            await _stateStore.ClearAsync(cancellationToken).ConfigureAwait(false);
        }

        await SaveSettingsAsync(settings).ConfigureAwait(false);
        PublishIfLatest(intentVersion, Current with
        {
            Initialization = CloudSyncInitializationState.Ready,
            Configuration = CreateConfiguration(settings.CloudConfigSync),
            Credential = wasActive ? CloudCredentialState.Unknown : Current.Credential,
            Readiness = wasActive ? CloudProviderReadiness.Disabled : Current.Readiness,
            Transfer = wasActive
                ? new CloudTransferState(CloudTransferPhase.Idle)
                : Current.Transfer,
            Operation = null,
            LastFailure = null
        });
    }

    private async Task SynchronizeAndPublishAsync(
        long intentVersion,
        CloudSyncConfiguration configuration,
        ICloudConfigStorageSession session,
        bool includeSecrets,
        CancellationToken cancellationToken)
    {
        // 日常 SyncNow / 自动同步：基线未知时 fail-closed，不静默覆盖本地。
        var transfer = await SynchronizeAsync(
            session,
            configuration.ProviderId,
            includeSecrets,
            null,
            null,
            CloudSyncFirstAdoptPolicy.RequireManual,
            cancellationToken).ConfigureAwait(false);
        var publishedConfiguration = configuration;
        if (transfer.Phase == CloudTransferPhase.Succeeded &&
            transfer.LastSuccess?.Outcome is CloudTransferOutcome.Restored)
        {
            var restoredSettings = await _appSettings.LoadAsync().ConfigureAwait(false);
            publishedConfiguration = CreateConfiguration(restoredSettings.CloudConfigSync);
        }

        PublishIfLatest(intentVersion, Current with
        {
            Initialization = CloudSyncInitializationState.Ready,
            Configuration = publishedConfiguration,
            Readiness = transfer.Phase == CloudTransferPhase.Succeeded
                ? CloudProviderReadiness.Ready
                : ToReadiness(transfer.Failure!),
            Transfer = transfer,
            Operation = null,
            LastFailure = transfer.Failure
        });
    }

    private async Task<CloudTransferState> SynchronizeAsync(
        ICloudConfigStorageSession session,
        string providerId,
        bool includeSecrets,
        AppSettings? settingsOverride,
        IReadOnlyDictionary<string, CloudSecretUpdate>? secretOverrides,
        CloudSyncFirstAdoptPolicy firstAdoptPolicy,
        CancellationToken cancellationToken)
    {
        try
        {
            var state = await _stateStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            var remote = await session.TryDownloadAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (remote is null)
            {
                return await UploadLocalAsync(
                        session,
                        state,
                        providerId,
                        null,
                        includeSecrets,
                        settingsOverride,
                        secretOverrides,
                        firstAdoptPolicy,
                        allowPreconditionRetry: true,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            // Provider 切换时丢弃旧基线，按首次采用处理。
            if (!string.IsNullOrWhiteSpace(state.ProviderId) &&
                !string.Equals(state.ProviderId, providerId, StringComparison.OrdinalIgnoreCase))
            {
                state.SyncedFingerprint = string.Empty;
                state.RemoteETag = string.Empty;
            }

            return await ResolveByContentAsync(
                    session,
                    state,
                    providerId,
                    remote,
                    includeSecrets,
                    settingsOverride,
                    secretOverrides,
                    firstAdoptPolicy,
                    allowPreconditionRetry: true,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new CloudTransferState(
                CloudTransferPhase.Failed,
                CreateLastSuccess(await _stateStore.LoadAsync(cancellationToken).ConfigureAwait(false)),
                ClassifyFailure(ex));
        }
    }

    /// <summary>
    /// 内容寻址 3-way：指纹计算 + 纯函数判定 + 副作用执行。
    /// ETag 仅作上传 If-Match；方向判定不读时钟、不做 LWW。
    /// </summary>
    private async Task<CloudTransferState> ResolveByContentAsync(
        ICloudConfigStorageSession session,
        CloudConfigSyncState state,
        string providerId,
        CloudConfigRemoteFile remote,
        bool includeSecrets,
        AppSettings? settingsOverride,
        IReadOnlyDictionary<string, CloudSecretUpdate>? secretOverrides,
        CloudSyncFirstAdoptPolicy firstAdoptPolicy,
        bool allowPreconditionRetry,
        CancellationToken cancellationToken)
    {
        var localFingerprint = await _fingerprint.ComputeLocalAsync(
                includeSecrets,
                settingsOverride,
                providerId,
                secretOverrides,
                cancellationToken)
            .ConfigureAwait(false);
        var remoteFingerprint = _fingerprint.ComputeFromPackage(remote.Content, includeSecrets);
        var syncedFingerprint = state.SyncedFingerprint ?? string.Empty;
        // includeSecrets 与写入基线时不一致 → 旧指纹不可比，按基线未知处理。
        var baselineKnown = !string.IsNullOrWhiteSpace(syncedFingerprint) &&
            string.Equals(state.ProviderId, providerId, StringComparison.OrdinalIgnoreCase) &&
            state.SyncedIncludeSecrets == includeSecrets;

        var decision = CloudSyncContentDecisionMaker.Decide(new CloudSyncContentDecisionInput(
            localFingerprint,
            remoteFingerprint,
            syncedFingerprint,
            baselineKnown,
            firstAdoptPolicy));

        _logger.LogInformation(
            "Cloud sync content decision {Action}: {Reason}; baselineKnown={BaselineKnown}; includeSecrets={IncludeSecrets}",
            decision.Action,
            decision.Reason,
            baselineKnown,
            includeSecrets);

        return decision.Action switch
        {
            CloudSyncContentAction.RefreshBaseline => await RefreshBaselineAsync(
                    state,
                    providerId,
                    remote.ETag,
                    remoteFingerprint,
                    includeSecrets,
                    cancellationToken)
                .ConfigureAwait(false),

            CloudSyncContentAction.UploadLocal => await UploadLocalAsync(
                    session,
                    state,
                    providerId,
                    // 基线未知时不用 If-Match，避免空/未知并发令牌导致无意义 PreconditionFailed。
                    baselineKnown ? remote.ETag : null,
                    includeSecrets,
                    settingsOverride,
                    secretOverrides,
                    firstAdoptPolicy,
                    allowPreconditionRetry,
                    cancellationToken)
                .ConfigureAwait(false),

            CloudSyncContentAction.RestoreRemote => await RestoreRemoteAsync(
                    state,
                    providerId,
                    remote,
                    remoteFingerprint,
                    includeSecrets,
                    CloudTransferOutcome.Restored,
                    cancellationToken)
                .ConfigureAwait(false),

            _ => await FailClosedConflictAsync(state, remote, decision.Reason, cancellationToken)
                .ConfigureAwait(false)
        };
    }

    private async Task<CloudTransferState> FailClosedConflictAsync(
        CloudConfigSyncState state,
        CloudConfigRemoteFile remote,
        string reason,
        CancellationToken cancellationToken)
    {
        // 不改本地 config、不改 SyncedFingerprint；只落冲突工件供人工决策。
        string? artifactPath = null;
        try
        {
            artifactPath = await _packageService.PersistConflictArtifactsAsync(remote.Content, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist cloud config conflict artifacts");
        }

        return new CloudTransferState(
            CloudTransferPhase.Failed,
            CreateLastSuccess(state),
            new CloudSyncFailure(
                CloudSyncFailureKind.RemoteConflict,
                string.IsNullOrWhiteSpace(reason)
                    ? "Local and remote configs both changed; manual resolution required."
                    : reason,
                artifactPath));
    }

    private async Task<CloudTransferState> UploadLocalAsync(
        ICloudConfigStorageSession session,
        CloudConfigSyncState state,
        string providerId,
        string? expectedETag,
        bool includeSecrets,
        AppSettings? settingsOverride,
        IReadOnlyDictionary<string, CloudSecretUpdate>? secretOverrides,
        CloudSyncFirstAdoptPolicy firstAdoptPolicy,
        bool allowPreconditionRetry,
        CancellationToken cancellationToken)
    {
        var package = await _packageService.CreatePackageAsync(
                includeSecrets,
                settingsOverride,
                providerId,
                secretOverrides,
                cancellationToken)
            .ConfigureAwait(false);
        var upload = await session.UploadAsync(package, expectedETag, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (upload.Status == CloudConfigUploadStatus.PreconditionFailed)
        {
            // 预条件失败：重新下载并走 3-way，而非无条件 restore。
            // 仅重试一次，避免 If-Match 与远端持续错位时无限递归。
            var remote = await session.TryDownloadAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (remote is null)
            {
                return new CloudTransferState(
                    CloudTransferPhase.Failed,
                    CreateLastSuccess(state),
                    upload.Failure ?? new CloudSyncFailure(CloudSyncFailureKind.RemoteConflict, "Remote conflict could not be resolved."));
            }

            if (!allowPreconditionRetry)
            {
                return new CloudTransferState(
                    CloudTransferPhase.Failed,
                    CreateLastSuccess(state),
                    upload.Failure ?? new CloudSyncFailure(CloudSyncFailureKind.RemoteConflict, "Remote conflict could not be resolved."));
            }

            // 方向改由内容 3-way 重新判定；不再允许二次 PreconditionFailed 重入。
            return await ResolveByContentAsync(
                    session,
                    state,
                    providerId,
                    remote,
                    includeSecrets,
                    settingsOverride,
                    secretOverrides,
                    firstAdoptPolicy,
                    allowPreconditionRetry: false,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (upload.Status != CloudConfigUploadStatus.Uploaded)
        {
            return new CloudTransferState(
                CloudTransferPhase.Failed,
                CreateLastSuccess(state),
                upload.Failure ?? new CloudSyncFailure(CloudSyncFailureKind.Network, "Cloud upload failed."));
        }

        // 指纹以实际落地的包内容为准（与远端将看到的一致）。
        var fingerprint = _fingerprint.ComputeFromPackage(package, includeSecrets);
        // WebDAV 等实现可能不在 PUT 响应返回 ETag；回读一次补强乐观并发令牌。
        var remoteETag = upload.ETag;
        if (string.IsNullOrWhiteSpace(remoteETag))
        {
            try
            {
                var head = await session.TryDownloadAsync(cancellationToken).ConfigureAwait(false);
                if (head is not null &&
                    string.Equals(
                        _fingerprint.ComputeFromPackage(head.Content, includeSecrets),
                        fingerprint,
                        StringComparison.Ordinal))
                {
                    remoteETag = head.ETag;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to reconcile remote ETag after upload");
            }
        }

        var now = DateTimeOffset.UtcNow;
        CommitSyncedState(state, providerId, remoteETag, fingerprint, includeSecrets, now);
        await _stateStore.SaveAsync(state, cancellationToken).ConfigureAwait(false);
        return new CloudTransferState(
            CloudTransferPhase.Succeeded,
            new CloudTransferSuccess(CloudTransferOutcome.Uploaded, now, state.RemoteETag));
    }

    private async Task<CloudTransferState> RestoreRemoteAsync(
        CloudConfigSyncState state,
        string providerId,
        CloudConfigRemoteFile remote,
        string remoteFingerprint,
        bool includeSecrets,
        CloudTransferOutcome outcome,
        CancellationToken cancellationToken)
    {
        var backupPath = await _packageService.RestorePackageAsync(remote.Content, cancellationToken).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        CommitSyncedState(state, providerId, remote.ETag, remoteFingerprint, includeSecrets, now);
        await _stateStore.SaveAsync(state, cancellationToken).ConfigureAwait(false);
        return new CloudTransferState(
            CloudTransferPhase.Succeeded,
            new CloudTransferSuccess(
                outcome,
                now,
                state.RemoteETag,
                Directory.Exists(backupPath) ? backupPath : null));
    }

    private async Task<CloudTransferState> RefreshBaselineAsync(
        CloudConfigSyncState state,
        string providerId,
        string? remoteETag,
        string fingerprint,
        bool includeSecrets,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        // 收敛刷新：保留已有 ETag（若本次远端未带回）。
        var effectiveETag = !string.IsNullOrEmpty(remoteETag) ? remoteETag : state.RemoteETag;
        CommitSyncedState(state, providerId, effectiveETag, fingerprint, includeSecrets, now);
        await _stateStore.SaveAsync(state, cancellationToken).ConfigureAwait(false);
        return new CloudTransferState(
            CloudTransferPhase.Succeeded,
            new CloudTransferSuccess(CloudTransferOutcome.None, now, state.RemoteETag));
    }

    private static void CommitSyncedState(
        CloudConfigSyncState state,
        string providerId,
        string? remoteETag,
        string fingerprint,
        bool includeSecrets,
        DateTimeOffset completedAt)
    {
        state.ProviderId = providerId;
        state.RemoteETag = remoteETag ?? string.Empty;
        state.SyncedFingerprint = fingerprint;
        state.SyncedIncludeSecrets = includeSecrets;
        state.LastSyncUtc = completedAt.ToString("O");
    }

    private async Task<CloudTransferState> LoadIdleTransferStateAsync(
        string providerId,
        CancellationToken cancellationToken)
    {
        var state = await _stateStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var lastSuccess = !string.IsNullOrWhiteSpace(providerId) &&
            !string.IsNullOrWhiteSpace(state.ProviderId) &&
            string.Equals(state.ProviderId, providerId, StringComparison.OrdinalIgnoreCase)
            ? CreateLastSuccess(state)
            : null;
        return new CloudTransferState(CloudTransferPhase.Idle, lastSuccess);
    }

    private void PublishConfigurationFailure(
        long intentVersion,
        CloudSyncConfiguration configuration,
        CloudProviderReadiness readiness,
        CloudSyncFailure failure,
        CloudCredentialState? credential = null)
    {
        var transfer = string.Equals(
            Current.Configuration.ProviderId,
            configuration.ProviderId,
            StringComparison.OrdinalIgnoreCase)
            ? Current.Transfer with { Phase = CloudTransferPhase.Failed, Failure = failure }
            : new CloudTransferState(CloudTransferPhase.Failed, Failure: failure);
        PublishIfLatest(intentVersion, Current with
        {
            Initialization = CloudSyncInitializationState.Ready,
            Configuration = configuration,
            Credential = credential ?? ToCredentialState(failure),
            Readiness = readiness,
            Transfer = transfer,
            Operation = null,
            LastFailure = failure
        });
    }

    private void PublishFailure(
        long intentVersion,
        CloudSyncFailure failure,
        CloudCredentialState? credential = null)
    {
        PublishIfLatest(intentVersion, Current with
        {
            Initialization = CloudSyncInitializationState.Ready,
            Credential = credential ?? ToCredentialState(failure),
            Readiness = ToReadiness(failure),
            Transfer = Current.Transfer with { Phase = CloudTransferPhase.Failed, Failure = failure },
            Operation = null,
            LastFailure = failure
        });
    }

    private void PublishIfLatest(long intentVersion, CloudConfigSyncSnapshot snapshot)
    {
        if (IsLatestIntent(intentVersion))
        {
            Publish(snapshot);
        }
    }

    private void Publish(CloudConfigSyncSnapshot snapshot)
    {
        CloudConfigSyncSnapshot published;
        lock (_snapshotGate)
        {
            published = snapshot with { Version = _current.Version + 1 };
            _current = published;
        }

        SnapshotChanged?.Invoke(this, published);
    }

    private bool IsLatestIntent(long intentVersion) => intentVersion == Volatile.Read(ref _intentVersion);

    private bool TryGetProvider(string? providerId, out ICloudConfigStorageProvider provider)
    {
        provider = null!;
        return !string.IsNullOrWhiteSpace(providerId) && _providers.TryGetValue(providerId.Trim(), out provider!);
    }

    private static CloudSyncConfiguration CreateConfiguration(CloudConfigSyncSettings settings)
    {
        var providerId = settings.ProviderId?.Trim() ?? string.Empty;
        var options = settings.ProviderOptions.TryGetValue(providerId, out var stored)
            ? new Dictionary<string, string>(stored, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return new CloudSyncConfiguration(settings.Enabled, providerId, settings.Revision, options);
    }

    private static IReadOnlyDictionary<string, string> NormalizeOptions(IReadOnlyDictionary<string, string> options) =>
        options
            .Where(option => !string.IsNullOrWhiteSpace(option.Key) && option.Value is not null)
            .ToDictionary(
                option => option.Key.Trim(),
                option => option.Value.Trim(),
                StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, Dictionary<string, string>> CloneProviderOptions(
        IReadOnlyDictionary<string, Dictionary<string, string>> source) =>
        source.ToDictionary(
            option => option.Key,
            option => new Dictionary<string, string>(option.Value, StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

    private CloudSyncConfiguration CreateCandidateConfiguration(
        CloudConfigSyncSettings current,
        string providerId,
        IReadOnlyDictionary<string, string> options)
    {
        var nextRevision = Math.Max(current.Revision, Current.Configuration.Revision) + 1;
        return new CloudSyncConfiguration(true, providerId, nextRevision, options);
    }

    private static void ApplyCandidateConfiguration(
        AppSettings settings,
        CloudSyncConfiguration candidate,
        bool includeSecrets)
    {
        var providerOptions = CloneProviderOptions(settings.CloudConfigSync.ProviderOptions);
        providerOptions[candidate.ProviderId] = new Dictionary<string, string>(
            candidate.Options,
            StringComparer.OrdinalIgnoreCase);
        settings.CloudConfigSync = new CloudConfigSyncSettings
        {
            Enabled = true,
            ProviderId = candidate.ProviderId,
            Revision = candidate.Revision,
            IncludeSecrets = includeSecrets,
            ProviderOptions = providerOptions
        };
    }

    private bool TryCompleteLatestIntent(
        long intentVersion,
        ICloudSecretUpdateTransaction secretTransaction)
    {
        lock (_intentGate)
        {
            if (!IsLatestIntent(intentVersion))
            {
                return false;
            }

            secretTransaction.Complete();
            return true;
        }
    }

    private static CloudTransferSuccess? CreateLastSuccess(CloudConfigSyncState state)
    {
        var timestamp = CloudConfigSyncStateStore.ParseLastSync(state.LastSyncUtc);
        return timestamp.HasValue
            ? new CloudTransferSuccess(CloudTransferOutcome.None, timestamp.Value, state.RemoteETag)
            : null;
    }

    private static CloudSyncFailure ClassifyFailure(Exception exception)
    {
        if (exception is SecureStorageUnavailableException)
        {
            return new CloudSyncFailure(CloudSyncFailureKind.CredentialStoreUnavailable, exception.Message);
        }

        if (exception is HttpRequestException httpException)
        {
            var kind = httpException.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                ? CloudSyncFailureKind.Authentication
                : CloudSyncFailureKind.Network;
            return new CloudSyncFailure(kind, exception.Message);
        }

        if (exception is IOException)
        {
            return new CloudSyncFailure(CloudSyncFailureKind.LocalPackage, exception.Message);
        }

        return new CloudSyncFailure(CloudSyncFailureKind.Unknown, exception.Message);
    }

    private static CloudSyncFailure CreateCredentialFailure(CloudCredentialState state) => state switch
    {
        CloudCredentialState.Missing =>
            new CloudSyncFailure(CloudSyncFailureKind.CredentialMissing, "Saved cloud credentials are missing."),
        CloudCredentialState.StoreUnavailable =>
            new CloudSyncFailure(CloudSyncFailureKind.CredentialStoreUnavailable, "Secure storage is unavailable."),
        _ => new CloudSyncFailure(CloudSyncFailureKind.Unknown, "Cloud credential inspection failed.")
    };

    private static CloudCredentialState ToCredentialState(CloudSyncFailure failure) => failure.Kind switch
    {
        CloudSyncFailureKind.CredentialMissing or CloudSyncFailureKind.Authentication => CloudCredentialState.Missing,
        CloudSyncFailureKind.CredentialStoreUnavailable => CloudCredentialState.StoreUnavailable,
        _ => CloudCredentialState.Unknown
    };

    private static CloudProviderReadiness ToReadiness(CloudCredentialState credential) => credential switch
    {
        CloudCredentialState.Missing => CloudProviderReadiness.AuthenticationRequired,
        CloudCredentialState.StoreUnavailable => CloudProviderReadiness.Faulted,
        CloudCredentialState.Faulted => CloudProviderReadiness.Faulted,
        _ => CloudProviderReadiness.Checking
    };

    private static CloudProviderReadiness ToReadiness(CloudSyncFailure failure) => failure.Kind switch
    {
        CloudSyncFailureKind.Validation => CloudProviderReadiness.NeedsConfiguration,
        CloudSyncFailureKind.CredentialMissing or CloudSyncFailureKind.Authentication =>
            CloudProviderReadiness.AuthenticationRequired,
        CloudSyncFailureKind.Network => CloudProviderReadiness.Unavailable,
        // 内容冲突不是连接故障：保持 Ready，允许用户显式 ResolveConflict / 再次 SyncNow。
        CloudSyncFailureKind.RemoteConflict => CloudProviderReadiness.Ready,
        _ => CloudProviderReadiness.Faulted
    };

    private void OnConfigChanged(object? sender, ConfigChangedEventArgs args)
    {
        if (!IsUnderConfigRoot(args.Path))
        {
            return;
        }

        var snapshot = Current;
        if (snapshot.Initialization != CloudSyncInitializationState.Ready ||
            !snapshot.Configuration.Enabled ||
            snapshot.Readiness != CloudProviderReadiness.Ready ||
            snapshot.Operation is not null)
        {
            return;
        }

        ScheduleAutoSync(AutoSyncDebounce);
    }

    private void ScheduleAutoSync(TimeSpan delay)
    {
        CancellationToken token;
        lock (_autoSyncGate)
        {
            _autoSyncCts?.Cancel();
            _autoSyncCts?.Dispose();
            _autoSyncCts = new CancellationTokenSource();
            token = _autoSyncCts.Token;
        }

        _ = RunAutoSyncAsync(delay, token);
    }

    private void CancelPendingAutoSync()
    {
        lock (_autoSyncGate)
        {
            _autoSyncCts?.Cancel();
            _autoSyncCts?.Dispose();
            _autoSyncCts = null;
        }
    }

    private async Task RunAutoSyncAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        var gateHeld = false;
        try
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            var intentVersion = Volatile.Read(ref _intentVersion);
            if (!CanRunAutoSync(Current) ||
                !await _operationGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            gateHeld = true;
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsLatestIntent(intentVersion) || !CanRunAutoSync(Current))
            {
                return;
            }

            PublishIfLatest(intentVersion, Current with
            {
                Operation = new CloudSyncOperation(intentVersion, CloudSyncOperationKind.SyncNow, DateTimeOffset.UtcNow),
                LastFailure = null
            });
            await SyncNowCoreAsync(intentVersion, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Automatic cloud config sync failed");
        }
        finally
        {
            if (gateHeld)
            {
                _operationGate.Release();
            }
        }
    }

    private static bool CanRunAutoSync(CloudConfigSyncSnapshot snapshot) =>
        snapshot.Initialization == CloudSyncInitializationState.Ready &&
        snapshot.Configuration.Enabled &&
        snapshot.Readiness == CloudProviderReadiness.Ready &&
        snapshot.Operation is null;

    private bool IsUnderConfigRoot(string path)
    {
        var root = Path.GetFullPath(_appData.ConfigRootPath);
        var fullPath = Path.GetFullPath(path);
        return fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
               string.Equals(fullPath, root, StringComparison.Ordinal);
    }

    private async Task SaveSettingsAsync(AppSettings settings)
    {
        using var suppression = _configChangeSignal.Suppress();
        await _appSettings.SaveAsync(settings).ConfigureAwait(false);
    }
}
