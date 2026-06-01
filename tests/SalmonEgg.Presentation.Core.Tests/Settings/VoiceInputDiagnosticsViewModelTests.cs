using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using SalmonEgg.Presentation.Core.Resources;
using SalmonEgg.Presentation.Core.Services.Input;
using SalmonEgg.Presentation.Core.Tests.Localization;
using SalmonEgg.Presentation.Core.Tests.Threading;
using SalmonEgg.Presentation.ViewModels.Settings;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Settings;

public sealed class VoiceInputDiagnosticsViewModelTests
{
    [Fact]
    public async Task RefreshSnapshotAsync_WhenLatestSessionReadyButSilent_ShowsTargetedRecommendation()
    {
        var service = new Mock<IVoiceInputDiagnosticsService>();
        service.Setup(sut => sut.GetSnapshotAsync(default)).ReturnsAsync(new VoiceInputDiagnosticsSnapshot(
            IsSupported: true,
            IsListening: false,
            CurrentLanguageTag: "zh-CN",
            Permission: VoiceInputPermissionResult.Granted(),
            LatestLogFilePath: "C:/app/logs/app.log",
            LatestLogTimestamp: null,
            LatestSession: new VoiceInputDiagnosticSession(
                "req-1",
                VoiceInputDiagnosticSessionOutcome.ReadyWithoutRecognition,
                StartRequestedAt: new DateTimeOffset(2026, 6, 2, 0, 40, 9, 136, TimeSpan.FromHours(8)),
                RecognizerReadyAt: new DateTimeOffset(2026, 6, 2, 0, 40, 12, 28, TimeSpan.FromHours(8)),
                FirstPartialAt: null,
                FinalResultAt: null,
                StopRequestedAt: new DateTimeOffset(2026, 6, 2, 0, 40, 19, 645, TimeSpan.FromHours(8)),
                EndedAt: new DateTimeOffset(2026, 6, 2, 0, 40, 19, 652, TimeSpan.FromHours(8)),
                ErrorAt: null,
                ErrorCode: null,
                ErrorMessage: null,
                LanguageTag: "zh-CN")));

        var viewModel = new VoiceInputDiagnosticsViewModel(
            service.Object,
            CreateProbeViewModel(),
            new TestCoreStringLocalizer(),
            Mock.Of<ILogger<VoiceInputDiagnosticsViewModel>>());

        await viewModel.RefreshSnapshotCommand.ExecuteAsync(null);

        Assert.Equal("已支持", viewModel.SupportStatusText);
        Assert.Equal("已通过", viewModel.PermissionStatusText);
        Assert.Contains("识别器已 ready", viewModel.SessionStatusText);
        Assert.Contains("没有收到任何识别结果", viewModel.SessionStatusText);
        Assert.Contains("zh-CN", viewModel.RecommendationText);
        Assert.Contains("2.89s", viewModel.TimelineText);
    }

    [Fact]
    public async Task OpenAuthorizationHelpAsync_WhenPermissionRequiresAuthorization_UsesDiagnosticsService()
    {
        var service = new Mock<IVoiceInputDiagnosticsService>();
        service.Setup(sut => sut.GetSnapshotAsync(default)).ReturnsAsync(new VoiceInputDiagnosticsSnapshot(
            IsSupported: true,
            IsListening: false,
            CurrentLanguageTag: "en-US",
            Permission: new VoiceInputPermissionResult(VoiceInputPermissionStatus.Denied, "blocked", RequiresAuthorization: true),
            LatestLogFilePath: null,
            LatestLogTimestamp: null,
            LatestSession: null));
        service.Setup(sut => sut.TryOpenAuthorizationSettingsAsync(default)).ReturnsAsync(true);
        var viewModel = new VoiceInputDiagnosticsViewModel(
            service.Object,
            CreateProbeViewModel(),
            new TestCoreStringLocalizer(),
            Mock.Of<ILogger<VoiceInputDiagnosticsViewModel>>());

        await viewModel.RefreshSnapshotCommand.ExecuteAsync(null);
        await viewModel.OpenAuthorizationHelpCommand.ExecuteAsync(null);

        service.Verify(sut => sut.TryOpenAuthorizationSettingsAsync(default), Times.Once);
    }

    private static VoiceInputDiagnosticsProbeViewModel CreateProbeViewModel()
        => new(
            NoOpVoiceInputService.Instance,
            new ImmediateUiDispatcher(),
            new TestCoreStringLocalizer(),
            Mock.Of<ILogger<VoiceInputDiagnosticsProbeViewModel>>());
}
