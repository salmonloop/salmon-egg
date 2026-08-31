using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Tests.Localization;
using SalmonEgg.Presentation.Core.Tests.Threading;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.ViewModels.Settings;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Settings;

public sealed class CloudConfigSettingsViewModelTests
{
    [Fact]
    public void StatusProjection_WithoutConfiguredProvider_ShowsSetupStateWithoutConnectionContext()
    {
        var snapshot = CreateSnapshot(
            enabled: false,
            providerId: string.Empty,
            options: new Dictionary<string, string>(),
            transfer: new CloudTransferState(CloudTransferPhase.Idle));
        var localizer = new TestCoreStringLocalizer();
        var viewModel = CreateViewModel(new FakeCoordinator(snapshot), localizer: localizer);

        Assert.Equal(localizer["DataStorage_CloudSyncNotConfiguredHeadline"], viewModel.StatusHeadline);
        Assert.Empty(viewModel.ConnectionContextText);
        Assert.Equal(localizer["DataStorage_CloudSyncNeverSynced"], viewModel.TransferStatusText);
    }

    [Fact]
    public void StatusProjection_WithDisabledConfiguredProvider_SeparatesCurrentStateFromLastSync()
    {
        var completedAt = new DateTimeOffset(2026, 7, 13, 4, 28, 0, TimeSpan.Zero);
        var snapshot = CreateSnapshot(
            enabled: false,
            providerId: "webdav",
            options: new Dictionary<string, string>
            {
                ["file_url"] = "https://dav.example.test/config/"
            },
            transfer: new CloudTransferState(
                CloudTransferPhase.Idle,
                new CloudTransferSuccess(CloudTransferOutcome.Uploaded, completedAt)));
        var localizer = new TestCoreStringLocalizer();
        var viewModel = CreateViewModel(new FakeCoordinator(snapshot), localizer: localizer);

        Assert.Equal(localizer["DataStorage_CloudSyncDisabledHeadline"], viewModel.StatusHeadline);
        Assert.Equal(
            string.Format(
                CultureInfo.CurrentUICulture,
                localizer["DataStorage_CloudSyncSavedConnectionContext"],
                "WebDAV"),
            viewModel.ConnectionContextText);
        Assert.Equal(
            string.Format(
                CultureInfo.CurrentUICulture,
                localizer["DataStorage_CloudSyncTransferWithTime"],
                localizer["DataStorage_CloudSyncUploadedLocal"].Value,
                completedAt.ToLocalTime()),
            viewModel.TransferStatusText);
    }

    [Fact]
    public void StatusProjection_WithEnabledConfiguredProvider_ShowsCurrentServiceAndLabeledTarget()
    {
        var snapshot = CreateSnapshot(
            enabled: true,
            providerId: "webdav",
            options: new Dictionary<string, string>
            {
                ["file_url"] = "https://dav.example.test/config/"
            },
            transfer: new CloudTransferState(CloudTransferPhase.Idle),
            readiness: CloudProviderReadiness.Ready);
        var localizer = new TestCoreStringLocalizer();
        var viewModel = CreateViewModel(new FakeCoordinator(snapshot), localizer: localizer);

        Assert.Equal(
            string.Format(
                CultureInfo.CurrentUICulture,
                localizer["DataStorage_CloudSyncEnabledHeadline"],
                "WebDAV"),
            viewModel.StatusHeadline);
        Assert.Equal(
            string.Format(
                CultureInfo.CurrentUICulture,
                localizer["DataStorage_CloudSyncTargetContext"],
                "https://dav.example.test/config/"),
            viewModel.ConnectionContextText);
    }

