using System;
using System.IO;
using System.Linq;
using FlaUI.Core.AutomationElements;

namespace SalmonEgg.GuiTests.Windows;

public sealed class ChatInputSelectorSmokeTests
{
    [SkippableFact]
    public void StartComposer_LoadsModeSelectorWithoutCrashing()
    {
        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();

        Assert.True(
            WaitUntilActuallyVisible(session, "StartView.ModeSelector", TimeSpan.FromSeconds(6)),
            $"Expected start mode selector to be visible.{Environment.NewLine}{appData.ReadBootLogTail()}");
    }

    [SkippableFact]
    public void ChatComposer_ForExistingSession_UsesVisibleModeSelectorSubsetOnly()
    {
        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData(withContent: true);
        using var session = WindowsGuiAppSession.LaunchFresh();

        var sessionItem = session.FindByAutomationId("MainNav.Session.gui-session-01", TimeSpan.FromSeconds(15));
        session.ActivateElement(sessionItem);

        Assert.True(
            session.WaitUntilOnscreen("ChatView.CurrentSessionTitle", TimeSpan.FromSeconds(10)),
            $"Session navigation did not activate the chat view. Visible=[{string.Join(" | ", session.GetVisibleTexts())}]");

        if (session.TryFindByAutomationId("ChatView.LoadingOverlay", TimeSpan.FromSeconds(2)) is not null)
        {
            Assert.True(
                session.WaitUntilHidden("ChatView.LoadingOverlay", TimeSpan.FromSeconds(10)),
                "Loading overlay did not disappear before validating the chat composer selector subset.");
        }

        Assert.True(
            WaitUntilActuallyVisible(session, "ChatInputArea.ModeSelector", TimeSpan.FromSeconds(6)),
            $"Expected formal chat composer to keep the mode selector visible. Visible=[{string.Join(" | ", session.GetVisibleTexts())}]");

        var agentVisible = WaitUntilActuallyVisible(session, "ChatInputArea.AgentSelector", TimeSpan.FromMilliseconds(800));
        var projectVisible = WaitUntilActuallyVisible(session, "ChatInputArea.ProjectSelector", TimeSpan.FromMilliseconds(800));
        if (agentVisible || projectVisible)
        {
            var screenshotPath = CaptureComposerScreenshot(session, "chat-composer-selector-subset");
            var agentDescriptor = DescribeElement(session, "ChatInputArea.AgentSelector");
            var projectDescriptor = DescribeElement(session, "ChatInputArea.ProjectSelector");
            Assert.Fail(
                $"Expected formal chat composer to hide agent/project selectors. agentVisible={agentVisible} projectVisible={projectVisible}{Environment.NewLine}" +
                $"Agent={agentDescriptor}{Environment.NewLine}" +
                $"Project={projectDescriptor}{Environment.NewLine}" +
                $"VisibleTexts=[{string.Join(" | ", session.GetVisibleTexts())}]{Environment.NewLine}" +
                $"VisibleButtons=[{string.Join(" | ", session.GetVisibleButtons())}]{Environment.NewLine}" +
                $"Screenshot={screenshotPath}{Environment.NewLine}{appData.ReadBootLogTail()}");
        }
    }

    [SkippableFact]
    public void ChatComposer_ForRemoteAcpSessionWithModelConfig_ShowsModelSelector_AndSelectingModelUsesSetConfigOptionOnly()
    {
        GuiTestGate.RequireEnabled();

        using var appData = GuiAppDataScope.CreateDeterministicMockAcpHarnessData(
            new GuiAppDataScope.MockAcpHarnessScenario
            {
                TransportKind = GuiAppDataScope.MockAcpTransportKind.WebSocket,
                InitializeBehavior = GuiAppDataScope.MockAcpInitializeBehavior.Success,
                SessionNewBehavior = GuiAppDataScope.MockAcpSessionNewBehavior.Success,
                CwdAcceptancePolicy = GuiAppDataScope.MockAcpCwdAcceptancePolicy.AcceptAny,
                ModesVariant = GuiAppDataScope.MockAcpModesVariant.Normal,
                IncludeModelConfig = true
            },
            cachedMessageCount: 1,
            replayMessageCount: 6,
            includeLocalConversation: false,
            localMessageCount: 3,
            remoteConversationCount: 1);
        using var session = WindowsGuiAppSession.LaunchFresh();

        Assert.True(
            ActivateSessionAndWaitForChat(session, "MainNav.Session.gui-remote-conversation-01", TimeSpan.FromSeconds(25)),
            $"Remote session navigation did not reach ChatView before validating the model selector. Visible=[{string.Join(" | ", session.GetVisibleTexts())}]{Environment.NewLine}{appData.ReadBootLogTail()}");

        Assert.True(
            WaitUntilActuallyVisibleAndEnabled(session, "ChatInputArea.ModelSelector", TimeSpan.FromSeconds(12)),
            $"Expected ChatInputArea.ModelSelector for remote ACP session with model config. Visible=[{string.Join(" | ", session.GetVisibleTexts())}]{Environment.NewLine}{appData.ReadBootLogTail()}");

        TriggerPreviousComboBoxItemByKeyboard(
            session,
            "ChatInputArea.ModelSelector");

        Assert.True(
            appData.WaitForMockAcpRequestCount(
                method: "session/set_config_option",
                predicate: request => string.Equals(request.SessionId, "gui-remote-session-01", StringComparison.Ordinal)
                    && string.Equals(request.ConfigId, "model", StringComparison.Ordinal)
                    && string.Equals(request.Value, "claude-haiku", StringComparison.Ordinal),
                expectedCount: 1,
                timeout: TimeSpan.FromSeconds(5)),
            $"Expected exactly one session/set_config_option model request.{Environment.NewLine}{appData.ReadLatestMockAcpRequests()}\n{appData.ReadBootLogTail()}");

        Assert.Equal(
            0,
            appData.CountMockAcpRequests("session/set_mode"));
    }

