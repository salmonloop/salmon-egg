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
    private readonly GamepadReadingPipeline _pipeline = new();
    private readonly object _sync = new();
    private readonly List<Gamepad> _connectedGamepads = new();
    private readonly List<RawGameController> _connectedRawControllers = new();
    private readonly Dictionary<Gamepad, WindowsStandardGamepadIdentity> _standardGamepadIdentities = new();

    private DispatcherQueueTimer? _timer;
    private bool _isStarted;
    private bool _isDisposed;
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
            _standardGamepadIdentities.Clear();
            foreach (var gamepad in Gamepad.Gamepads)
            {
                _connectedGamepads.Add(gamepad);
                CacheStandardGamepadIdentity(gamepad);
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

            _ = _pipeline.Reset();
            _connectedGamepads.Clear();
            _standardGamepadIdentities.Clear();
            _connectedRawControllers.Clear();
            _isStarted = false;
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
                CacheStandardGamepadIdentity(gamepad);
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
                _standardGamepadIdentities.Remove(gamepad);
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
        Gamepad[] gamepads;
        WindowsStandardGamepadIdentity[] identities;
        RawGameController[] rawControllers;
        lock (_sync)
        {
            gamepads = _connectedGamepads.ToArray();
            identities = new WindowsStandardGamepadIdentity[gamepads.Length];
            for (var i = 0; i < gamepads.Length; i++)
            {
                identities[i] = GetOrCacheStandardGamepadIdentity(gamepads[i]);
            }

            rawControllers = _connectedRawControllers.ToArray();
        }

        var gamepadReadings = new GamepadInputReading[gamepads.Length];
        for (var i = 0; i < gamepads.Length; i++)
        {
            gamepadReadings[i] = GetInputReading(gamepads[i], identities[i]);
        }

        var rawReadings = Array.ConvertAll(rawControllers, _rawMapper.GetInputReading);

        GamepadReadingPipelineFrame frame;
        lock (_sync)
        {
            frame = _pipeline.ProcessFrame(gamepadReadings, rawReadings, DateTimeOffset.UtcNow);
            LogPathTransitionIfNeeded(frame.PathTransition, gamepads.Length, rawControllers.Length);
        }

        foreach (var intent in frame.RaisedIntents)
        {
            EmitIntent(intent, tick);
        }

        foreach (var shortcut in frame.RaisedShortcuts)
        {
            EmitShortcut(shortcut, tick);
        }

        foreach (var intent in frame.RaisedContextIntents)
        {
            EmitContextIntent(intent, tick);
        }
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

    private static GamepadInputReading GetInputReading(
        Gamepad gamepad,
        WindowsStandardGamepadIdentity identity)
        => WindowsStandardGamepadReadingMapper.GetInputReading(gamepad, identity);

    private void CacheStandardGamepadIdentity(Gamepad gamepad)
        => _standardGamepadIdentities[gamepad] = WindowsGameControllerButtonLabelMapper.GetIdentity(gamepad);

    private WindowsStandardGamepadIdentity GetOrCacheStandardGamepadIdentity(Gamepad gamepad)
    {
        if (_standardGamepadIdentities.TryGetValue(gamepad, out var identity))
        {
            return identity;
        }

        identity = WindowsGameControllerButtonLabelMapper.GetIdentity(gamepad);
        _standardGamepadIdentities[gamepad] = identity;
        return identity;
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

    private void LogPathTransitionIfNeeded(
        GamepadInputPathTransition transition,
        int standardGamepadCount,
        int rawGameControllerCount)
    {
        if (!transition.Changed || transition.Path == GamepadInputPath.None)
        {
            return;
        }

        _logger.LogInformation(
            "Gamepad input path is using {InputPath}. KnownStandardGamepads={KnownStandardGamepadCount}. KnownRawControllers={KnownRawControllerCount}.",
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
