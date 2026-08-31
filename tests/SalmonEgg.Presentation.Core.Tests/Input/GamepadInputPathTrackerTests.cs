using SalmonEgg.Presentation.Core.Services.Input;

namespace SalmonEgg.Presentation.Core.Tests.Input;

public sealed class GamepadInputPathTrackerTests
{
    [Fact]
    public void Apply_TransitionsFromIdleToGamepad_WhenActiveReadingUsesStandardGamepad()
    {
        var tracker = new GamepadInputPathTracker();

        var transition = tracker.Apply(hasActiveReading: true, GamepadInputPath.Gamepad);

        Assert.True(transition.Changed);
        Assert.Equal(GamepadInputPath.Gamepad, transition.Path);
        Assert.Equal(GamepadInputPath.Gamepad, tracker.CurrentPath);
    }

    [Fact]
    public void Apply_DoesNotReportChange_WhenPathRemainsTheSame()
    {
        var tracker = new GamepadInputPathTracker();
        _ = tracker.Apply(hasActiveReading: true, GamepadInputPath.RawGameController);

        var transition = tracker.Apply(hasActiveReading: true, GamepadInputPath.RawGameController);

        Assert.False(transition.Changed);
        Assert.Equal(GamepadInputPath.RawGameController, transition.Path);
        Assert.Equal(GamepadInputPath.RawGameController, tracker.CurrentPath);
    }

    [Fact]
    public void Apply_ResetsToIdle_WhenNoActiveReadingIsPresent()
    {
        var tracker = new GamepadInputPathTracker();
        _ = tracker.Apply(hasActiveReading: true, GamepadInputPath.Gamepad);

        var transition = tracker.Apply(hasActiveReading: false, GamepadInputPath.None);

        Assert.True(transition.Changed);
        Assert.Equal(GamepadInputPath.None, transition.Path);
        Assert.Equal(GamepadInputPath.None, tracker.CurrentPath);
    }

    [Fact]
    public void Reset_TransitionsBackToIdle_WhenPreviousPathWasActive()
    {
        var tracker = new GamepadInputPathTracker();
        _ = tracker.Apply(hasActiveReading: true, GamepadInputPath.RawGameController);

        var transition = tracker.Reset();

        Assert.True(transition.Changed);
        Assert.Equal(GamepadInputPath.None, transition.Path);
        Assert.Equal(GamepadInputPath.None, tracker.CurrentPath);
    }

    [Fact]
    public void Apply_TransitionsFromGamepadToRaw_WhenActivePathFlips()
    {
        // Dual-enumeration hosts can flip authoritative path when standard goes idle
        // and raw remains active (common DualSense/Switch raw-only face paths).
        var tracker = new GamepadInputPathTracker();
        _ = tracker.Apply(hasActiveReading: true, GamepadInputPath.Gamepad);

        var transition = tracker.Apply(hasActiveReading: true, GamepadInputPath.RawGameController);

        Assert.True(transition.Changed);
        Assert.Equal(GamepadInputPath.RawGameController, transition.Path);
        Assert.Equal(GamepadInputPath.RawGameController, tracker.CurrentPath);
    }
}