    [Fact]
    public void LanguageChanged_ReprojectsCachedStatusHeadline()
    {
        var snapshot = CreateSnapshot(
            enabled: true,
            providerId: "webdav",
            options: new Dictionary<string, string>
            {
                ["file_url"] = "https://dav.example.test/config/"
            },
            transfer: new CloudTransferState(CloudTransferPhase.Idle),
            readiness: CloudProviderReadiness.Ready);
        var coordinator = new FakeCoordinator(snapshot);
        var languageService = new Mock<IAppLanguageService>();
        var currentLanguageTag = "zh-Hans";
        var localizer = new MutableTestCoreStringLocalizer();
        localizer.Set("zh-Hans", "DataStorage_CloudSyncEnabledHeadline", "云同步已开启 · {0}");
        localizer.Set("zh-Hans", "DataStorage_CloudSyncRetryConnection", "重新检查登录信息");
        localizer.Set("en-US", "DataStorage_CloudSyncEnabledHeadline", "Cloud sync enabled · {0}");
        localizer.Set("en-US", "DataStorage_CloudSyncRetryConnection", "Retry connection");
        languageService.SetupGet(service => service.CurrentLanguageTag).Returns(() => currentLanguageTag);

        var viewModel = new CloudConfigSettingsViewModel(
            coordinator,
            Mock.Of<IUiInteractionService>(),
            new ImmediateUiDispatcher(),
            localizer,
            languageService.Object);

        Assert.Equal("云同步已开启 · WebDAV", viewModel.StatusHeadline);
        Assert.Equal("重新检查登录信息", viewModel.RetryCredentialCheckText);

        currentLanguageTag = "en-US";
        localizer.SetLanguageTag("en-US");
        languageService.Raise(service => service.LanguageChanged += null, EventArgs.Empty);

        Assert.Equal("Cloud sync enabled · WebDAV", viewModel.StatusHeadline);
        Assert.Equal("Retry connection", viewModel.RetryCredentialCheckText);
    }

    [Fact]
    public void ActionProjection_OnlyShowsActionsRelevantToCurrentState()
    {
        var localizer = new TestCoreStringLocalizer();
        var unconfigured = CreateViewModel(
            new FakeCoordinator(CreateSnapshot(
                enabled: false,
                providerId: string.Empty,
                options: new Dictionary<string, string>(),
                transfer: new CloudTransferState(CloudTransferPhase.Idle))),
            localizer: localizer);
        Assert.True(unconfigured.ShowEditAction);
        Assert.Equal(localizer["DataStorage_CloudSyncSetupAction"], unconfigured.EditActionText);
        Assert.False(unconfigured.ShowSyncAction);
        Assert.False(unconfigured.ShowDisableAction);
        Assert.False(unconfigured.ShowRemoveAction);

        var disabled = CreateViewModel(
            new FakeCoordinator(CreateSnapshot(
                enabled: false,
                providerId: "webdav",
                options: new Dictionary<string, string>(),
                transfer: new CloudTransferState(CloudTransferPhase.Idle))),
            localizer: localizer);
        Assert.True(disabled.ShowEditAction);
        Assert.Equal(localizer["DataStorage_CloudSyncReopenAction"], disabled.EditActionText);
        Assert.False(disabled.ShowSyncAction);
        Assert.False(disabled.ShowDisableAction);
        Assert.True(disabled.ShowRemoveAction);

        var enabled = CreateViewModel(
            new FakeCoordinator(CreateReadySnapshot(
                "webdav",
                new Dictionary<string, string>(),
                CloudCredentialState.Available)),
            localizer: localizer);
        Assert.True(enabled.ShowEditAction);
        Assert.Equal(localizer["DataStorage_CloudSyncEditAction"], enabled.EditActionText);
        Assert.True(enabled.ShowSyncAction);
        Assert.True(enabled.ShowDisableAction);
        Assert.True(enabled.ShowRemoveAction);

        enabled.EditCommand.Execute(null);

        Assert.False(enabled.ShowEditAction);
        Assert.False(enabled.ShowSyncAction);
        Assert.False(enabled.ShowDisableAction);
        Assert.False(enabled.ShowRemoveAction);
    }

