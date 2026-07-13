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
        IConfigChangeSignal configChangeSignal,
        IAppDataService appData,
        ILogger<CloudConfigSyncCoordinator> logger)
    {
        _appSettings = appSettings ?? throw new ArgumentNullException(nameof(appSettings));
        ArgumentNullException.ThrowIfNull(providers);
        _providers = providers.ToDictionary(provider => provider.Descriptor.ProviderId, StringComparer.OrdinalIgnoreCase);
        _packageService = packageService ?? throw new ArgumentNullException(nameof(packageService));
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
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
        var intentVersion = Interlocked.Increment(ref _intentVersion);
        var intentCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (_intentGate)
        {
            _activeIntentCts?.Cancel();
            _activeIntentCts = intentCts;
        }

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
                Transfer = await LoadIdleTransferStateAsync(cancellationToken).ConfigureAwait(false),
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
            Initialization = CloudSyncInitializationState.Ready,
            Configuration = configuration,
            Credential = sessionResult.Credential,
            Readiness = CloudProviderReadiness.Checking,
            Transfer = Current.Transfer with { Phase = CloudTransferPhase.Syncing, Failure = null }
        });
        await SynchronizeAndPublishAsync(
            intentVersion,
            configuration,
            sessionResult.Session!,
            settings.CloudConfigSync.IncludeSecrets,
            cancellationToken).ConfigureAwait(false);
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

        var transfer = await SynchronizeAsync(
            sessionResult.Session!,
            provider.Descriptor.ProviderId,
            draft.IncludeSecrets,
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

        // A successful synchronization may have restored the authoritative remote app settings.
        // Reload before committing the cloud-sync subtree so those restored values are preserved.
        var settings = await _appSettings.LoadAsync().ConfigureAwait(false);
        var nextRevision = Math.Max(settings.CloudConfigSync.Revision, Current.Configuration.Revision) + 1;
        var candidate = new CloudSyncConfiguration(true, provider.Descriptor.ProviderId, nextRevision, normalizedOptions);
        var previous = settings.CloudConfigSync;
        var providerOptions = CloneProviderOptions(previous.ProviderOptions);
        providerOptions[candidate.ProviderId] = new Dictionary<string, string>(normalizedOptions, StringComparer.OrdinalIgnoreCase);
        settings.CloudConfigSync = new CloudConfigSyncSettings
        {
            Enabled = true,
            ProviderId = candidate.ProviderId,
            Revision = candidate.Revision,
            IncludeSecrets = draft.IncludeSecrets,
            ProviderOptions = providerOptions
        };

        await SaveSettingsAsync(settings).ConfigureAwait(false);
        try
        {
            await provider.CommitSecretsAsync(draft.Secrets, cancellationToken).ConfigureAwait(false);
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
        var transfer = await SynchronizeAsync(
            session,
            configuration.ProviderId,
            includeSecrets,
            cancellationToken).ConfigureAwait(false);
        PublishIfLatest(intentVersion, Current with
        {
            Initialization = CloudSyncInitializationState.Ready,
            Configuration = configuration,
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
        CancellationToken cancellationToken)
    {
        try
        {
            var state = await _stateStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            var previousSuccess = CreateLastSuccess(state);
            var remote = await session.TryDownloadAsync(cancellationToken).ConfigureAwait(false);
            if (remote is null)
            {
                return await UploadLocalAsync(session, state, providerId, null, includeSecrets, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (string.IsNullOrWhiteSpace(state.RemoteETag) ||
                !string.Equals(state.ProviderId, providerId, StringComparison.OrdinalIgnoreCase))
            {
                return await RestoreRemoteAsync(state, providerId, remote, CloudTransferOutcome.Restored, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (!string.Equals(state.RemoteETag, remote.ETag, StringComparison.Ordinal))
            {
                return await RestoreRemoteAsync(
                    state,
                    providerId,
                    remote,
                    CloudTransferOutcome.ConflictRemoteApplied,
                    cancellationToken).ConfigureAwait(false);
            }

            return await UploadLocalAsync(
                session,
                state,
                providerId,
                remote.ETag,
                includeSecrets,
                cancellationToken).ConfigureAwait(false);
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

    private async Task<CloudTransferState> UploadLocalAsync(
        ICloudConfigStorageSession session,
        CloudConfigSyncState state,
        string providerId,
        string? expectedETag,
        bool includeSecrets,
        CancellationToken cancellationToken)
    {
        var package = await _packageService.CreatePackageAsync(includeSecrets, cancellationToken).ConfigureAwait(false);
        var upload = await session.UploadAsync(package, expectedETag, cancellationToken).ConfigureAwait(false);
        if (upload.Status == CloudConfigUploadStatus.PreconditionFailed)
        {
            var remote = await session.TryDownloadAsync(cancellationToken).ConfigureAwait(false);
            if (remote is null)
            {
                return new CloudTransferState(
                    CloudTransferPhase.Failed,
                    CreateLastSuccess(state),
                    upload.Failure ?? new CloudSyncFailure(CloudSyncFailureKind.RemoteConflict, "Remote conflict could not be resolved."));
            }

            return await RestoreRemoteAsync(
                state,
                providerId,
                remote,
                CloudTransferOutcome.ConflictRemoteApplied,
                cancellationToken).ConfigureAwait(false);
        }

        if (upload.Status != CloudConfigUploadStatus.Uploaded)
        {
            return new CloudTransferState(
                CloudTransferPhase.Failed,
                CreateLastSuccess(state),
                upload.Failure ?? new CloudSyncFailure(CloudSyncFailureKind.Network, "Cloud upload failed."));
        }

        var now = DateTimeOffset.UtcNow;
        state.ProviderId = providerId;
        state.RemoteETag = upload.ETag ?? string.Empty;
        state.LastSyncUtc = now.ToString("O");
        await _stateStore.SaveAsync(state, cancellationToken).ConfigureAwait(false);
        return new CloudTransferState(
            CloudTransferPhase.Succeeded,
            new CloudTransferSuccess(CloudTransferOutcome.Uploaded, now, state.RemoteETag));
    }

    private async Task<CloudTransferState> RestoreRemoteAsync(
        CloudConfigSyncState state,
        string providerId,
        CloudConfigRemoteFile remote,
        CloudTransferOutcome outcome,
        CancellationToken cancellationToken)
    {
        var backupPath = await _packageService.RestorePackageAsync(remote.Content, cancellationToken).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        state.ProviderId = providerId;
        state.RemoteETag = remote.ETag ?? string.Empty;
        state.LastSyncUtc = now.ToString("O");
        await _stateStore.SaveAsync(state, cancellationToken).ConfigureAwait(false);
        return new CloudTransferState(
            CloudTransferPhase.Succeeded,
            new CloudTransferSuccess(
                outcome,
                now,
                state.RemoteETag,
                Directory.Exists(backupPath) ? backupPath : null));
    }

    private async Task<CloudTransferState> LoadIdleTransferStateAsync(CancellationToken cancellationToken)
    {
        var state = await _stateStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        return new CloudTransferState(CloudTransferPhase.Idle, CreateLastSuccess(state));
    }

    private void PublishConfigurationFailure(
        long intentVersion,
        CloudSyncConfiguration configuration,
        CloudProviderReadiness readiness,
        CloudSyncFailure failure,
        CloudCredentialState? credential = null)
    {
        PublishIfLatest(intentVersion, Current with
        {
            Initialization = CloudSyncInitializationState.Ready,
            Configuration = configuration,
            Credential = credential ?? ToCredentialState(failure),
            Readiness = readiness,
            Transfer = Current.Transfer with { Phase = CloudTransferPhase.Failed, Failure = failure },
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

        CancellationToken token;
        lock (_autoSyncGate)
        {
            _autoSyncCts?.Cancel();
            _autoSyncCts?.Dispose();
            _autoSyncCts = new CancellationTokenSource();
            token = _autoSyncCts.Token;
        }

        _ = RunAutoSyncAsync(token);
    }

    private async Task RunAutoSyncAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(AutoSyncDebounce, cancellationToken).ConfigureAwait(false);
            await SyncNowAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Automatic cloud config sync failed");
        }
    }

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
