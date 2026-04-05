using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Moq;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.ViewModels.Chat;
using SalmonEgg.Presentation.ViewModels.Settings;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Settings;

public sealed class DiagnosticsSettingsViewModelTests
{
    [Fact]
    public void Constructor_ComposesLiveLogViewer()
    {
        var chat = (ChatViewModel)RuntimeHelpers.GetUninitializedObject(typeof(ChatViewModel));
        var paths = new Mock<IAppDataService>();
        paths.SetupGet(p => p.AppDataRootPath).Returns("C:/app");
        paths.SetupGet(p => p.LogsDirectoryPath).Returns("C:/app/logs");
        var bundle = new Mock<IDiagnosticsBundleService>();
        var shell = new Mock<IPlatformShellService>();
        var service = new Mock<ILiveLogStreamService>();
        var liveLogger = new Mock<ILogger<LiveLogViewerViewModel>>();
        var diagnosticsLogger = new Mock<ILogger<DiagnosticsSettingsViewModel>>();
        var liveLogViewer = new LiveLogViewerViewModel(service.Object, paths.Object.LogsDirectoryPath, liveLogger.Object);

        var viewModel = new DiagnosticsSettingsViewModel(
            chat,
            paths.Object,
            bundle.Object,
            shell.Object,
            liveLogViewer,
            diagnosticsLogger.Object);

        Assert.Same(liveLogViewer, viewModel.LiveLogViewer);
    }
}
