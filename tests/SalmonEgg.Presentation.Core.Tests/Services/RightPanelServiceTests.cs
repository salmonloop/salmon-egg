using System;
using Xunit;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.ViewModels.Navigation;

namespace SalmonEgg.Presentation.Core.Tests.Services;

public class RightPanelServiceTests
{
    [Fact]
    public void CurrentMode_ShouldChangeStateAndNotify()
    {
        // Arrange
        var service = new RightPanelService();
        var modeChangedCalled = 0;
        service.ModeChanged += (s, e) => modeChangedCalled++;

        // Act
        service.CurrentMode = RightPanelMode.Todo;

        // Assert
        Assert.Equal(RightPanelMode.Todo, service.CurrentMode);
        Assert.Equal(1, modeChangedCalled);
    }

    [Fact]
    public void CurrentMode_ShouldNotNotifyIfValueIsSame()
    {
        // Arrange
        var service = new RightPanelService { CurrentMode = RightPanelMode.Diff };
        var modeChangedCalled = 0;
        service.ModeChanged += (s, e) => modeChangedCalled++;

        // Act
        service.CurrentMode = RightPanelMode.Diff;

        // Assert
        Assert.Equal(RightPanelMode.Diff, service.CurrentMode);
        Assert.Equal(0, modeChangedCalled);
    }
}
