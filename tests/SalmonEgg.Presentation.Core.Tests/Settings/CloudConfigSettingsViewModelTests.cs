using System;
using System.Collections.Generic;
using System.Globalization;
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
        var viewModel = CreateViewModel(new FakeCoordinator(snapshot));

        Assert.Equal("尚未设置云同步", viewModel.StatusHeadline);
        Assert.Empty(viewModel.ConnectionContextText);
        Assert.Equal("尚未同步", viewModel.TransferStatusText);
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
        var viewModel = CreateViewModel(new FakeCoordinator(snapshot));

        Assert.Equal("云同步已关闭", viewModel.StatusHeadline);
        Assert.Equal("已保留 WebDAV 连接设置", viewModel.ConnectionContextText);
        Assert.StartsWith("上次同步：已上传本地配置 · ", viewModel.TransferStatusText, StringComparison.Ordinal);
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
        var viewModel = CreateViewModel(new FakeCoordinator(snapshot));

        Assert.Equal("云同步已开启 · WebDAV", viewModel.StatusHeadline);
        Assert.Equal("同步位置：https://dav.example.test/config/", viewModel.ConnectionContextText);
    }

    [Fact]
    public void ActionProjection_OnlyShowsActionsRelevantToCurrentState()
    {
        var unconfigured = CreateViewModel(new FakeCoordinator(CreateSnapshot(
            enabled: false,
            providerId: string.Empty,
            options: new Dictionary<string, string>(),
            transfer: new CloudTransferState(CloudTransferPhase.Idle))));
        Assert.True(unconfigured.ShowEditAction);
        Assert.Equal("设置云同步", unconfigured.EditActionText);
        Assert.False(unconfigured.ShowSyncAction);
        Assert.False(unconfigured.ShowDisableAction);
        Assert.False(unconfigured.ShowRemoveAction);

        var disabled = CreateViewModel(new FakeCoordinator(CreateSnapshot(
            enabled: false,
            providerId: "webdav",
            options: new Dictionary<string, string>(),
            transfer: new CloudTransferState(CloudTransferPhase.Idle))));
        Assert.True(disabled.ShowEditAction);
        Assert.Equal("编辑或重新开启", disabled.EditActionText);
        Assert.False(disabled.ShowSyncAction);
        Assert.False(disabled.ShowDisableAction);
        Assert.True(disabled.ShowRemoveAction);

        var enabled = CreateViewModel(new FakeCoordinator(CreateReadySnapshot(
            "webdav",
            new Dictionary<string, string>(),
            CloudCredentialState.Available)));
        Assert.True(enabled.ShowEditAction);
        Assert.Equal("编辑云同步设置", enabled.EditActionText);
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
        var viewModel = CreateViewModel(new FakeCoordinator(snapshot));

        Assert.StartsWith("上次同步：已上传本地配置 · ", viewModel.TransferStatusText, StringComparison.Ordinal);
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
        var viewModel = CreateViewModel(new FakeCoordinator(snapshot));

        Assert.Equal("云同步需要处理", viewModel.StatusHeadline);
        Assert.Equal("无法访问安全存储。请重试。", viewModel.ErrorText);
        Assert.False(viewModel.IsChecking);
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

        var viewModel = CreateViewModel(coordinator);

        Assert.True(viewModel.IsEnabled);
        Assert.False(viewModel.IsEditing);
        Assert.Equal("", viewModel.WebDavPassword);
        Assert.Equal("已保存登录信息。留空会继续使用现有密码或访问密钥。", viewModel.CredentialStatusText);
        Assert.Equal("新密码（可选）", viewModel.WebDavPasswordHeaderText);
        Assert.Equal("留空以继续使用已保存的密码", viewModel.WebDavPasswordPlaceholderText);
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
        var viewModel = CreateViewModel(new FakeCoordinator(snapshot));

        Assert.Equal("WebDAV 密码", viewModel.WebDavPasswordHeaderText);
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
        var viewModel = CreateViewModel(coordinator, ui.Object);
        viewModel.EditCommand.Execute(null);
        viewModel.SelectedProviderId = "s3";
        viewModel.S3Endpoint = "https://s3.example.test";
        viewModel.S3Bucket = "config";
        await viewModel.RetryCredentialCheckCommand.ExecuteAsync(null);

        await viewModel.ApplyCommand.ExecuteAsync(null);

        Assert.Equal("切换到 S3 compatible 并同步？", title);
        Assert.Equal(
            "这会停止使用 WebDAV。如果 S3 compatible 中已有配置，将先备份本机配置，再应用云端版本。",
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
        var viewModel = CreateViewModel(coordinator, ui.Object);
        viewModel.EditCommand.Execute(null);
        await viewModel.RetryCredentialCheckCommand.ExecuteAsync(null);

        await viewModel.ApplyCommand.ExecuteAsync(null);

        Assert.Equal("使用 OneDrive 开始同步？", title);
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
        var viewModel = CreateViewModel(coordinator, ui.Object);

        await viewModel.ForgetCommand.ExecuteAsync(null);

        Assert.Equal("移除此设备上的 WebDAV 同步设置？", title);
        Assert.Equal(
            "这会关闭同步，并删除此设备保存的连接设置、登录信息和同步记录。不会删除云端数据或当前应用配置。",
            message);
        Assert.Equal("移除本机设置", primary);
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
        var viewModel = CreateViewModel(coordinator, ui.Object);

        await viewModel.ForgetCommand.ExecuteAsync(null);

        Assert.Equal(
            "这会关闭同步，并从此设备移除 Salmon Egg 保存的所有 OneDrive 登录账户和同步记录。不会删除 OneDrive 中的数据或当前应用配置。",
            message);
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
        var viewModel = CreateViewModel(new FakeCoordinator(snapshot));
        viewModel.EditCommand.Execute(null);

        await viewModel.RetryCredentialCheckCommand.ExecuteAsync(null);

        Assert.False(viewModel.CanApply);
        Assert.NotEmpty(viewModel.ValidationMessage);
        Assert.Equal("未找到已保存的登录信息。请输入密码或访问密钥。", viewModel.CredentialStatusText);
        Assert.Equal("WebDAV 密码", viewModel.WebDavPasswordHeaderText);
        Assert.Equal("输入 WebDAV 密码", viewModel.WebDavPasswordPlaceholderText);

        viewModel.WebDavPassword = "replacement";

        Assert.True(viewModel.CanApply);
    }

    private static CloudConfigSettingsViewModel CreateViewModel(
        FakeCoordinator coordinator,
        IUiInteractionService? ui = null) => new(
            coordinator,
            ui ?? Mock.Of<IUiInteractionService>(),
            new ImmediateUiDispatcher(),
            new TestCoreStringLocalizer());

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
