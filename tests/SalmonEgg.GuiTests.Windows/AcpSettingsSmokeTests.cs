using System;
using System.IO;
using FlaUI.Core.Definitions;
using Xunit.Sdk;

namespace SalmonEgg.GuiTests.Windows;

public sealed class AcpSettingsSmokeTests
{
    [Fact]
    public void AcpSettingsPage_OpensProfilesManagementByDefault()
    {
        GuiAcceptanceDiagnostics.Record("ACP settings: creating AppData");
        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        GuiAcceptanceDiagnostics.Record("ACP settings: launching current package");
        using var session = WindowsGuiAppSession.LaunchFresh();
        GuiAcceptanceDiagnostics.Record("ACP settings: window attached");
        MaximizeMainWindow(session);
        NavigateToAcpSettings(session);
        GuiAcceptanceDiagnostics.Record("ACP settings: navigation completed");

        Assert.True(
            session.WaitUntilOnscreen("Acp.Profiles.Refresh", TimeSpan.FromSeconds(10)),
            $"ACP profiles management did not become visible.{Environment.NewLine}{appData.ReadBootLogTail()}");
    }

    private static void NavigateToAcpSettings(WindowsGuiAppSession session)
    {
        var settingsItem = session.FindByAutomationId("SettingsItem", TimeSpan.FromSeconds(10));
        session.ClickElement(settingsItem);

        var acpSettingsItem = session.TryFindByAutomationId("SettingsNav.AgentAcp", TimeSpan.FromSeconds(10))
            ?? session.TryFindVisibleElementByNameAnywhere("Agent (ACP)", TimeSpan.FromSeconds(10));

        if (acpSettingsItem is null)
        {
            var captureRoot = Path.Combine(Path.GetTempPath(), "SalmonEgg.GuiTests");
            Directory.CreateDirectory(captureRoot);
            var capturePath = Path.Combine(captureRoot, $"settings-acp-entry-missing-{DateTime.UtcNow:yyyyMMddHHmmssfff}.png");
            session.CaptureMainWindowToFile(capturePath);
            var visibleTexts = string.Join(", ", session.GetVisibleTexts());
            throw new XunitException(
                $"Agent (ACP) settings entry did not become visible after opening settings.{Environment.NewLine}" +
                $"Screenshot: {capturePath}{Environment.NewLine}" +
                $"Visible texts: [{visibleTexts}]");
        }

        session.ActivateElement(acpSettingsItem!);

        Assert.True(
            session.WaitUntilOnscreen("Acp.Profiles.Refresh", TimeSpan.FromSeconds(10)),
            "ACP profiles section did not become visible.");
    }

    private static void MaximizeMainWindow(WindowsGuiAppSession session)
    {
        try
        {
            if (session.MainWindow.Patterns.Window.IsSupported)
            {
                session.MainWindow.Patterns.Window.Pattern.SetWindowVisualState(WindowVisualState.Maximized);
            }
        }
        catch
        {
        }
        // A hosted desktop may be smaller than 1400x900. ACP navigation is the contract here;
        // its native onscreen controls below must work at the window size Windows actually permits.
    }
}
