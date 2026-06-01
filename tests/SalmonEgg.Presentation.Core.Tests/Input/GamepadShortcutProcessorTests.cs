using SalmonEgg.Presentation.Core.Services.Input;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Input;

public sealed class GamepadShortcutProcessorTests
{
    [Fact]
    public void Process_RaisesShortcutOnInitialPressOnly()
    {
        var processor = new GamepadShortcutProcessor();
        var reading = new GamepadInputReading(
            MoveUp: false,
            MoveDown: false,
            MoveLeft: false,
            MoveRight: false,
            Activate: false,
            Back: false,
            ShortcutVoiceToggle: true);

        var first = processor.Process(reading);
        var held = processor.Process(reading);

        Assert.Equal(new[] { GamepadShortcutIntent.ToggleVoiceInput }, first);
        Assert.Empty(held);
    }

    [Fact]
    public void Process_RaisesShortcutAgainAfterRelease()
    {
        var processor = new GamepadShortcutProcessor();
        var reading = new GamepadInputReading(
            MoveUp: false,
            MoveDown: false,
            MoveLeft: false,
            MoveRight: false,
            Activate: false,
            Back: false,
            ShortcutVoiceToggle: true);

        _ = processor.Process(reading);
        var released = processor.Process(default);
        var pressedAgain = processor.Process(reading);

        Assert.Empty(released);
        Assert.Equal(new[] { GamepadShortcutIntent.ToggleVoiceInput }, pressedAgain);
    }

    [Fact]
    public void Reset_ClearsPressedShortcutState()
    {
        var processor = new GamepadShortcutProcessor();
        var reading = new GamepadInputReading(
            MoveUp: false,
            MoveDown: false,
            MoveLeft: false,
            MoveRight: false,
            Activate: false,
            Back: false,
            ShortcutVoiceToggle: true);

        _ = processor.Process(reading);
        processor.Reset();
        var afterReset = processor.Process(reading);

        Assert.Equal(new[] { GamepadShortcutIntent.ToggleVoiceInput }, afterReset);
    }
}
