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
        Assert.Equal("MoveDown, Activate", viewModel.ActiveInputsText);
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

        await viewModel.RefreshSnapshotCommand.ExecuteAsync(null);

        Assert.Equal(0, service.ReadCount);
        Assert.Equal("当前平台不支持手柄输入", viewModel.StatusText);
        Assert.False(viewModel.CanStartMonitoring);
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

    private static GamepadDiagnosticsViewModel CreateViewModel(
        IGamepadDiagnosticsService service,
        bool supportsGamepadInput)
    {
        var capabilities = new Mock<IPlatformCapabilityService>();
        capabilities.SetupGet(s => s.SupportsGamepadInput).Returns(supportsGamepadInput);

        return new GamepadDiagnosticsViewModel(
            service,
            capabilities.Object,
            new ImmediateUiDispatcher(),
            new TestCoreStringLocalizer(),
            Mock.Of<ILogger<GamepadDiagnosticsViewModel>>());
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
