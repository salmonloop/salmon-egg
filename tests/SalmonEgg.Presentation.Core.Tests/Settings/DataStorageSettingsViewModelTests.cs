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
    public async Task ConnectSelectedCloudConfigProviderCommand_WhenOneDriveUploads_UpdatesStatus()
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

        await viewModel.ConnectSelectedCloudConfigProviderCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsCloudConfigSyncEnabled);
        Assert.Equal("本地配置已上传到云端。", viewModel.CloudConfigSyncStatusText);
        Assert.Empty(viewModel.CloudConfigSyncErrorText);
    }

    [Fact]
    public async Task ConnectSelectedCloudConfigProviderCommand_WhenProviderMissingConfig_ShowsErrorStatus()
    {
        var cloudSync = new Mock<ICloudConfigSyncService>();
        cloudSync.SetupGet(service => service.Providers).Returns(Array.Empty<CloudConfigProviderDescriptor>());
        cloudSync
            .Setup(service => service.AuthorizeAndSyncAsync("onedrive", default))
            .ReturnsAsync(CloudConfigSyncResult.NotConfigured("onedrive"));
        var viewModel = CreateViewModel(cloudSync: cloudSync);

        await viewModel.ConnectSelectedCloudConfigProviderCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsCloudConfigSyncEnabled);
        Assert.Equal("所选云同步 provider 尚未配置。", viewModel.CloudConfigSyncErrorText);
    }

    [Fact]
    public async Task ConnectSelectedCloudConfigProviderCommand_WhenWebDavSelected_ConfiguresThenSyncsOnlyWebDav()
    {
        var cloudSync = new Mock<ICloudConfigSyncService>();
        cloudSync.SetupGet(service => service.Providers).Returns(new[]
        {
            new CloudConfigProviderDescriptor("onedrive", "OneDrive", true),
            new CloudConfigProviderDescriptor("webdav", "WebDAV", true)
        });
        cloudSync
            .Setup(service => service.ConfigureProviderAsync(
                "webdav",
                It.Is<IReadOnlyDictionary<string, string>>(options =>
                    options["file_url"] == "https://dav.example.test/salmonegg-config.zip" &&
                    options["username"] == "alice"),
                It.Is<IReadOnlyDictionary<string, string>>(secrets => secrets["password"] == "app-password"),
                default))
            .ReturnsAsync(new CloudConfigSyncResult(CloudConfigSyncStatus.Disabled, "webdav"));
        cloudSync
            .Setup(service => service.AuthorizeAndSyncAsync("webdav", default))
            .ReturnsAsync(new CloudConfigSyncResult(CloudConfigSyncStatus.Uploaded, "webdav"));
        var viewModel = CreateViewModel(cloudSync: cloudSync);

        viewModel.SelectedCloudConfigProviderId = "webdav";
        viewModel.WebDavFileUrl = "https://dav.example.test/salmonegg-config.zip";
        viewModel.WebDavUsername = "alice";
        viewModel.WebDavPassword = "app-password";
        await viewModel.ConnectSelectedCloudConfigProviderCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsCloudConfigSyncEnabled);
        Assert.Equal("webdav", viewModel.Preferences.CloudConfigSync.ProviderId);
        Assert.Equal("https://dav.example.test/salmonegg-config.zip", viewModel.Preferences.CloudConfigSync.ProviderOptions["webdav"]["file_url"]);
        cloudSync.Verify(service => service.AuthorizeAndSyncAsync("onedrive", default), Times.Never);
        cloudSync.VerifyAll();
    }

    [Fact]
    public void SelectedCloudConfigProviderId_AllowsOnlyOneVisibleProviderConfiguration()
    {
        var cloudSync = new Mock<ICloudConfigSyncService>();
        cloudSync.SetupGet(service => service.Providers).Returns(new[]
        {
            new CloudConfigProviderDescriptor("onedrive", "OneDrive", true),
            new CloudConfigProviderDescriptor("webdav", "WebDAV", true),
            new CloudConfigProviderDescriptor("s3", "S3 compatible", true)
        });
        var viewModel = CreateViewModel(cloudSync: cloudSync);

        viewModel.SelectedCloudConfigProviderId = "onedrive";
        Assert.True(viewModel.IsOneDriveCloudConfigProviderSelected);
        Assert.False(viewModel.IsWebDavCloudConfigProviderSelected);
        Assert.False(viewModel.IsS3CloudConfigProviderSelected);

        viewModel.SelectedCloudConfigProviderId = "webdav";
        Assert.False(viewModel.IsOneDriveCloudConfigProviderSelected);
        Assert.True(viewModel.IsWebDavCloudConfigProviderSelected);
        Assert.False(viewModel.IsS3CloudConfigProviderSelected);

        viewModel.SelectedCloudConfigProviderId = "s3";
        Assert.False(viewModel.IsOneDriveCloudConfigProviderSelected);
        Assert.False(viewModel.IsWebDavCloudConfigProviderSelected);
        Assert.True(viewModel.IsS3CloudConfigProviderSelected);
    }

    [Fact]
    public async Task ConnectSelectedCloudConfigProviderCommand_WhenS3Selected_ConfiguresThenSyncsOnlyS3()
    {
        var cloudSync = new Mock<ICloudConfigSyncService>();
        cloudSync.SetupGet(service => service.Providers).Returns(new[]
        {
            new CloudConfigProviderDescriptor("onedrive", "OneDrive", true),
            new CloudConfigProviderDescriptor("webdav", "WebDAV", true),
            new CloudConfigProviderDescriptor("s3", "S3 compatible", true)
        });
        cloudSync
            .Setup(service => service.ConfigureProviderAsync(
                "s3",
                It.Is<IReadOnlyDictionary<string, string>>(options =>
                    options["endpoint"] == "https://s3.example.test" &&
                    options["bucket"] == "salmonegg" &&
                    options["region"] == "auto" &&
                    options["object_key"] == "config-sync/salmonegg-config.zip" &&
                    options["force_path_style"] == "True"),
                It.Is<IReadOnlyDictionary<string, string>>(secrets =>
                    secrets["access_key_id"] == "access-key" &&
                    secrets["secret_access_key"] == "secret-key"),
                default))
            .ReturnsAsync(new CloudConfigSyncResult(CloudConfigSyncStatus.Disabled, "s3"));
        cloudSync
            .Setup(service => service.AuthorizeAndSyncAsync("s3", default))
            .ReturnsAsync(new CloudConfigSyncResult(CloudConfigSyncStatus.Uploaded, "s3"));
        var viewModel = CreateViewModel(cloudSync: cloudSync);

        viewModel.SelectedCloudConfigProviderId = "s3";
        viewModel.S3Endpoint = "https://s3.example.test";
        viewModel.S3Bucket = "salmonegg";
        viewModel.S3Region = "auto";
        viewModel.S3ObjectKey = "config-sync/salmonegg-config.zip";
        viewModel.S3ForcePathStyle = true;
        viewModel.S3AccessKeyId = "access-key";
        viewModel.S3SecretAccessKey = "secret-key";
        await viewModel.ConnectSelectedCloudConfigProviderCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsCloudConfigSyncEnabled);
        Assert.Equal("s3", viewModel.Preferences.CloudConfigSync.ProviderId);
        Assert.Equal("https://s3.example.test", viewModel.Preferences.CloudConfigSync.ProviderOptions["s3"]["endpoint"]);
        Assert.Equal("salmonegg", viewModel.Preferences.CloudConfigSync.ProviderOptions["s3"]["bucket"]);
        Assert.DoesNotContain("secret_access_key", viewModel.Preferences.CloudConfigSync.ProviderOptions["s3"].Keys);
        cloudSync.Verify(service => service.AuthorizeAndSyncAsync("onedrive", default), Times.Never);
        cloudSync.Verify(service => service.AuthorizeAndSyncAsync("webdav", default), Times.Never);
        cloudSync.VerifyAll();
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
