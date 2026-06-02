#if WINDOWS
using System;
using System.Collections.Generic;
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
        }
    }

    public void Stop()
    {
        if (!_isStarted)
        {
            return;
        }

        lock (_sync)
        {
            if (!_isStarted)
            {
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
            }
        }
    }

    private void OnGamepadRemoved(object? sender, Gamepad gamepad)
    {
        lock (_sync)
        {
            _connectedGamepads.Remove(gamepad);
        }
    }

    private void OnTick(DispatcherQueueTimer sender, object args)
    {
        if (!TryGetActiveReading(out var reading))
        {
            lock (_sync)
            {
                _intentProcessor.Reset();
                _shortcutProcessor.Reset();
                _contextIntentProcessor.Reset();
                UpdateInputPath(hasActiveReading: false, GamepadInputPath.None);
            }

            return;
        }

        lock (_sync)
        {
            var raisedIntents = _intentProcessor.Process(reading, DateTimeOffset.UtcNow);
            foreach (var intent in raisedIntents)
            {
                EmitIntent(intent);
            }

            foreach (var shortcut in _shortcutProcessor.Process(reading))
            {
                EmitShortcut(shortcut);
            }

            foreach (var intent in _contextIntentProcessor.Process(reading))
            {
                EmitContextIntent(intent);
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

        UpdateInputPath(selected, selection.InputPath);
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
            }
        }
    }

    private void OnRawGameControllerRemoved(object? sender, RawGameController controller)
    {
        lock (_sync)
        {
            _connectedRawControllers.Remove(controller);
        }
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

    private void EmitIntent(GamepadNavigationIntent intent)
    {
        IntentRaised?.Invoke(this, intent);
    }

    private void EmitShortcut(GamepadShortcutIntent shortcut)
    {
        ShortcutRaised?.Invoke(this, shortcut);
    }

    private void EmitContextIntent(GamepadContextIntent intent)
    {
        ContextIntentRaised?.Invoke(this, intent);
    }

    private void UpdateInputPath(bool hasActiveReading, GamepadInputPath path)
    {
        var transition = _inputPathTracker.Apply(hasActiveReading, path);
        if (!transition.Changed)
        {
            return;
        }

        if (transition.Path == GamepadInputPath.None)
        {
            _logger.LogDebug("Gamepad input path is now idle.");
            return;
        }

        _logger.LogInformation("Gamepad input path is using {InputPath}.", transition.Path);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }
}
#endif
