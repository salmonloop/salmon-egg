using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using FlaUI.Core.Definitions;
using Xunit.Sdk;

namespace SalmonEgg.GuiTests.Windows;

public sealed class AcpSettingsSmokeTests
{
    [SkippableFact]
    public void GlobalAcpEnabledToggle_PersistsAcrossRestart()
    {
        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using (var session = WindowsGuiAppSession.LaunchFresh())
        {
            EnsureMainWindowWide(session);
            NavigateToAcpSettings(session);

            var toggle = session.FindByAutomationId("Acp.Global.Enabled", TimeSpan.FromSeconds(10));
            Assert.Equal(ToggleState.On, toggle.Patterns.Toggle.Pattern.ToggleState.Value);

            session.ClickElement(toggle);

            Assert.True(
                WaitUntil(() => appData.ReadAppYaml().Contains("acp_enabled: false", StringComparison.Ordinal), TimeSpan.FromSeconds(5)),
                $"Global ACP toggle did not persist false to app.yaml.{Environment.NewLine}{appData.ReadAppYaml()}");
        }

        using (var session = WindowsGuiAppSession.LaunchFresh())
        {
            EnsureMainWindowWide(session);
            NavigateToAcpSettings(session);

            var toggle = session.FindByAutomationId("Acp.Global.Enabled", TimeSpan.FromSeconds(10));
            Assert.Equal(ToggleState.Off, toggle.Patterns.Toggle.Pattern.ToggleState.Value);
        }
    }

    private static void NavigateToAcpSettings(WindowsGuiAppSession session)
    {
        var settingsItem = session.FindByAutomationId("SettingsItem", TimeSpan.FromSeconds(10));
        session.ClickElement(settingsItem);
        Thread.Sleep(250);

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
            "ACP global toggle did not become visible.");
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

        ResizeMainWindow(width: 1400, height: 900);
    }

    private static void ResizeMainWindow(int width, int height)
    {
        var process = Process.GetProcessesByName("SalmonEgg")
            .OrderByDescending(candidate => candidate.StartTime)
            .First();

        if (NativeMethods.MoveWindow(process.MainWindowHandle, 80, 80, width, height, true))
        {
            return;
        }

        if (NativeMethods.SetWindowPos(process.MainWindowHandle, IntPtr.Zero, 80, 80, width, height, 0))
        {
            return;
        }

        throw new InvalidOperationException("Failed to resize the SalmonEgg window.");
    }

    private static bool WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(120);
        }

        return condition();
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool MoveWindow(IntPtr hWnd, int x, int y, int width, int height, bool repaint);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int x,
            int y,
            int cx,
            int cy,
            uint uFlags);
    }
}
