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
        var service = TestSourceFiles.ReadAllText(
            @"SalmonEgg\SalmonEgg\Presentation\Services\Input\WindowsGamepadInputService.cs");
        var mapper = TestSourceFiles.ReadAllText(
            @"SalmonEgg\SalmonEgg\Presentation\Services\Input\WindowsStandardGamepadReadingMapper.cs");

        // Triggers are facts in the shared Windows mapper; Core pipeline owns context intent processing.
        Assert.Contains("ContextIntentRaised", service, StringComparison.Ordinal);
        Assert.Contains("WindowsStandardGamepadReadingMapper.GetInputReading", service, StringComparison.Ordinal);
        Assert.Contains("GamepadReadingPipeline", service, StringComparison.Ordinal);
        Assert.Contains("leftTrigger: reading.LeftTrigger", mapper, StringComparison.Ordinal);
        Assert.Contains("rightTrigger: reading.RightTrigger", mapper, StringComparison.Ordinal);
        Assert.DoesNotContain("new GamepadContextIntentProcessor", service, StringComparison.Ordinal);
        Assert.DoesNotContain("GamepadNavigationIntent.PageDown", service, StringComparison.Ordinal);
        Assert.DoesNotContain("GamepadNavigationIntent.PageDown", mapper, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowsMainPage_DoesNotBridgeNativeTriggerKeysThroughShellContextDispatcher()
    {
        var code = TestSourceFiles.ReadAllText(
            @"SalmonEgg\SalmonEgg\Platforms\Windows\MainPage.Windows.cs");

        Assert.DoesNotContain("case Windows.System.VirtualKey.GamepadLeftTrigger:", code);
        Assert.DoesNotContain("case Windows.System.VirtualKey.GamepadRightTrigger:", code);
        Assert.DoesNotContain("RecordNativeGamepadContextIntent", code);
        Assert.DoesNotContain("TryDispatchNativeGamepadContextIntent", code);
        Assert.DoesNotContain("_virtualGamepadContextIntentDispatcher", code);
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
