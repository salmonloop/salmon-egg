using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Localization;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Resources;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Services;

namespace SalmonEgg.Presentation.ViewModels.Settings;

public partial class CloudConfigSettingsViewModel : ObservableObject
{
    private const string OneDriveProviderId = "onedrive";
    private const string WebDavProviderId = "webdav";
    private const string S3ProviderId = "s3";
    private const string WebDavFileUrlOptionKey = "file_url";
    private const string WebDavUsernameOptionKey = "username";
    private const string WebDavPasswordSecretKey = "password";
    private const string S3EndpointOptionKey = "endpoint";
    private const string S3BucketOptionKey = "bucket";
    private const string S3RegionOptionKey = "region";
    private const string S3ObjectKeyOptionKey = "object_key";
    private const string S3ForcePathStyleOptionKey = "force_path_style";
    private const string S3AccessKeyIdSecretKey = "access_key_id";
    private const string S3SecretAccessKeySecretKey = "secret_access_key";
    private const string DefaultS3Region = "us-east-1";
    private const string DefaultS3ObjectKey = CloudConfigSyncDefaults.RemotePackagePath;

    private readonly ICloudConfigSyncCoordinator _coordinator;
    private readonly IUiInteractionService _ui;
    private readonly IUiDispatcher _dispatcher;
    private readonly IStringLocalizer<CoreStrings> _localizer;
    private CancellationTokenSource? _credentialRefreshCts;
    private long _credentialRefreshVersion;
    private CloudConfigSyncSnapshot _snapshot;
    private CloudCredentialState _draftCredential = CloudCredentialState.Unknown;

    public CloudConfigSettingsViewModel(
        ICloudConfigSyncCoordinator coordinator,
        IUiInteractionService ui,
        IUiDispatcher dispatcher,
        IStringLocalizer<CoreStrings> localizer)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _snapshot = coordinator.Current;
        foreach (var provider in coordinator.Providers.OrderBy(provider => provider.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            Providers.Add(new CloudConfigProviderOptionViewModel(
                provider.ProviderId,
                provider.DisplayName,
                provider.IsConfigured));
        }

        LoadDraftFromSnapshot();
        _coordinator.SnapshotChanged += OnSnapshotChanged;
    }

    public ObservableCollection<CloudConfigProviderOptionViewModel> Providers { get; } = new();

    public bool IsBusy => _snapshot.Operation is not null;

    public bool IsChecking =>
        _snapshot.Initialization == CloudSyncInitializationState.Loading ||
        _snapshot.Credential == CloudCredentialState.Checking ||
        _snapshot.Readiness == CloudProviderReadiness.Checking ||
        _draftCredential == CloudCredentialState.Checking;

    public bool CanRetryCredentialCheck =>
        !IsBusy && _draftCredential is CloudCredentialState.StoreUnavailable or CloudCredentialState.Faulted;

    public string RetryCredentialCheckText => _localizer["DataStorage_CloudSyncRetryConnection"];

    public bool IsEnabled => _snapshot.Configuration.Enabled;

    public bool HasActiveProvider => !string.IsNullOrWhiteSpace(_snapshot.Configuration.ProviderId);

    public bool CanSync =>
        IsEnabled && _snapshot.Readiness == CloudProviderReadiness.Ready && !IsBusy;

    public bool CanEdit => !IsBusy;

    public bool CanDisable => IsEnabled && !IsBusy;

    public bool CanForget => HasActiveProvider && !IsBusy;

    public bool CanApply =>
        !IsBusy &&
        _draftCredential is not (CloudCredentialState.Checking or CloudCredentialState.StoreUnavailable or CloudCredentialState.Faulted) &&
        string.IsNullOrEmpty(ValidationMessage);

    public bool IsOneDriveSelected => string.Equals(SelectedProviderId, OneDriveProviderId, StringComparison.OrdinalIgnoreCase);

    public bool IsWebDavSelected => string.Equals(SelectedProviderId, WebDavProviderId, StringComparison.OrdinalIgnoreCase);

