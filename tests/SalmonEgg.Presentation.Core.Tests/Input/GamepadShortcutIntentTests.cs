using System;
using SalmonEgg.Presentation.Core.Services.Input;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Input;

public sealed class GamepadShortcutIntentTests
{
    [Fact]
    public void GamepadShortcutIntent_StartsWithToggleVoiceInputOnly()
    {
        Assert.Equal(
            new[] { "ToggleVoiceInput" },
            Enum.GetNames(typeof(GamepadShortcutIntent)));
    }
}
