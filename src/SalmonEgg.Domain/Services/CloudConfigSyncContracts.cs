using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models;

namespace SalmonEgg.Domain.Services;

public enum CloudConfigSyncStatus
{
    Disabled,
    NotConfigured,
    NotAuthorized,
    Uploading,
    Uploaded,
    Restoring,
    Restored,
    ConflictRemoteApplied,
    SignedOut,
    Failed
}

public sealed record CloudConfigSyncResult(
    CloudConfigSyncStatus Status,
    string? ProviderId = null,
    string? RemoteETag = null,
    DateTimeOffset? LastSyncUtc = null,
    string? BackupPath = null,
    string? UserMessage = null)
{
    public static CloudConfigSyncResult Disabled() => new(CloudConfigSyncStatus.Disabled);

    public static CloudConfigSyncResult NotConfigured(string providerId) =>
        new(CloudConfigSyncStatus.NotConfigured, providerId);

    public static CloudConfigSyncResult NotAuthorized(string providerId) =>
        new(CloudConfigSyncStatus.NotAuthorized, providerId);

    public static CloudConfigSyncResult Failed(string? providerId, string userMessage) =>
        new(CloudConfigSyncStatus.Failed, providerId, UserMessage: userMessage);
}

public sealed record CloudConfigAuthorizationResult(
    bool Succeeded,
    bool RequiresInteraction,
    string? UserMessage = null)
{
    public static CloudConfigAuthorizationResult Success() => new(true, false);

    public static CloudConfigAuthorizationResult InteractionRequired(string? message = null) => new(false, true, message);

    public static CloudConfigAuthorizationResult Failed(string message) => new(false, false, message);
}

public sealed record CloudConfigRemoteFile(byte[] Content, string? ETag, DateTimeOffset? LastModifiedUtc);

public enum CloudConfigUploadStatus
{
    Uploaded,
    PreconditionFailed,
    Failed
}

public sealed record CloudConfigUploadResult(CloudConfigUploadStatus Status, string? ETag = null, string? UserMessage = null)
{
    public static CloudConfigUploadResult Uploaded(string? etag) => new(CloudConfigUploadStatus.Uploaded, etag);

    public static CloudConfigUploadResult PreconditionFailed(string? message = null) =>
        new(CloudConfigUploadStatus.PreconditionFailed, UserMessage: message);

    public static CloudConfigUploadResult Failed(string message) => new(CloudConfigUploadStatus.Failed, UserMessage: message);
}

public interface ICloudConfigStorageProvider
{
    CloudConfigProviderDescriptor Descriptor { get; }

    Task<CloudConfigAuthorizationResult> EnsureAuthorizedAsync(bool interactive, CancellationToken cancellationToken = default);

    Task SignOutAsync(CancellationToken cancellationToken = default);

    Task<CloudConfigRemoteFile?> TryDownloadAsync(CancellationToken cancellationToken = default);

    Task<CloudConfigUploadResult> UploadAsync(
        byte[] content,
        string? expectedETag,
        CancellationToken cancellationToken = default);
}

public interface ICloudConfigSyncService
{
    IReadOnlyList<CloudConfigProviderDescriptor> Providers { get; }

    Task<CloudConfigSyncResult> InitializeAsync(CancellationToken cancellationToken = default);

    Task<CloudConfigSyncResult> AuthorizeAndSyncAsync(string providerId, CancellationToken cancellationToken = default);

    Task<CloudConfigSyncResult> SyncNowAsync(CancellationToken cancellationToken = default);

    Task<CloudConfigSyncResult> DisconnectAsync(CancellationToken cancellationToken = default);
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
