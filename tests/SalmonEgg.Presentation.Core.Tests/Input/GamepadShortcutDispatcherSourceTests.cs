using SalmonEgg.Presentation.Core.Tests;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Input;

public sealed class GamepadShortcutDispatcherSourceTests
{
    [Fact]
    public void MainShellShortcutDispatcher_UsesFocusedAncestorShortcutConsumerPattern()
    {
        var code = TestSourceFiles.ReadAllText(
            @"SalmonEgg\SalmonEgg\Presentation\Services\Input\MainShellGamepadShortcutDispatcher.cs");

        Assert.Contains("IGamepadShortcutConsumer", code);
        Assert.Contains("TryConsumeShortcutIntent", code);
        Assert.DoesNotContain("TryMoveFocus", code);
        Assert.DoesNotContain("AutomationPeer", code);
    }

    [Fact]
    public void WindowsGamepadInputService_MapsYToShortcutEvent_NotNavigationIntent()
    {
        var service = TestSourceFiles.ReadAllText(
            @"SalmonEgg\SalmonEgg\Presentation\Services\Input\WindowsGamepadInputService.cs");
        var mapper = TestSourceFiles.ReadAllText(
            @"SalmonEgg\SalmonEgg\Presentation\Services\Input\WindowsStandardGamepadReadingMapper.cs");

        // Platform host raises shortcut events from Core pipeline frames; WGI Y → faceY is shared mapper only.
        Assert.Contains("WindowsStandardGamepadReadingMapper.GetInputReading", service, StringComparison.Ordinal);
        Assert.Contains("ShortcutRaised", service, StringComparison.Ordinal);
        Assert.Contains("faceYPressed: reading.Buttons.HasFlag(GamepadButtons.Y)", mapper, StringComparison.Ordinal);
        Assert.DoesNotContain("GamepadNavigationIntent.ToggleVoiceInput", service, StringComparison.Ordinal);
        Assert.DoesNotContain("GamepadNavigationIntent.ToggleVoiceInput", mapper, StringComparison.Ordinal);
    }
}
