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
    public void WindowsMainPage_BridgesNativeTriggerKeysOnlyForSuppression_NotDirectContextDispatch()
    {
        var code = File.ReadAllText(@"..\..\..\..\..\SalmonEgg\SalmonEgg\Platforms\Windows\MainPage.Windows.cs");

        Assert.Contains("case Windows.System.VirtualKey.GamepadLeftTrigger:", code);
        Assert.Contains("case Windows.System.VirtualKey.GamepadRightTrigger:", code);
        Assert.Contains("RecordNativeGamepadContextIntent(GamepadContextIntent.PageUp);", code);
        Assert.Contains("RecordNativeGamepadContextIntent(GamepadContextIntent.PageDown);", code);
        Assert.DoesNotContain("_virtualGamepadContextIntentDispatcher?.TryDispatch(GamepadContextIntent.PageUp)", code);
        Assert.DoesNotContain("_virtualGamepadContextIntentDispatcher?.TryDispatch(GamepadContextIntent.PageDown)", code);
    }
}
