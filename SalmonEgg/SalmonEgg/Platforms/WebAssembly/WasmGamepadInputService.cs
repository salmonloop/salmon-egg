#if __WASM__
using System;
using System.Runtime.Versioning;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using SalmonEgg.Presentation.Core.Services.Input;

namespace SalmonEgg.Platforms.WebAssembly;

[SupportedOSPlatform("browser")]
public sealed class WasmGamepadInputService : IGamepadInputService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    private readonly ILogger<WasmGamepadInputService> _logger;
    private readonly GamepadIntentProcessor _intentProcessor = new();
    private readonly GamepadShortcutProcessor _shortcutProcessor = new();
    private readonly GamepadContextIntentProcessor _contextIntentProcessor = new();
    private readonly object _sync = new();
    private readonly GamepadInputPathTracker _inputPathTracker = new();

    private DispatcherQueueTimer? _timer;
    private bool _isStarted;
    private bool _isDisposed;
    private long _tickSequence;

    public WasmGamepadInputService(ILogger<WasmGamepadInputService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public event EventHandler<GamepadNavigationIntent>? IntentRaised;

    public event EventHandler<GamepadShortcutIntent>? ShortcutRaised;

    public event EventHandler<GamepadContextIntent>? ContextIntentRaised;

    public void Start()
    {
        ThrowIfDisposed();

        if (_isStarted)
        {
            _logger.LogDebug("WASM gamepad input service Start ignored; already started.");
            return;
        }

        var queue = DispatcherQueue.GetForCurrentThread();
        if (queue == null)
        {
            _logger.LogWarning("WASM gamepad input service start skipped because no DispatcherQueue is available on the current thread.");
            return;
        }

        lock (_sync)
        {
            if (_isStarted)
            {
                return;
            }

            _timer = queue.CreateTimer();
            _timer.Interval = PollInterval;
            _timer.IsRepeating = true;
            _timer.Tick += OnTick;
            _isStarted = true;
            _timer.Start();

            _logger.LogInformation(
                "WASM gamepad input service started. PollIntervalMs={PollIntervalMs}.",
                PollInterval.TotalMilliseconds);
        }
    }

    public void Stop()
    {
        if (!_isStarted)
        {
            _logger.LogDebug("WASM gamepad input service Stop ignored; not started.");
            return;
        }

        lock (_sync)
        {
            if (!_isStarted)
            {
                return;
            }

            if (_timer != null)
            {
                _timer.Tick -= OnTick;
                _timer.Stop();
                _timer = null;
            }

            _intentProcessor.Reset();
            _shortcutProcessor.Reset();
            _contextIntentProcessor.Reset();
            _ = _inputPathTracker.Reset();
            _isStarted = false;
            _logger.LogInformation("WASM gamepad input service stopped.");
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

    private void OnTick(DispatcherQueueTimer sender, object args)
    {
        var tick = Interlocked.Increment(ref _tickSequence);
        var readings = WasmGamepadSnapshotReader.ReadInputReadings();

        if (!GamepadActiveReadingSelector.TrySelectActiveReading(readings, [], out var selection))
        {
            lock (_sync)
            {
                _intentProcessor.Reset();
                _shortcutProcessor.Reset();
                _contextIntentProcessor.Reset();
                UpdateInputPath(hasActiveReading: false, GamepadInputPath.None, readings.Count);
            }

            return;
        }

        lock (_sync)
        {
            UpdateInputPath(hasActiveReading: true, selection.InputPath, readings.Count);

            var raisedIntents = _intentProcessor.Process(selection.Reading, DateTimeOffset.UtcNow);
            var raisedShortcuts = _shortcutProcessor.Process(selection.Reading);
            var raisedContextIntents = _contextIntentProcessor.Process(selection.Reading);
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

    private void UpdateInputPath(bool hasActiveReading, GamepadInputPath path, int standardGamepadCount)
    {
        var transition = _inputPathTracker.Apply(hasActiveReading, path);
        if (!transition.Changed || transition.Path == GamepadInputPath.None)
        {
            return;
        }

        _logger.LogInformation(
            "WASM gamepad input path is using {InputPath}. KnownStandardGamepads={KnownStandardGamepadCount}.",
            transition.Path,
            standardGamepadCount);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }
}
#endif
