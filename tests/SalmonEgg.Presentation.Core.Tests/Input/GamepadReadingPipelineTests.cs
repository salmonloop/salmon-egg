using System;
using SalmonEgg.Presentation.Core.Services.Input;

namespace SalmonEgg.Presentation.Core.Tests.Input;

public sealed class GamepadReadingPipelineTests
{
    [Fact]
    public void ProcessFrame_RaisesNavigationIntentOnInitialPress_AndTracksStandardPath()
    {
        var pipeline = new GamepadReadingPipeline();
        var now = new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero);
        var reading = new GamepadInputReading(
            MoveUp: false,
            MoveDown: true,
            MoveLeft: false,
            MoveRight: false,
            Activate: false,
            Back: false);

        var frame = pipeline.ProcessFrame([reading], [], now);

        Assert.True(frame.HasActiveReading);
        Assert.Equal(GamepadInputPath.Gamepad, frame.Selection.InputPath);
        Assert.Equal([GamepadNavigationIntent.MoveDown], frame.RaisedIntents);
        Assert.Empty(frame.RaisedShortcuts);
        Assert.Empty(frame.RaisedContextIntents);
        Assert.True(frame.PathTransition.Changed);
        Assert.Equal(GamepadInputPath.Gamepad, frame.PathTransition.Path);
        Assert.Equal(GamepadInputPath.Gamepad, pipeline.CurrentPath);
    }

    [Fact]
    public void ProcessFrame_PrefersStandardVoiceShortcutOverRawFace_AndRaisesOnlyShortcutEdge()
    {
        var pipeline = new GamepadReadingPipeline();
        var now = new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero);
        var standard = new GamepadInputReading(
            MoveUp: false,
            MoveDown: false,
            MoveLeft: false,
            MoveRight: false,
            Activate: false,
            Back: false,
            ShortcutVoiceToggle: true);
        var raw = new GamepadInputReading(
            MoveUp: false,
            MoveDown: false,
            MoveLeft: false,
            MoveRight: false,
            Activate: true,
            Back: false);

        var frame = pipeline.ProcessFrame([standard], [raw], now);

        Assert.True(frame.HasActiveReading);
        Assert.Equal(GamepadInputPath.Gamepad, frame.Selection.InputPath);
        Assert.True(frame.Selection.Reading.ShortcutVoiceToggle);
        Assert.False(frame.Selection.Reading.Activate);
        Assert.Empty(frame.RaisedIntents);
        Assert.Equal([GamepadShortcutIntent.ToggleVoiceInput], frame.RaisedShortcuts);
    }

    [Fact]
    public void ProcessFrame_WhenIdle_ResetsProcessorsAndPath()
    {
        var pipeline = new GamepadReadingPipeline();
        var now = new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero);
        var active = new GamepadInputReading(
            MoveUp: true,
            MoveDown: false,
            MoveLeft: false,
            MoveRight: false,
            Activate: false,
            Back: false);

        _ = pipeline.ProcessFrame([active], [], now);
        var idle = pipeline.ProcessFrame([default], [], now.AddMilliseconds(20));

        Assert.False(idle.HasActiveReading);
        Assert.Empty(idle.RaisedIntents);
        Assert.True(idle.PathTransition.Changed);
        Assert.Equal(GamepadInputPath.None, idle.PathTransition.Path);
        Assert.Equal(GamepadInputPath.None, pipeline.CurrentPath);
    }

    [Fact]
    public void ProcessFrame_DoesNotRetriggerHeldNavigationUntilRepeatDelay()
    {
        var pipeline = new GamepadReadingPipeline();
        var now = new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero);
        var reading = new GamepadInputReading(
            MoveUp: false,
            MoveDown: true,
            MoveLeft: false,
            MoveRight: false,
            Activate: false,
            Back: false);

        var first = pipeline.ProcessFrame([reading], [], now);
        var held = pipeline.ProcessFrame([reading], [], now.AddMilliseconds(50));

        Assert.Equal([GamepadNavigationIntent.MoveDown], first.RaisedIntents);
        Assert.Empty(held.RaisedIntents);
        Assert.False(held.PathTransition.Changed);
    }

    [Fact]
    public void Reset_ClearsPathAndAllowsIntentToRaiseAgain()
    {
        var pipeline = new GamepadReadingPipeline();
        var now = new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero);
        var reading = new GamepadInputReading(
            MoveUp: false,
            MoveDown: false,
            MoveLeft: false,
            MoveRight: false,
            Activate: true,
            Back: false);

        _ = pipeline.ProcessFrame([reading], [], now);
        var reset = pipeline.Reset();
        var again = pipeline.ProcessFrame([reading], [], now.AddMilliseconds(10));

        Assert.True(reset.Changed);
        Assert.Equal(GamepadInputPath.None, reset.Path);
        Assert.Equal([GamepadNavigationIntent.Activate], again.RaisedIntents);
    }
}
