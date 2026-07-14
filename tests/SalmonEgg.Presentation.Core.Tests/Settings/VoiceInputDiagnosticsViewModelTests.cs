using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Resources;
using SalmonEgg.Presentation.Core.Services;
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
            DefaultInputDeviceName: "USB Microphone",
            DefaultInputDeviceId: "device-1",
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
                LanguageTag: "zh-CN",
                PartialResultCount: 0,
                FinalResultCount: 0,
                EmptyPartialResultCount: 2,
                EmptyFinalResultCount: 1,
                CompletionStatus: "StoppedByApp")));
        var dispatcher = new TrackingUiDispatcher();

        var viewModel = new VoiceInputDiagnosticsViewModel(
            service.Object,
            CreateProbeViewModel(),
            dispatcher,
            new TestCoreStringLocalizer(),
            Mock.Of<ILogger<VoiceInputDiagnosticsViewModel>>());

        await viewModel.RefreshSnapshotCommand.ExecuteAsync(null);

        Assert.Equal("已支持", viewModel.SupportStatusText);
        Assert.Equal("已通过", viewModel.PermissionStatusText);
        Assert.Equal("USB Microphone", viewModel.InputDeviceText);
        Assert.Contains("识别器已 ready", viewModel.SessionStatusText);
        Assert.Contains("没有收到任何识别结果", viewModel.SessionStatusText);
        Assert.Contains("empty partial 2", viewModel.CallbackObservationText);
        Assert.Contains("empty final 1", viewModel.CallbackObservationText);
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
            DefaultInputDeviceName: null,
            DefaultInputDeviceId: null,
            LatestLogFilePath: null,
            LatestLogTimestamp: null,
            LatestSession: null));
        service.Setup(sut => sut.TryOpenAuthorizationSettingsAsync(default)).ReturnsAsync(true);
        var viewModel = new VoiceInputDiagnosticsViewModel(
            service.Object,
            CreateProbeViewModel(),
            new ImmediateUiDispatcher(),
            new TestCoreStringLocalizer(),
            Mock.Of<ILogger<VoiceInputDiagnosticsViewModel>>());

        await viewModel.RefreshSnapshotCommand.ExecuteAsync(null);
        await viewModel.OpenAuthorizationHelpCommand.ExecuteAsync(null);

        service.Verify(sut => sut.TryOpenAuthorizationSettingsAsync(default), Times.Once);
    }

    [Fact]
    public async Task RefreshSnapshotAsync_MarshalsSnapshotProjectionThroughUiDispatcher()
    {
        var snapshot = new VoiceInputDiagnosticsSnapshot(
            IsSupported: true,
            IsListening: false,
            CurrentLanguageTag: "zh-CN",
            Permission: VoiceInputPermissionResult.Granted(),
            DefaultInputDeviceName: "USB Microphone",
            DefaultInputDeviceId: "device-1",
            LatestLogFilePath: "C:/app/logs/app.log",
            LatestLogTimestamp: null,
            LatestSession: null);
        var service = new Mock<IVoiceInputDiagnosticsService>();
        service.Setup(sut => sut.GetSnapshotAsync(default)).Returns(async () =>
        {
            await Task.Yield();
            return snapshot;
        });
        var dispatcher = new TrackingUiDispatcher();
        var viewModel = new VoiceInputDiagnosticsViewModel(
            service.Object,
            CreateProbeViewModel(),
            dispatcher,
            new TestCoreStringLocalizer(),
            Mock.Of<ILogger<VoiceInputDiagnosticsViewModel>>());
        var supportStatusRaisedOnUi = false;

        viewModel.PropertyChanged += (_, args) =>
        {
            if (!string.Equals(args.PropertyName, nameof(VoiceInputDiagnosticsViewModel.SupportStatusText), StringComparison.Ordinal))
            {
                return;
            }

            if (string.Equals(viewModel.SupportStatusText, "已支持", StringComparison.Ordinal))
            {
                supportStatusRaisedOnUi = dispatcher.IsExecutingCallback;
            }
        };

        await viewModel.RefreshSnapshotCommand.ExecuteAsync(null);

        Assert.True(supportStatusRaisedOnUi);
    }

    [Fact]
    public async Task RefreshSnapshotAsync_WhenRefreshFails_MarshalsFailureProjectionThroughUiDispatcher()
    {
        var service = new Mock<IVoiceInputDiagnosticsService>();
        service.Setup(sut => sut.GetSnapshotAsync(default)).Returns(async () =>
        {
            await Task.Yield();
            throw new InvalidOperationException("refresh failed");
        });
        var dispatcher = new TrackingUiDispatcher();
        var viewModel = new VoiceInputDiagnosticsViewModel(
            service.Object,
            CreateProbeViewModel(),
            dispatcher,
            new TestCoreStringLocalizer(),
            Mock.Of<ILogger<VoiceInputDiagnosticsViewModel>>());
        var supportStatusRaisedOnUi = false;

        viewModel.PropertyChanged += (_, args) =>
        {
            if (!string.Equals(args.PropertyName, nameof(VoiceInputDiagnosticsViewModel.SupportStatusText), StringComparison.Ordinal))
            {
                return;
            }

            if (string.Equals(viewModel.SupportStatusText, "读取失败，请稍后重试", StringComparison.Ordinal))
            {
                supportStatusRaisedOnUi = dispatcher.IsExecutingCallback;
            }
        };

        await viewModel.RefreshSnapshotCommand.ExecuteAsync(null);

        Assert.True(supportStatusRaisedOnUi);
        Assert.Equal("读取失败，请稍后重试", viewModel.SupportStatusText);
        Assert.Equal("读取失败，请稍后重试", viewModel.InputDeviceText);
        Assert.Equal("读取失败，请稍后重试", viewModel.CallbackObservationText);
    }

    [Fact]
    public async Task LanguageChanged_ReprojectsCachedSnapshotProjection()
    {
        var snapshot = new VoiceInputDiagnosticsSnapshot(
            IsSupported: true,
            IsListening: false,
            CurrentLanguageTag: "zh-CN",
            Permission: VoiceInputPermissionResult.Granted(),
            DefaultInputDeviceName: "USB Microphone",
            DefaultInputDeviceId: "device-1",
            LatestLogFilePath: null,
            LatestLogTimestamp: null,
            LatestSession: null);
        var service = new Mock<IVoiceInputDiagnosticsService>();
        service.Setup(sut => sut.GetSnapshotAsync(default)).ReturnsAsync(snapshot);
        var languageService = new Mock<IAppLanguageService>();
        var currentLanguageTag = "zh-Hans";
        var localizer = CreateLocalizer();
        languageService.SetupGet(s => s.CurrentLanguageTag).Returns(() => currentLanguageTag);
        var viewModel = new VoiceInputDiagnosticsViewModel(
            service.Object,
            CreateProbeViewModel(),
            new ImmediateUiDispatcher(),
            localizer,
            Mock.Of<ILogger<VoiceInputDiagnosticsViewModel>>(),
            languageService.Object);

        await viewModel.RefreshSnapshotCommand.ExecuteAsync(null);
        Assert.Equal("已支持", viewModel.SupportStatusText);
        Assert.Equal("最近日志里还没有语音会话记录。", viewModel.SessionStatusText);

        currentLanguageTag = "en-US";
        localizer.SetLanguageTag("en-US");
        languageService.Raise(s => s.LanguageChanged += null, EventArgs.Empty);

        Assert.Equal("Supported", viewModel.SupportStatusText);
        Assert.Equal("No recent voice session in the latest logs.", viewModel.SessionStatusText);
    }

    private static VoiceInputDiagnosticsProbeViewModel CreateProbeViewModel()
        => new(
            NoOpVoiceInputService.Instance,
            new NoOpAudioInputSignalDiagnosticsService(),
            new ImmediateUiDispatcher(),
            new TestCoreStringLocalizer(),
            Mock.Of<ILogger<VoiceInputDiagnosticsProbeViewModel>>());

    private static MutableTestCoreStringLocalizer CreateLocalizer()
    {
        var localizer = new MutableTestCoreStringLocalizer();
        localizer.Set("zh-Hans", "VoiceDiagnostics_PendingRefresh", "等待刷新");
        localizer.Set("zh-Hans", "VoiceDiagnostics_RefreshFailed", "读取失败，请稍后重试");
        localizer.Set("zh-Hans", "VoiceDiagnostics_Supported", "已支持");
        localizer.Set("zh-Hans", "VoiceDiagnostics_Unsupported", "当前平台不支持语音输入");
        localizer.Set("zh-Hans", "VoiceDiagnostics_PermissionGranted", "已通过");
        localizer.Set("zh-Hans", "VoiceDiagnostics_PermissionDenied", "未通过");
        localizer.Set("zh-Hans", "VoiceDiagnostics_UnknownLanguage", "未知");
        localizer.Set("zh-Hans", "VoiceDiagnostics_InputDeviceUnavailable", "未能读取当前默认输入设备。");
        localizer.Set("zh-Hans", "VoiceDiagnostics_NoRecentSession", "最近日志里还没有语音会话记录。");
        localizer.Set("zh-Hans", "VoiceDiagnostics_CallbackObservationUnavailable", "最近会话还没有回调观测结果。");
        localizer.Set("zh-Hans", "VoiceDiagnostics_TimelineUnavailable", "暂无最近会话时间线。");
        localizer.Set("zh-Hans", "VoiceDiagnostics_RecommendationNoRecentSession", "重新触发一次语音输入后再刷新，这里会显示最近一次启动、ready 和识别结果链路。");
        localizer.Set("zh-Hans", "VoiceDiagnostics_RecommendationRefreshFailed", "语音诊断刷新失败，请稍后重试。");
        localizer.Set("en-US", "VoiceDiagnostics_PendingRefresh", "Waiting for refresh");
        localizer.Set("en-US", "VoiceDiagnostics_RefreshFailed", "Failed to read, try again later");
        localizer.Set("en-US", "VoiceDiagnostics_Supported", "Supported");
        localizer.Set("en-US", "VoiceDiagnostics_Unsupported", "Voice input is not supported on this platform");
        localizer.Set("en-US", "VoiceDiagnostics_PermissionGranted", "Granted");
        localizer.Set("en-US", "VoiceDiagnostics_PermissionDenied", "Denied");
        localizer.Set("en-US", "VoiceDiagnostics_UnknownLanguage", "Unknown");
        localizer.Set("en-US", "VoiceDiagnostics_InputDeviceUnavailable", "Unable to read the current default input device.");
        localizer.Set("en-US", "VoiceDiagnostics_NoRecentSession", "No recent voice session in the latest logs.");
        localizer.Set("en-US", "VoiceDiagnostics_CallbackObservationUnavailable", "No callback observations are available for the latest session.");
        localizer.Set("en-US", "VoiceDiagnostics_TimelineUnavailable", "No recent session timeline is available.");
        localizer.Set("en-US", "VoiceDiagnostics_RecommendationNoRecentSession", "Trigger voice input again and refresh to inspect the latest start, ready, and recognition path.");
        localizer.Set("en-US", "VoiceDiagnostics_RecommendationRefreshFailed", "Voice diagnostics refresh failed, try again later.");
        return localizer;
    }

    private sealed class TrackingUiDispatcher : IUiDispatcher
    {
        public bool IsExecutingCallback { get; private set; }

        public bool HasThreadAccess => false;

        public void Enqueue(Action action)
        {
            IsExecutingCallback = true;
            try
            {
                action();
            }
            finally
            {
                IsExecutingCallback = false;
            }
        }

        public Task EnqueueAsync(Action action)
        {
            IsExecutingCallback = true;
            try
            {
                action();
                return Task.CompletedTask;
            }
            finally
            {
                IsExecutingCallback = false;
            }
        }

        public Task EnqueueAsync(Func<Task> function)
        {
            IsExecutingCallback = true;
            try
            {
                return AwaitAndResetAsync(function);
            }
            catch
            {
                IsExecutingCallback = false;
                throw;
            }
        }

        private async Task AwaitAndResetAsync(Func<Task> function)
        {
            try
            {
                await function();
            }
            finally
            {
                IsExecutingCallback = false;
            }
        }
    }
}
