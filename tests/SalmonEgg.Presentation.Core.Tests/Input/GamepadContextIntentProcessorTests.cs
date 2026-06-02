using SalmonEgg.Presentation.Core.Services.Input;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Input;

public sealed class GamepadContextIntentProcessorTests
{
    [Fact]
    public void Process_RaisesPageIntentOnInitialTriggerPressOnly()
    {
        var processor = new GamepadContextIntentProcessor();
        var reading = new GamepadInputReading(
            MoveUp: false,
            MoveDown: false,
            MoveLeft: false,
            MoveRight: false,
            Activate: false,
            Back: false,
            LeftTrigger: 0.75);

        var first = processor.Process(reading);
        var held = processor.Process(reading);

        Assert.Equal(new[] { GamepadContextIntent.PageUp }, first);
        Assert.Empty(held);
    }

    [Fact]
    public void Process_RaisesPageIntentAgainAfterRelease()
    {
        var processor = new GamepadContextIntentProcessor();
        var reading = new GamepadInputReading(
            MoveUp: false,
            MoveDown: false,
            MoveLeft: false,
            MoveRight: false,
            Activate: false,
            Back: false,
            RightTrigger: 0.75);

        _ = processor.Process(reading);
        var released = processor.Process(default);
        var pressedAgain = processor.Process(reading);

        Assert.Empty(released);
        Assert.Equal(new[] { GamepadContextIntent.PageDown }, pressedAgain);
    }

    [Fact]
    public void Process_IgnoresTriggerValuesBelowThreshold()
    {
        var processor = new GamepadContextIntentProcessor();
        var reading = new GamepadInputReading(
            MoveUp: false,
            MoveDown: false,
            MoveLeft: false,
            MoveRight: false,
            Activate: false,
            Back: false,
            LeftTrigger: 0.49,
            RightTrigger: 0.49);

        var intents = processor.Process(reading);

        Assert.Empty(intents);
    }
}
