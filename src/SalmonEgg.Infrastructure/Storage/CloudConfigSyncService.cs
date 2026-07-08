using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Infrastructure.Storage;

public sealed class CloudConfigSyncService : ICloudConfigSyncService
{
    private static readonly TimeSpan AutoSyncDebounce = TimeSpan.FromSeconds(2);

    private readonly IAppSettingsService _appSettings;
    private readonly IReadOnlyDictionary<string, ICloudConfigStorageProvider> _providers;
    private readonly ConfigSyncPackageService _packageService;
    private readonly CloudConfigSyncStateStore _stateStore;
    private readonly IConfigChangeSignal _configChangeSignal;
    private readonly IAppDataService _appData;
    private readonly ILogger<CloudConfigSyncService> _logger;
    private readonly SemaphoreSlim _syncGate = new(1, 1);
    private CancellationTokenSource? _autoSyncCts;

    public CloudConfigSyncService(
        IAppSettingsService appSettings,
        IEnumerable<ICloudConfigStorageProvider> providers,
        ConfigSyncPackageService packageService,
        CloudConfigSyncStateStore stateStore,
        IConfigChangeSignal configChangeSignal,
        IAppDataService appData,
        ILogger<CloudConfigSyncService> logger)
    {
        _appSettings = appSettings ?? throw new ArgumentNullException(nameof(appSettings));
        if (providers is null) throw new ArgumentNullException(nameof(providers));
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

    public async Task<CloudConfigSyncResult> InitializeAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _appSettings.LoadAsync().ConfigureAwait(false);
        if (!settings.CloudConfigSync.Enabled)
        {
            return CloudConfigSyncResult.Disabled();
        }

        return await SyncNowAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<CloudConfigSyncResult> AuthorizeAndSyncAsync(string providerId, CancellationToken cancellationToken = default)
    {
        if (!TryGetProvider(providerId, out var provider))
        {
            return CloudConfigSyncResult.NotConfigured(providerId);
        }

        var authorization = await provider.EnsureAuthorizedAsync(interactive: true, cancellationToken).ConfigureAwait(false);
        if (!authorization.Succeeded)
        {
            return authorization.RequiresInteraction
                ? CloudConfigSyncResult.NotAuthorized(provider.Descriptor.ProviderId)
                : CloudConfigSyncResult.Failed(provider.Descriptor.ProviderId, authorization.UserMessage ?? "Cloud authorization failed.");
        }

        var settings = await _appSettings.LoadAsync().ConfigureAwait(false);
        await TrySignOutPreviousProviderAsync(
            settings.CloudConfigSync.ProviderId,
            provider.Descriptor.ProviderId,
            cancellationToken).ConfigureAwait(false);
        settings.CloudConfigSync = new CloudConfigSyncSettings
        {
            Enabled = true,
            ProviderId = provider.Descriptor.ProviderId,
            IncludeSecrets = true,
            ProviderOptions = settings.CloudConfigSync.ProviderOptions
        };
        await _appSettings.SaveAsync(settings).ConfigureAwait(false);

        return await SyncNowAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task TrySignOutPreviousProviderAsync(
        string? previousProviderId,
        string nextProviderId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(previousProviderId) ||
            string.Equals(previousProviderId, nextProviderId, StringComparison.OrdinalIgnoreCase) ||
            !TryGetProvider(previousProviderId, out var previousProvider))
        {
            return;
        }

        try
        {
            await previousProvider.SignOutAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Failed to sign out previous cloud config provider {ProviderId}",
                previousProvider.Descriptor.ProviderId);
        }
    }

    public async Task<CloudConfigSyncResult> ConfigureProviderAsync(
        string providerId,
        IReadOnlyDictionary<string, string> options,
        IReadOnlyDictionary<string, string> secrets,
        CancellationToken cancellationToken = default)
    {
        options ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        secrets ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!TryGetProvider(providerId, out var provider))
        {
            return CloudConfigSyncResult.NotConfigured(providerId);
        }

