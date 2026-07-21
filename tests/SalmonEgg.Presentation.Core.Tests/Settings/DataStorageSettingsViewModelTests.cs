using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using SalmonEgg.Domain.Models.Diagnostics;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Tests.Localization;
using SalmonEgg.Presentation.Core.Tests.Threading;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.ViewModels.Chat;
using SalmonEgg.Presentation.ViewModels.Settings;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Settings;

public sealed class DataStorageSettingsViewModelTests
{
    [Fact]
    public async Task ExportCurrentSessionJsonCommand_WhenLocalFileExportUnsupported_DoesNotExport()
    {
        var sessionExport = new Mock<ISessionExportService>();
        var ui = new Mock<IUiInteractionService>();
        var localizer = new TestCoreStringLocalizer();
        var viewModel = CreateViewModel(
            supportsLocalFileExport: false,
            sessionExport: sessionExport,
            ui: ui,
            localizer: localizer);

        await viewModel.ExportCurrentSessionJsonCommand.ExecuteAsync(null);

        sessionExport.Verify(service => service.ExportAsync(It.IsAny<SessionExportRequest>(), default), Times.Never);
        ui.Verify(service => service.ShowInfoAsync(localizer["Platform_LocalFileExportUnsupported"]), Times.Once);
    }

    [Fact]
    public async Task CreateDiagnosticsBundleCommand_WhenLocalFileExportUnsupported_DoesNotCreateBundle()
    {
        var diagnostics = new Mock<IDiagnosticsBundleService>();
        var ui = new Mock<IUiInteractionService>();
        var localizer = new TestCoreStringLocalizer();
        var viewModel = CreateViewModel(
            supportsLocalFileExport: false,
            diagnostics: diagnostics,
            ui: ui,
            localizer: localizer);

        await viewModel.CreateDiagnosticsBundleCommand.ExecuteAsync(null);

        diagnostics.Verify(service => service.CreateBundleAsync(It.IsAny<DiagnosticsSnapshot>()), Times.Never);
        ui.Verify(service => service.ShowInfoAsync(localizer["Platform_LocalFileExportUnsupported"]), Times.Once);
    }

    [Fact]
    public async Task ClearCacheCommand_WhenMaintenanceSucceeds_ShowsLocalizedSuccess()
    {
        var maintenance = new Mock<IAppMaintenanceService>();
        maintenance.Setup(service => service.ClearCacheAsync()).Returns(Task.CompletedTask);
        var ui = new Mock<IUiInteractionService>();
        var localizer = new TestCoreStringLocalizer();
        var viewModel = CreateViewModel(
            supportsLocalFileExport: true,
            maintenance: maintenance,
            ui: ui,
            localizer: localizer);

        await viewModel.ClearCacheCommand.ExecuteAsync(null);

        maintenance.Verify(service => service.ClearCacheAsync(), Times.Once);
        ui.Verify(service => service.ShowInfoAsync(localizer["General_ClearCacheSuccess"]), Times.Once);
    }

    [Fact]
    public async Task ClearCacheCommand_WhenMaintenanceFails_ShowsLocalizedFailure()
    {
        var maintenance = new Mock<IAppMaintenanceService>();
        maintenance.Setup(service => service.ClearCacheAsync())
            .ThrowsAsync(new IOException("locked"));
        var ui = new Mock<IUiInteractionService>();
        var localizer = new TestCoreStringLocalizer();
        var viewModel = CreateViewModel(
            supportsLocalFileExport: true,
            maintenance: maintenance,
            ui: ui,
            localizer: localizer);

        await viewModel.ClearCacheCommand.ExecuteAsync(null);

        ui.Verify(service => service.ShowInfoAsync(localizer["General_ClearCacheFailed"]), Times.Once);
    }

    [Fact]
    public async Task ClearAllLocalDataCommand_WhenMaintenanceFails_ShowsLocalizedFailure()
    {
        var maintenance = new Mock<IAppMaintenanceService>();
        maintenance.Setup(service => service.ClearAllLocalDataAsync())
            .ThrowsAsync(new UnauthorizedAccessException("denied"));
        var ui = new Mock<IUiInteractionService>();
        var localizer = new TestCoreStringLocalizer();
        var viewModel = CreateViewModel(
            supportsLocalFileExport: true,
            maintenance: maintenance,
            ui: ui,
            localizer: localizer);

        await viewModel.ClearAllLocalDataCommand.ExecuteAsync(null);

        ui.Verify(service => service.ShowInfoAsync(localizer["DataStorage_ClearAllLocalDataFailed"]), Times.Once);
    }

    private static DataStorageSettingsViewModel CreateViewModel(
        bool supportsLocalFileExport,
        Mock<IDiagnosticsBundleService>? diagnostics = null,
        Mock<ISessionExportService>? sessionExport = null,
        Mock<IAppMaintenanceService>? maintenance = null,
        Mock<IUiInteractionService>? ui = null,
        TestCoreStringLocalizer? localizer = null)
    {
        localizer ??= new TestCoreStringLocalizer();
        var preferences = (AppPreferencesViewModel)RuntimeHelpers.GetUninitializedObject(typeof(AppPreferencesViewModel));
        var chat = (ChatViewModel)RuntimeHelpers.GetUninitializedObject(typeof(ChatViewModel));
        var capabilities = new Mock<IPlatformCapabilityService>();
        capabilities.SetupGet(service => service.SupportsExternalFileOpen).Returns(true);
        capabilities.SetupGet(service => service.SupportsLocalFileExport).Returns(supportsLocalFileExport);
        var coordinator = new Mock<ICloudConfigSyncCoordinator>();
        coordinator.SetupGet(service => service.Current).Returns(CloudConfigSyncSnapshot.Initial);
        coordinator.SetupGet(service => service.Providers).Returns([]);
        var cloudConfig = new CloudConfigSettingsViewModel(
            coordinator.Object,
            ui?.Object ?? Mock.Of<IUiInteractionService>(),
            new ImmediateUiDispatcher(),
            new TestCoreStringLocalizer());

        return new DataStorageSettingsViewModel(
            preferences,
            chat,
            cloudConfig,
            Mock.Of<IAppDataService>(),
            maintenance?.Object ?? Mock.Of<IAppMaintenanceService>(),
            diagnostics?.Object ?? Mock.Of<IDiagnosticsBundleService>(),
            Mock.Of<IPlatformShellService>(),
            capabilities.Object,
            Mock.Of<IStorageLocationService>(),
            sessionExport?.Object ?? Mock.Of<ISessionExportService>(),
            ui?.Object ?? Mock.Of<IUiInteractionService>(),
            localizer,
            Mock.Of<ILogger<DataStorageSettingsViewModel>>());
    }
}
