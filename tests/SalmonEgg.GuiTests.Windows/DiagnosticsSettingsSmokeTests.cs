using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using FlaUI.Core.Definitions;
using Xunit.Sdk;

namespace SalmonEgg.GuiTests.Windows;

public sealed class DiagnosticsSettingsSmokeTests
{
    [SkippableFact]
    public void GamepadDiagnosticsMonitor_CanRefreshAndStartFromDiagnosticsSettings()
    {
        GuiTestGate.RequireEnabled();

        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();

        EnsureMainWindowWide(session);
        NavigateToDiagnosticsSettings(session);

        var startButton = FindAndScrollIntoView(session, "Diagnostics.GamepadStart", TimeSpan.FromSeconds(10));
        Assert.False(
            startButton.IsOffscreen,
            $"Gamepad diagnostics start button did not become visible."
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");
        Assert.True(startButton.IsEnabled, "Gamepad diagnostics start button should be enabled on Windows.");

        session.ActivateElement(FindAndScrollIntoView(session, "Diagnostics.GamepadRefresh", TimeSpan.FromSeconds(5)));
        Assert.NotNull(session.FindByAutomationId("Diagnostics.GamepadStandardCount", TimeSpan.FromSeconds(5)));
        Assert.NotNull(session.FindByAutomationId("Diagnostics.GamepadRawCount", TimeSpan.FromSeconds(5)));
        Assert.NotNull(session.FindByAutomationId("Diagnostics.GamepadActiveInputs", TimeSpan.FromSeconds(5)));
        Assert.NotNull(session.FindByAutomationId("Diagnostics.GamepadRawDetails", TimeSpan.FromSeconds(5)));

        session.ActivateElement(startButton);
        var stopButton = FindAndScrollIntoView(session, "Diagnostics.GamepadStop", TimeSpan.FromSeconds(10));
        Assert.False(
            stopButton.IsOffscreen,
            $"Gamepad diagnostics stop button did not become visible after starting the monitor.{Environment.NewLine}{appData.ReadBootLogTail()}");
        Assert.True(stopButton.IsEnabled, "Gamepad diagnostics stop button should become enabled after starting the monitor.");
    }

    [SkippableFact]
    public void GamepadDiagnosticsMonitor_NativeDeviceBackend_ReflectsVirtualControllerInput()
    {
        GuiTestGate.RequireEnabled();
        Skip.IfNot(
            NativeDeviceGamepadTestInput.IsBridgeConfigured(out var failureReason),
            failureReason);

        using var backendScope = new EnvironmentVariableScope("SALMONEGG_GUI_GAMEPAD_INPUT_BACKEND", "native-device");
        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();

        EnsureMainWindowWide(session);
        NavigateToDiagnosticsSettings(session);

        var startButton = FindAndScrollIntoView(session, "Diagnostics.GamepadStart", TimeSpan.FromSeconds(10));
        session.ActivateElement(startButton);

        Assert.True(
            session.WaitUntilEnabled("Diagnostics.GamepadStop", TimeSpan.FromSeconds(5)),
            $"Gamepad diagnostics stop button did not become enabled after starting native-device monitoring.{Environment.NewLine}{appData.ReadBootLogTail()}");

        var gamepad = session.CreateGamepadInput();
        var standardCount = session.FindByAutomationId("Diagnostics.GamepadStandardCount", TimeSpan.FromSeconds(5));

        Assert.True(
            session.WaitUntil(
                () => !string.Equals(ReadElementText(standardCount), "0", StringComparison.Ordinal),
                TimeSpan.FromSeconds(5)),
            $"Native-device bridge did not surface a connected virtual controller in diagnostics."
            + $"{Environment.NewLine}StandardCount={ReadElementText(standardCount)}"
            + $"{Environment.NewLine}RawCount={ReadElementText(session.FindByAutomationId("Diagnostics.GamepadRawCount", TimeSpan.FromSeconds(2)))}"
            + $"{Environment.NewLine}InputSource={ReadElementText(session.FindByAutomationId("Diagnostics.GamepadInputSource", TimeSpan.FromSeconds(2)))}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");

        gamepad.PressDown();
        var activeInputs = session.FindByAutomationId("Diagnostics.GamepadActiveInputs", TimeSpan.FromSeconds(5));

        Assert.True(
            session.WaitUntil(
                () => ReadElementText(activeInputs).Contains("MoveDown", StringComparison.Ordinal),
                TimeSpan.FromSeconds(5)),
            $"Native-device bridge did not project D-pad input into the diagnostics active-intents text."
            + $"{Environment.NewLine}ActiveInputs={ReadElementText(activeInputs)}"
            + $"{Environment.NewLine}InputSource={ReadElementText(session.FindByAutomationId("Diagnostics.GamepadInputSource", TimeSpan.FromSeconds(2)))}"
            + $"{Environment.NewLine}{appData.ReadBootLogTail()}");
    }

    private static void NavigateToDiagnosticsSettings(WindowsGuiAppSession session)
    {
        var settingsItem = session.FindByAutomationId("SettingsItem", TimeSpan.FromSeconds(10));
        session.FocusElement(settingsItem);
        session.PressEnter();

        var diagnosticsItem = session.TryFindByAutomationId("SettingsNav.Diagnostics", TimeSpan.FromSeconds(10));
        if (diagnosticsItem is null)
        {
            throw CreateNavigationFailure(session, "Diagnostics settings entry did not become visible after opening settings.");
        }

        session.FocusElement(diagnosticsItem);
        session.PressEnter();
        if (!session.WaitUntilOnscreen("Diagnostics.GamepadMonitorHeader", TimeSpan.FromSeconds(10)))
        {
            throw CreateNavigationFailure(session, "Gamepad diagnostics monitor header did not become visible.");
        }
    }

    private static XunitException CreateNavigationFailure(WindowsGuiAppSession session, string message)
    {
        var captureRoot = Path.Combine(Path.GetTempPath(), "SalmonEgg.GuiTests");
        Directory.CreateDirectory(captureRoot);
        var capturePath = Path.Combine(captureRoot, $"settings-diagnostics-missing-{DateTime.UtcNow:yyyyMMddHHmmssfff}.png");
        session.CaptureMainWindowToFile(capturePath);
        var visibleTexts = string.Join(", ", session.GetVisibleTexts());
        return new XunitException(
            $"{message}{Environment.NewLine}" +
            $"Screenshot: {capturePath}{Environment.NewLine}" +
            $"Visible texts: [{visibleTexts}]");
    }

    private static string ReadElementText(FlaUI.Core.AutomationElements.AutomationElement element)
    {
        return element.Patterns.Value.IsSupported
            ? element.Patterns.Value.Pattern.Value ?? string.Empty
            : element.Name ?? string.Empty;
    }

    private static FlaUI.Core.AutomationElements.AutomationElement FindAndScrollIntoView(
        WindowsGuiAppSession session,
        string automationId,
        TimeSpan timeout)
    {
        FlaUI.Core.AutomationElements.AutomationElement? element = null;

        session.WaitUntil(
            () =>
            {
                element = session.TryFindByAutomationId(automationId, TimeSpan.FromMilliseconds(250));
                if (element is not null)
                {
                    return true;
                }

                session.ScrollWheel(-120);
                return false;
            },
            timeout);

        element ??= session.FindByAutomationId(automationId, TimeSpan.FromMilliseconds(250));
        if (element.Patterns.ScrollItem.IsSupported)
        {
            element.Patterns.ScrollItem.Pattern.ScrollIntoView();
            session.WaitUntil(() => !element.IsOffscreen, TimeSpan.FromSeconds(1));
        }

        return element;
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

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _previousValue;

        public EnvironmentVariableScope(string name, string? value)
        {
            _name = name;
            _previousValue = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(_name, _previousValue);
        }
    }
}
