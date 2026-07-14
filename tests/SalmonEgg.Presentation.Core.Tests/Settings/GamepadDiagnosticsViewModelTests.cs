using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Services.Input;
using SalmonEgg.Presentation.Core.Tests.Localization;
using SalmonEgg.Presentation.Core.Tests.Threading;
using SalmonEgg.Presentation.ViewModels.Settings;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Settings;

public sealed class GamepadDiagnosticsViewModelTests
{
    [Fact]
    public async Task RefreshSnapshotCommand_WhenSupported_ProjectsPortableSnapshot()
    {
        var service = new FakeGamepadDiagnosticsService(new GamepadDiagnosticsSnapshot(
            IsSupported: true,
            ConnectedGamepadCount: 1,
            ConnectedRawControllerCount: 2,
            InputSource: GamepadDiagnosticsInputSource.RawGameController,
            Reading: new GamepadInputReading(
                MoveUp: false,
                MoveDown: true,
                MoveLeft: false,
                MoveRight: false,
                Activate: true,
                Back: false,
                ThumbstickX: 0.25,
                ThumbstickY: -0.5),
            ActiveIntents: new[]
            {
                GamepadNavigationIntent.MoveDown,
                GamepadNavigationIntent.Activate
            },
            ActiveContextIntents: new[]
            {
                GamepadContextIntent.PageDown
            },
            RawControllers:
            [
                new RawGameControllerDiagnostics(
                    DisplayName: "Wireless Controller",
                    HardwareVendorId: 0x054C,
                    HardwareProductId: 0x0CE6,
                    IsWireless: true,
                    ButtonCount: 16,
                    SwitchCount: 1,
                    AxisCount: 6,
                    PressedButtons: ["B0:Cross"],
                    ActiveSwitches: ["S0:Down"],
                    Axes: [0.5, 1.0])
            ]));
        var viewModel = CreateViewModel(service, supportsGamepadInput: true);

        await viewModel.RefreshSnapshotCommand.ExecuteAsync(null);

        Assert.Equal("1", viewModel.ConnectedGamepadsText);
        Assert.Equal("2", viewModel.ConnectedRawControllersText);
        Assert.Equal("RawGameController", viewModel.InputSourceText);
        Assert.Equal("MoveDown, Activate, PageDown", viewModel.ActiveInputsText);
        Assert.Equal("X 0.25, Y -0.50", viewModel.ThumbstickText);
        Assert.Contains("Wireless Controller", viewModel.RawControllersText);
        Assert.Contains("VID 054C PID 0CE6", viewModel.RawControllersText);
        Assert.Contains("B0:Cross", viewModel.RawControllersText);
        Assert.Contains("S0:Down", viewModel.RawControllersText);
        Assert.Contains("A1:1.00", viewModel.RawControllersText);
    }

    [Fact]
    public async Task RefreshSnapshotCommand_WhenUnsupported_DoesNotPollPlatformService()
    {
        var service = new FakeGamepadDiagnosticsService(GamepadDiagnosticsSnapshot.Unsupported);
        var viewModel = CreateViewModel(service, supportsGamepadInput: false);
        viewModel.ConnectedGamepadsText = "9";
        viewModel.ConnectedRawControllersText = "8";
        viewModel.InputSourceText = "RawGameController";
        viewModel.ActiveInputsText = "MoveDown";
        viewModel.RawControllersText = "Wireless Controller";

        await viewModel.RefreshSnapshotCommand.ExecuteAsync(null);

        Assert.Equal(0, service.ReadCount);
        Assert.Equal("当前平台不支持手柄输入", viewModel.StatusText);
        Assert.Equal("0", viewModel.ConnectedGamepadsText);
        Assert.Equal("0", viewModel.ConnectedRawControllersText);
        Assert.Equal("无", viewModel.InputSourceText);
        Assert.Equal("无", viewModel.ActiveInputsText);
        Assert.Equal("未检测到 Raw 控制器", viewModel.RawControllersText);
        Assert.False(viewModel.CanStartMonitoring);
    }

    [Fact]
    public async Task StartMonitoringCommand_WhenUnsupported_DoesNotPollPlatformService()
    {
        var service = new FakeGamepadDiagnosticsService(GamepadDiagnosticsSnapshot.Unsupported);
        var viewModel = CreateViewModel(service, supportsGamepadInput: false);

        await viewModel.StartMonitoringCommand.ExecuteAsync(null);

        Assert.Equal(0, service.ReadCount);
        Assert.False(viewModel.IsMonitoring);
        Assert.False(viewModel.CanStartMonitoring);
        Assert.False(viewModel.CanStopMonitoring);
        Assert.Equal("当前平台不支持手柄输入", viewModel.StatusText);
    }

    [Fact]
    public async Task StartAndStopMonitoring_ReflectsBindableState()
    {
        var service = new FakeGamepadDiagnosticsService(new GamepadDiagnosticsSnapshot(
            IsSupported: true,
            ConnectedGamepadCount: 1,
            ConnectedRawControllerCount: 0,
            InputSource: GamepadDiagnosticsInputSource.Gamepad,
            Reading: default,
            ActiveIntents: [],
            ActiveContextIntents: [],
            RawControllers: []));
        var viewModel = CreateViewModel(service, supportsGamepadInput: true);

        await viewModel.StartMonitoringCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsMonitoring);
        Assert.False(viewModel.CanStartMonitoring);
        Assert.True(viewModel.CanStopMonitoring);

