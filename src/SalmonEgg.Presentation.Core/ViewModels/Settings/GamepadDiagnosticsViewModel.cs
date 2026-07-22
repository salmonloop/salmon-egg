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

public sealed partial class GamepadDiagnosticsViewModel : ObservableObject, IDisposable
{
    private enum GamepadStatusKind
    {
        NotStarted,
        Monitoring,
        Stopped,
        Unsupported,
        Failed
    }

    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(100);

    private readonly IGamepadDiagnosticsService _service;
    private readonly IPlatformCapabilityService _capabilities;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly IStringLocalizer<CoreStrings> _localizer;
    private readonly ILogger<GamepadDiagnosticsViewModel> _logger;
    private readonly TimeSpan _pollInterval;
    private readonly IAppLanguageService? _languageService;
    private CancellationTokenSource? _monitoringCancellationTokenSource;
    private Task? _monitoringTask;
    private GamepadDiagnosticsSnapshot _snapshot;
    private GamepadStatusKind _statusKind;
    private bool _disposed;

    public GamepadDiagnosticsViewModel(
        IGamepadDiagnosticsService service,
        IPlatformCapabilityService capabilities,
        IUiDispatcher uiDispatcher,
        IStringLocalizer<CoreStrings> localizer,
        ILogger<GamepadDiagnosticsViewModel> logger,
        IAppLanguageService? languageService = null)
        : this(service, capabilities, uiDispatcher, localizer, logger, DefaultPollInterval, languageService)
    {
    }

    internal GamepadDiagnosticsViewModel(
        IGamepadDiagnosticsService service,
        IPlatformCapabilityService capabilities,
        IUiDispatcher uiDispatcher,
        IStringLocalizer<CoreStrings> localizer,
        ILogger<GamepadDiagnosticsViewModel> logger,
        TimeSpan pollInterval,
        IAppLanguageService? languageService = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _languageService = languageService;
        _pollInterval = pollInterval > TimeSpan.Zero
            ? pollInterval
            : throw new ArgumentOutOfRangeException(nameof(pollInterval));

        _snapshot = GamepadDiagnosticsSnapshot.Unsupported with
        {
            IsSupported = _capabilities.SupportsGamepadInput
        };
        _statusKind = _capabilities.SupportsGamepadInput
            ? GamepadStatusKind.NotStarted
            : GamepadStatusKind.Unsupported;
        _statusText = ResolveStatusText();
        _inputSourceText = FormatInputSource(_snapshot.InputSource);
        _connectedGamepadsText = FormatCount(_snapshot.ConnectedGamepadCount);
        _connectedRawControllersText = FormatCount(_snapshot.ConnectedRawControllerCount);
        _activeInputsText = FormatActiveInputs(
            _snapshot.ActiveIntents,
            _snapshot.ActiveContextIntents,
            _snapshot.ActiveShortcuts);
        _thumbstickText = FormatThumbstick(_snapshot.Reading);
        _standardGamepadsText = FormatStandardGamepads(_snapshot.StandardGamepads);
        _rawControllersText = FormatRawControllers(_snapshot.RawControllers);
        if (_languageService is not null)
        {
            _languageService.LanguageChanged += OnLanguageChanged;
        }
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
    private string _standardGamepadsText;

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
        SetStatus(GamepadStatusKind.Monitoring);
        NotifyMonitoringStateChanged();

        var cancellationTokenSource = new CancellationTokenSource();
        _monitoringCancellationTokenSource = cancellationTokenSource;
        _monitoringTask = Task.Run(() => ObserveMonitoringAsync(cancellationTokenSource));
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task StopMonitoringAsync()
    {
        await StopMonitoringCoreAsync(GamepadStatusKind.Stopped).ConfigureAwait(false);
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
            await _uiDispatcher.EnqueueAsync(() => SetStatus(GamepadStatusKind.Failed))
                .ConfigureAwait(false);
        }
    }

    public Task HandlePageUnloadedAsync()
        => StopMonitoringCoreAsync(GamepadStatusKind.Stopped);

