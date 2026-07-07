using SalmonEgg.Presentation.Core.Tests;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Input;

public sealed class GamepadContextIntentDispatcherSourceTests
{
    [Fact]
    public void MainShellContextDispatcher_UsesFocusedAncestorConsumerPattern()
    {
        var code = TestSourceFiles.ReadAllText(
            @"SalmonEgg\SalmonEgg\Presentation\Services\Input\MainShellGamepadContextIntentDispatcher.cs");

        Assert.Contains("IGamepadContextIntentConsumer", code);
        Assert.Contains("TryConsumeContextIntent", code);
        Assert.DoesNotContain("TryMoveFocus", code);
        Assert.DoesNotContain("AutomationPeer", code);
    }

    [Fact]
    public void WindowsGamepadInputService_MapsTriggersToContextIntentEvents()
    {
        var code = TestSourceFiles.ReadAllText(
            @"SalmonEgg\SalmonEgg\Presentation\Services\Input\WindowsGamepadInputService.cs");

        Assert.Contains("GamepadContextIntentProcessor", code);
        Assert.Contains("ContextIntentRaised", code);
        Assert.Contains("StandardGamepadInputReadingMapper.GetInputReading", code);
        Assert.Contains("leftTrigger: reading.LeftTrigger", code);
        Assert.Contains("rightTrigger: reading.RightTrigger", code);
        Assert.DoesNotContain("GamepadNavigationIntent.PageDown", code);
    }

    [Fact]
    public void WindowsMainPage_BridgesNativeTriggerKeysThroughContextDispatcher()
    {
        var code = TestSourceFiles.ReadAllText(
            @"SalmonEgg\SalmonEgg\Platforms\Windows\MainPage.Windows.cs");

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
        var code = TestSourceFiles.ReadAllText(
            @"SalmonEgg\SalmonEgg\Presentation\Views\SettingsPageBase.cs");

        Assert.Contains("IGamepadContextIntentConsumer", code);
        Assert.Contains("TryConsumeContextIntent", code);
        Assert.Contains("TryScrollByPage", code);
    }

    [Fact]
    public void MainShellContextDispatcher_RetriesFromRootContentWhenFocusedElementIsNotConsumable()
    {
        var code = TestSourceFiles.ReadAllText(
            @"SalmonEgg\SalmonEgg\Presentation\Services\Input\MainShellGamepadContextIntentDispatcher.cs");

        Assert.Contains("TryDispatchFromRoot(_focusScope.GetFocusedElement(), intent)", code, System.StringComparison.Ordinal);
        Assert.Contains("TryDispatchFromRoot(_focusScope.GetCurrentRootContent(), intent)", code, System.StringComparison.Ordinal);
        Assert.Contains(
            "Main shell gamepad context intent was retried from current root content after focused dispatch miss",
            code,
            System.StringComparison.Ordinal);
    }

    [Fact]
    public void GamepadShellSmokeTests_CoverTranscriptTriggerFocusDomainIsolation()
    {
        var code = TestSourceFiles.ReadAllText(
            @"tests\SalmonEgg.GuiTests.Windows\GamepadShellSmokeTests.cs");

        Assert.Contains(
            "ChatTranscriptViewport_VirtualGamepadLeftTrigger_CanPageUpTranscript",
            code,
            System.StringComparison.Ordinal);
        Assert.Contains(
            "ChatInputBox_AfterTranscriptTrigger_VirtualGamepadLeftTrigger_DoesNotStealFocusOrScrollTranscript",
            code,
            System.StringComparison.Ordinal);
        Assert.Contains("session.PressVirtualGamepadLeftTrigger();", code, System.StringComparison.Ordinal);
        Assert.Contains("session.IsFocusWithinAutomationId(\"InputBox\")", code, System.StringComparison.Ordinal);
        Assert.Contains("Left trigger scrolled the transcript while chat input focus was active.", code, System.StringComparison.Ordinal);
    }

    [Fact]
    public void GamepadContextIntentDispatcher_DelegatesToShellFocusScope()
    {
        var code = TestSourceFiles.ReadAllText(
            @"SalmonEgg\SalmonEgg\Presentation\Services\Input\MainShellGamepadContextIntentDispatcher.cs");

        Assert.Contains("IShellFocusScope", code, System.StringComparison.Ordinal);
        Assert.Contains("_focusScope.GetFocusedElement()", code, System.StringComparison.Ordinal);
        Assert.Contains("_focusScope.GetCurrentRootContent()", code, System.StringComparison.Ordinal);
        Assert.Contains("_focusScope.EnumerateAncestors(", code, System.StringComparison.Ordinal);
        Assert.DoesNotContain("App.MainWindowInstance", code, System.StringComparison.Ordinal);
        Assert.DoesNotContain("XamlFocusManager.GetFocusedElement", code, System.StringComparison.Ordinal);
        Assert.DoesNotContain("VisualTreeHelper.GetParent", code, System.StringComparison.Ordinal);
    }

    [Fact]
    public void ShellFocusScope_IsTheSoleOwnerOfWindowFocusPlumbing()
    {
        var scope = TestSourceFiles.ReadAllText(
            @"SalmonEgg\SalmonEgg\Presentation\Services\Input\MainShellFocusScope.cs");
        var navDispatcher = TestSourceFiles.ReadAllText(
            @"SalmonEgg\SalmonEgg\Presentation\Services\Input\MainShellGamepadNavigationDispatcher.cs");
        var ctxDispatcher = TestSourceFiles.ReadAllText(
            @"SalmonEgg\SalmonEgg\Presentation\Services\Input\MainShellGamepadContextIntentDispatcher.cs");
        var scDispatcher = TestSourceFiles.ReadAllText(
            @"SalmonEgg\SalmonEgg\Presentation\Services\Input\MainShellGamepadShortcutDispatcher.cs");

        Assert.Contains("App.MainWindowInstance", scope, System.StringComparison.Ordinal);
        Assert.Contains("XamlFocusManager.GetFocusedElement", scope, System.StringComparison.Ordinal);
        Assert.Contains("VisualTreeHelper.GetParent", scope, System.StringComparison.Ordinal);

        foreach (var dispatcher in new[] { navDispatcher, ctxDispatcher, scDispatcher })
        {
            Assert.DoesNotContain("App.MainWindowInstance", dispatcher, System.StringComparison.Ordinal);
            Assert.DoesNotContain("XamlFocusManager.GetFocusedElement", dispatcher, System.StringComparison.Ordinal);
            Assert.DoesNotContain("VisualTreeHelper.GetParent", dispatcher, System.StringComparison.Ordinal);
        }
    }
}
