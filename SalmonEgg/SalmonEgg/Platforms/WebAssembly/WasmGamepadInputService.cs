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
    private readonly GamepadReadingPipeline _pipeline = new();
    private readonly object _sync = new();

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

            _ = _pipeline.Reset();
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

        GamepadReadingPipelineFrame frame;
        lock (_sync)
        {
            frame = _pipeline.ProcessFrame(readings, Array.Empty<GamepadInputReading>(), DateTimeOffset.UtcNow);
            LogPathTransitionIfNeeded(frame.PathTransition, readings.Count);
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

    private void LogPathTransitionIfNeeded(GamepadInputPathTransition transition, int standardGamepadCount)
    {
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
