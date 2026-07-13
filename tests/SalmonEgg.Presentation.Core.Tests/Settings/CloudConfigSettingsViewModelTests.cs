using System;
using System.Collections.Generic;
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
        Assert.Equal("DataStorage_CloudSyncCredentialAvailable", viewModel.CredentialStatusText);
        Assert.False(viewModel.HasError);
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
        CloudCredentialState credential) => new(
            Version: 1,
            Initialization: CloudSyncInitializationState.Ready,
            Configuration: new CloudSyncConfiguration(true, providerId, 1, options),
            Credential: credential,
            Readiness: CloudProviderReadiness.Ready,
            Transfer: new CloudTransferState(
                CloudTransferPhase.Succeeded,
                new CloudTransferSuccess(CloudTransferOutcome.Uploaded, DateTimeOffset.UtcNow)),
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