    private async Task StopMonitoringCoreAsync(GamepadStatusKind stoppedStatus)
    {
        var cancellationTokenSource = _monitoringCancellationTokenSource;
        var monitoringTask = _monitoringTask;

        if (cancellationTokenSource is null)
        {
            IsMonitoring = false;
            SetStatus(_capabilities.SupportsGamepadInput
                ? stoppedStatus
                : GamepadStatusKind.Unsupported);
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
            SetStatus(stoppedStatus);
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
                SetStatus(GamepadStatusKind.Failed);
                NotifyMonitoringStateChanged();
            }).ConfigureAwait(false);
        }
    }

    private void ApplySnapshot(GamepadDiagnosticsSnapshot snapshot)
    {
        _snapshot = snapshot;
        if (!snapshot.IsSupported)
        {
            ApplyUnsupported();
            return;
        }

        ProjectSnapshot(snapshot);

        if (IsMonitoring)
        {
            SetStatus(GamepadStatusKind.Monitoring);
        }
    }

    private void ProjectSnapshot(GamepadDiagnosticsSnapshot snapshot)
    {
        ConnectedGamepadsText = FormatCount(snapshot.ConnectedGamepadCount);
        ConnectedRawControllersText = FormatCount(snapshot.ConnectedRawControllerCount);
        InputSourceText = FormatInputSource(snapshot.InputSource);
        ActiveInputsText = FormatActiveInputs(
            snapshot.ActiveIntents,
            snapshot.ActiveContextIntents,
            snapshot.ActiveShortcuts);
        ThumbstickText = FormatThumbstick(snapshot.Reading);
        StandardGamepadsText = FormatStandardGamepads(snapshot.StandardGamepads);
        RawControllersText = FormatRawControllers(snapshot.RawControllers);
    }

    private void ApplyUnsupported()
    {
        _snapshot = GamepadDiagnosticsSnapshot.Unsupported;
        ProjectSnapshot(_snapshot);
        SetStatus(GamepadStatusKind.Unsupported);
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
        IReadOnlyCollection<GamepadContextIntent> activeContextIntents,
        IReadOnlyCollection<GamepadShortcutIntent> activeShortcuts)
    {
        if (activeIntents.Count == 0 && activeContextIntents.Count == 0 && activeShortcuts.Count == 0)
        {
            return _localizer["GamepadDiagnostics_ActiveInputsNone"];
        }

        return string.Join(", ", activeIntents.Select(static intent => intent.ToString())
            .Concat(activeContextIntents.Select(static intent => intent.ToString()))
            .Concat(activeShortcuts.Select(static intent => intent.ToString())));
    }

    private static string FormatCount(int count)
        => count.ToString(CultureInfo.InvariantCulture);

    private static string FormatThumbstick(GamepadInputReading reading)
        => string.Format(
            CultureInfo.InvariantCulture,
            "X {0:0.00}, Y {1:0.00}",
            reading.ThumbstickX,
            reading.ThumbstickY);


    private string FormatStandardGamepads(IReadOnlyList<StandardGamepadDiagnostics> gamepads)
    {
        if (gamepads.Count == 0)
        {
            return _localizer["GamepadDiagnostics_StandardGamepadsNone"];
        }

        var lines = new List<string>(gamepads.Count);
        for (var i = 0; i < gamepads.Count; i++)
        {
            var gamepad = gamepads[i];
            lines.Add(string.Format(
                CultureInfo.InvariantCulture,
                "#{0} labels {1}; pressed {2}",
                i,
                FormatStringList(gamepad.ButtonLabels),
                FormatStringList(gamepad.PressedButtons))
                + "; semantic "
                + FormatActiveInputs(
                    GamepadIntentProcessor.GetActiveIntents(gamepad.Reading),
                    GamepadContextIntentProjector.GetActiveIntents(gamepad.Reading),
                    GamepadShortcutIntentProjector.GetActiveShortcuts(gamepad.Reading))
                + "; reading "
                + FormatThumbstick(gamepad.Reading));
        }

        return string.Join(Environment.NewLine, lines);
    }

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
            var faceButtonLayout = RawGameControllerFaceButtonLayoutResolver.Resolve(
                controller.DisplayName,
                controller.HardwareVendorId);
            lines.Add(string.Format(
                CultureInfo.InvariantCulture,
                "#{0} {1} VID {2:X4} PID {3:X4} {4}; layout {5}; buttons {6}; switches {7}; axes {8}; pressed {9}; active switches {10}; axis values {11}",
                i,
                string.IsNullOrWhiteSpace(controller.DisplayName) ? "RawGameController" : controller.DisplayName,
                controller.HardwareVendorId,
                controller.HardwareProductId,
                controller.IsWireless
                    ? _localizer["GamepadDiagnostics_ConnectionWireless"]
                    : _localizer["GamepadDiagnostics_ConnectionWired"],
                FormatFaceButtonLayout(faceButtonLayout),
                controller.ButtonCount,
                controller.SwitchCount,
                controller.AxisCount,
                FormatStringList(controller.PressedButtons),
                FormatStringList(controller.ActiveSwitches),
                FormatAxisValues(controller.Axes))
                + "; semantic "
                + FormatActiveInputs(
                    GamepadIntentProcessor.GetActiveIntents(controller.Reading),
                    GamepadContextIntentProjector.GetActiveIntents(controller.Reading),
                    GamepadShortcutIntentProjector.GetActiveShortcuts(controller.Reading))
                + "; reading "
                + FormatThumbstick(controller.Reading));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private string FormatFaceButtonLayout(RawGameControllerFaceButtonLayout layout)
        => layout switch
        {
            RawGameControllerFaceButtonLayout.Nintendo => _localizer["GamepadDiagnostics_FaceButtonLayoutNintendo"],
            _ => _localizer["GamepadDiagnostics_FaceButtonLayoutStandard"]
        };

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

    private void OnLanguageChanged(object? sender, EventArgs e)
        => _ = _uiDispatcher.EnqueueAsync(ReprojectLocalizedState);

    private void ReprojectLocalizedState()
    {
        ProjectSnapshot(_snapshot);
        StatusText = ResolveStatusText();
    }

    private void SetStatus(GamepadStatusKind statusKind)
    {
        _statusKind = statusKind;
        StatusText = ResolveStatusText();
    }

    private string ResolveStatusText()
        => _statusKind switch
        {
            GamepadStatusKind.Monitoring => _localizer["GamepadDiagnostics_StatusMonitoring"],
            GamepadStatusKind.Stopped => _localizer["GamepadDiagnostics_StatusStopped"],
            GamepadStatusKind.Unsupported => _localizer["GamepadDiagnostics_StatusUnsupported"],
            GamepadStatusKind.Failed => _localizer["GamepadDiagnostics_StatusFailed"],
            _ => _localizer["GamepadDiagnostics_StatusNotStarted"]
        };

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_languageService is not null)
        {
            _languageService.LanguageChanged -= OnLanguageChanged;
        }
    }
}
