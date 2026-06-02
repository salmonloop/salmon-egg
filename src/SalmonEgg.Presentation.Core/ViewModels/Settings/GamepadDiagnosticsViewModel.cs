using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Resources;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.Input;

namespace SalmonEgg.Presentation.ViewModels.Settings;

public sealed partial class GamepadDiagnosticsViewModel : ObservableObject
{
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(100);

    private readonly IGamepadDiagnosticsService _service;
    private readonly IPlatformCapabilityService _capabilities;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly IStringLocalizer<CoreStrings> _localizer;
    private readonly ILogger<GamepadDiagnosticsViewModel> _logger;
    private readonly TimeSpan _pollInterval;
    private CancellationTokenSource? _monitoringCancellationTokenSource;
    private Task? _monitoringTask;

    public GamepadDiagnosticsViewModel(
        IGamepadDiagnosticsService service,
        IPlatformCapabilityService capabilities,
        IUiDispatcher uiDispatcher,
        IStringLocalizer<CoreStrings> localizer,
        ILogger<GamepadDiagnosticsViewModel> logger)
        : this(service, capabilities, uiDispatcher, localizer, logger, DefaultPollInterval)
    {
    }

    internal GamepadDiagnosticsViewModel(
        IGamepadDiagnosticsService service,
        IPlatformCapabilityService capabilities,
        IUiDispatcher uiDispatcher,
        IStringLocalizer<CoreStrings> localizer,
        ILogger<GamepadDiagnosticsViewModel> logger,
        TimeSpan pollInterval)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _pollInterval = pollInterval > TimeSpan.Zero
            ? pollInterval
            : throw new ArgumentOutOfRangeException(nameof(pollInterval));

