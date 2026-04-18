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
    private static readonly TimeSpan InitialRepeatDelay = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan RepeatInterval = TimeSpan.FromMilliseconds(120);

    private const double ThumbstickDeadzone = 0.5;

    private readonly ILogger<WindowsGamepadInputService> _logger;
    private readonly WindowsRawGameControllerMapper _rawMapper;
    private readonly object _sync = new();
    private readonly Dictionary<GamepadNavigationIntent, PressState> _pressedStates = new();
    private readonly List<Gamepad> _connectedGamepads = new();
    private readonly List<RawGameController> _connectedRawControllers = new();

    private DispatcherQueueTimer? _timer;
    private bool _isStarted;
    private bool _isDisposed;
    private InputPath _activeInputPath = InputPath.None;

    public WindowsGamepadInputService(
        ILogger<WindowsGamepadInputService> logger,
        WindowsRawGameControllerMapper rawMapper)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _rawMapper = rawMapper ?? throw new ArgumentNullException(nameof(rawMapper));
    }

    public event EventHandler<GamepadNavigationIntent>? IntentRaised;

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

            _pressedStates.Clear();
            _connectedGamepads.Clear();
            _connectedRawControllers.Clear();
            _isStarted = false;
            _activeInputPath = InputPath.None;
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
        if (!TryGetActiveIntents(out var activeIntents))
        {
            lock (_sync)
            {
                _pressedStates.Clear();
                UpdateInputPath(InputPath.None);
            }

            return;
        }

        var now = DateTimeOffset.UtcNow;

        lock (_sync)
        {
            foreach (var intent in activeIntents)
            {
                if (!_pressedStates.TryGetValue(intent, out var state))
                {
                    EmitIntent(intent);
                    _pressedStates[intent] = new PressState(now, now);
                    continue;
                }

                if (now - state.PressedSince >= InitialRepeatDelay
                    && now - state.LastRaised >= RepeatInterval)
                {
                    EmitIntent(intent);
                    _pressedStates[intent] = state with { LastRaised = now };
                }
            }

            var released = new List<GamepadNavigationIntent>();
            foreach (var pressedIntent in _pressedStates.Keys)
            {
                if (!activeIntents.Contains(pressedIntent))
                {
                    released.Add(pressedIntent);
                }
            }

            foreach (var releasedIntent in released)
            {
                _pressedStates.Remove(releasedIntent);
            }
        }
    }

    private bool TryGetActiveIntents(out HashSet<GamepadNavigationIntent> activeIntents)
    {
        Gamepad[] gamepads;
        RawGameController[] rawControllers;
        lock (_sync)
        {
            gamepads = _connectedGamepads.ToArray();
            rawControllers = _connectedRawControllers.ToArray();
        }

        foreach (var gamepad in gamepads)
        {
            activeIntents = GetActiveIntents(gamepad.GetCurrentReading());
            if (activeIntents.Count > 0)
            {
                UpdateInputPath(InputPath.Gamepad);
                return true;
            }
        }

        foreach (var controller in rawControllers)
        {
            if (HasMatchingGamepad(controller, gamepads))
            {
                continue;
            }

            activeIntents = _rawMapper.GetActiveIntents(controller);
            if (activeIntents.Count > 0)
            {
                UpdateInputPath(InputPath.RawGameController);
                return true;
            }
        }

        activeIntents = new HashSet<GamepadNavigationIntent>();
        return false;
    }

    private static bool HasMatchingGamepad(RawGameController controller, IReadOnlyList<Gamepad> gamepads)
    {
        foreach (var gamepad in gamepads)
        {
            if (ReferenceEquals(RawGameController.FromGameController(gamepad), controller))
            {
                return true;
            }
        }

        return false;
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

    private static HashSet<GamepadNavigationIntent> GetActiveIntents(GamepadReading reading)
    {
        var intents = new HashSet<GamepadNavigationIntent>();

        if (reading.Buttons.HasFlag(GamepadButtons.DPadUp))
        {
            intents.Add(GamepadNavigationIntent.MoveUp);
        }

        if (reading.Buttons.HasFlag(GamepadButtons.DPadDown))
        {
            intents.Add(GamepadNavigationIntent.MoveDown);
        }

        if (reading.Buttons.HasFlag(GamepadButtons.DPadLeft))
        {
            intents.Add(GamepadNavigationIntent.MoveLeft);
        }

        if (reading.Buttons.HasFlag(GamepadButtons.DPadRight))
        {
            intents.Add(GamepadNavigationIntent.MoveRight);
        }

        if (Math.Abs(reading.LeftThumbstickX) >= ThumbstickDeadzone
            || Math.Abs(reading.LeftThumbstickY) >= ThumbstickDeadzone)
        {
            if (Math.Abs(reading.LeftThumbstickX) > Math.Abs(reading.LeftThumbstickY))
            {
                intents.Add(reading.LeftThumbstickX >= 0
                    ? GamepadNavigationIntent.MoveRight
                    : GamepadNavigationIntent.MoveLeft);
            }
            else
            {
                intents.Add(reading.LeftThumbstickY >= 0
                    ? GamepadNavigationIntent.MoveUp
                    : GamepadNavigationIntent.MoveDown);
            }
        }

        if (reading.Buttons.HasFlag(GamepadButtons.A))
        {
            intents.Add(GamepadNavigationIntent.Activate);
        }

        if (reading.Buttons.HasFlag(GamepadButtons.B))
        {
            intents.Add(GamepadNavigationIntent.Back);
        }

        return intents;
    }

    private void EmitIntent(GamepadNavigationIntent intent)
    {
        IntentRaised?.Invoke(this, intent);
    }

    private void UpdateInputPath(InputPath path)
    {
        if (_activeInputPath == path)
        {
            return;
        }

        _activeInputPath = path;
        if (path == InputPath.None)
        {
            _logger.LogDebug("Gamepad input path is now idle.");
            return;
        }

        _logger.LogInformation("Gamepad input path is using {InputPath}.", path);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }

    private readonly record struct PressState(DateTimeOffset PressedSince, DateTimeOffset LastRaised);

    private enum InputPath
    {
        None,
        Gamepad,
        RawGameController
    }
}
#endif
