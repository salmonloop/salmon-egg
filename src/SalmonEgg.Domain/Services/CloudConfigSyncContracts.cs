using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models;

namespace SalmonEgg.Domain.Services;

public enum CloudSyncInitializationState
{
    NotStarted,
    Loading,
    Ready
}

public enum CloudCredentialState
{
    Unknown,
    Checking,
    NotRequired,
    Available,
    Missing,
    StoreUnavailable,
    Faulted
}

public enum CloudProviderReadiness
{
    Unknown,
    Disabled,
    NeedsConfiguration,
    Checking,
    Ready,
    AuthenticationRequired,
    Unavailable,
    Faulted
}

public enum CloudTransferPhase
{
    Idle,
    Syncing,
    Succeeded,
    Failed
}

public enum CloudTransferOutcome
{
    None,
    Uploaded,
    Restored,
    ConflictRemoteApplied
}

public enum CloudSyncOperationKind
{
    Initialize,
    ApplyAndActivate,
    SyncNow,
    Disable,
    ForgetProvider
}

public enum CloudSyncFailureKind
{
    Validation,
    CredentialMissing,
    CredentialStoreUnavailable,
    Authentication,
    Network,
    RemoteConflict,
    LocalPackage,
    Unknown
}

public enum CloudSecretUpdateKind
{
    KeepExisting,
    Replace,
    Clear
}

public sealed record CloudSecretUpdate(CloudSecretUpdateKind Kind, string? Value = null)
{
    public static CloudSecretUpdate KeepExisting() => new(CloudSecretUpdateKind.KeepExisting);

    public static CloudSecretUpdate Replace(string value) =>
        new(CloudSecretUpdateKind.Replace, value ?? throw new ArgumentNullException(nameof(value)));

    public static CloudSecretUpdate Clear() => new(CloudSecretUpdateKind.Clear);
}

public sealed record CloudSyncFailure(CloudSyncFailureKind Kind, string Message);

