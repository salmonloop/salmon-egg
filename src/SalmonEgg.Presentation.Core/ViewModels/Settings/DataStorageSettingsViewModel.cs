using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.Diagnostics;
using SalmonEgg.Domain.Models.Protocol;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Resources;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.ViewModels.Chat;

namespace SalmonEgg.Presentation.ViewModels.Settings;

public partial class DataStorageSettingsViewModel : ObservableObject
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

    private readonly IAppDataService _paths;
    private readonly IAppMaintenanceService _maintenance;
    private readonly IDiagnosticsBundleService _diagnostics;
    private readonly IPlatformShellService _shell;
    private readonly IPlatformCapabilityService _capabilities;
    private readonly IStorageLocationService _storageLocations;
    private readonly ISessionExportService _sessionExport;
    private readonly ICloudConfigSyncService _cloudConfigSync;
    private readonly IUiInteractionService _ui;
    private readonly IStringLocalizer<CoreStrings> _localizer;
    private readonly ILogger<DataStorageSettingsViewModel> _logger;

    public AppPreferencesViewModel Preferences { get; }
    public ChatViewModel Chat { get; }

    public string AppDataRootPath => _paths.AppDataRootPath;
    public string LogsDirectoryPath => _paths.LogsDirectoryPath;
    public string CacheRootPath => _paths.CacheRootPath;
    public string ExportsDirectoryPath => _paths.ExportsDirectoryPath;

    public bool CanOpenExternalFiles => _capabilities.SupportsExternalFileOpen;

    public bool CanExportLocalFiles => _capabilities.SupportsLocalFileExport;

    public ObservableCollection<CloudConfigProviderOptionViewModel> CloudConfigProviders { get; } = new();

    public bool HasActiveCloudConfigProvider =>
        !string.IsNullOrWhiteSpace(Preferences.CloudConfigSync?.ProviderId);

    public bool IsSelectedProviderDifferentFromActive =>
        HasActiveCloudConfigProvider &&
        !string.Equals(Preferences.CloudConfigSync.ProviderId, SelectedCloudConfigProviderId, StringComparison.OrdinalIgnoreCase);

    public bool IsCloudConfigSyncConfigured =>
        !IsCloudConfigSyncBusy &&
        (IsOneDriveCloudConfigProviderSelected
            ? GetSelectedProvider()?.IsConfigured == true
            : IsWebDavCloudConfigProviderSelected
                ? string.IsNullOrEmpty(WebDavValidationMessage)
                : IsS3CloudConfigProviderSelected && string.IsNullOrEmpty(S3ValidationMessage));

    public bool CanSyncCloudConfig => IsCloudConfigSyncEnabled && !IsCloudConfigSyncBusy;

    public bool CanDisconnectCloudConfig => IsCloudConfigSyncEnabled && !IsCloudConfigSyncBusy;

    public bool IsOneDriveCloudConfigProviderSelected =>
        string.Equals(SelectedCloudConfigProviderId, OneDriveProviderId, StringComparison.OrdinalIgnoreCase);

    public bool IsWebDavCloudConfigProviderSelected =>
        string.Equals(SelectedCloudConfigProviderId, WebDavProviderId, StringComparison.OrdinalIgnoreCase);

    public bool IsS3CloudConfigProviderSelected =>
        string.Equals(SelectedCloudConfigProviderId, S3ProviderId, StringComparison.OrdinalIgnoreCase);

    public string ConnectCloudConfigProviderButtonText => IsOneDriveCloudConfigProviderSelected
        ? _localizer["DataStorage_CloudSyncConnectOneDrive"]
        : IsWebDavCloudConfigProviderSelected
            ? _localizer["DataStorage_CloudSyncConnectWebDav"]
            : IsS3CloudConfigProviderSelected
                ? _localizer["DataStorage_CloudSyncConnectS3"]
                : _localizer["DataStorage_CloudSyncConnectSelected"];

    public string CloudConfigProviderCredentialStatusText => IsOneDriveCloudConfigProviderSelected
        ? string.Empty
        : SelectedCloudConfigProviderHasStoredCredentials
            ? _localizer["DataStorage_CloudSyncCredentialsSaved"]
            : _localizer["DataStorage_CloudSyncCredentialsMissing"];

    public bool SelectedCloudConfigProviderHasStoredCredentials => SelectedProviderHasStoredCredentials;

    public string WebDavValidationMessage => IsWebDavCloudConfigProviderSelected && string.IsNullOrWhiteSpace(WebDavFileUrl)
        ? _localizer["DataStorage_CloudSyncWebDavFileUrlRequired"]
        : IsWebDavCloudConfigProviderSelected &&
          !SelectedProviderHasStoredCredentials &&
          !string.IsNullOrWhiteSpace(WebDavUsername) &&
          string.IsNullOrEmpty(WebDavPassword)
            ? _localizer["DataStorage_CloudSyncWebDavCredentialsRequired"]
        : string.Empty;

    public string S3ValidationMessage
    {
        get
        {
            if (!IsS3CloudConfigProviderSelected)
            {
                return string.Empty;
            }

            if (string.IsNullOrWhiteSpace(S3Endpoint))
            {
                return _localizer["DataStorage_CloudSyncS3EndpointRequired"];
            }

            if (string.IsNullOrWhiteSpace(S3Bucket))
            {
                return _localizer["DataStorage_CloudSyncS3BucketRequired"];
            }

            if (!SelectedProviderHasStoredCredentials &&
                (string.IsNullOrWhiteSpace(S3AccessKeyId) || string.IsNullOrEmpty(S3SecretAccessKey)))
            {
                return _localizer["DataStorage_CloudSyncS3CredentialsRequired"];
            }

            return string.Empty;
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCloudConfigSyncConfigured))]
    [NotifyPropertyChangedFor(nameof(CanSyncCloudConfig))]
    [NotifyPropertyChangedFor(nameof(CanDisconnectCloudConfig))]
    private bool _isCloudConfigSyncBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSyncCloudConfig))]
    [NotifyPropertyChangedFor(nameof(CanDisconnectCloudConfig))]
    private bool _isCloudConfigSyncEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCloudConfigSyncConfigured))]
    [NotifyPropertyChangedFor(nameof(IsOneDriveCloudConfigProviderSelected))]
    [NotifyPropertyChangedFor(nameof(IsWebDavCloudConfigProviderSelected))]
    [NotifyPropertyChangedFor(nameof(IsS3CloudConfigProviderSelected))]
    [NotifyPropertyChangedFor(nameof(ConnectCloudConfigProviderButtonText))]
    [NotifyPropertyChangedFor(nameof(CloudConfigProviderCredentialStatusText))]
    [NotifyPropertyChangedFor(nameof(WebDavValidationMessage))]
    [NotifyPropertyChangedFor(nameof(S3ValidationMessage))]
    private string _selectedCloudConfigProviderId = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCloudConfigSyncConfigured))]
    [NotifyPropertyChangedFor(nameof(WebDavValidationMessage))]
    private string _webDavFileUrl = string.Empty;

    [ObservableProperty]
    private string _webDavUsername = string.Empty;

    [ObservableProperty]
    private string _webDavPassword = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCloudConfigSyncConfigured))]
    [NotifyPropertyChangedFor(nameof(S3ValidationMessage))]
    private string _s3Endpoint = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCloudConfigSyncConfigured))]
    [NotifyPropertyChangedFor(nameof(S3ValidationMessage))]
    private string _s3Bucket = string.Empty;

    [ObservableProperty]
    private string _s3Region = DefaultS3Region;

    [ObservableProperty]
    private string _s3ObjectKey = DefaultS3ObjectKey;

    [ObservableProperty]
    private bool _s3ForcePathStyle = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCloudConfigSyncConfigured))]
    [NotifyPropertyChangedFor(nameof(S3ValidationMessage))]
    private string _s3AccessKeyId = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCloudConfigSyncConfigured))]
    [NotifyPropertyChangedFor(nameof(S3ValidationMessage))]
    private string _s3SecretAccessKey = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedCloudConfigProviderHasStoredCredentials))]
    [NotifyPropertyChangedFor(nameof(CloudConfigProviderCredentialStatusText))]
    [NotifyPropertyChangedFor(nameof(IsCloudConfigSyncConfigured))]
    [NotifyPropertyChangedFor(nameof(S3ValidationMessage))]
    private bool _selectedProviderHasStoredCredentials;

    [ObservableProperty]
    private string _cloudConfigSyncStatusText = string.Empty;

    [ObservableProperty]
    private string _cloudConfigSyncLastSyncText = string.Empty;

    [ObservableProperty]
    private string _cloudConfigSyncErrorText = string.Empty;

    public DataStorageSettingsViewModel(
        AppPreferencesViewModel preferences,
        ChatViewModel chatViewModel,
        IAppDataService paths,
        IAppMaintenanceService maintenance,
        IDiagnosticsBundleService diagnostics,
        IPlatformShellService shell,
        IPlatformCapabilityService capabilities,
        IStorageLocationService storageLocations,
        ISessionExportService sessionExport,
        ICloudConfigSyncService cloudConfigSync,
        IUiInteractionService ui,
        IStringLocalizer<CoreStrings> localizer,
        ILogger<DataStorageSettingsViewModel> logger)
    {
        Preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        Chat = chatViewModel ?? throw new ArgumentNullException(nameof(chatViewModel));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _maintenance = maintenance ?? throw new ArgumentNullException(nameof(maintenance));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        _storageLocations = storageLocations ?? throw new ArgumentNullException(nameof(storageLocations));
        _sessionExport = sessionExport ?? throw new ArgumentNullException(nameof(sessionExport));
        _cloudConfigSync = cloudConfigSync ?? throw new ArgumentNullException(nameof(cloudConfigSync));
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        InitializeCloudConfigProviders();
        ApplyWebDavOptions(Preferences.CloudConfigSync);
        ApplyS3Options(Preferences.CloudConfigSync);
        SelectedCloudConfigProviderId = ResolveInitialProviderId(Preferences.CloudConfigSync);
        ApplyCloudConfigSyncSettings(Preferences.CloudConfigSync);
        _ = RefreshSelectedProviderConfigurationStatusAsync();
    }

    [RelayCommand]
    private Task OpenAppDataFolderAsync() => OpenStorageLocationAsync(AppStorageLocation.AppData);

    [RelayCommand]
    private Task OpenCacheFolderAsync() => OpenStorageLocationAsync(AppStorageLocation.Cache);

    [RelayCommand]
    private Task OpenLogsFolderAsync() => OpenStorageLocationAsync(AppStorageLocation.Logs);

    [RelayCommand]
    private Task OpenExportsFolderAsync() => OpenStorageLocationAsync(AppStorageLocation.Exports);

    [RelayCommand]
    private async Task ExportCurrentSessionMarkdownAsync()
    {
        await ExportCurrentSessionAsync("md");
    }

    [RelayCommand]
    private async Task ExportCurrentSessionJsonAsync()
    {
        await ExportCurrentSessionAsync("json");
    }

    private async Task ExportCurrentSessionAsync(string format)
    {
        try
        {
            if (!CanExportLocalFiles)
            {
                await NotifyLocalFileExportUnsupportedAsync();
                return;
            }

            var transcript = await Chat.GetCurrentSessionTranscriptSnapshotAsync();
            var request = new SessionExportRequest(
                format,
                Chat.CurrentSessionId,
                Chat.AgentName,
                Chat.AgentVersion,
                transcript.Select(m => new SessionExportMessage(
                    m.Id,
                    ToExportTimestamp(m.Timestamp),
                    m.IsOutgoing,
                    m.ContentType,
                    m.Title,
                    m.TextContent)).ToList());

            var result = await _sessionExport.ExportAsync(request);
            await OpenExportResultOrNotifyAsync(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ExportCurrentSession failed");
        }
    }

    [RelayCommand]
    private async Task CreateDiagnosticsBundleAsync()
    {
        try
        {
            if (!CanExportLocalFiles)
            {
                await NotifyLocalFileExportUnsupportedAsync();
                return;
            }

            var appVersion = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString()
                ?? System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString()
                ?? "unknown";
            var snapshot = new DiagnosticsSnapshot
            {
                AppVersion = appVersion,
                ProtocolVersion = new InitializeParams().ProtocolVersion.ToString(),
                OsDescription = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                FrameworkDescription = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                Properties =
                {
                    ["AgentName"] = Chat.AgentName ?? string.Empty,
                    ["AgentVersion"] = Chat.AgentVersion ?? string.Empty,
                    ["IsConnected"] = Chat.IsConnected.ToString(),
                    ["CurrentSessionId"] = Chat.CurrentSessionId ?? string.Empty,
                }
            };

            var result = await _diagnostics.CreateBundleAsync(snapshot);
            await OpenDiagnosticsBundleResultOrNotifyAsync(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CreateDiagnosticsBundle failed");
        }
    }

    [RelayCommand]
    private async Task ClearCacheAsync()
    {
        await _maintenance.ClearCacheAsync();
    }

    [RelayCommand]
    private async Task ClearAllLocalDataAsync()
    {
        await _maintenance.ClearAllLocalDataAsync();
    }

    [RelayCommand]
    private async Task ConnectSelectedCloudConfigProviderAsync()
    {
        if (IsCloudConfigSyncBusy)
        {
            return;
        }

        if (!IsCloudConfigSyncConfigured)
        {
            ShowCloudConfigValidationError();
            return;
        }

        if (!await ConfirmProviderSwitchIfNeededAsync().ConfigureAwait(true))
        {
            return;
        }

        if (IsWebDavCloudConfigProviderSelected)
        {
            await RunCloudSyncOperationAsync(ConnectWebDavCloudConfigProviderAsync);
            return;
        }

        if (IsS3CloudConfigProviderSelected)
        {
            await RunCloudSyncOperationAsync(ConnectS3CloudConfigProviderAsync);
            return;
        }

        var providerId = string.IsNullOrWhiteSpace(SelectedCloudConfigProviderId)
            ? OneDriveProviderId
            : SelectedCloudConfigProviderId;
        await RunCloudSyncOperationAsync(() => _cloudConfigSync.AuthorizeAndSyncAsync(providerId));
    }

    [RelayCommand]
    private async Task SyncCloudConfigAsync()
    {
        if (!CanSyncCloudConfig)
        {
            return;
        }

        await RunCloudSyncOperationAsync(() => _cloudConfigSync.SyncNowAsync());
    }

    [RelayCommand]
    private async Task DisconnectCloudConfigAsync()
    {
        if (!CanDisconnectCloudConfig)
        {
            return;
        }

        await RunCloudSyncOperationAsync(() => _cloudConfigSync.DisconnectAsync());
    }

    private async Task OpenStorageLocationAsync(AppStorageLocation location)
    {
        if (!await _storageLocations.OpenAsync(location))
        {
            await NotifyExternalOpenUnsupportedAsync();
        }
    }

    private async Task OpenFileOrNotifyAsync(string path)
    {
        if (!await _shell.OpenFileAsync(path))
        {
            await NotifyExternalOpenUnsupportedAsync();
        }
    }

    private Task NotifyExternalOpenUnsupportedAsync()
        => _ui.ShowInfoAsync(_localizer["Platform_ExternalOpenUnsupported"]);

    private Task NotifyLocalFileExportUnsupportedAsync()
        => _ui.ShowInfoAsync(_localizer["Platform_LocalFileExportUnsupported"]);

    partial void OnSelectedCloudConfigProviderIdChanged(string value)
    {
        RefreshCloudConfigDerivedState();
        _ = RefreshSelectedProviderConfigurationStatusAsync();
    }

    partial void OnWebDavFileUrlChanged(string value)
    {
        RefreshCloudConfigDerivedState();
        _ = RefreshSelectedProviderConfigurationStatusAsync();
    }

    partial void OnWebDavUsernameChanged(string value)
    {
        RefreshCloudConfigDerivedState();
        _ = RefreshSelectedProviderConfigurationStatusAsync();
    }

    partial void OnWebDavPasswordChanged(string value)
    {
        RefreshCloudConfigDerivedState();
    }

    partial void OnS3EndpointChanged(string value)
    {
        RefreshCloudConfigDerivedState();
        _ = RefreshSelectedProviderConfigurationStatusAsync();
    }

    partial void OnS3BucketChanged(string value)
    {
        RefreshCloudConfigDerivedState();
        _ = RefreshSelectedProviderConfigurationStatusAsync();
    }

    partial void OnS3RegionChanged(string value)
    {
        _ = RefreshSelectedProviderConfigurationStatusAsync();
    }

    partial void OnS3ObjectKeyChanged(string value)
    {
        _ = RefreshSelectedProviderConfigurationStatusAsync();
    }

    partial void OnS3ForcePathStyleChanged(bool value)
    {
        _ = RefreshSelectedProviderConfigurationStatusAsync();
    }

    partial void OnS3AccessKeyIdChanged(string value)
    {
        RefreshCloudConfigDerivedState();
    }

    partial void OnS3SecretAccessKeyChanged(string value)
    {
        RefreshCloudConfigDerivedState();
    }

    private async Task<CloudConfigSyncResult> ConnectWebDavCloudConfigProviderAsync()
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [WebDavFileUrlOptionKey] = WebDavFileUrl,
            [WebDavUsernameOptionKey] = WebDavUsername
        };
        var secrets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(WebDavPassword))
        {
            secrets[WebDavPasswordSecretKey] = WebDavPassword;
        }

        var configuration = await _cloudConfigSync.ConfigureProviderAsync(WebDavProviderId, options, secrets);
        if (configuration.Status == CloudConfigSyncStatus.Failed || configuration.Status == CloudConfigSyncStatus.NotConfigured)
        {
            return configuration;
        }

        return await _cloudConfigSync.AuthorizeAndSyncAsync(WebDavProviderId);
    }

    private async Task<CloudConfigSyncResult> ConnectS3CloudConfigProviderAsync()
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [S3EndpointOptionKey] = S3Endpoint,
            [S3BucketOptionKey] = S3Bucket,
            [S3RegionOptionKey] = S3Region,
            [S3ObjectKeyOptionKey] = S3ObjectKey,
            [S3ForcePathStyleOptionKey] = S3ForcePathStyle.ToString()
        };
        var secrets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(S3AccessKeyId))
        {
            secrets[S3AccessKeyIdSecretKey] = S3AccessKeyId;
        }

        if (!string.IsNullOrEmpty(S3SecretAccessKey))
        {
            secrets[S3SecretAccessKeySecretKey] = S3SecretAccessKey;
        }

        var configuration = await _cloudConfigSync.ConfigureProviderAsync(S3ProviderId, options, secrets);
        if (configuration.Status == CloudConfigSyncStatus.Failed || configuration.Status == CloudConfigSyncStatus.NotConfigured)
        {
            return configuration;
        }

        return await _cloudConfigSync.AuthorizeAndSyncAsync(S3ProviderId);
    }

    private async Task RunCloudSyncOperationAsync(Func<Task<CloudConfigSyncResult>> operation)
    {
        try
        {
            IsCloudConfigSyncBusy = true;
            RefreshCloudConfigDerivedState();
            CloudConfigSyncErrorText = string.Empty;
            var result = await operation();
            ApplyCloudConfigSyncResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cloud config sync command failed");
            CloudConfigSyncErrorText = _localizer["DataStorage_CloudSyncStatusFailed"];
            CloudConfigSyncStatusText = CloudConfigSyncErrorText;
        }
        finally
        {
            IsCloudConfigSyncBusy = false;
            RefreshCloudConfigDerivedState();
            await RefreshSelectedProviderConfigurationStatusAsync().ConfigureAwait(true);
        }
    }

    private async Task<bool> ConfirmProviderSwitchIfNeededAsync()
    {
        if (!IsSelectedProviderDifferentFromActive)
        {
            return true;
        }

        var confirmed = await _ui.ConfirmAsync(
            _localizer["DataStorage_CloudSyncSwitchConfirmTitle"],
            _localizer["DataStorage_CloudSyncSwitchConfirmMessage"],
            _localizer["DataStorage_CloudSyncSwitchConfirmPrimary"],
            _localizer["DataStorage_CloudSyncSwitchConfirmCancel"]).ConfigureAwait(true);

        return confirmed;
    }

    private void ShowCloudConfigValidationError()
    {
        var message = IsWebDavCloudConfigProviderSelected
            ? WebDavValidationMessage
            : IsS3CloudConfigProviderSelected
                ? S3ValidationMessage
                : string.Empty;

        CloudConfigSyncErrorText = string.IsNullOrWhiteSpace(message)
            ? _localizer["DataStorage_CloudSyncStatusNotConfigured"]
            : message;
        CloudConfigSyncStatusText = CloudConfigSyncErrorText;
    }

    private async Task RefreshSelectedProviderConfigurationStatusAsync()
    {
        var providerId = SelectedCloudConfigProviderId;
        if (string.IsNullOrWhiteSpace(providerId) || IsOneDriveCloudConfigProviderSelected)
        {
            SelectedProviderHasStoredCredentials = true;
            RefreshCloudConfigDerivedState();
            return;
        }

        try
        {
            var status = await _cloudConfigSync.GetProviderConfigurationStatusAsync(
                providerId,
                CreateSelectedProviderOptions()).ConfigureAwait(true);
            if (!string.Equals(providerId, SelectedCloudConfigProviderId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            SelectedProviderHasStoredCredentials = status.HasStoredCredentials;
            RefreshCloudConfigDerivedState();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cloud config provider configuration status refresh failed for provider {ProviderId}", providerId);
            if (string.Equals(providerId, SelectedCloudConfigProviderId, StringComparison.OrdinalIgnoreCase))
            {
                SelectedProviderHasStoredCredentials = false;
                RefreshCloudConfigDerivedState();
            }
        }
    }

    private IReadOnlyDictionary<string, string> CreateSelectedProviderOptions()
    {
        if (IsWebDavCloudConfigProviderSelected)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [WebDavFileUrlOptionKey] = WebDavFileUrl,
                [WebDavUsernameOptionKey] = WebDavUsername
            };
        }

        if (IsS3CloudConfigProviderSelected)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [S3EndpointOptionKey] = S3Endpoint,
                [S3BucketOptionKey] = S3Bucket,
                [S3RegionOptionKey] = S3Region,
                [S3ObjectKeyOptionKey] = S3ObjectKey,
                [S3ForcePathStyleOptionKey] = S3ForcePathStyle.ToString()
            };
        }

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private void RefreshCloudConfigDerivedState()
    {
        OnPropertyChanged(nameof(IsCloudConfigSyncConfigured));
        OnPropertyChanged(nameof(CanSyncCloudConfig));
        OnPropertyChanged(nameof(CanDisconnectCloudConfig));
        OnPropertyChanged(nameof(HasActiveCloudConfigProvider));
        OnPropertyChanged(nameof(IsSelectedProviderDifferentFromActive));
        OnPropertyChanged(nameof(IsOneDriveCloudConfigProviderSelected));
        OnPropertyChanged(nameof(IsWebDavCloudConfigProviderSelected));
        OnPropertyChanged(nameof(IsS3CloudConfigProviderSelected));
        OnPropertyChanged(nameof(ConnectCloudConfigProviderButtonText));
        OnPropertyChanged(nameof(CloudConfigProviderCredentialStatusText));
        OnPropertyChanged(nameof(WebDavValidationMessage));
        OnPropertyChanged(nameof(S3ValidationMessage));
    }

    private void ApplyCloudConfigSyncSettings(CloudConfigSyncSettings? settings)
    {
        IsCloudConfigSyncEnabled = settings?.Enabled == true;
        ApplyWebDavOptions(settings);
        ApplyS3Options(settings);
        SelectedCloudConfigProviderId = ResolveInitialProviderId(settings);
        CloudConfigSyncStatusText = IsCloudConfigSyncEnabled
            ? _localizer["DataStorage_CloudSyncStatusEnabled"]
            : _localizer["DataStorage_CloudSyncStatusDisabled"];
    }

    private void ApplyCloudConfigSyncResult(CloudConfigSyncResult result)
    {
        IsCloudConfigSyncEnabled = result.Status is CloudConfigSyncStatus.Uploaded
            or CloudConfigSyncStatus.Restored
            or CloudConfigSyncStatus.ConflictRemoteApplied;
        CloudConfigSyncStatusText = result.Status switch
        {
            CloudConfigSyncStatus.Uploaded => _localizer["DataStorage_CloudSyncStatusUploaded"],
            CloudConfigSyncStatus.Restored => _localizer["DataStorage_CloudSyncStatusRestored"],
            CloudConfigSyncStatus.ConflictRemoteApplied => _localizer["DataStorage_CloudSyncStatusConflict"],
            CloudConfigSyncStatus.NotConfigured => _localizer["DataStorage_CloudSyncStatusNotConfigured"],
            CloudConfigSyncStatus.NotAuthorized => _localizer["DataStorage_CloudSyncStatusNotAuthorized"],
            CloudConfigSyncStatus.SignedOut => _localizer["DataStorage_CloudSyncStatusSignedOut"],
            CloudConfigSyncStatus.Disabled => _localizer["DataStorage_CloudSyncStatusDisabled"],
            CloudConfigSyncStatus.Failed => result.UserMessage ?? _localizer["DataStorage_CloudSyncStatusFailed"],
            _ => _localizer["DataStorage_CloudSyncStatusEnabled"]
        };

        CloudConfigSyncErrorText = result.Status is CloudConfigSyncStatus.Failed or CloudConfigSyncStatus.NotConfigured or CloudConfigSyncStatus.NotAuthorized
            ? CloudConfigSyncStatusText
            : string.Empty;

        if (result.LastSyncUtc.HasValue)
        {
            CloudConfigSyncLastSyncText = string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                _localizer["DataStorage_CloudSyncLastSyncFormat"],
                result.LastSyncUtc.Value.ToLocalTime());
        }

        Preferences.SetCloudConfigSyncSettings(new CloudConfigSyncSettings
        {
            Enabled = IsCloudConfigSyncEnabled,
            ProviderId = IsCloudConfigSyncEnabled ? result.ProviderId ?? SelectedCloudConfigProviderId : string.Empty,
            IncludeSecrets = true,
            ProviderOptions = CreateProviderOptionsSnapshot()
        });
    }

    private Dictionary<string, Dictionary<string, string>> CreateProviderOptionsSnapshot()
    {
        var options = CloneProviderOptions(Preferences.CloudConfigSync?.ProviderOptions);
        if (!string.IsNullOrWhiteSpace(WebDavFileUrl) || !string.IsNullOrWhiteSpace(WebDavUsername))
        {
            options[WebDavProviderId] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [WebDavFileUrlOptionKey] = WebDavFileUrl.Trim(),
                [WebDavUsernameOptionKey] = WebDavUsername.Trim()
            };
        }

        if (!string.IsNullOrWhiteSpace(S3Endpoint) || !string.IsNullOrWhiteSpace(S3Bucket))
        {
            options[S3ProviderId] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [S3EndpointOptionKey] = S3Endpoint.Trim(),
                [S3BucketOptionKey] = S3Bucket.Trim(),
                [S3RegionOptionKey] = S3Region.Trim(),
                [S3ObjectKeyOptionKey] = S3ObjectKey.Trim(),
                [S3ForcePathStyleOptionKey] = S3ForcePathStyle.ToString()
            };
        }

        return options;
    }

    private void InitializeCloudConfigProviders()
    {
        CloudConfigProviders.Clear();
        foreach (var provider in (_cloudConfigSync.Providers ?? []).OrderBy(provider => provider.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            CloudConfigProviders.Add(new CloudConfigProviderOptionViewModel(
                provider.ProviderId,
                provider.DisplayName,
                provider.IsConfigured));
        }
    }

    private CloudConfigProviderOptionViewModel? GetSelectedProvider()
        => CloudConfigProviders.FirstOrDefault(provider =>
            string.Equals(provider.ProviderId, SelectedCloudConfigProviderId, StringComparison.OrdinalIgnoreCase));

    private string ResolveInitialProviderId(CloudConfigSyncSettings? settings)
    {
        if (!string.IsNullOrWhiteSpace(settings?.ProviderId) &&
            CloudConfigProviders.Any(provider => string.Equals(provider.ProviderId, settings.ProviderId, StringComparison.OrdinalIgnoreCase)))
        {
            return settings.ProviderId.Trim();
        }

        return CloudConfigProviders.FirstOrDefault()?.ProviderId ?? string.Empty;
    }

    private void ApplyWebDavOptions(CloudConfigSyncSettings? settings)
    {
        if (settings?.ProviderOptions is null ||
            !settings.ProviderOptions.TryGetValue(WebDavProviderId, out var options))
        {
            return;
        }

        WebDavFileUrl = GetOptionValue(options, WebDavFileUrlOptionKey);
        WebDavUsername = GetOptionValue(options, WebDavUsernameOptionKey);
    }

    private void ApplyS3Options(CloudConfigSyncSettings? settings)
    {
        if (settings?.ProviderOptions is null ||
            !settings.ProviderOptions.TryGetValue(S3ProviderId, out var options))
        {
            return;
        }

        S3Endpoint = GetOptionValue(options, S3EndpointOptionKey);
        S3Bucket = GetOptionValue(options, S3BucketOptionKey);
        S3Region = GetOptionValue(options, S3RegionOptionKey) is { Length: > 0 } region ? region : DefaultS3Region;
        S3ObjectKey = GetOptionValue(options, S3ObjectKeyOptionKey) is { Length: > 0 } objectKey
            ? objectKey
            : DefaultS3ObjectKey;
        S3ForcePathStyle = GetOptionValue(options, S3ForcePathStyleOptionKey) is { Length: > 0 } forcePathStyle
            ? bool.TryParse(forcePathStyle, out var parsed) && parsed
            : true;
    }

    private static string GetOptionValue(IReadOnlyDictionary<string, string> options, string key)
        => options.FirstOrDefault(option => string.Equals(option.Key, key, StringComparison.OrdinalIgnoreCase)).Value ?? string.Empty;

    private static Dictionary<string, Dictionary<string, string>> CloneProviderOptions(
        IReadOnlyDictionary<string, Dictionary<string, string>>? options)
    {
        var clone = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        if (options is null)
        {
            return clone;
        }

        foreach (var provider in options)
        {
            if (string.IsNullOrWhiteSpace(provider.Key) || provider.Value is null)
            {
                continue;
            }

            clone[provider.Key.Trim()] = provider.Value
                .Where(option => !string.IsNullOrWhiteSpace(option.Key) && option.Value is not null)
                .ToDictionary(
                    option => option.Key.Trim(),
                    option => option.Value.Trim(),
                    StringComparer.OrdinalIgnoreCase);
        }

        return clone;
    }

    private async Task OpenExportResultOrNotifyAsync(SessionExportResult result)
    {
        if (result.Status is SessionExportStatus.Unsupported || string.IsNullOrWhiteSpace(result.Path))
        {
            await NotifyLocalFileExportUnsupportedAsync();
            return;
        }

        await OpenFileOrNotifyAsync(result.Path);
    }

    private async Task OpenDiagnosticsBundleResultOrNotifyAsync(DiagnosticsBundleResult result)
    {
        if (result.Status is DiagnosticsBundleStatus.Unsupported || string.IsNullOrWhiteSpace(result.Path))
        {
            await NotifyLocalFileExportUnsupportedAsync();
            return;
        }

        await OpenFileOrNotifyAsync(result.Path);
    }

    private static DateTimeOffset ToExportTimestamp(DateTime timestamp)
    {
        var utc = timestamp.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(timestamp, DateTimeKind.Utc)
            : timestamp.ToUniversalTime();
        return new DateTimeOffset(utc);
    }
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
