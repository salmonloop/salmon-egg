using System.IO;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Input;

public sealed class GamepadShortcutDispatcherSourceTests
{
    [Fact]
    public void MainShellShortcutDispatcher_UsesFocusedAncestorShortcutConsumerPattern()
    {
        var code = File.ReadAllText("../../../../../SalmonEgg/SalmonEgg/Presentation/Services/Input/MainShellGamepadShortcutDispatcher.cs");

        Assert.Contains("IGamepadShortcutConsumer", code);
        Assert.Contains("TryConsumeShortcutIntent", code);
        Assert.DoesNotContain("TryMoveFocus", code);
        Assert.DoesNotContain("AutomationPeer", code);
    }

    [Fact]
    public void WindowsGamepadInputService_MapsYToShortcutEvent_NotNavigationIntent()
    {
        var code = File.ReadAllText("../../../../../SalmonEgg/SalmonEgg/Presentation/Services/Input/WindowsGamepadInputService.cs");

        Assert.Contains("GamepadButtons.Y", code);
        Assert.Contains("ShortcutRaised", code);
        Assert.DoesNotContain("GamepadNavigationIntent.ToggleVoiceInput", code);
    }
}
