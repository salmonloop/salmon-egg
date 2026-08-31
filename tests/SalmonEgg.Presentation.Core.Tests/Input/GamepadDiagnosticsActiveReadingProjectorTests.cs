using SalmonEgg.Presentation.Core.Services.Input;

namespace SalmonEgg.Presentation.Core.Tests.Input;

public sealed class GamepadDiagnosticsActiveReadingProjectorTests
{
    [Fact]
    public void Project_WhenNoActiveReading_ReturnsNoneProjection()
    {
        var projection = GamepadDiagnosticsActiveReadingProjector.Project(
            [default(GamepadInputReading)],
            [default(GamepadInputReading)]);

        Assert.Equal(GamepadDiagnosticsInputSource.None, projection.InputSource);
        Assert.Equal(default, projection.Reading);
        Assert.Empty(projection.ActiveIntents);
        Assert.Empty(projection.ActiveContextIntents);
        Assert.Empty(projection.ActiveShortcuts);
    }

    [Fact]
    public void Project_PrefersStandardOverRaw_AndProjectsActiveIntents()
    {
        var standard = new GamepadInputReading(
            MoveUp: false,
            MoveDown: true,
            MoveLeft: false,
            MoveRight: false,
            Activate: false,
            Back: false,
            LeftTrigger: 1);
        var raw = new GamepadInputReading(
            MoveUp: false,
            MoveDown: false,
            MoveLeft: false,
            MoveRight: false,
            Activate: true,
            Back: false);

        var projection = GamepadDiagnosticsActiveReadingProjector.Project([standard], [raw]);

        Assert.Equal(GamepadDiagnosticsInputSource.Gamepad, projection.InputSource);
        Assert.Equal(standard, projection.Reading);
        Assert.Contains(GamepadNavigationIntent.MoveDown, projection.ActiveIntents);
        Assert.Contains(GamepadContextIntent.PageUp, projection.ActiveContextIntents);
        Assert.DoesNotContain(GamepadNavigationIntent.Activate, projection.ActiveIntents);
    }

    [Fact]
    public void Project_FallsBackToRaw_WhenStandardIsIdle()
    {
        var raw = new GamepadInputReading(
            MoveUp: false,
            MoveDown: false,
            MoveLeft: false,
            MoveRight: false,
            Activate: true,
            Back: false);

        var projection = GamepadDiagnosticsActiveReadingProjector.Project([default], [raw]);

        Assert.Equal(GamepadDiagnosticsInputSource.RawGameController, projection.InputSource);
        Assert.Equal(raw, projection.Reading);
        Assert.Contains(GamepadNavigationIntent.Activate, projection.ActiveIntents);
    }

    [Fact]
    public void ToInputSource_MapsDualPathTokens()
    {
        Assert.Equal(
            GamepadDiagnosticsInputSource.Gamepad,
            GamepadDiagnosticsActiveReadingProjector.ToInputSource(GamepadInputPath.Gamepad));
        Assert.Equal(
            GamepadDiagnosticsInputSource.RawGameController,
            GamepadDiagnosticsActiveReadingProjector.ToInputSource(GamepadInputPath.RawGameController));
        Assert.Equal(
            GamepadDiagnosticsInputSource.None,
            GamepadDiagnosticsActiveReadingProjector.ToInputSource(GamepadInputPath.None));
    }
}
