using SalmonEgg.Presentation.Core.Services;

namespace SalmonEgg.Presentation.Core.Tests.Services;

public sealed class ApplicationWindowActivityTrackerTests
{
    [Fact]
    public void Attach_FirstWindow_UsesItAsInitialActiveWindow()
    {
        // Arrange
        var tracker = new ApplicationWindowActivityTracker<string>();

        // Act
        var attached = tracker.Attach("main");

        // Assert
        Assert.True(attached);
        Assert.True(tracker.IsActive);
        Assert.Equal("main", tracker.ActiveWindow);
    }

    [Fact]
    public void Attach_AdditionalWindow_DoesNotActivateItWithoutNativeSignal()
    {
        // Arrange
        var tracker = new ApplicationWindowActivityTracker<string>();
        tracker.Attach("main");

        // Act
        tracker.Attach("mini");
        tracker.Deactivate("main");

        // Assert
        Assert.False(tracker.IsActive);
        Assert.Null(tracker.ActiveWindow);
    }

    [Fact]
    public void Deactivate_OneOfTwoActiveWindows_KeepsApplicationActive()
    {
        // Arrange
        var tracker = new ApplicationWindowActivityTracker<string>();
        tracker.Attach("main");
        tracker.Attach("mini");
        tracker.Activate("mini");

        // Act
        tracker.Deactivate("mini");

        // Assert
        Assert.True(tracker.IsActive);
        Assert.Equal("main", tracker.ActiveWindow);
    }

    [Fact]
    public void Detach_CurrentWindow_SelectsAnotherActiveWindow()
    {
        // Arrange
        var tracker = new ApplicationWindowActivityTracker<string>();
        tracker.Attach("main");
        tracker.Attach("mini");
        tracker.Activate("mini");

        // Act
        var detached = tracker.Detach("mini");

        // Assert
        Assert.True(detached);
        Assert.True(tracker.IsActive);
        Assert.Equal("main", tracker.ActiveWindow);
    }

    [Fact]
    public void Detach_LastActiveWindow_MarksApplicationInactive()
    {
        // Arrange
        var tracker = new ApplicationWindowActivityTracker<string>();
        tracker.Attach("main");

        // Act
        tracker.Detach("main");

        // Assert
        Assert.False(tracker.IsActive);
        Assert.Null(tracker.ActiveWindow);
    }

    [Fact]
    public void Activate_UnattachedWindow_DoesNotChangeState()
    {
        // Arrange
        var tracker = new ApplicationWindowActivityTracker<string>();

        // Act
        var activated = tracker.Activate("unknown");

        // Assert
        Assert.False(activated);
        Assert.False(tracker.IsActive);
        Assert.Null(tracker.ActiveWindow);
    }
}
