using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.Diagnostics;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Tests.Localization;
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

    [Fact]
    public async Task AuthorizeOneDriveCloudSyncCommand_WhenProviderUploads_UpdatesStatus()
    {
        var cloudSync = new Mock<ICloudConfigSyncService>();
        cloudSync.SetupGet(service => service.Providers).Returns(new[]
        {
            new CloudConfigProviderDescriptor("onedrive", "OneDrive", true)
        });
        cloudSync
            .Setup(service => service.AuthorizeAndSyncAsync("onedrive", default))
            .ReturnsAsync(new CloudConfigSyncResult(
                CloudConfigSyncStatus.Uploaded,
                "onedrive",
                "etag-1",
                new DateTimeOffset(2026, 7, 7, 12, 0, 0, TimeSpan.Zero)));
        var viewModel = CreateViewModel(cloudSync: cloudSync);

        await viewModel.AuthorizeOneDriveCloudSyncCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsCloudConfigSyncEnabled);
        Assert.Equal("本地配置已上传到 OneDrive。", viewModel.CloudConfigSyncStatusText);
        Assert.Empty(viewModel.CloudConfigSyncErrorText);
    }

    [Fact]
    public async Task AuthorizeOneDriveCloudSyncCommand_WhenProviderMissingConfig_ShowsErrorStatus()
    {
        var cloudSync = new Mock<ICloudConfigSyncService>();
        cloudSync.SetupGet(service => service.Providers).Returns(Array.Empty<CloudConfigProviderDescriptor>());
        cloudSync
            .Setup(service => service.AuthorizeAndSyncAsync("onedrive", default))
            .ReturnsAsync(CloudConfigSyncResult.NotConfigured("onedrive"));
        var viewModel = CreateViewModel(cloudSync: cloudSync);

        await viewModel.AuthorizeOneDriveCloudSyncCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsCloudConfigSyncEnabled);
        Assert.Equal("OneDrive 应用注册未配置。", viewModel.CloudConfigSyncErrorText);
    }

    private static DataStorageSettingsViewModel CreateViewModel(
        bool supportsLocalFileExport = true,
        Mock<IDiagnosticsBundleService>? diagnostics = null,
        Mock<ISessionExportService>? sessionExport = null,
        Mock<ICloudConfigSyncService>? cloudSync = null,
        Mock<IUiInteractionService>? ui = null)
    {
        var preferences = (AppPreferencesViewModel)RuntimeHelpers.GetUninitializedObject(typeof(AppPreferencesViewModel));
        var chat = (ChatViewModel)RuntimeHelpers.GetUninitializedObject(typeof(ChatViewModel));
        var capabilities = new Mock<IPlatformCapabilityService>();
        capabilities.SetupGet(service => service.SupportsExternalFileOpen).Returns(true);
        capabilities.SetupGet(service => service.SupportsLocalFileExport).Returns(supportsLocalFileExport);

        return new DataStorageSettingsViewModel(
            preferences,
            chat,
            Mock.Of<IAppDataService>(),
            Mock.Of<IAppMaintenanceService>(),
            diagnostics?.Object ?? Mock.Of<IDiagnosticsBundleService>(),
            Mock.Of<IPlatformShellService>(),
            capabilities.Object,
            Mock.Of<IStorageLocationService>(),
            sessionExport?.Object ?? Mock.Of<ISessionExportService>(),
            cloudSync?.Object ?? CreateDefaultCloudConfigSync().Object,
            ui?.Object ?? Mock.Of<IUiInteractionService>(),
            new TestCoreStringLocalizer(),
            Mock.Of<ILogger<DataStorageSettingsViewModel>>());
    }

    private static Mock<ICloudConfigSyncService> CreateDefaultCloudConfigSync()
    {
        var cloudSync = new Mock<ICloudConfigSyncService>();
        cloudSync.SetupGet(service => service.Providers).Returns(Array.Empty<CloudConfigProviderDescriptor>());
        return cloudSync;
    }
}
