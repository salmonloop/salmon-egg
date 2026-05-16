using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
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

    [Theory]
    [InlineData(AppStorageLocation.AppData)]
    [InlineData(AppStorageLocation.Cache)]
    [InlineData(AppStorageLocation.Logs)]
    [InlineData(AppStorageLocation.Exports)]
    public async Task OpenFolderCommands_WhenOpenFails_NotifiesUnsupported(AppStorageLocation location)
    {
        var storageLocations = new Mock<IStorageLocationService>();
        storageLocations.Setup(s => s.OpenAsync(location)).ReturnsAsync(false);
        var ui = new Mock<IUiInteractionService>();
        var viewModel = CreateViewModel(storageLocations: storageLocations, ui: ui);

        switch (location)
        {
            case AppStorageLocation.AppData:
                await viewModel.OpenAppDataFolderCommand.ExecuteAsync(null);
                break;
            case AppStorageLocation.Cache:
                await viewModel.OpenCacheFolderCommand.ExecuteAsync(null);
                break;
            case AppStorageLocation.Logs:
                await viewModel.OpenLogsFolderCommand.ExecuteAsync(null);
                break;
            case AppStorageLocation.Exports:
                await viewModel.OpenExportsFolderCommand.ExecuteAsync(null);
                break;
        }

        ui.Verify(service => service.ShowInfoAsync("当前平台暂不支持打开本地文件或目录。"), Times.Once);
    }

    [Theory]
    [InlineData(AppStorageLocation.AppData)]
    [InlineData(AppStorageLocation.Cache)]
    [InlineData(AppStorageLocation.Logs)]
    [InlineData(AppStorageLocation.Exports)]
    public async Task OpenFolderCommands_WhenOpenSucceeds_DoesNotNotify(AppStorageLocation location)
    {
        var storageLocations = new Mock<IStorageLocationService>();
        storageLocations.Setup(s => s.OpenAsync(location)).ReturnsAsync(true);
        var ui = new Mock<IUiInteractionService>();
        var viewModel = CreateViewModel(storageLocations: storageLocations, ui: ui);

        switch (location)
        {
            case AppStorageLocation.AppData:
                await viewModel.OpenAppDataFolderCommand.ExecuteAsync(null);
                break;
            case AppStorageLocation.Cache:
                await viewModel.OpenCacheFolderCommand.ExecuteAsync(null);
                break;
            case AppStorageLocation.Logs:
                await viewModel.OpenLogsFolderCommand.ExecuteAsync(null);
                break;
            case AppStorageLocation.Exports:
                await viewModel.OpenExportsFolderCommand.ExecuteAsync(null);
                break;
        }

        ui.Verify(service => service.ShowInfoAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ClearCacheCommand_CallsMaintenanceService()
    {
        var maintenance = new Mock<IAppMaintenanceService>();
        var viewModel = CreateViewModel(maintenance: maintenance);

        await viewModel.ClearCacheCommand.ExecuteAsync(null);

        maintenance.Verify(service => service.ClearCacheAsync(), Times.Once);
    }

    [Fact]
    public async Task ClearAllLocalDataCommand_CallsMaintenanceService()
    {
        var maintenance = new Mock<IAppMaintenanceService>();
        var viewModel = CreateViewModel(maintenance: maintenance);

        await viewModel.ClearAllLocalDataCommand.ExecuteAsync(null);

        maintenance.Verify(service => service.ClearAllLocalDataAsync(), Times.Once);
    }

    [Fact]
    public void PropertyGetters_ReturnValuesFromAppDataService()
    {
        var paths = new Mock<IAppDataService>();
        paths.SetupGet(p => p.AppDataRootPath).Returns("/appdata");
        paths.SetupGet(p => p.LogsDirectoryPath).Returns("/logs");
        paths.SetupGet(p => p.CacheRootPath).Returns("/cache");
        paths.SetupGet(p => p.ExportsDirectoryPath).Returns("/exports");

        var viewModel = CreateViewModel(paths: paths);

        Assert.Equal("/appdata", viewModel.AppDataRootPath);
        Assert.Equal("/logs", viewModel.LogsDirectoryPath);
        Assert.Equal("/cache", viewModel.CacheRootPath);
        Assert.Equal("/exports", viewModel.ExportsDirectoryPath);
    }

    private static DataStorageSettingsViewModel CreateViewModel(
        bool supportsLocalFileExport = true,
        Mock<IDiagnosticsBundleService>? diagnostics = null,
        Mock<ISessionExportService>? sessionExport = null,
        Mock<IUiInteractionService>? ui = null,
        Mock<IStorageLocationService>? storageLocations = null,
        Mock<IAppMaintenanceService>? maintenance = null,
        Mock<IAppDataService>? paths = null,
        Mock<IPlatformShellService>? shell = null)
    {
        var preferences = (AppPreferencesViewModel)RuntimeHelpers.GetUninitializedObject(typeof(AppPreferencesViewModel));
        var chat = (ChatViewModel)RuntimeHelpers.GetUninitializedObject(typeof(ChatViewModel));
        var capabilities = new Mock<IPlatformCapabilityService>();
        capabilities.SetupGet(service => service.SupportsExternalFileOpen).Returns(true);
        capabilities.SetupGet(service => service.SupportsLocalFileExport).Returns(supportsLocalFileExport);

        return new DataStorageSettingsViewModel(
            preferences,
            chat,
            paths?.Object ?? Mock.Of<IAppDataService>(),
            maintenance?.Object ?? Mock.Of<IAppMaintenanceService>(),
            diagnostics?.Object ?? Mock.Of<IDiagnosticsBundleService>(),
            shell?.Object ?? Mock.Of<IPlatformShellService>(),
            capabilities.Object,
            storageLocations?.Object ?? Mock.Of<IStorageLocationService>(),
            sessionExport?.Object ?? Mock.Of<ISessionExportService>(),
            ui?.Object ?? Mock.Of<IUiInteractionService>(),
            new TestCoreStringLocalizer(),
            Mock.Of<ILogger<DataStorageSettingsViewModel>>());
    }
}