    [Fact]
    public void SelectedProviderIdChanged_UpdatesSelectedProviderOption()
    {
        var viewModel = CreateViewModel(new FakeCoordinator(CreateSnapshot(
            enabled: false,
            providerId: string.Empty,
            options: new Dictionary<string, string>(),
            transfer: new CloudTransferState(CloudTransferPhase.Idle))));

        viewModel.SelectedProviderId = "s3";

        Assert.NotNull(viewModel.SelectedProviderOption);
        Assert.Equal("s3", viewModel.SelectedProviderOption.ProviderId);
    }

    [Fact]
    public void SelectedProviderOptionChanged_UpdatesSelectedProviderId()
    {
        var viewModel = CreateViewModel(new FakeCoordinator(CreateSnapshot(
            enabled: false,
            providerId: string.Empty,
            options: new Dictionary<string, string>(),
            transfer: new CloudTransferState(CloudTransferPhase.Idle))));

        viewModel.SelectedProviderOption = viewModel.Providers.Single(provider => provider.ProviderId == "webdav");

        Assert.Equal("webdav", viewModel.SelectedProviderId);
    }

    [Fact]
    public void StatusProjection_FormatsLastSyncUsingUiCulture()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("zh-CN");
            var snapshot = CreateSnapshot(
                enabled: false,
                providerId: "webdav",
                options: new Dictionary<string, string>(),
                transfer: new CloudTransferState(
                    CloudTransferPhase.Idle,
                    new CloudTransferSuccess(
                        CloudTransferOutcome.Uploaded,
                        new DateTimeOffset(2026, 7, 13, 4, 28, 0, TimeSpan.Zero))));
            var viewModel = CreateViewModel(new FakeCoordinator(snapshot));