        if (provider is IConfigurableCloudConfigStorageProvider configurable)
        {
            var configuration = await configurable.ConfigureAsync(options, secrets, cancellationToken).ConfigureAwait(false);
            if (!configuration.Succeeded)
            {
                return CloudConfigSyncResult.Failed(provider.Descriptor.ProviderId, configuration.UserMessage ?? "Cloud provider configuration failed.");
            }
        }

        var settings = await _appSettings.LoadAsync().ConfigureAwait(false);
        settings.CloudConfigSync.ProviderOptions ??= new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        settings.CloudConfigSync.ProviderOptions[provider.Descriptor.ProviderId] = options
            .Where(option => !string.IsNullOrWhiteSpace(option.Key) && option.Value is not null)
            .ToDictionary(
                option => option.Key.Trim(),
                option => option.Value.Trim(),
                StringComparer.OrdinalIgnoreCase);
        await _appSettings.SaveAsync(settings).ConfigureAwait(false);
        return new CloudConfigSyncResult(CloudConfigSyncStatus.Disabled, provider.Descriptor.ProviderId);
    }

    public async Task<CloudConfigProviderConfigurationStatus> GetProviderConfigurationStatusAsync(
        string providerId,
        IReadOnlyDictionary<string, string> options,
        CancellationToken cancellationToken = default)
    {
        options ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!TryGetProvider(providerId, out var provider))
        {
            return CloudConfigProviderConfigurationStatus.Missing("Cloud provider is not configured.");
        }

        if (provider is IConfigurableCloudConfigStorageProvider configurable)
        {
            return await configurable.GetConfigurationStatusAsync(options, cancellationToken).ConfigureAwait(false);
        }

        return CloudConfigProviderConfigurationStatus.NotRequired();
    }

    public async Task<CloudConfigSyncResult> SyncNowAsync(CancellationToken cancellationToken = default)
    {
        await _syncGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var settings = await _appSettings.LoadAsync().ConfigureAwait(false);
            var syncSettings = settings.CloudConfigSync;
            if (!syncSettings.Enabled)
            {
                return CloudConfigSyncResult.Disabled();
            }

            if (!TryGetProvider(syncSettings.ProviderId, out var provider))
            {
                return CloudConfigSyncResult.NotConfigured(syncSettings.ProviderId);
            }

            var authorization = await provider.EnsureAuthorizedAsync(interactive: false, cancellationToken).ConfigureAwait(false);
            if (!authorization.Succeeded)
            {
                return authorization.RequiresInteraction
                    ? CloudConfigSyncResult.NotAuthorized(provider.Descriptor.ProviderId)
                    : CloudConfigSyncResult.Failed(provider.Descriptor.ProviderId, authorization.UserMessage ?? "Cloud authorization failed.");
            }

            var state = await _stateStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            var remote = await provider.TryDownloadAsync(cancellationToken).ConfigureAwait(false);
            if (remote is null)
            {
                return await UploadLocalAsync(provider, state, expectedETag: null, syncSettings.IncludeSecrets, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (string.IsNullOrWhiteSpace(state.RemoteETag) ||
                !string.Equals(state.ProviderId, provider.Descriptor.ProviderId, StringComparison.OrdinalIgnoreCase))
            {
                return await RestoreRemoteAsync(provider, state, remote, CloudConfigSyncStatus.Restored, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (!string.Equals(state.RemoteETag, remote.ETag, StringComparison.Ordinal))
            {
                return await RestoreRemoteAsync(provider, state, remote, CloudConfigSyncStatus.ConflictRemoteApplied, cancellationToken)
                    .ConfigureAwait(false);
            }

            return await UploadLocalAsync(provider, state, remote.ETag, syncSettings.IncludeSecrets, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cloud config sync failed");
            return CloudConfigSyncResult.Failed(null, "Cloud config sync failed.");
        }
        finally
        {
            _syncGate.Release();
        }
    }

    public async Task<CloudConfigSyncResult> DisconnectAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _appSettings.LoadAsync().ConfigureAwait(false);
        var providerId = settings.CloudConfigSync.ProviderId;
        if (TryGetProvider(providerId, out var provider))
        {
            await provider.SignOutAsync(cancellationToken).ConfigureAwait(false);
        }

        settings.CloudConfigSync.Enabled = false;
        settings.CloudConfigSync.ProviderId = string.Empty;
        await _appSettings.SaveAsync(settings).ConfigureAwait(false);
        await _stateStore.ClearAsync(cancellationToken).ConfigureAwait(false);
        return new CloudConfigSyncResult(CloudConfigSyncStatus.SignedOut, providerId);
    }

    private async Task<CloudConfigSyncResult> UploadLocalAsync(
        ICloudConfigStorageProvider provider,
        CloudConfigSyncState state,
        string? expectedETag,
        bool includeSecrets,
        CancellationToken cancellationToken)
    {
        var package = await _packageService.CreatePackageAsync(includeSecrets, cancellationToken).ConfigureAwait(false);
        var upload = await provider.UploadAsync(package, expectedETag, cancellationToken).ConfigureAwait(false);
        if (upload.Status == CloudConfigUploadStatus.PreconditionFailed)
        {
            var remote = await provider.TryDownloadAsync(cancellationToken).ConfigureAwait(false);
            if (remote is null)
            {
                return CloudConfigSyncResult.Failed(provider.Descriptor.ProviderId, upload.UserMessage ?? "Cloud upload conflict could not be resolved.");
            }

            return await RestoreRemoteAsync(provider, state, remote, CloudConfigSyncStatus.ConflictRemoteApplied, cancellationToken)
                .ConfigureAwait(false);
        }

        if (upload.Status != CloudConfigUploadStatus.Uploaded)
        {
            return CloudConfigSyncResult.Failed(provider.Descriptor.ProviderId, upload.UserMessage ?? "Cloud upload failed.");
        }

        var now = DateTimeOffset.UtcNow;
        state.ProviderId = provider.Descriptor.ProviderId;
        state.RemoteETag = upload.ETag ?? string.Empty;
        state.LastSyncUtc = now.ToString("O");
        await _stateStore.SaveAsync(state, cancellationToken).ConfigureAwait(false);
        return new CloudConfigSyncResult(
            CloudConfigSyncStatus.Uploaded,
            provider.Descriptor.ProviderId,
            state.RemoteETag,
            now);
    }

    private async Task<CloudConfigSyncResult> RestoreRemoteAsync(
        ICloudConfigStorageProvider provider,
        CloudConfigSyncState state,
        CloudConfigRemoteFile remote,
        CloudConfigSyncStatus status,
        CancellationToken cancellationToken)
    {
        var backupPath = await _packageService.RestorePackageAsync(remote.Content, cancellationToken).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        state.ProviderId = provider.Descriptor.ProviderId;
        state.RemoteETag = remote.ETag ?? string.Empty;
        state.LastSyncUtc = now.ToString("O");
        await _stateStore.SaveAsync(state, cancellationToken).ConfigureAwait(false);
        return new CloudConfigSyncResult(
            status,
            provider.Descriptor.ProviderId,
            state.RemoteETag,
            now,
            Directory.Exists(backupPath) ? backupPath : null);
    }

    private bool TryGetProvider(string? providerId, out ICloudConfigStorageProvider provider)
    {
        provider = null!;
        return !string.IsNullOrWhiteSpace(providerId) &&
               _providers.TryGetValue(providerId.Trim(), out provider!);
    }

    private void OnConfigChanged(object? sender, ConfigChangedEventArgs args)
    {
        if (!IsUnderConfigRoot(args.Path))
        {
            return;
        }

        _autoSyncCts?.Cancel();
        _autoSyncCts?.Dispose();
        _autoSyncCts = new CancellationTokenSource();
        var token = _autoSyncCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(AutoSyncDebounce, token).ConfigureAwait(false);
                await SyncNowAsync(token).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Automatic cloud config sync failed");
            }
        }, token);
    }

    private bool IsUnderConfigRoot(string path)
    {
        var root = Path.GetFullPath(_appData.ConfigRootPath);
        var fullPath = Path.GetFullPath(path);
        return fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
               string.Equals(fullPath, root, StringComparison.Ordinal);
    }
}
