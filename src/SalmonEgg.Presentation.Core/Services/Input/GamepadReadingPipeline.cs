using System;
using System.Collections.Generic;

namespace SalmonEgg.Presentation.Core.Services.Input;

/// <summary>
/// Authoritative poll-frame host for dual-path selection, edge intent emission,
/// and input-path tracking. Platform services only supply raw readings and timer I/O.
/// </summary>
public sealed class GamepadReadingPipeline
{
    private readonly GamepadIntentProcessor _intentProcessor;
    private readonly GamepadShortcutProcessor _shortcutProcessor;
    private readonly GamepadContextIntentProcessor _contextIntentProcessor;
    private readonly GamepadInputPathTracker _inputPathTracker = new();

    public GamepadReadingPipeline()
        : this(new GamepadIntentProcessor(), new GamepadShortcutProcessor(), new GamepadContextIntentProcessor())
    {
    }

    public GamepadReadingPipeline(
        GamepadIntentProcessor intentProcessor,
        GamepadShortcutProcessor shortcutProcessor,
        GamepadContextIntentProcessor contextIntentProcessor)
    {
        _intentProcessor = intentProcessor ?? throw new ArgumentNullException(nameof(intentProcessor));
        _shortcutProcessor = shortcutProcessor ?? throw new ArgumentNullException(nameof(shortcutProcessor));
        _contextIntentProcessor = contextIntentProcessor ?? throw new ArgumentNullException(nameof(contextIntentProcessor));
    }

    public GamepadInputPath CurrentPath => _inputPathTracker.CurrentPath;

    public GamepadReadingPipelineFrame ProcessFrame(
        IReadOnlyList<GamepadInputReading> gamepadReadings,
        IReadOnlyList<GamepadInputReading> rawReadings,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(gamepadReadings);
        ArgumentNullException.ThrowIfNull(rawReadings);

        if (!GamepadActiveReadingSelector.TrySelectActiveReading(gamepadReadings, rawReadings, out var selection))
        {
            _intentProcessor.Reset();
            _shortcutProcessor.Reset();
            _contextIntentProcessor.Reset();
            var idleTransition = _inputPathTracker.Apply(hasActiveReading: false, GamepadInputPath.None);
            return new GamepadReadingPipelineFrame(
                HasActiveReading: false,
                Selection: default,
                PathTransition: idleTransition,
                RaisedIntents: Array.Empty<GamepadNavigationIntent>(),
                RaisedShortcuts: Array.Empty<GamepadShortcutIntent>(),
                RaisedContextIntents: Array.Empty<GamepadContextIntent>());
        }

        var pathTransition = _inputPathTracker.Apply(hasActiveReading: true, selection.InputPath);
        var raisedIntents = _intentProcessor.Process(selection.Reading, now);
        var raisedShortcuts = _shortcutProcessor.Process(selection.Reading);
        var raisedContextIntents = _contextIntentProcessor.Process(selection.Reading);

        return new GamepadReadingPipelineFrame(
            HasActiveReading: true,
            Selection: selection,
            PathTransition: pathTransition,
            RaisedIntents: CopyRaised(raisedIntents),
            RaisedShortcuts: CopyRaised(raisedShortcuts),
            RaisedContextIntents: CopyRaised(raisedContextIntents));
    }

    public GamepadInputPathTransition Reset()
    {
        _intentProcessor.Reset();
        _shortcutProcessor.Reset();
        _contextIntentProcessor.Reset();
        return _inputPathTracker.Reset();
    }

    private static IReadOnlyList<T> CopyRaised<T>(IReadOnlyCollection<T> raised)
    {
        if (raised.Count == 0)
        {
            return Array.Empty<T>();
        }

        if (raised is IReadOnlyList<T> list)
        {
            return list;
        }

        var copy = new T[raised.Count];
        var index = 0;
        foreach (var item in raised)
        {
            copy[index++] = item;
        }

        return copy;
    }
}

public readonly record struct GamepadReadingPipelineFrame(
    bool HasActiveReading,
    GamepadActiveReadingSelection Selection,
    GamepadInputPathTransition PathTransition,
    IReadOnlyList<GamepadNavigationIntent> RaisedIntents,
    IReadOnlyList<GamepadShortcutIntent> RaisedShortcuts,
    IReadOnlyList<GamepadContextIntent> RaisedContextIntents);