    public bool IsS3Selected => string.Equals(SelectedProviderId, S3ProviderId, StringComparison.OrdinalIgnoreCase);

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);

    public bool HasUnsavedChanges => IsEditing && !DraftMatchesActiveConfiguration();

    public string StatusHeadline => _snapshot.Initialization switch
    {
        CloudSyncInitializationState.NotStarted or CloudSyncInitializationState.Loading =>
            _localizer["DataStorage_CloudSyncCheckingStatus"],
        _ when !_snapshot.Configuration.Enabled => _localizer["DataStorage_CloudSyncDisabledHeadline"],
        _ when _snapshot.Readiness == CloudProviderReadiness.Ready => string.Format(
            CultureInfo.CurrentCulture,
            _localizer["DataStorage_CloudSyncEnabledHeadline"],
            ActiveProviderDisplayName),
        _ when _snapshot.Readiness == CloudProviderReadiness.AuthenticationRequired =>
            _localizer["DataStorage_CloudSyncAuthenticationRequiredHeadline"],
        _ when _snapshot.Readiness == CloudProviderReadiness.NeedsConfiguration =>
            _localizer["DataStorage_CloudSyncNeedsConfigurationHeadline"],
        _ when _snapshot.Readiness == CloudProviderReadiness.Unavailable =>
            _localizer["DataStorage_CloudSyncUnavailableHeadline"],
        _ => _localizer["DataStorage_CloudSyncCheckingStatus"]
    };

    public string TransferStatusText
    {
        get
        {
            if (_snapshot.Transfer.Phase == CloudTransferPhase.Syncing)
            {
                return _localizer["DataStorage_CloudSyncComparing"];
            }

            if (_snapshot.Transfer.Phase == CloudTransferPhase.Failed)
            {
                return _localizer["DataStorage_CloudSyncTransferAttemptFailed"];
            }

            var success = _snapshot.Transfer.LastSuccess;
            if (success is null)
            {
                return _localizer["DataStorage_CloudSyncNeverSynced"];
            }

            var outcome = success.Outcome switch
            {
                CloudTransferOutcome.Uploaded => _localizer["DataStorage_CloudSyncUploadedLocal"],
                CloudTransferOutcome.Restored => _localizer["DataStorage_CloudSyncAppliedRemote"],
                CloudTransferOutcome.ConflictRemoteApplied => _localizer["DataStorage_CloudSyncAppliedRemoteConflict"],
                _ => _localizer["DataStorage_CloudSyncLastSucceeded"]
            };
            return string.Format(
                CultureInfo.CurrentCulture,
                _localizer["DataStorage_CloudSyncTransferWithTime"],
                outcome,
                success.CompletedAt.ToLocalTime());
        }
    }

    public string ErrorText => _snapshot.LastFailure?.Kind switch
    {
        CloudSyncFailureKind.CredentialMissing => _localizer["DataStorage_CloudSyncCredentialMissingAction"],
        CloudSyncFailureKind.CredentialStoreUnavailable => _localizer["DataStorage_CloudSyncCredentialStoreUnavailable"],
        CloudSyncFailureKind.Authentication => _localizer["DataStorage_CloudSyncAuthenticationRejected"],
        CloudSyncFailureKind.Network => _localizer["DataStorage_CloudSyncNetworkUnavailable"],
        CloudSyncFailureKind.Validation => _localizer["DataStorage_CloudSyncValidationFailed"],
        CloudSyncFailureKind.RemoteConflict => _localizer["DataStorage_CloudSyncConflictFailed"],
        CloudSyncFailureKind.LocalPackage => _localizer["DataStorage_CloudSyncLocalPackageFailed"],
        CloudSyncFailureKind.Unknown => _localizer["DataStorage_CloudSyncStatusFailed"],
        _ => string.Empty
    };

    public string CredentialStatusText => _draftCredential switch
    {
        CloudCredentialState.Checking => _localizer["DataStorage_CloudSyncCredentialChecking"],
        CloudCredentialState.Available => _localizer["DataStorage_CloudSyncCredentialAvailable"],
        CloudCredentialState.NotRequired => _localizer["DataStorage_CloudSyncCredentialNotRequired"],
        CloudCredentialState.Missing => _localizer["DataStorage_CloudSyncCredentialMissingAction"],
        CloudCredentialState.StoreUnavailable => _localizer["DataStorage_CloudSyncCredentialStoreUnavailable"],
        CloudCredentialState.Faulted => _localizer["DataStorage_CloudSyncCredentialCheckFailed"],
        _ => string.Empty
    };

    public string ActiveProviderDisplayName => GetProviderDisplayName(_snapshot.Configuration.ProviderId);

    public string ActiveRemoteTarget => FormatRemoteTarget(
        _snapshot.Configuration.ProviderId,
        _snapshot.Configuration.Options);

    public string ValidationMessage
    {
        get
        {
            if (IsOneDriveSelected)
            {
                return Providers.FirstOrDefault(provider => provider.ProviderId == OneDriveProviderId)?.IsConfigured == true
                    ? string.Empty
                    : _localizer["DataStorage_CloudSyncOneDriveUnavailable"];
            }

            if (IsWebDavSelected)
            {
                if (!IsAbsoluteHttpUrl(WebDavFileUrl))
                {
                    return _localizer["DataStorage_CloudSyncWebDavFileUrlInvalid"];
                }

                if (!string.IsNullOrWhiteSpace(WebDavUsername) &&
                    _draftCredential == CloudCredentialState.Missing &&
                    string.IsNullOrEmpty(WebDavPassword))
                {
                    return _localizer["DataStorage_CloudSyncWebDavCredentialsRequired"];
                }

                return string.Empty;
            }

            if (IsS3Selected)
            {
                if (!IsAbsoluteHttpUrl(S3Endpoint))
                {
                    return _localizer["DataStorage_CloudSyncS3EndpointInvalid"];
                }

                if (string.IsNullOrWhiteSpace(S3Bucket))
                {
                    return _localizer["DataStorage_CloudSyncS3BucketRequired"];
                }

                if (_draftCredential == CloudCredentialState.Missing &&
                    (string.IsNullOrWhiteSpace(S3AccessKeyId) || string.IsNullOrEmpty(S3SecretAccessKey)))
                {
                    return _localizer["DataStorage_CloudSyncS3CredentialsRequired"];
                }

                return string.Empty;
            }

            return _localizer["DataStorage_CloudSyncNeedsConfigurationHeadline"];
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    private bool _isEditing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOneDriveSelected))]
    [NotifyPropertyChangedFor(nameof(IsWebDavSelected))]
    [NotifyPropertyChangedFor(nameof(IsS3Selected))]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    [NotifyPropertyChangedFor(nameof(CanApply))]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    private string _selectedProviderId = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    [NotifyPropertyChangedFor(nameof(CanApply))]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    private string _webDavFileUrl = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    [NotifyPropertyChangedFor(nameof(CanApply))]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    private string _webDavUsername = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    [NotifyPropertyChangedFor(nameof(CanApply))]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    private string _webDavPassword = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    [NotifyPropertyChangedFor(nameof(CanApply))]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    private string _s3Endpoint = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    [NotifyPropertyChangedFor(nameof(CanApply))]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    private string _s3Bucket = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    private string _s3Region = DefaultS3Region;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    private string _s3ObjectKey = DefaultS3ObjectKey;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    private bool _s3ForcePathStyle = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    [NotifyPropertyChangedFor(nameof(CanApply))]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    private string _s3AccessKeyId = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    [NotifyPropertyChangedFor(nameof(CanApply))]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    private string _s3SecretAccessKey = string.Empty;

    partial void OnSelectedProviderIdChanged(string value) => ScheduleCredentialRefresh();

    partial void OnWebDavFileUrlChanged(string value) => ScheduleCredentialRefresh();

    partial void OnWebDavUsernameChanged(string value) => ScheduleCredentialRefresh();

    partial void OnS3EndpointChanged(string value) => ScheduleCredentialRefresh();

    partial void OnS3BucketChanged(string value) => ScheduleCredentialRefresh();

    [RelayCommand]
    private void Edit()
    {
        LoadDraftFromSnapshot();
        IsEditing = true;
        ScheduleCredentialRefresh();
    }

    [RelayCommand]
    private void CancelEdit()
    {
        LoadDraftFromSnapshot();
        IsEditing = false;
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        if (!CanApply)
        {
            return;
        }

        await _coordinator.ApplyAndActivateAsync(CreateDraft()).ConfigureAwait(true);
        if (_coordinator.Current.Readiness == CloudProviderReadiness.Ready &&
            _coordinator.Current.Transfer.Phase == CloudTransferPhase.Succeeded)
        {
            WebDavPassword = string.Empty;
            S3AccessKeyId = string.Empty;
            S3SecretAccessKey = string.Empty;
            IsEditing = false;
        }
    }

    [RelayCommand]
    private Task SyncNowAsync() => CanSync ? _coordinator.SyncNowAsync() : Task.CompletedTask;

    [RelayCommand]
    private Task DisableAsync() => CanDisable ? _coordinator.DisableAsync() : Task.CompletedTask;

    [RelayCommand]
    private async Task ForgetAsync()
    {
        if (!CanForget)
        {
            return;
        }

        var confirmed = await _ui.ConfirmAsync(
            _localizer["DataStorage_CloudSyncForgetTitle"],
            _localizer["DataStorage_CloudSyncForgetMessage"],
            _localizer["DataStorage_CloudSyncForgetPrimary"],
            _localizer["Common_Cancel"]).ConfigureAwait(true);
        if (confirmed)
        {
            await _coordinator.ForgetProviderAsync(_snapshot.Configuration.ProviderId).ConfigureAwait(true);
            IsEditing = false;
        }
    }

    [RelayCommand]
    private Task RetryCredentialCheckAsync() => RefreshCredentialAsync(TimeSpan.Zero);

    private CloudProviderDraft CreateDraft()
    {
        var options = CreateSelectedOptions();
        var secrets = new Dictionary<string, CloudSecretUpdate>(StringComparer.OrdinalIgnoreCase);
        if (IsWebDavSelected)
        {
            secrets[WebDavPasswordSecretKey] = string.IsNullOrEmpty(WebDavPassword)
                ? CloudSecretUpdate.KeepExisting()
                : CloudSecretUpdate.Replace(WebDavPassword);
        }
        else if (IsS3Selected)
        {
            secrets[S3AccessKeyIdSecretKey] = string.IsNullOrWhiteSpace(S3AccessKeyId)
                ? CloudSecretUpdate.KeepExisting()
                : CloudSecretUpdate.Replace(S3AccessKeyId.Trim());
            secrets[S3SecretAccessKeySecretKey] = string.IsNullOrEmpty(S3SecretAccessKey)
                ? CloudSecretUpdate.KeepExisting()
                : CloudSecretUpdate.Replace(S3SecretAccessKey);
        }

        return new CloudProviderDraft(SelectedProviderId, options, secrets);
    }

    private IReadOnlyDictionary<string, string> CreateSelectedOptions()
    {
        if (IsWebDavSelected)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [WebDavFileUrlOptionKey] = WebDavFileUrl.Trim(),
                [WebDavUsernameOptionKey] = WebDavUsername.Trim()
            };
        }

        if (IsS3Selected)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [S3EndpointOptionKey] = S3Endpoint.Trim(),
                [S3BucketOptionKey] = S3Bucket.Trim(),
                [S3RegionOptionKey] = S3Region.Trim(),
                [S3ObjectKeyOptionKey] = S3ObjectKey.Trim(),
                [S3ForcePathStyleOptionKey] = S3ForcePathStyle.ToString()
            };
        }

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private void OnSnapshotChanged(object? sender, CloudConfigSyncSnapshot snapshot)
    {
        if (_dispatcher.HasThreadAccess)
        {
            ApplySnapshot(snapshot);
            return;
        }

        _dispatcher.Enqueue(() => ApplySnapshot(snapshot));
    }

    private void ApplySnapshot(CloudConfigSyncSnapshot snapshot)
    {
        _snapshot = snapshot;
        NotifySnapshotProjectionChanged();
        if (!IsEditing)
        {
            LoadDraftFromSnapshot();
        }
    }

    private void NotifySnapshotProjectionChanged()
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(IsChecking));
        OnPropertyChanged(nameof(IsEnabled));
        OnPropertyChanged(nameof(HasActiveProvider));
        OnPropertyChanged(nameof(CanSync));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanDisable));
        OnPropertyChanged(nameof(CanForget));
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(StatusHeadline));
        OnPropertyChanged(nameof(TransferStatusText));
        OnPropertyChanged(nameof(ErrorText));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(ActiveProviderDisplayName));
        OnPropertyChanged(nameof(ActiveRemoteTarget));
        OnPropertyChanged(nameof(CredentialStatusText));
    }

    private void LoadDraftFromSnapshot()
    {
        SelectedProviderId = !string.IsNullOrWhiteSpace(_snapshot.Configuration.ProviderId)
            ? _snapshot.Configuration.ProviderId
            : Providers.FirstOrDefault()?.ProviderId ?? string.Empty;
        var options = _snapshot.Configuration.Options;
        WebDavFileUrl = GetValue(options, WebDavFileUrlOptionKey);
        WebDavUsername = GetValue(options, WebDavUsernameOptionKey);
        S3Endpoint = GetValue(options, S3EndpointOptionKey);
        S3Bucket = GetValue(options, S3BucketOptionKey);
        S3Region = GetValue(options, S3RegionOptionKey) is { Length: > 0 } region ? region : DefaultS3Region;
        S3ObjectKey = GetValue(options, S3ObjectKeyOptionKey) is { Length: > 0 } objectKey
            ? objectKey
            : DefaultS3ObjectKey;
        S3ForcePathStyle = !bool.TryParse(GetValue(options, S3ForcePathStyleOptionKey), out var forcePathStyle) || forcePathStyle;
        WebDavPassword = string.Empty;
        S3AccessKeyId = string.Empty;
        S3SecretAccessKey = string.Empty;
        _draftCredential = _snapshot.Credential;
        OnPropertyChanged(nameof(CredentialStatusText));
        OnPropertyChanged(nameof(IsChecking));
        OnPropertyChanged(nameof(CanRetryCredentialCheck));
        OnPropertyChanged(nameof(ValidationMessage));
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(HasUnsavedChanges));
    }

    private void ScheduleCredentialRefresh()
    {
        if (!IsEditing)
        {
            return;
        }

        _credentialRefreshCts?.Cancel();
        _credentialRefreshCts?.Dispose();
        _credentialRefreshCts = new CancellationTokenSource();
        _ = RefreshCredentialAsync(TimeSpan.FromMilliseconds(300), _credentialRefreshCts.Token);
    }

    private Task RefreshCredentialAsync(TimeSpan delay) => RefreshCredentialAsync(delay, CancellationToken.None);

    private async Task RefreshCredentialAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        var version = Interlocked.Increment(ref _credentialRefreshVersion);
        try
        {
            _draftCredential = CloudCredentialState.Checking;
            OnPropertyChanged(nameof(CredentialStatusText));
            OnPropertyChanged(nameof(IsChecking));
            OnPropertyChanged(nameof(CanRetryCredentialCheck));
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(true);
            }

            var providerId = SelectedProviderId;
            var options = CreateSelectedOptions();
            var inspection = await _coordinator.InspectCredentialAsync(providerId, options, cancellationToken).ConfigureAwait(true);
            if (version != Volatile.Read(ref _credentialRefreshVersion) ||
                !string.Equals(providerId, SelectedProviderId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _draftCredential = inspection.State;
            OnPropertyChanged(nameof(CredentialStatusText));
            OnPropertyChanged(nameof(IsChecking));
            OnPropertyChanged(nameof(CanRetryCredentialCheck));
            OnPropertyChanged(nameof(ValidationMessage));
            OnPropertyChanged(nameof(CanApply));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private bool DraftMatchesActiveConfiguration()
    {
        if (!string.Equals(SelectedProviderId, _snapshot.Configuration.ProviderId, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(WebDavPassword) ||
            !string.IsNullOrEmpty(S3AccessKeyId) ||
            !string.IsNullOrEmpty(S3SecretAccessKey))
        {
            return false;
        }

        var options = CreateSelectedOptions();
        return options.Count == _snapshot.Configuration.Options.Count &&
               options.All(option => _snapshot.Configuration.Options.TryGetValue(option.Key, out var value) &&
                                     string.Equals(option.Value, value, StringComparison.Ordinal));
    }

    private string GetProviderDisplayName(string providerId) =>
        Providers.FirstOrDefault(provider =>
            string.Equals(provider.ProviderId, providerId, StringComparison.OrdinalIgnoreCase))?.DisplayName ?? providerId;

    private static string FormatRemoteTarget(string providerId, IReadOnlyDictionary<string, string> options)
    {
        if (string.Equals(providerId, WebDavProviderId, StringComparison.OrdinalIgnoreCase))
        {
            return GetValue(options, WebDavFileUrlOptionKey);
        }

        if (!string.Equals(providerId, S3ProviderId, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        var endpoint = GetValue(options, S3EndpointOptionKey).TrimEnd('/');
        var bucket = GetValue(options, S3BucketOptionKey).Trim('/');
        var objectKey = GetValue(options, S3ObjectKeyOptionKey).TrimStart('/');
        return string.IsNullOrWhiteSpace(objectKey)
            ? $"{endpoint}/{bucket}"
            : $"{endpoint}/{bucket}/{objectKey}";
    }

    private static string GetValue(IReadOnlyDictionary<string, string> options, string key) =>
        options.FirstOrDefault(option => string.Equals(option.Key, key, StringComparison.OrdinalIgnoreCase)).Value ?? string.Empty;

    private static bool IsAbsoluteHttpUrl(string value) =>
        Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}

public sealed class CloudConfigProviderOptionViewModel
{
    public CloudConfigProviderOptionViewModel(string providerId, string displayName, bool isConfigured)
    {
        ProviderId = providerId?.Trim() ?? string.Empty;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? ProviderId : displayName.Trim();
        IsConfigured = isConfigured;
    }

    public string ProviderId { get; }

    public string DisplayName { get; }

    public bool IsConfigured { get; }
}
