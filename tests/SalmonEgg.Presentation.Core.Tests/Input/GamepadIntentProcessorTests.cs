using System;
using System.Linq;
using SalmonEgg.Presentation.Core.Services.Input;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Input;

public sealed class GamepadIntentProcessorTests
{
    [Fact]
    public void GetActiveIntents_MapsDigitalDirectionsAndActions()
    {
        var reading = new GamepadInputReading(
            MoveUp: true,
            MoveDown: false,
            MoveLeft: true,
            MoveRight: false,
            Activate: true,
            Back: true);

        var intents = GamepadIntentProcessor.GetActiveIntents(reading);

        Assert.Equal(
            new[]
            {
                GamepadNavigationIntent.MoveUp,
                GamepadNavigationIntent.MoveLeft,
                GamepadNavigationIntent.Activate,
                GamepadNavigationIntent.Back
            }.OrderBy(static intent => intent),
            intents.OrderBy(static intent => intent));
    }

    [Fact]
    public void GetActiveIntents_IgnoresThumbstickInsideDeadzone()
    {
        var reading = new GamepadInputReading(
            MoveUp: false,
            MoveDown: false,
            MoveLeft: false,
            MoveRight: false,
            Activate: false,
            Back: false,
            ThumbstickX: 0.49,
            ThumbstickY: -0.49);

        var intents = GamepadIntentProcessor.GetActiveIntents(reading);

        Assert.Empty(intents);
    }

    [Theory]
    [InlineData(0.7, 0.2, GamepadNavigationIntent.MoveRight)]
    [InlineData(-0.7, 0.2, GamepadNavigationIntent.MoveLeft)]
    [InlineData(0.2, 0.7, GamepadNavigationIntent.MoveUp)]
    [InlineData(0.2, -0.7, GamepadNavigationIntent.MoveDown)]
    public void GetActiveIntents_MapsDominantThumbstickAxis(double x, double y, GamepadNavigationIntent expected)
    {
        var reading = new GamepadInputReading(
            MoveUp: false,
            MoveDown: false,
            MoveLeft: false,
            MoveRight: false,
            Activate: false,
            Back: false,
            ThumbstickX: x,
            ThumbstickY: y);

        var intents = GamepadIntentProcessor.GetActiveIntents(reading);

        Assert.Equal(new[] { expected }, intents);
    }

    [Fact]
    public void Process_RaisesIntentImmediatelyThenWaitsForRepeatDelay()
    {
        var processor = new GamepadIntentProcessor(TimeSpan.FromMilliseconds(350), TimeSpan.FromMilliseconds(120));
        var start = DateTimeOffset.Parse("2026-05-19T00:00:00Z");
        var reading = Reading(moveDown: true);

        var first = processor.Process(reading, start);
        var beforeDelay = processor.Process(reading, start.AddMilliseconds(300));

        Assert.Equal(new[] { GamepadNavigationIntent.MoveDown }, first);
        Assert.Empty(beforeDelay);
    }

    [Fact]
    public void Process_RepeatsIntentAfterInitialDelayAndRepeatInterval()
    {
        var processor = new GamepadIntentProcessor(TimeSpan.FromMilliseconds(350), TimeSpan.FromMilliseconds(120));
        var start = DateTimeOffset.Parse("2026-05-19T00:00:00Z");
        var reading = Reading(moveDown: true);

        _ = processor.Process(reading, start);
        var firstRepeat = processor.Process(reading, start.AddMilliseconds(350));
        var beforeInterval = processor.Process(reading, start.AddMilliseconds(430));
        var secondRepeat = processor.Process(reading, start.AddMilliseconds(470));

        Assert.Equal(new[] { GamepadNavigationIntent.MoveDown }, firstRepeat);
        Assert.Empty(beforeInterval);
        Assert.Equal(new[] { GamepadNavigationIntent.MoveDown }, secondRepeat);
    }

    [Fact]
    public void Process_RaisesAgainAfterRelease()
    {
        var processor = new GamepadIntentProcessor(TimeSpan.FromMilliseconds(350), TimeSpan.FromMilliseconds(120));
        var start = DateTimeOffset.Parse("2026-05-19T00:00:00Z");
        var reading = Reading(moveDown: true);

        _ = processor.Process(reading, start);
        var released = processor.Process(default, start.AddMilliseconds(50));
        var pressedAgain = processor.Process(reading, start.AddMilliseconds(60));

        Assert.Empty(released);
        Assert.Equal(new[] { GamepadNavigationIntent.MoveDown }, pressedAgain);
    }

    [Fact]
    public void Process_RaisesNewDirectionImmediately_WhenDirectionChangesBeforeRepeatDelay()
    {
        var processor = new GamepadIntentProcessor(TimeSpan.FromMilliseconds(350), TimeSpan.FromMilliseconds(120));
        var start = DateTimeOffset.Parse("2026-05-19T00:00:00Z");

        _ = processor.Process(Reading(moveDown: true), start);
        var changedDirection = processor.Process(
            new GamepadInputReading(
                MoveUp: false,
                MoveDown: false,
                MoveLeft: false,
                MoveRight: true,
                Activate: false,
                Back: false),
            start.AddMilliseconds(50));

        Assert.Equal(new[] { GamepadNavigationIntent.MoveRight }, changedDirection);
    }

    [Fact]
    public void Reset_ClearsRepeatState()
    {
        var processor = new GamepadIntentProcessor(TimeSpan.FromMilliseconds(350), TimeSpan.FromMilliseconds(120));
        var start = DateTimeOffset.Parse("2026-05-19T00:00:00Z");
        var reading = Reading(moveDown: true);

        _ = processor.Process(reading, start);
        processor.Reset();
        var afterReset = processor.Process(reading, start.AddMilliseconds(10));

        Assert.Equal(new[] { GamepadNavigationIntent.MoveDown }, afterReset);
    }

    private static GamepadInputReading Reading(bool moveDown)
    {
        return new GamepadInputReading(
            MoveUp: false,
            MoveDown: moveDown,
            MoveLeft: false,
            MoveRight: false,
            Activate: false,
            Back: false);
    }
}
