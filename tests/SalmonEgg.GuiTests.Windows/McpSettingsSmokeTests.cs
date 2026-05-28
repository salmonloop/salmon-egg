using System;
using System.IO;
using FlaUI.Core.Definitions;
using Xunit.Sdk;

namespace SalmonEgg.GuiTests.Windows;

public sealed class McpSettingsSmokeTests
{
    [SkippableFact]
    public void PerServerEnabledToggle_PersistsWhenLeavingAndReturningToSettingsPage()
    {
        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        appData.WriteMcpYaml(
            """
            schema_version: 1
            servers:
            - transport: http
              name: search
              enabled: false
              url: https://example.com/mcp
            """);
        using var session = WindowsGuiAppSession.LaunchFresh();

        EnsureMainWindowWide(session);
        NavigateToMcpSettings(session);

        var toggle = session.FindByAutomationId("Mcp.Server.Enabled", TimeSpan.FromSeconds(10));
        Assert.Equal(ToggleState.Off, toggle.Patterns.Toggle.Pattern.ToggleState.Value);

        session.ClickElement(toggle);

        Assert.True(
            session.WaitUntil(
                () => appData.ReadMcpYaml().Contains("enabled: true", StringComparison.Ordinal),
                TimeSpan.FromSeconds(5)),
            $"MCP service toggle did not persist true to mcp.yaml.{Environment.NewLine}{appData.ReadMcpYaml()}");

        NavigateToAcpSettings(session);
        NavigateToMcpSettings(session);

        toggle = session.FindByAutomationId("Mcp.Server.Enabled", TimeSpan.FromSeconds(10));
        Assert.Equal(ToggleState.On, toggle.Patterns.Toggle.Pattern.ToggleState.Value);
    }

    [SkippableFact]
    public void AddCloseAddServerEditor_DoesNotCrash()
    {
        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();

        EnsureMainWindowWide(session);
        NavigateToMcpSettings(session);

        ClickButtonAndWaitForOnscreen(session, "Mcp.AddServer", "Mcp.Editor.Close");

        ClickButtonAndWaitForHidden(session, "Mcp.Editor.Close", "Mcp.Editor.Close");
        Assert.True(
            session.WaitUntilHidden("Mcp.Editor.Close", TimeSpan.FromSeconds(5)),
            "MCP editor stayed visible after closing the draft.");

        ClickButtonAndWaitForOnscreen(session, "Mcp.AddServer", "Mcp.Editor.Close");

        Assert.True(
            session.WaitUntilOnscreen("Mcp.AddServer", TimeSpan.FromSeconds(2)),
            "MCP settings page stopped responding after add-delete-add.");
    }

    private static void ClickButtonAndWaitForOnscreen(
        WindowsGuiAppSession session,
        string automationId,
        string expectedAutomationId)
    {
        ClickButton(session, automationId);
        Assert.True(
            session.WaitUntilOnscreen(expectedAutomationId, TimeSpan.FromSeconds(5)),
            $"Expected '{expectedAutomationId}' to become visible after clicking '{automationId}'.");
    }

    private static void ClickButtonAndWaitForHidden(
        WindowsGuiAppSession session,
        string automationId,
        string expectedHiddenAutomationId)
    {
        ClickButton(session, automationId);
        Assert.True(
            session.WaitUntilHidden(expectedHiddenAutomationId, TimeSpan.FromSeconds(5)),
            $"Expected '{expectedHiddenAutomationId}' to become hidden after clicking '{automationId}'.");
    }

    private static void ClickButton(WindowsGuiAppSession session, string automationId)
    {
        Assert.True(
            session.WaitUntilOnscreen(automationId, TimeSpan.FromSeconds(5)),
            $"Button '{automationId}' did not become visible.");

        session.ClickElement(session.FindByAutomationId(automationId));
    }

    private static void NavigateToMcpSettings(WindowsGuiAppSession session)
    {
        var settingsItem = session.FindByAutomationId("SettingsItem", TimeSpan.FromSeconds(10));
        session.ActivateElement(settingsItem);
        session.ClickElement(settingsItem);

        var mcpSettingsItem = session.TryFindByAutomationId("SettingsNav.Mcp", TimeSpan.FromSeconds(10))
            ?? session.TryFindVisibleElementByNameAnywhere("MCP", TimeSpan.FromSeconds(10));

        if (mcpSettingsItem is null)
        {
            var captureRoot = Path.Combine(Path.GetTempPath(), "SalmonEgg.GuiTests");
            Directory.CreateDirectory(captureRoot);
            var capturePath = Path.Combine(captureRoot, $"settings-mcp-entry-missing-{DateTime.UtcNow:yyyyMMddHHmmssfff}.png");
            session.CaptureMainWindowToFile(capturePath);
            var visibleTexts = string.Join(", ", session.GetVisibleTexts());
            throw new XunitException(
                $"MCP settings entry did not become visible after opening settings.{Environment.NewLine}" +
                $"Screenshot: {capturePath}{Environment.NewLine}" +
                $"Visible texts: [{visibleTexts}]");
        }

        session.ActivateElement(mcpSettingsItem!);

        Assert.True(
            session.WaitUntilEnabled("Mcp.AddServer", TimeSpan.FromSeconds(10)),
            "MCP settings page did not become visible.");
    }

    private static void NavigateToAcpSettings(WindowsGuiAppSession session)
    {
        var settingsItem = session.FindByAutomationId("SettingsItem", TimeSpan.FromSeconds(10));
        session.ActivateElement(settingsItem);
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
            session.WaitUntilOnscreen("Acp.Global.Enabled", TimeSpan.FromSeconds(10)),
            "ACP settings page did not become visible.");
    }

    private static void EnsureMainWindowWide(WindowsGuiAppSession session)
    {
        try
        {
            if (session.MainWindow.Patterns.Window.IsSupported)
            {
                session.MainWindow.Patterns.Window.Pattern.SetWindowVisualState(WindowVisualState.Normal);
            }
        }
        catch
        {
        }

        session.ResizeMainWindow(width: 1400, height: 900);
    }
}
