using System;
using System.Linq;
using System.Threading;

namespace SalmonEgg.GuiTests.Windows;

public sealed class ChatInputSelectorSmokeTests
{
    [SkippableFact]
    public void StartComposer_WhenModeHasNoAuthoritativeOptions_ShowsDefaultPlaceholder()
    {
        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();

        Assert.True(
            session.WaitUntilOnscreen("StartView.ModeSelector", TimeSpan.FromSeconds(6)),
            $"Expected start mode selector to be visible.{Environment.NewLine}{appData.ReadBootLogTail()}");

        Assert.True(
            WaitUntilVisibleText(session, "Default mode", TimeSpan.FromSeconds(5)),
            $"Expected start mode selector to project the default placeholder instead of a blank selection. Visible=[{string.Join(" | ", session.GetVisibleTexts())}]{Environment.NewLine}{appData.ReadBootLogTail()}");
    }

    [SkippableFact]
    public void ChatComposer_ForExistingSession_UsesModeSelectorSubsetOnly()
    {
        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData(withContent: true);
        using var session = WindowsGuiAppSession.LaunchFresh();

        var sessionItem = session.FindByAutomationId("MainNav.Session.gui-session-01", TimeSpan.FromSeconds(15));
        session.ActivateElement(sessionItem);

        if (session.TryFindByAutomationId("ChatView.LoadingOverlay", TimeSpan.FromSeconds(2)) is not null)
        {
            Assert.True(
                session.WaitUntilHidden("ChatView.LoadingOverlay", TimeSpan.FromSeconds(10)),
                "Loading overlay did not disappear before validating the chat composer selector subset.");
        }

        Assert.True(
            session.WaitUntilOnscreen("ChatInputArea.ModeSelector", TimeSpan.FromSeconds(6)),
            $"Expected formal chat composer to keep the mode selector visible. Visible=[{string.Join(" | ", session.GetVisibleTexts())}]");
        Assert.Null(session.TryFindByAutomationId("ChatInputArea.AgentSelector", TimeSpan.FromMilliseconds(300)));
        Assert.Null(session.TryFindByAutomationId("ChatInputArea.ProjectSelector", TimeSpan.FromMilliseconds(300)));

        Assert.True(
            WaitUntilVisibleText(session, "Default mode", TimeSpan.FromSeconds(5)),
            $"Expected formal chat mode selector to project the same placeholder subset policy as the start composer. Visible=[{string.Join(" | ", session.GetVisibleTexts())}]{Environment.NewLine}{appData.ReadBootLogTail()}");
    }

    private static bool WaitUntilVisibleText(
        WindowsGuiAppSession session,
        string expectedText,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (session.GetVisibleTexts().Any(text => string.Equals(text, expectedText, StringComparison.Ordinal)))
            {
                return true;
            }

            Thread.Sleep(120);
        }

        return false;
    }
}
