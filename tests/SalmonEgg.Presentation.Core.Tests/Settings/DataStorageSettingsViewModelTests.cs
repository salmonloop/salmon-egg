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
        var viewModel = CreateViewModel(
            supportsLocalFileExport: false,
            sessionExport: sessionExport,
            ui: ui);

        await viewModel.ExportCurrentSessionJsonCommand.ExecuteAsync(null);

        sessionExport.Verify(service => service.ExportAsync(It.IsAny<SessionExportRequest>(), default), Times.Never);
        ui.Verify(service => service.ShowInfoAsync("当前平台暂不支持导出本地文件。"), Times.Once);
    }

    [Fact]
    public async Task CreateDiagnosticsBundleCommand_WhenLocalFileExportUnsupported_DoesNotCreateBundle()
    {
        var diagnostics = new Mock<IDiagnosticsBundleService>();
        var ui = new Mock<IUiInteractionService>();
        var viewModel = CreateViewModel(
            supportsLocalFileExport: false,
            diagnostics: diagnostics,
            ui: ui);

        await viewModel.CreateDiagnosticsBundleCommand.ExecuteAsync(null);

        diagnostics.Verify(service => service.CreateBundleAsync(It.IsAny<DiagnosticsSnapshot>()), Times.Never);
        ui.Verify(service => service.ShowInfoAsync("当前平台暂不支持导出本地文件。"), Times.Once);
    }

    private static DataStorageSettingsViewModel CreateViewModel(
        bool supportsLocalFileExport,
        Mock<IDiagnosticsBundleService>? diagnostics = null,
        Mock<ISessionExportService>? sessionExport = null,
        Mock<IUiInteractionService>? ui = null)
    {
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
            Mock.Of<IAppMaintenanceService>(),
            diagnostics?.Object ?? Mock.Of<IDiagnosticsBundleService>(),
            Mock.Of<IPlatformShellService>(),
            capabilities.Object,
            Mock.Of<IStorageLocationService>(),
            sessionExport?.Object ?? Mock.Of<ISessionExportService>(),
            ui?.Object ?? Mock.Of<IUiInteractionService>(),
            new TestCoreStringLocalizer(),
            Mock.Of<ILogger<DataStorageSettingsViewModel>>());
    }
}