        await viewModel.StopMonitoringCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsMonitoring);
        Assert.True(viewModel.CanStartMonitoring);
        Assert.False(viewModel.CanStopMonitoring);
    }

    [Fact]
    public void LanguageChanged_ReprojectsCachedUnsupportedStatus()
    {
        var service = new FakeGamepadDiagnosticsService(GamepadDiagnosticsSnapshot.Unsupported);
        var languageService = new Mock<IAppLanguageService>();
        var currentLanguageTag = "zh-Hans";
        var localizer = CreateLocalizer();
        languageService.SetupGet(s => s.CurrentLanguageTag).Returns(() => currentLanguageTag);

        var viewModel = new GamepadDiagnosticsViewModel(
            service,
            CreateCapabilities(false),
            new ImmediateUiDispatcher(),
            localizer,
            Mock.Of<ILogger<GamepadDiagnosticsViewModel>>(),
            languageService.Object);

        Assert.Equal("当前平台不支持手柄输入", viewModel.StatusText);

        currentLanguageTag = "en-US";
        localizer.SetLanguageTag("en-US");
        languageService.Raise(s => s.LanguageChanged += null, EventArgs.Empty);

        Assert.Equal("Gamepad input is not supported on this platform", viewModel.StatusText);
    }

    private static GamepadDiagnosticsViewModel CreateViewModel(
        IGamepadDiagnosticsService service,
        bool supportsGamepadInput)
    {
        return new GamepadDiagnosticsViewModel(
            service,
            CreateCapabilities(supportsGamepadInput),
            new ImmediateUiDispatcher(),
            new TestCoreStringLocalizer(),
            Mock.Of<ILogger<GamepadDiagnosticsViewModel>>());
    }

    private static IPlatformCapabilityService CreateCapabilities(bool supportsGamepadInput)
    {
        var capabilities = new Mock<IPlatformCapabilityService>();
        capabilities.SetupGet(s => s.SupportsGamepadInput).Returns(supportsGamepadInput);
        return capabilities.Object;
    }

    private static MutableTestCoreStringLocalizer CreateLocalizer()
    {
        var localizer = new MutableTestCoreStringLocalizer();
        localizer.Set("zh-Hans", "GamepadDiagnostics_StatusNotStarted", "未启动");
        localizer.Set("zh-Hans", "GamepadDiagnostics_StatusMonitoring", "正在监测");
        localizer.Set("zh-Hans", "GamepadDiagnostics_StatusStopped", "已停止");
        localizer.Set("zh-Hans", "GamepadDiagnostics_StatusUnsupported", "当前平台不支持手柄输入");
        localizer.Set("zh-Hans", "GamepadDiagnostics_StatusFailed", "读取失败，请稍后重试");
        localizer.Set("zh-Hans", "GamepadDiagnostics_InputSourceNone", "无");
        localizer.Set("zh-Hans", "GamepadDiagnostics_ActiveInputsNone", "无");
        localizer.Set("zh-Hans", "GamepadDiagnostics_RawControllersNone", "未检测到 Raw 控制器");
        localizer.Set("zh-Hans", "GamepadDiagnostics_ConnectionWireless", "无线");
        localizer.Set("zh-Hans", "GamepadDiagnostics_ConnectionWired", "有线");
        localizer.Set("en-US", "GamepadDiagnostics_StatusNotStarted", "Not started");
        localizer.Set("en-US", "GamepadDiagnostics_StatusMonitoring", "Monitoring");
        localizer.Set("en-US", "GamepadDiagnostics_StatusStopped", "Stopped");
        localizer.Set("en-US", "GamepadDiagnostics_StatusUnsupported", "Gamepad input is not supported on this platform");
        localizer.Set("en-US", "GamepadDiagnostics_StatusFailed", "Failed to read, try again later");
        localizer.Set("en-US", "GamepadDiagnostics_InputSourceNone", "None");
        localizer.Set("en-US", "GamepadDiagnostics_ActiveInputsNone", "None");
        localizer.Set("en-US", "GamepadDiagnostics_RawControllersNone", "No Raw controllers detected");
        localizer.Set("en-US", "GamepadDiagnostics_ConnectionWireless", "Wireless");
        localizer.Set("en-US", "GamepadDiagnostics_ConnectionWired", "Wired");
        return localizer;
    }

    private sealed class FakeGamepadDiagnosticsService : IGamepadDiagnosticsService
    {
        private readonly GamepadDiagnosticsSnapshot _snapshot;

        public FakeGamepadDiagnosticsService(GamepadDiagnosticsSnapshot snapshot)
        {
            _snapshot = snapshot;
        }

        public int ReadCount { get; private set; }

        public GamepadDiagnosticsSnapshot GetCurrentSnapshot()
        {
            ReadCount++;
            return _snapshot;
        }
    }
}