    private static string CaptureComposerScreenshot(WindowsGuiAppSession session, string scenario)
    {
        var captureRoot = Path.Combine(Path.GetTempPath(), "SalmonEgg.GuiTests");
        Directory.CreateDirectory(captureRoot);
        var capturePath = Path.Combine(captureRoot, $"{scenario}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.png");
        session.CaptureMainWindowToFile(capturePath);
        return capturePath;
    }

    private static bool WaitUntilActuallyVisible(WindowsGuiAppSession session, string automationId, TimeSpan timeout)
        => session.WaitUntil(() => IsActuallyVisible(session, automationId), timeout, TimeSpan.FromMilliseconds(120));

    private static bool WaitUntilActuallyVisibleAndEnabled(WindowsGuiAppSession session, string automationId, TimeSpan timeout)
        => session.WaitUntil(() => IsActuallyVisibleAndEnabled(session, automationId), timeout, TimeSpan.FromMilliseconds(120));

    private static bool IsActuallyVisible(WindowsGuiAppSession session, string automationId)
    {
        var element = session.TryFindByAutomationId(automationId, TimeSpan.FromMilliseconds(150));
        return element is not null && HasUsableOnscreenBounds(session, element);
    }

    private static bool IsActuallyVisibleAndEnabled(WindowsGuiAppSession session, string automationId)
    {
        var element = session.TryFindByAutomationId(automationId, TimeSpan.FromMilliseconds(150));
        return element is not null
               && element.IsEnabled
               && HasUsableOnscreenBounds(session, element);
    }

    private static bool ActivateSessionAndWaitForChat(
        WindowsGuiAppSession session,
        string sessionAutomationId,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var sessionItem = session.TryFindByAutomationId(sessionAutomationId, TimeSpan.FromMilliseconds(500));
            if (sessionItem is not null)
            {
                session.ActivateElement(sessionItem);
            }

            if (session.WaitUntil(
                    () => session.TryFindByAutomationId("ChatView.CurrentSessionTitle", TimeSpan.FromMilliseconds(120)) is not null
                          || session.TryFindByAutomationId("ChatView.LoadingOverlayStatus", TimeSpan.FromMilliseconds(120)) is not null
                          || session.TryFindByAutomationId("ChatInputArea.ModelSelector", TimeSpan.FromMilliseconds(120)) is not null,
                    TimeSpan.FromMilliseconds(900),
                    TimeSpan.FromMilliseconds(120)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasUsableOnscreenBounds(WindowsGuiAppSession session, AutomationElement element)
    {
        if (element.IsOffscreen)
        {
            return false;
        }

        var bounds = element.BoundingRectangle;
        var windowBounds = session.MainWindow.BoundingRectangle;
        return bounds.Width > 20
               && bounds.Height > 20
               && bounds.Left >= windowBounds.Left
               && bounds.Top >= windowBounds.Top
               && bounds.Right <= windowBounds.Right
               && bounds.Bottom <= windowBounds.Bottom;
    }

    private static string DescribeElement(WindowsGuiAppSession session, string automationId)
    {
        var element = session.TryFindByAutomationId(automationId, TimeSpan.FromMilliseconds(150));
        if (element is null)
        {
            return "<missing>";
        }

        var bounds = element.BoundingRectangle;
        return $"offscreen={element.IsOffscreen} enabled={element.IsEnabled} bounds={bounds}";
    }

    private static void TriggerPreviousComboBoxItemByKeyboard(
        WindowsGuiAppSession session,
        string selectorAutomationId)
    {
        var selector = session.FindByAutomationId(selectorAutomationId, TimeSpan.FromSeconds(10));
        session.FocusElement(selector);
        session.PressUp();
        session.PressEnter();
    }
}