        _statusText = _capabilities.SupportsGamepadInput
            ? _localizer["GamepadDiagnostics_StatusNotStarted"]
            : _localizer["GamepadDiagnostics_StatusUnsupported"];
        _inputSourceText = _localizer["GamepadDiagnostics_InputSourceNone"];
        _connectedGamepadsText = FormatCount(0);
        _connectedRawControllersText = FormatCount(0);
        _activeInputsText = _localizer["GamepadDiagnostics_ActiveInputsNone"];
        _thumbstickText = FormatThumbstick(default);
        _rawControllersText = _localizer["GamepadDiagnostics_RawControllersNone"];
    }

    [ObservableProperty]
    private bool _isMonitoring;

    [ObservableProperty]
    private string _statusText;

    [ObservableProperty]
    private string _inputSourceText;

    [ObservableProperty]
    private string _connectedGamepadsText;

    [ObservableProperty]
    private string _connectedRawControllersText;

    [ObservableProperty]
    private string _activeInputsText;

    [ObservableProperty]
    private string _thumbstickText;

    [ObservableProperty]
    private string _rawControllersText;

    public bool CanStartMonitoring => _capabilities.SupportsGamepadInput && !IsMonitoring;

    public bool CanStopMonitoring => IsMonitoring;

    [RelayCommand]
    private async Task StartMonitoringAsync()
    {
        if (!_capabilities.SupportsGamepadInput || IsMonitoring)
        {
            return;
        }

        IsMonitoring = true;
        StatusText = _localizer["GamepadDiagnostics_StatusMonitoring"];
        NotifyMonitoringStateChanged();

        var cancellationTokenSource = new CancellationTokenSource();
        _monitoringCancellationTokenSource = cancellationTokenSource;
        _monitoringTask = Task.Run(() => ObserveMonitoringAsync(cancellationTokenSource));
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task StopMonitoringAsync()
    {
        await StopMonitoringCoreAsync(_localizer["GamepadDiagnostics_StatusStopped"]).ConfigureAwait(false);
    }

    [RelayCommand]
    private async Task RefreshSnapshotAsync()
    {
        if (!_capabilities.SupportsGamepadInput)
        {
            ApplyUnsupported();
            return;
        }

        try
        {
            var snapshot = await Task.Run(_service.GetCurrentSnapshot).ConfigureAwait(false);
            await _uiDispatcher.EnqueueAsync(() => ApplySnapshot(snapshot)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gamepad diagnostics snapshot refresh failed.");
            await _uiDispatcher.EnqueueAsync(() => StatusText = _localizer["GamepadDiagnostics_StatusFailed"])
                .ConfigureAwait(false);
        }
    }

    public Task HandlePageUnloadedAsync()
        => StopMonitoringCoreAsync(_localizer["GamepadDiagnostics_StatusStopped"]);

    private async Task StopMonitoringCoreAsync(string stoppedStatusText)
    {
        var cancellationTokenSource = _monitoringCancellationTokenSource;
        var monitoringTask = _monitoringTask;

        if (cancellationTokenSource is null)
        {
            IsMonitoring = false;
            StatusText = _capabilities.SupportsGamepadInput
                ? stoppedStatusText
                : _localizer["GamepadDiagnostics_StatusUnsupported"];
            NotifyMonitoringStateChanged();
            return;
        }

        _monitoringCancellationTokenSource = null;
        _monitoringTask = null;
        cancellationTokenSource.Cancel();

        try
        {
            if (monitoringTask is not null)
            {
                await monitoringTask.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            cancellationTokenSource.Dispose();
        }

        await _uiDispatcher.EnqueueAsync(() =>
        {
            IsMonitoring = false;
            StatusText = stoppedStatusText;
            NotifyMonitoringStateChanged();
        }).ConfigureAwait(false);
    }

    private async Task ObserveMonitoringAsync(CancellationTokenSource cancellationTokenSource)
    {
        try
        {
            while (!cancellationTokenSource.IsCancellationRequested)
            {
                var snapshot = _service.GetCurrentSnapshot();
                await _uiDispatcher.EnqueueAsync(() => ApplySnapshot(snapshot)).ConfigureAwait(false);
                await Task.Delay(_pollInterval, cancellationTokenSource.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationTokenSource.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gamepad diagnostics monitoring failed.");
            await _uiDispatcher.EnqueueAsync(() =>
            {
                if (!ReferenceEquals(_monitoringCancellationTokenSource, cancellationTokenSource))
                {
                    return;
                }

                IsMonitoring = false;
                StatusText = _localizer["GamepadDiagnostics_StatusFailed"];
                NotifyMonitoringStateChanged();
            }).ConfigureAwait(false);
        }
    }

    private void ApplySnapshot(GamepadDiagnosticsSnapshot snapshot)
    {
        if (!snapshot.IsSupported)
        {
            ApplyUnsupported();
            return;
        }

        ConnectedGamepadsText = FormatCount(snapshot.ConnectedGamepadCount);
        ConnectedRawControllersText = FormatCount(snapshot.ConnectedRawControllerCount);
        InputSourceText = FormatInputSource(snapshot.InputSource);
        ActiveInputsText = FormatActiveInputs(snapshot.ActiveIntents, snapshot.ActiveContextIntents);
        ThumbstickText = FormatThumbstick(snapshot.Reading);
        RawControllersText = FormatRawControllers(snapshot.RawControllers);

        if (IsMonitoring)
        {
            StatusText = _localizer["GamepadDiagnostics_StatusMonitoring"];
        }
    }

    private void ApplyUnsupported()
    {
        ConnectedGamepadsText = FormatCount(0);
        ConnectedRawControllersText = FormatCount(0);
        InputSourceText = _localizer["GamepadDiagnostics_InputSourceNone"];
        ActiveInputsText = _localizer["GamepadDiagnostics_ActiveInputsNone"];
        ThumbstickText = FormatThumbstick(default);
        RawControllersText = _localizer["GamepadDiagnostics_RawControllersNone"];
        StatusText = _localizer["GamepadDiagnostics_StatusUnsupported"];
    }

    private string FormatInputSource(GamepadDiagnosticsInputSource inputSource)
        => inputSource switch
        {
            GamepadDiagnosticsInputSource.Gamepad => _localizer["GamepadDiagnostics_InputSourceGamepad"],
            GamepadDiagnosticsInputSource.RawGameController => _localizer["GamepadDiagnostics_InputSourceRawController"],
            _ => _localizer["GamepadDiagnostics_InputSourceNone"]
        };

    private string FormatActiveInputs(
        IReadOnlyCollection<GamepadNavigationIntent> activeIntents,
        IReadOnlyCollection<GamepadContextIntent> activeContextIntents)
    {
        if (activeIntents.Count == 0 && activeContextIntents.Count == 0)
        {
            return _localizer["GamepadDiagnostics_ActiveInputsNone"];
        }

        return string.Join(", ", activeIntents.Select(static intent => intent.ToString())
            .Concat(activeContextIntents.Select(static intent => intent.ToString())));
    }

    private static string FormatCount(int count)
        => count.ToString(CultureInfo.InvariantCulture);

    private static string FormatThumbstick(GamepadInputReading reading)
        => string.Format(
            CultureInfo.InvariantCulture,
            "X {0:0.00}, Y {1:0.00}",
            reading.ThumbstickX,
            reading.ThumbstickY);

    private string FormatRawControllers(IReadOnlyList<RawGameControllerDiagnostics> controllers)
    {
        if (controllers.Count == 0)
        {
            return _localizer["GamepadDiagnostics_RawControllersNone"];
        }

        var lines = new List<string>(controllers.Count);
        for (var i = 0; i < controllers.Count; i++)
        {
            var controller = controllers[i];
            lines.Add(string.Format(
                CultureInfo.InvariantCulture,
                "#{0} {1} VID {2:X4} PID {3:X4} {4}; buttons {5}; switches {6}; axes {7}; pressed {8}; active switches {9}; axis values {10}",
                i,
                string.IsNullOrWhiteSpace(controller.DisplayName) ? "RawGameController" : controller.DisplayName,
                controller.HardwareVendorId,
                controller.HardwareProductId,
                controller.IsWireless
                    ? _localizer["GamepadDiagnostics_ConnectionWireless"]
                    : _localizer["GamepadDiagnostics_ConnectionWired"],
                controller.ButtonCount,
                controller.SwitchCount,
                controller.AxisCount,
                FormatStringList(controller.PressedButtons),
                FormatStringList(controller.ActiveSwitches),
                FormatAxisValues(controller.Axes)));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private string FormatStringList(IReadOnlyList<string> values)
        => values.Count == 0
            ? _localizer["GamepadDiagnostics_ActiveInputsNone"]
            : string.Join(", ", values);

    private string FormatAxisValues(IReadOnlyList<double> axes)
    {
        if (axes.Count == 0)
        {
            return _localizer["GamepadDiagnostics_ActiveInputsNone"];
        }

        var values = new List<string>(axes.Count);
        for (var i = 0; i < axes.Count; i++)
        {
            values.Add(string.Format(CultureInfo.InvariantCulture, "A{0}:{1:0.00}", i, axes[i]));
        }

        return string.Join(", ", values);
    }

    private void NotifyMonitoringStateChanged()
    {
        OnPropertyChanged(nameof(CanStartMonitoring));
        OnPropertyChanged(nameof(CanStopMonitoring));
    }
}