public sealed record CloudSyncConfiguration(
    bool Enabled,
    string ProviderId,
    long Revision,
    IReadOnlyDictionary<string, string> Options)
{
    public static CloudSyncConfiguration Disabled() =>
        new(false, string.Empty, 0, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
}

public sealed record CloudTransferSuccess(
    CloudTransferOutcome Outcome,
    DateTimeOffset CompletedAt,
    string? RemoteETag = null,
    string? BackupPath = null);

public sealed record CloudTransferState(
    CloudTransferPhase Phase,
    CloudTransferSuccess? LastSuccess = null,
    CloudSyncFailure? Failure = null);

public sealed record CloudSyncOperation(
    long IntentVersion,
    CloudSyncOperationKind Kind,
    DateTimeOffset StartedAt);

public sealed record CloudConfigSyncSnapshot(
    long Version,
    CloudSyncInitializationState Initialization,
    CloudSyncConfiguration Configuration,
    CloudCredentialState Credential,
    CloudProviderReadiness Readiness,
    CloudTransferState Transfer,
    CloudSyncOperation? Operation,
    CloudSyncFailure? LastFailure)
{
    public static CloudConfigSyncSnapshot Initial { get; } = new(
        0,
        CloudSyncInitializationState.NotStarted,
        CloudSyncConfiguration.Disabled(),
        CloudCredentialState.Unknown,
        CloudProviderReadiness.Unknown,
        new CloudTransferState(CloudTransferPhase.Idle),
        null,
        null);
}

public sealed record CloudProviderDraft(
    string ProviderId,
    IReadOnlyDictionary<string, string> Options,
    IReadOnlyDictionary<string, CloudSecretUpdate> Secrets,
    bool IncludeSecrets = true);

public sealed record CloudProviderValidationResult(bool Succeeded, CloudSyncFailure? Failure = null)
{
    public static CloudProviderValidationResult Success() => new(true);

    public static CloudProviderValidationResult Failed(string message) =>
        new(false, new CloudSyncFailure(CloudSyncFailureKind.Validation, message));
}

public sealed record CloudCredentialInspection(CloudCredentialState State, CloudSyncFailure? Failure = null);

public sealed record CloudProviderSessionResult(
    ICloudConfigStorageSession? Session,
    CloudCredentialState Credential,
    CloudSyncFailure? Failure = null)
{
    public bool Succeeded => Session is not null && Failure is null;

    public static CloudProviderSessionResult Success(
        ICloudConfigStorageSession session,
        CloudCredentialState credential) => new(session, credential);

    public static CloudProviderSessionResult Failed(
        CloudCredentialState credential,
        CloudSyncFailure failure) => new(null, credential, failure);
}

public sealed record CloudConfigRemoteFile(byte[] Content, string? ETag, DateTimeOffset? LastModifiedUtc);

public enum CloudConfigUploadStatus
{
    Uploaded,
    PreconditionFailed,
    Failed
}

public sealed record CloudConfigUploadResult(
    CloudConfigUploadStatus Status,
    string? ETag = null,
    CloudSyncFailure? Failure = null)
{
    public static CloudConfigUploadResult Uploaded(string? etag) => new(CloudConfigUploadStatus.Uploaded, etag);

    public static CloudConfigUploadResult PreconditionFailed(string message) =>
        new(
            CloudConfigUploadStatus.PreconditionFailed,
            Failure: new CloudSyncFailure(CloudSyncFailureKind.RemoteConflict, message));

    public static CloudConfigUploadResult Failed(CloudSyncFailure failure) =>
        new(CloudConfigUploadStatus.Failed, Failure: failure);
}

public interface ICloudConfigStorageSession
{
    Task<CloudConfigRemoteFile?> TryDownloadAsync(CancellationToken cancellationToken = default);

    Task<CloudConfigUploadResult> UploadAsync(
        byte[] content,
        string? expectedETag,
        CancellationToken cancellationToken = default);
}

public interface ICloudConfigStorageProvider
{
    CloudConfigProviderDescriptor Descriptor { get; }

    CloudProviderValidationResult Validate(IReadOnlyDictionary<string, string> options);

    Task<CloudCredentialInspection> InspectCredentialAsync(
        IReadOnlyDictionary<string, string> options,
        CancellationToken cancellationToken = default);

    Task<CloudProviderSessionResult> CreateSessionAsync(
        IReadOnlyDictionary<string, string> options,
        IReadOnlyDictionary<string, CloudSecretUpdate> secrets,
        bool interactive,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, CloudSecretUpdate>> ResolveSecretUpdatesAsync(
        IReadOnlyDictionary<string, CloudSecretUpdate> secrets,
        CancellationToken cancellationToken = default);

    Task<ICloudSecretUpdateTransaction> BeginSecretUpdateAsync(
        IReadOnlyDictionary<string, CloudSecretUpdate> secrets,
        CancellationToken cancellationToken = default);

    Task ForgetCredentialsAsync(CancellationToken cancellationToken = default);
}

public interface ICloudSecretUpdateTransaction : IAsyncDisposable
{
    void Complete();
}

public interface ICloudConfigSyncCoordinator
{
    IReadOnlyList<CloudConfigProviderDescriptor> Providers { get; }

    CloudConfigSyncSnapshot Current { get; }

    event EventHandler<CloudConfigSyncSnapshot>? SnapshotChanged;

    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task ApplyAndActivateAsync(CloudProviderDraft draft, CancellationToken cancellationToken = default);

    Task SyncNowAsync(CancellationToken cancellationToken = default);

    Task DisableAsync(CancellationToken cancellationToken = default);

    Task ForgetProviderAsync(string providerId, CancellationToken cancellationToken = default);

    Task<CloudCredentialInspection> InspectCredentialAsync(
        string providerId,
        IReadOnlyDictionary<string, string> options,
        CancellationToken cancellationToken = default);
}

public enum ConfigChangeKind
{
    Written,
    Deleted
}

public sealed record ConfigChangedEventArgs(string Path, ConfigChangeKind Kind);

public interface IConfigChangeSignal
{
    event EventHandler<ConfigChangedEventArgs>? Changed;

    bool IsSuppressed { get; }

    IDisposable Suppress();

    void NotifyChanged(string path, ConfigChangeKind kind);
}
