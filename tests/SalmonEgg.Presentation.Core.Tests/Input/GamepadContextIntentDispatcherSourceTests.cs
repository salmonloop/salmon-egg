using System.IO;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Input;

public sealed class GamepadContextIntentDispatcherSourceTests
{
    [Fact]
    public void MainShellContextDispatcher_UsesFocusedAncestorConsumerPattern()
    {
        var code = File.ReadAllText(@"..\..\..\..\..\SalmonEgg\SalmonEgg\Presentation\Services\Input\MainShellGamepadContextIntentDispatcher.cs");

        Assert.Contains("IGamepadContextIntentConsumer", code);
        Assert.Contains("TryConsumeContextIntent", code);
        Assert.DoesNotContain("TryMoveFocus", code);
        Assert.DoesNotContain("AutomationPeer", code);
    }

    [Fact]
    public void WindowsGamepadInputService_MapsTriggersToContextIntentEvents()
    {
        var code = File.ReadAllText(@"..\..\..\..\..\SalmonEgg\SalmonEgg\Presentation\Services\Input\WindowsGamepadInputService.cs");

        Assert.Contains("GamepadContextIntentProcessor", code);
        Assert.Contains("ContextIntentRaised", code);
        Assert.Contains("LeftTrigger: reading.LeftTrigger", code);
        Assert.Contains("RightTrigger: reading.RightTrigger", code);
        Assert.DoesNotContain("GamepadNavigationIntent.PageDown", code);
    }

    [Fact]
    public void WindowsMainPage_BridgesNativeTriggerKeysThroughContextDispatcher()
    {
        var code = File.ReadAllText(@"..\..\..\..\..\SalmonEgg\SalmonEgg\Platforms\Windows\MainPage.Windows.cs");

        Assert.Contains("case Windows.System.VirtualKey.GamepadLeftTrigger:", code);
        Assert.Contains("case Windows.System.VirtualKey.GamepadRightTrigger:", code);
        Assert.Contains("RecordNativeGamepadContextIntent(GamepadContextIntent.PageUp);", code);
        Assert.Contains("RecordNativeGamepadContextIntent(GamepadContextIntent.PageDown);", code);
        Assert.Contains("TryDispatchNativeGamepadContextIntent(GamepadContextIntent.PageUp)", code);
        Assert.Contains("TryDispatchNativeGamepadContextIntent(GamepadContextIntent.PageDown)", code);
        Assert.Contains("_virtualGamepadContextIntentDispatcher.TryDispatch(intent);", code);
    }

    [Fact]
    public void SettingsPageBase_ImplementsContextIntentConsumer()
    {
        var code = File.ReadAllText(@"..\..\..\..\..\SalmonEgg\SalmonEgg\Presentation\Views\SettingsPageBase.cs");

        Assert.Contains("IGamepadContextIntentConsumer", code);
        Assert.Contains("TryConsumeContextIntent", code);
        Assert.Contains("TryScrollByPage", code);
    }

    [Fact]
    public void MainShellContextDispatcher_RetriesFromRootContentWhenFocusedElementIsNotConsumable()
    {
        var code = File.ReadAllText(
            @"..\..\..\..\..\SalmonEgg\SalmonEgg\Presentation\Services\Input\MainShellGamepadContextIntentDispatcher.cs");

        Assert.Contains("TryDispatchFromRoot(focused, intent)", code, System.StringComparison.Ordinal);
        Assert.Contains("TryDispatchFromRoot(GetCurrentRootContent(), intent)", code, System.StringComparison.Ordinal);
        Assert.Contains(
            "Main shell gamepad context intent was retried from current root content after focused dispatch miss",
            code,
            System.StringComparison.Ordinal);
    }
}
