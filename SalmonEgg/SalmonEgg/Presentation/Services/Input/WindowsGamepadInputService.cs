#if WINDOWS
using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using SalmonEgg.Presentation.Core.Services.Input;
using Windows.Gaming.Input;

namespace SalmonEgg.Presentation.Services.Input;

public sealed class WindowsGamepadInputService : IGamepadInputService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    private readonly ILogger<WindowsGamepadInputService> _logger;
    private readonly WindowsRawGameControllerMapper _rawMapper;
    private readonly GamepadIntentProcessor _intentProcessor = new();
    private readonly GamepadShortcutProcessor _shortcutProcessor = new();
    private readonly GamepadContextIntentProcessor _contextIntentProcessor = new();
    private readonly object _sync = new();
    private readonly List<Gamepad> _connectedGamepads = new();
    private readonly List<RawGameController> _connectedRawControllers = new();

    private DispatcherQueueTimer? _timer;
    private bool _isStarted;
    private bool _isDisposed;
    private readonly GamepadInputPathTracker _inputPathTracker = new();
    private long _tickSequence;

    public WindowsGamepadInputService(
        ILogger<WindowsGamepadInputService> logger,
        WindowsRawGameControllerMapper rawMapper)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _rawMapper = rawMapper ?? throw new ArgumentNullException(nameof(rawMapper));
    }

    public event EventHandler<GamepadNavigationIntent>? IntentRaised;

    public event EventHandler<GamepadShortcutIntent>? ShortcutRaised;

    public event EventHandler<GamepadContextIntent>? ContextIntentRaised;

    public void Start()
    {
        ThrowIfDisposed();

        if (_isStarted)
        {
            _logger.LogDebug("Gamepad input service Start ignored; already started.");
            return;
        }

        var queue = DispatcherQueue.GetForCurrentThread();
        if (queue == null)
        {
            _logger.LogWarning("Gamepad input service start skipped because no DispatcherQueue is available on the current thread.");
            return;
        }

        lock (_sync)
        {
            if (_isStarted)
            {
                return;
            }

            _connectedGamepads.Clear();
            foreach (var gamepad in Gamepad.Gamepads)
            {
                _connectedGamepads.Add(gamepad);
            }

            foreach (var controller in RawGameController.RawGameControllers)
            {
                _connectedRawControllers.Add(controller);
            }

            _timer = queue.CreateTimer();
            _timer.Interval = PollInterval;
            _timer.IsRepeating = true;
            _timer.Tick += OnTick;

            Gamepad.GamepadAdded += OnGamepadAdded;
            Gamepad.GamepadRemoved += OnGamepadRemoved;
            RawGameController.RawGameControllerAdded += OnRawGameControllerAdded;
            RawGameController.RawGameControllerRemoved += OnRawGameControllerRemoved;

            _isStarted = true;
            _timer.Start();

            _logger.LogInformation(
                "Gamepad input service started. StandardGamepadCount={StandardGamepadCount} RawGameControllerCount={RawGameControllerCount} PollIntervalMs={PollIntervalMs}.",
                _connectedGamepads.Count,
                _connectedRawControllers.Count,
                PollInterval.TotalMilliseconds);
        }
    }

    public void Stop()
    {
        if (!_isStarted)
        {
            _logger.LogDebug("Gamepad input service Stop ignored; not started.");
            return;
        }

        lock (_sync)
        {
            if (!_isStarted)
            {
                _logger.LogDebug("Gamepad input service Stop ignored after lock; not started.");
                return;
            }

            Gamepad.GamepadAdded -= OnGamepadAdded;
            Gamepad.GamepadRemoved -= OnGamepadRemoved;
            RawGameController.RawGameControllerAdded -= OnRawGameControllerAdded;
            RawGameController.RawGameControllerRemoved -= OnRawGameControllerRemoved;

            if (_timer != null)
            {
                _timer.Tick -= OnTick;
                _timer.Stop();
                _timer = null;
            }

            _intentProcessor.Reset();
            _shortcutProcessor.Reset();
            _contextIntentProcessor.Reset();
            _connectedGamepads.Clear();
            _connectedRawControllers.Clear();
            _isStarted = false;
            _ = _inputPathTracker.Reset();
            _logger.LogInformation("Gamepad input service stopped.");
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        Stop();
        _isDisposed = true;
    }

    private void OnGamepadAdded(object? sender, Gamepad gamepad)
    {
        lock (_sync)
        {
            if (!_connectedGamepads.Contains(gamepad))
            {
                _connectedGamepads.Add(gamepad);
                _logger.LogInformation(
                    "Gamepad added. StandardGamepadCount={StandardGamepadCount}.",
                    _connectedGamepads.Count);
                return;
            }
        }

        _logger.LogDebug("Gamepad add event ignored as duplicate device.");
    }

    private void OnGamepadRemoved(object? sender, Gamepad gamepad)
    {
        lock (_sync)
        {
            if (_connectedGamepads.Remove(gamepad))
            {
                _logger.LogInformation(
                    "Gamepad removed. StandardGamepadCount={StandardGamepadCount}.",
                    _connectedGamepads.Count);
                return;
            }
        }

        _logger.LogDebug("Gamepad remove event ignored for unknown device.");
    }

    private void OnTick(DispatcherQueueTimer sender, object args)
    {
        var tick = Interlocked.Increment(ref _tickSequence);
        if (!TryGetActiveReading(out var reading))
        {
            int standardGamepadCount;
            int rawGameControllerCount;
            lock (_sync)
            {
                standardGamepadCount = _connectedGamepads.Count;
                rawGameControllerCount = _connectedRawControllers.Count;
            }

            lock (_sync)
            {
                _intentProcessor.Reset();
                _shortcutProcessor.Reset();
                _contextIntentProcessor.Reset();
                UpdateInputPath(
                    hasActiveReading: false,
                    path: GamepadInputPath.None,
                    standardGamepadCount: standardGamepadCount,
                    rawGameControllerCount: rawGameControllerCount);
            }

            return;
        }

        lock (_sync)
        {
            var raisedIntents = _intentProcessor.Process(reading, DateTimeOffset.UtcNow);
            var raisedShortcuts = _shortcutProcessor.Process(reading);
            var raisedContextIntents = _contextIntentProcessor.Process(reading);
            foreach (var intent in raisedIntents)
            {
                EmitIntent(intent, tick);
            }

            foreach (var shortcut in raisedShortcuts)
            {
                EmitShortcut(shortcut, tick);
            }

            foreach (var intent in raisedContextIntents)
            {
                EmitContextIntent(intent, tick);
            }
        }
    }

    private bool TryGetActiveReading(out GamepadInputReading reading)
    {
        Gamepad[] gamepads;
        RawGameController[] rawControllers;
        lock (_sync)
        {
            gamepads = _connectedGamepads.ToArray();
            rawControllers = _connectedRawControllers.ToArray();
        }

        var gamepadReadings = Array.ConvertAll(gamepads, static gamepad => GetInputReading(gamepad.GetCurrentReading()));
        var rawReadings = Array.ConvertAll(rawControllers, _rawMapper.GetInputReading);
        var selected = GamepadActiveReadingSelector.TrySelectActiveReading(gamepadReadings, rawReadings, out var selection);

        UpdateInputPath(selected, selection.InputPath, gamepads.Length, rawControllers.Length);
        reading = selection.Reading;
        return selected;
    }

    private void OnRawGameControllerAdded(object? sender, RawGameController controller)
    {
        lock (_sync)
        {
            if (!_connectedRawControllers.Contains(controller))
            {
                _connectedRawControllers.Add(controller);
                _logger.LogInformation(
                    "Raw game controller added. RawGameControllerCount={RawGameControllerCount}.",
                    _connectedRawControllers.Count);
                return;
            }
        }

        _logger.LogDebug("Raw game controller add event ignored as duplicate device.");
    }

    private void OnRawGameControllerRemoved(object? sender, RawGameController controller)
    {
        lock (_sync)
        {
            if (_connectedRawControllers.Remove(controller))
            {
                _logger.LogInformation(
                    "Raw game controller removed. RawGameControllerCount={RawGameControllerCount}.",
                    _connectedRawControllers.Count);
                return;
            }
        }

        _logger.LogDebug("Raw game controller remove event ignored for unknown device.");
    }

    private static GamepadInputReading GetInputReading(GamepadReading reading)
    {
        return new GamepadInputReading(
            MoveUp: reading.Buttons.HasFlag(GamepadButtons.DPadUp),
            MoveDown: reading.Buttons.HasFlag(GamepadButtons.DPadDown),
            MoveLeft: reading.Buttons.HasFlag(GamepadButtons.DPadLeft),
            MoveRight: reading.Buttons.HasFlag(GamepadButtons.DPadRight),
            Activate: reading.Buttons.HasFlag(GamepadButtons.A),
            Back: reading.Buttons.HasFlag(GamepadButtons.B),
            ShortcutVoiceToggle: reading.Buttons.HasFlag(GamepadButtons.Y),
            LeftTrigger: reading.LeftTrigger,
            RightTrigger: reading.RightTrigger,
            ThumbstickX: reading.LeftThumbstickX,
            ThumbstickY: reading.LeftThumbstickY);
    }

    private void EmitIntent(GamepadNavigationIntent intent, long tick)
    {
        IntentRaised?.Invoke(this, intent);
    }

    private void EmitShortcut(GamepadShortcutIntent shortcut, long tick)
    {
        ShortcutRaised?.Invoke(this, shortcut);
    }

    private void EmitContextIntent(GamepadContextIntent intent, long tick)
    {
        ContextIntentRaised?.Invoke(this, intent);
    }

    private void UpdateInputPath(bool hasActiveReading, GamepadInputPath path, int standardGamepadCount, int rawGameControllerCount)
    {
        var transition = _inputPathTracker.Apply(hasActiveReading, path);
        if (!transition.Changed)
        {
            return;
        }

        if (transition.Path == GamepadInputPath.None)
        {
            return;
        }

        _logger.LogInformation(
            "Gamepad input path is using {InputPath}. KnownStandardGamepads={KnownStandardGamepadCount} KnownRawGameControllers={RawGameControllerCount}.",
            transition.Path,
            standardGamepadCount,
            rawGameControllerCount);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }
}
#endif
