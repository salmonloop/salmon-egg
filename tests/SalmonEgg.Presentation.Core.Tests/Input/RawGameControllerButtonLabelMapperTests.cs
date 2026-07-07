using SalmonEgg.Presentation.Core.Services.Input;

namespace SalmonEgg.Presentation.Core.Tests.Input;

public sealed class RawGameControllerButtonLabelMapperTests
{
    [Theory]
    [InlineData(RawGameControllerButtonLabel.XboxUp, GamepadNavigationIntent.MoveUp)]
    [InlineData(RawGameControllerButtonLabel.Up, GamepadNavigationIntent.MoveUp)]
    [InlineData(RawGameControllerButtonLabel.XboxDown, GamepadNavigationIntent.MoveDown)]
    [InlineData(RawGameControllerButtonLabel.Down, GamepadNavigationIntent.MoveDown)]
    [InlineData(RawGameControllerButtonLabel.XboxLeft, GamepadNavigationIntent.MoveLeft)]
    [InlineData(RawGameControllerButtonLabel.Left, GamepadNavigationIntent.MoveLeft)]
    [InlineData(RawGameControllerButtonLabel.XboxRight, GamepadNavigationIntent.MoveRight)]
    [InlineData(RawGameControllerButtonLabel.Right, GamepadNavigationIntent.MoveRight)]
    [InlineData(RawGameControllerButtonLabel.XboxA, GamepadNavigationIntent.Activate)]
    [InlineData(RawGameControllerButtonLabel.Cross, GamepadNavigationIntent.Activate)]
    [InlineData(RawGameControllerButtonLabel.LetterA, GamepadNavigationIntent.Activate)]
    [InlineData(RawGameControllerButtonLabel.XboxB, GamepadNavigationIntent.Back)]
    [InlineData(RawGameControllerButtonLabel.Circle, GamepadNavigationIntent.Back)]
    [InlineData(RawGameControllerButtonLabel.LetterB, GamepadNavigationIntent.Back)]
    [InlineData(RawGameControllerButtonLabel.Back, GamepadNavigationIntent.Back)]
    public void Apply_MapsNavigationButtonLabelsToIntent(
        RawGameControllerButtonLabel label,
        GamepadNavigationIntent expected)
    {
        var reading = RawGameControllerButtonLabelMapper.Apply(label, default);

        Assert.Equal([expected], GamepadIntentProcessor.GetActiveIntents(reading));
        Assert.False(reading.ShortcutVoiceToggle);
        Assert.Empty(GamepadContextIntentProjector.GetActiveIntents(reading));
    }

    [Theory]
    [InlineData(RawGameControllerButtonLabel.XboxY)]
    [InlineData(RawGameControllerButtonLabel.Triangle)]
    [InlineData(RawGameControllerButtonLabel.LetterY)]
    public void Apply_MapsVoiceShortcutButtonLabelsWithoutNavigation(
        RawGameControllerButtonLabel label)
    {
        var reading = RawGameControllerButtonLabelMapper.Apply(label, default);

        Assert.True(reading.ShortcutVoiceToggle);
        Assert.Empty(GamepadIntentProcessor.GetActiveIntents(reading));
        Assert.Equal([GamepadShortcutIntent.ToggleVoiceInput], GamepadShortcutIntentProjector.GetActiveShortcuts(reading));
    }

    [Theory]
    [InlineData(RawGameControllerButtonLabel.XboxLeftTrigger, GamepadContextIntent.PageUp)]
    [InlineData(RawGameControllerButtonLabel.LeftTrigger, GamepadContextIntent.PageUp)]
    [InlineData(RawGameControllerButtonLabel.XboxRightTrigger, GamepadContextIntent.PageDown)]
    [InlineData(RawGameControllerButtonLabel.RightTrigger, GamepadContextIntent.PageDown)]
    public void Apply_MapsTriggerLabelsToContextIntentWithoutNavigation(
        RawGameControllerButtonLabel label,
        GamepadContextIntent expected)
    {
        var reading = RawGameControllerButtonLabelMapper.Apply(label, default);

        Assert.Equal([expected], GamepadContextIntentProjector.GetActiveIntents(reading));
        Assert.Empty(GamepadIntentProcessor.GetActiveIntents(reading));
    }

    [Fact]
    public void Apply_NoneDoesNotChangeReading()
    {
        var seed = new GamepadInputReading(
            MoveUp: false,
            MoveDown: false,
            MoveLeft: true,
            MoveRight: false,
            Activate: false,
            Back: false);

        var reading = RawGameControllerButtonLabelMapper.Apply(RawGameControllerButtonLabel.None, seed);

        Assert.Equal(seed, reading);
    }
}
