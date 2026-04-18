using System;
using SalmonEgg.Presentation.Core.Services.Input;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Input;

public sealed class GamepadNavigationIntentTests
{
    [Fact]
    public void GamepadNavigationIntent_DefinesShellSafeDirectionsAndActions()
    {
        var values = Enum.GetNames(typeof(GamepadNavigationIntent));

        Assert.Equal(
            new[] { "MoveUp", "MoveDown", "MoveLeft", "MoveRight", "Activate", "Back" },
            values);
    }
}