            Assert.Contains("2026", viewModel.TransferStatusText, StringComparison.Ordinal);
            Assert.DoesNotContain("AM", viewModel.TransferStatusText, StringComparison.Ordinal);
            Assert.DoesNotContain("PM", viewModel.TransferStatusText, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    [Theory]
    [InlineData(CloudTransferPhase.Syncing)]
    [InlineData(CloudTransferPhase.Failed)]
    public void StatusProjection_CurrentTransferPhaseDoesNotReplaceLastSuccessfulSyncHistory(
        CloudTransferPhase phase)
    {
        var completedAt = new DateTimeOffset(2026, 7, 13, 4, 28, 0, TimeSpan.Zero);
        var snapshot = CreateSnapshot(
            enabled: true,
            providerId: "webdav",
            options: new Dictionary<string, string>(),
            transfer: new CloudTransferState(
                phase,
                new CloudTransferSuccess(CloudTransferOutcome.Uploaded, completedAt),
                phase == CloudTransferPhase.Failed
                    ? new CloudSyncFailure(CloudSyncFailureKind.Network, "Network failed.")
                    : null),
            readiness: CloudProviderReadiness.Ready);
        var localizer = new TestCoreStringLocalizer();
        var viewModel = CreateViewModel(new FakeCoordinator(snapshot), localizer: localizer);

        Assert.Equal(
            string.Format(
                CultureInfo.CurrentUICulture,
                localizer["DataStorage_CloudSyncTransferWithTime"],
                localizer["DataStorage_CloudSyncUploadedLocal"].Value,
                completedAt.ToLocalTime()),
            viewModel.TransferStatusText);
    }

    [Fact]
    public void StatusProjection_WhenReadinessFaulted_ShowsNeedsAttentionInsteadOfChecking()
    {
        var snapshot = CreateSnapshot(
            enabled: true,
            providerId: "webdav",
            options: new Dictionary<string, string>(),
            transfer: new CloudTransferState(CloudTransferPhase.Failed),
            credential: CloudCredentialState.StoreUnavailable,
            readiness: CloudProviderReadiness.Faulted) with
        {
            LastFailure = new CloudSyncFailure(
                CloudSyncFailureKind.CredentialStoreUnavailable,
                "Secure storage unavailable.")
        };
        var localizer = new TestCoreStringLocalizer();
        var viewModel = CreateViewModel(new FakeCoordinator(snapshot), localizer: localizer);

        Assert.Equal(localizer["DataStorage_CloudSyncFaultedHeadline"], viewModel.StatusHeadline);
        Assert.Equal(localizer["DataStorage_CloudSyncCredentialStoreUnavailable"], viewModel.ErrorText);
        Assert.False(viewModel.IsChecking);
    }

    [Fact]
    public void StatusProjection_WhenRemoteConflictPending_ExposesResolutionActionsAndBlocksSync()
    {
        var snapshot = CreateSnapshot(
            enabled: true,
            providerId: "webdav",
            options: new Dictionary<string, string> { ["file_url"] = "https://dav.example.test/config.zip" },
            transfer: new CloudTransferState(
                CloudTransferPhase.Failed,
                Failure: new CloudSyncFailure(
                    CloudSyncFailureKind.RemoteConflict,
                    "true conflict",
                    ArtifactPath: "/tmp/conflict")),
            readiness: CloudProviderReadiness.Ready) with
        {
            LastFailure = new CloudSyncFailure(
                CloudSyncFailureKind.RemoteConflict,
                "true conflict",
                ArtifactPath: "/tmp/conflict")
        };
        var localizer = new TestCoreStringLocalizer();
        var viewModel = CreateViewModel(new FakeCoordinator(snapshot), localizer: localizer);

        Assert.True(viewModel.HasPendingConflict);
        Assert.True(viewModel.CanResolveConflict);
        Assert.False(viewModel.CanSync);
        Assert.Equal(localizer["DataStorage_CloudSyncConflictNeedsResolution"], viewModel.ErrorText);
        Assert.Equal(localizer["DataStorage_CloudSyncKeepLocal"], viewModel.KeepLocalConflictText);
        Assert.Equal(localizer["DataStorage_CloudSyncApplyRemote"], viewModel.ApplyRemoteConflictText);
    }

    [Fact]
    public async Task KeepLocalConflictCommand_WhenPending_InvokesCoordinatorWithKeepLocal()
    {
        var snapshot = CreateSnapshot(
            enabled: true,
            providerId: "webdav",
            options: new Dictionary<string, string>(),
            transfer: new CloudTransferState(
                CloudTransferPhase.Failed,
                Failure: new CloudSyncFailure(CloudSyncFailureKind.RemoteConflict, "true conflict")),
            readiness: CloudProviderReadiness.Ready) with
        {
            LastFailure = new CloudSyncFailure(CloudSyncFailureKind.RemoteConflict, "true conflict")
        };
        var coordinator = new FakeCoordinator(snapshot);
        var viewModel = CreateViewModel(coordinator);

        await viewModel.KeepLocalConflictCommand.ExecuteAsync(null);

        Assert.Equal(CloudSyncConflictResolution.KeepLocal, coordinator.LastConflictResolution);
        Assert.False(viewModel.HasPendingConflict);
        Assert.Equal(CloudTransferOutcome.Uploaded, coordinator.Current.Transfer.LastSuccess?.Outcome);
    }

    [Fact]
    public void ColdStart_WithStoredWebDavCredential_ShowsAvailableCredentialWithoutRequestingPassword()
    {
        var coordinator = new FakeCoordinator(CreateReadySnapshot(
            "webdav",
            new Dictionary<string, string>
            {
                ["file_url"] = "https://dav.example.test/config/",
                ["username"] = "alice"
            },
            CloudCredentialState.Available));

        var localizer = new TestCoreStringLocalizer();
        var viewModel = CreateViewModel(coordinator, localizer: localizer);

        Assert.True(viewModel.IsEnabled);
        Assert.False(viewModel.IsEditing);
        Assert.Equal("", viewModel.WebDavPassword);
        Assert.Equal(localizer["DataStorage_CloudSyncCredentialAvailable"], viewModel.CredentialStatusText);
        Assert.Equal(localizer["DataStorage_CloudSyncWebDavPasswordSavedHeader"], viewModel.WebDavPasswordHeaderText);
        Assert.Equal(localizer["DataStorage_CloudSyncWebDavPasswordSavedPlaceholder"], viewModel.WebDavPasswordPlaceholderText);
        Assert.False(viewModel.HasError);
    }

    [Fact]
    public void CredentialProjection_WhenInspectionIsUnknown_DoesNotClaimSavedSecretExists()
    {
        var snapshot = CreateSnapshot(
            enabled: false,
            providerId: "webdav",
            options: new Dictionary<string, string>
            {
                ["file_url"] = "https://dav.example.test/config/",
                ["username"] = "alice"
            },
            transfer: new CloudTransferState(CloudTransferPhase.Idle));
        var localizer = new TestCoreStringLocalizer();
        var viewModel = CreateViewModel(new FakeCoordinator(snapshot), localizer: localizer);

        Assert.Equal(localizer["DataStorage_CloudSyncWebDavPasswordHeader"], viewModel.WebDavPasswordHeaderText);
        Assert.Empty(viewModel.WebDavPasswordPlaceholderText);
        Assert.Empty(viewModel.S3AccessKeyIdPlaceholderText);
        Assert.Empty(viewModel.S3SecretAccessKeyPlaceholderText);
    }

    [Fact]
    public async Task ApplyCommand_WithBlankPassword_UsesKeepExistingSecretUpdate()
    {
        var coordinator = new FakeCoordinator(CreateReadySnapshot(
            "webdav",
            new Dictionary<string, string>
            {
                ["file_url"] = "https://dav.example.test/config/",
                ["username"] = "alice"
            },
            CloudCredentialState.Available));
        var viewModel = CreateViewModel(coordinator);
        viewModel.EditCommand.Execute(null);
        await viewModel.RetryCredentialCheckCommand.ExecuteAsync(null);

        await viewModel.ApplyCommand.ExecuteAsync(null);

        Assert.NotNull(coordinator.LastDraft);
        Assert.Equal(CloudSecretUpdateKind.KeepExisting, coordinator.LastDraft!.Secrets["password"].Kind);
    }

    [Fact]
    public async Task ApplyCommand_WithNewPassword_UsesReplaceAndClearsSecretFieldAfterSuccess()
    {
        var coordinator = new FakeCoordinator(CreateReadySnapshot(
            "webdav",
            new Dictionary<string, string>
            {
                ["file_url"] = "https://dav.example.test/config/",
                ["username"] = "alice"
            },
            CloudCredentialState.Available));
        var viewModel = CreateViewModel(coordinator);
        viewModel.EditCommand.Execute(null);
        viewModel.WebDavPassword = "new-password";
        await viewModel.RetryCredentialCheckCommand.ExecuteAsync(null);

        await viewModel.ApplyCommand.ExecuteAsync(null);

        Assert.Equal(CloudSecretUpdateKind.Replace, coordinator.LastDraft!.Secrets["password"].Kind);
        Assert.Equal("new-password", coordinator.LastDraft.Secrets["password"].Value);
        Assert.Empty(viewModel.WebDavPassword);
        Assert.False(viewModel.IsEditing);
    }

    [Fact]
    public async Task ApplyCommand_WhenSwitchingProviderAndConfirmationIsCancelled_DoesNotApply()
    {
        var coordinator = new FakeCoordinator(CreateReadySnapshot(
            "webdav",
            new Dictionary<string, string> { ["file_url"] = "https://dav.example.test/config/" },
            CloudCredentialState.Available));
        var ui = new Mock<IUiInteractionService>();
        string? title = null;
        string? message = null;
        ui.Setup(service => service.ConfirmAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Callback<string, string, string, string>((capturedTitle, capturedMessage, _, _) =>
            {
                title = capturedTitle;
                message = capturedMessage;
            })
            .ReturnsAsync(false);
        var localizer = new TestCoreStringLocalizer();
        var viewModel = CreateViewModel(coordinator, ui.Object, localizer);
        viewModel.EditCommand.Execute(null);
        viewModel.SelectedProviderId = "s3";
        viewModel.S3Endpoint = "https://s3.example.test";
        viewModel.S3Bucket = "config";
        await viewModel.RetryCredentialCheckCommand.ExecuteAsync(null);

        await viewModel.ApplyCommand.ExecuteAsync(null);

        Assert.Equal(
            string.Format(
                CultureInfo.CurrentUICulture,
                localizer["DataStorage_CloudSyncSwitchConfirmTitle"],
                "S3 compatible"),
            title);
        Assert.Equal(
            string.Format(
                CultureInfo.CurrentUICulture,
                localizer["DataStorage_CloudSyncSwitchConfirmMessage"],
                "WebDAV",
                "S3 compatible"),
            message);
        Assert.Null(coordinator.LastDraft);
        Assert.True(viewModel.IsEditing);
    }

    [Fact]
    public async Task ApplyCommand_WhenConfiguringFirstProviderAndConfirmationIsAccepted_Applies()
    {
        var coordinator = new FakeCoordinator(CreateSnapshot(
            enabled: false,
            providerId: string.Empty,
            options: new Dictionary<string, string>(),
            transfer: new CloudTransferState(CloudTransferPhase.Idle)));
        var ui = new Mock<IUiInteractionService>();
        string? title = null;
        ui.Setup(service => service.ConfirmAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Callback<string, string, string, string>((capturedTitle, _, _, _) => title = capturedTitle)
            .ReturnsAsync(true);
        var localizer = new TestCoreStringLocalizer();
        var viewModel = CreateViewModel(coordinator, ui.Object, localizer);
        viewModel.EditCommand.Execute(null);
        await viewModel.RetryCredentialCheckCommand.ExecuteAsync(null);

        await viewModel.ApplyCommand.ExecuteAsync(null);

        Assert.Equal(
            string.Format(
                CultureInfo.CurrentUICulture,
                localizer["DataStorage_CloudSyncActivationConfirmTitle"],
                "OneDrive"),
            title);
        Assert.NotNull(coordinator.LastDraft);
        Assert.Equal("onedrive", coordinator.LastDraft!.ProviderId);
    }

    [Fact]
    public async Task DisableCommand_DisablesWithoutForgettingProvider()
    {
        var coordinator = new FakeCoordinator(CreateReadySnapshot("onedrive", new Dictionary<string, string>(), CloudCredentialState.Available));
        var viewModel = CreateViewModel(coordinator);

        await viewModel.DisableCommand.ExecuteAsync(null);

        Assert.Equal(1, coordinator.DisableCount);
        Assert.Equal(0, coordinator.ForgetCount);
    }

    [Fact]
    public async Task ForgetCommand_WhenConfirmed_ForgetsActiveProvider()
    {
        var coordinator = new FakeCoordinator(CreateReadySnapshot("onedrive", new Dictionary<string, string>(), CloudCredentialState.Available));
        var ui = new Mock<IUiInteractionService>();
        ui.Setup(service => service.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);
        var viewModel = CreateViewModel(coordinator, ui.Object);

        await viewModel.ForgetCommand.ExecuteAsync(null);

        Assert.Equal(1, coordinator.ForgetCount);
        Assert.Equal("onedrive", coordinator.LastForgottenProviderId);
    }

    [Fact]
    public async Task ForgetCommand_ConfirmationExplainsLocalScopeAndRemotePreservation()
    {
        var coordinator = new FakeCoordinator(CreateReadySnapshot(
            "webdav",
            new Dictionary<string, string> { ["file_url"] = "https://dav.example.test/config/" },
            CloudCredentialState.Available));
        var ui = new Mock<IUiInteractionService>();
        string? title = null;
        string? message = null;
        string? primary = null;
        ui.Setup(service => service.ConfirmAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Callback<string, string, string, string>((capturedTitle, capturedMessage, capturedPrimary, _) =>
            {
                title = capturedTitle;
                message = capturedMessage;
                primary = capturedPrimary;
            })
            .ReturnsAsync(false);
        var localizer = new TestCoreStringLocalizer();
        var viewModel = CreateViewModel(coordinator, ui.Object, localizer);

        await viewModel.ForgetCommand.ExecuteAsync(null);

        Assert.Equal(
            string.Format(
                CultureInfo.CurrentUICulture,
                localizer["DataStorage_CloudSyncForgetTitle"],
                "WebDAV"),
            title);
        Assert.Equal(localizer["DataStorage_CloudSyncForgetMessage"], message);
        Assert.Equal(localizer["DataStorage_CloudSyncForgetPrimary"], primary);
        Assert.Equal(0, coordinator.ForgetCount);
    }

    [Fact]
    public async Task ForgetCommand_ForOneDrive_DisclosesAllSavedAccountsAreRemovedFromThisDevice()
    {
        var coordinator = new FakeCoordinator(CreateReadySnapshot(
            "onedrive",
            new Dictionary<string, string>(),
            CloudCredentialState.Available));
        var ui = new Mock<IUiInteractionService>();
        string? message = null;
        ui.Setup(service => service.ConfirmAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Callback<string, string, string, string>((_, capturedMessage, _, _) => message = capturedMessage)
            .ReturnsAsync(false);
        var localizer = new TestCoreStringLocalizer();
        var viewModel = CreateViewModel(coordinator, ui.Object, localizer);

        await viewModel.ForgetCommand.ExecuteAsync(null);

        Assert.Equal(localizer["DataStorage_CloudSyncForgetOneDriveMessage"], message);
    }

    [Fact]
    public async Task MissingStoredCredential_RequiresPasswordOnlyWhenUsernameIsPresent()
    {
        var snapshot = CreateReadySnapshot(
            "webdav",
            new Dictionary<string, string>
            {
                ["file_url"] = "https://dav.example.test/config/",
                ["username"] = "alice"
            },
            CloudCredentialState.Missing) with
        {
            Readiness = CloudProviderReadiness.AuthenticationRequired,
            LastFailure = new CloudSyncFailure(CloudSyncFailureKind.CredentialMissing, "Missing.")
        };
        var localizer = new TestCoreStringLocalizer();
        var viewModel = CreateViewModel(new FakeCoordinator(snapshot), localizer: localizer);
        viewModel.EditCommand.Execute(null);

        await viewModel.RetryCredentialCheckCommand.ExecuteAsync(null);

        Assert.False(viewModel.CanApply);
        Assert.NotEmpty(viewModel.ValidationMessage);
        Assert.Equal(localizer["DataStorage_CloudSyncCredentialMissingAction"], viewModel.CredentialStatusText);
        Assert.Equal(localizer["DataStorage_CloudSyncWebDavPasswordHeader"], viewModel.WebDavPasswordHeaderText);
        Assert.Equal(localizer["DataStorage_CloudSyncWebDavPasswordMissingPlaceholder"], viewModel.WebDavPasswordPlaceholderText);

        viewModel.WebDavPassword = "replacement";

        Assert.True(viewModel.CanApply);
    }

    private static CloudConfigSettingsViewModel CreateViewModel(
        FakeCoordinator coordinator,
        IUiInteractionService? ui = null,
        TestCoreStringLocalizer? localizer = null) => new(
            coordinator,
            ui ?? Mock.Of<IUiInteractionService>(),
            new ImmediateUiDispatcher(),
            localizer ?? new TestCoreStringLocalizer());

    private static CloudConfigSyncSnapshot CreateReadySnapshot(
        string providerId,
        IReadOnlyDictionary<string, string> options,
        CloudCredentialState credential) => CreateSnapshot(
            enabled: true,
            providerId,
            options,
            new CloudTransferState(
                CloudTransferPhase.Succeeded,
                new CloudTransferSuccess(CloudTransferOutcome.Uploaded, DateTimeOffset.UtcNow)),
            credential,
            CloudProviderReadiness.Ready);

    private static CloudConfigSyncSnapshot CreateSnapshot(
        bool enabled,
        string providerId,
        IReadOnlyDictionary<string, string> options,
        CloudTransferState transfer,
        CloudCredentialState credential = CloudCredentialState.Unknown,
        CloudProviderReadiness readiness = CloudProviderReadiness.Disabled) => new(
            Version: 1,
            Initialization: CloudSyncInitializationState.Ready,
            Configuration: new CloudSyncConfiguration(enabled, providerId, 1, options),
            Credential: credential,
            Readiness: readiness,
            Transfer: transfer,
            Operation: null,
            LastFailure: null);

    private sealed class FakeCoordinator : ICloudConfigSyncCoordinator
    {
        public FakeCoordinator(CloudConfigSyncSnapshot snapshot)
        {
            Current = snapshot;
            Providers =
            [
                new CloudConfigProviderDescriptor("onedrive", "OneDrive", true),
                new CloudConfigProviderDescriptor("webdav", "WebDAV", true),
                new CloudConfigProviderDescriptor("s3", "S3 compatible", true)
            ];
        }

        public IReadOnlyList<CloudConfigProviderDescriptor> Providers { get; }

        public CloudConfigSyncSnapshot Current { get; private set; }

        public CloudProviderDraft? LastDraft { get; private set; }

        public int DisableCount { get; private set; }

        public int ForgetCount { get; private set; }

        public string? LastForgottenProviderId { get; private set; }

        public event EventHandler<CloudConfigSyncSnapshot>? SnapshotChanged;

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ApplyAndActivateAsync(CloudProviderDraft draft, CancellationToken cancellationToken = default)
        {
            LastDraft = draft;
            Current = Current with
            {
                Configuration = new CloudSyncConfiguration(true, draft.ProviderId, Current.Configuration.Revision + 1, draft.Options),
                Credential = CloudCredentialState.Available,
                Readiness = CloudProviderReadiness.Ready,
                Transfer = new CloudTransferState(
                    CloudTransferPhase.Succeeded,
                    new CloudTransferSuccess(CloudTransferOutcome.Uploaded, DateTimeOffset.UtcNow)),
                Operation = null,
                LastFailure = null
            };
            SnapshotChanged?.Invoke(this, Current);
            return Task.CompletedTask;
        }

        public Task SyncNowAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public CloudSyncConflictResolution? LastConflictResolution { get; private set; }

        public Task ResolveConflictAsync(
            CloudSyncConflictResolution resolution,
            CancellationToken cancellationToken = default)
        {
            LastConflictResolution = resolution;
            Current = Current with
            {
                Transfer = new CloudTransferState(
                    CloudTransferPhase.Succeeded,
                    new CloudTransferSuccess(
                        resolution == CloudSyncConflictResolution.KeepLocal
                            ? CloudTransferOutcome.Uploaded
                            : CloudTransferOutcome.Restored,
                        DateTimeOffset.UtcNow)),
                Operation = null,
                LastFailure = null
            };
            SnapshotChanged?.Invoke(this, Current);
            return Task.CompletedTask;
        }

        public Task DisableAsync(CancellationToken cancellationToken = default)
        {
            DisableCount++;
            return Task.CompletedTask;
        }

        public Task ForgetProviderAsync(string providerId, CancellationToken cancellationToken = default)
        {
            ForgetCount++;
            LastForgottenProviderId = providerId;
            return Task.CompletedTask;
        }

        public Task<CloudCredentialInspection> InspectCredentialAsync(
            string providerId,
            IReadOnlyDictionary<string, string> options,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new CloudCredentialInspection(Current.Credential));
    }
}
