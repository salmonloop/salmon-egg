using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using FlaUI.Core.Definitions;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;

namespace SalmonEgg.GuiTests.Windows;

[SupportedOSPlatform("windows")]
internal sealed class WindowsGuiAppSession : IDisposable
{
    private const string ProcessName = "SalmonEgg";
    // Windows.System.VirtualKey.GamepadDPadDown; this does not exercise Windows.Gaming.Input polling.
    // Windows.System.VirtualKey mapping:
    // GamepadA = 195, GamepadDPadUp = 203, GamepadDPadDown = 204.
    // GamepadDPadLeft = 205, GamepadDPadRight = 206.
    private const VirtualKeyShort VirtualGamepadA = (VirtualKeyShort)195;
    private const VirtualKeyShort VirtualGamepadDPadDown = (VirtualKeyShort)204;
    private const VirtualKeyShort VirtualGamepadDPadUp = (VirtualKeyShort)203;
    private const VirtualKeyShort VirtualGamepadDPadLeft = (VirtualKeyShort)205;
    private const VirtualKeyShort VirtualGamepadDPadRight = (VirtualKeyShort)206;
    private const VirtualKeyShort VirtualGamepadB = (VirtualKeyShort)196;
    private static readonly string[] MainShellWindowAnchorAutomationIds =
    [
        "TitleBar.OpenMiniWindow",
        "TitleBarMiniWindowButton",
        "MainNav.Automation.SelectionState",
        "MainNav.Start",
        "MainNav.DiscoverSessions"
    ];
    private readonly UIA3Automation _automation;
    private readonly Application _application;
    private readonly bool _ownsProcess;
    private Window _mainWindow;
    private IGamepadTestInput? _gamepadInput;

    private WindowsGuiAppSession(Application application, UIA3Automation automation, Window mainWindow, bool ownsProcess)
    {
        _application = application;
        _automation = automation;
        _mainWindow = mainWindow;
        _ownsProcess = ownsProcess;
    }

    public Window MainWindow => ResolveMainWindow();

    public static WindowsGuiAppSession LaunchOrAttach()
    {
        GuiTestGate.RequireEnabled();

        var currentInstall = GuiTestGate.GetRequiredCurrentInstall();
        var executablePath = currentInstall.InstalledExecutablePath
            ?? throw new InvalidOperationException(currentInstall.FailureMessage);
        var existing = FindRunningProcess(executablePath);

        if (existing == null)
        {
            using var activationEnvironment = ActivationEnvironmentScope.ApplySalmonEggVariables();
            var launchedAtUtc = DateTime.UtcNow;
            var activatedProcessId = LaunchInstalledMsix(executablePath);
            existing = WaitForProcess(executablePath, launchedAtUtc, TimeSpan.FromSeconds(20), activatedProcessId);
        }

        var automation = new UIA3Automation();
        var application = Application.Attach(existing);
        var mainWindow = RetryUntil(
            () => application.GetMainWindow(automation),
            window => window != null && !TryGetIsOffscreen(window),
            TimeSpan.FromSeconds(20),
            "Timed out waiting for SalmonEgg main window.");

        return new WindowsGuiAppSession(application, automation, mainWindow!, ownsProcess: false);
    }

    public static WindowsGuiAppSession LaunchFresh()
    {
        GuiTestGate.RequireEnabled();
        StopAllRunningInstances();

        var currentInstall = GuiTestGate.GetRequiredCurrentInstall();
        var executablePath = currentInstall.InstalledExecutablePath
            ?? throw new InvalidOperationException(currentInstall.FailureMessage);
        using var activationEnvironment = ActivationEnvironmentScope.ApplySalmonEggVariables();
        var launchedAtUtc = DateTime.UtcNow;
        var activatedProcessId = LaunchInstalledMsix(executablePath);

        var process = WaitForProcess(executablePath, launchedAtUtc, TimeSpan.FromSeconds(20), activatedProcessId);

        return AttachToProcess(process, ownsProcess: true);
    }

    public static bool IsInstalled()
    {
        return GuiTestGate.GetRequiredCurrentInstall().IsCurrentInstall;
    }

    public static void StopAllRunningInstances()
    {
        var targetExecutablePath = TryGetCurrentInstallExecutablePath();

        foreach (var process in Process.GetProcessesByName(ProcessName))
        {
            try
            {
                if (!ShouldStopProcess(process, targetExecutablePath))
                {
                    continue;
                }

                if (!process.HasExited)
                {
                    if (process.CloseMainWindow())
                    {
                        process.WaitForExit(5000);
                    }

                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                        process.WaitForExit(5000);
                    }
                }
            }
            catch
            {
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    public AutomationElement FindByAutomationId(string automationId, TimeSpan? timeout = null)
    {
        return RetryUntil(
            () => MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(automationId)),
            element => element != null,
            timeout ?? TimeSpan.FromSeconds(10),
            $"AutomationId '{automationId}' was not found.")!;
    }

    public AutomationElement FindByAutomationIdAnywhere(string automationId, TimeSpan? timeout = null)
    {
        return RetryUntil(
            () => FindByAutomationIdInApplicationWindows(automationId),
            element => element != null,
            timeout ?? TimeSpan.FromSeconds(10),
            $"AutomationId '{automationId}' was not found in any SalmonEgg application window.")!;
    }

    public AutomationElement? TryFindByAutomationId(string automationId, TimeSpan? timeout = null)
    {
        try
        {
            return FindByAutomationId(automationId, timeout);
        }
        catch (TimeoutException)
        {
            return null;
        }
    }

    public AutomationElement? TryFindByAutomationIdAnywhere(string automationId, TimeSpan? timeout = null)
    {
        try
        {
            return FindByAutomationIdAnywhere(automationId, timeout);
        }
        catch (TimeoutException)
        {
            return null;
        }
    }

    public AutomationElement? FindFirstByAutomationIdPrefix(string prefix, TimeSpan? timeout = null)
    {
        return RetryUntil(
            () => MainWindow
                .FindAllDescendants()
                .FirstOrDefault(element => HasAutomationIdPrefix(element, prefix)),
            element => element != null,
            timeout ?? TimeSpan.FromSeconds(5),
            $"AutomationId prefix '{prefix}' was not found.");
    }

    public AutomationElement? FindFirstDescendantByControlType(
        AutomationElement scope,
        ControlType controlType,
        TimeSpan? timeout = null)
    {
        return RetryUntil(
            () => scope.FindFirstDescendant(cf => cf.ByControlType(controlType)),
            element => element != null,
            timeout ?? TimeSpan.FromSeconds(10),
            $"ControlType '{controlType}' was not found.")!;
    }

    public AutomationElement? FindVisibleText(string text, AutomationElement? scope = null, TimeSpan? timeout = null)
    {
        var expectedText = NormalizeVisibleText(text);

        return RetryUntil(
            () => (scope ?? MainWindow)
                .FindAllDescendants(cf => cf.ByControlType(ControlType.Text))
                .FirstOrDefault(element =>
                    !TryGetIsOffscreen(element)
                    && string.Equals(NormalizeVisibleText(element.Name), expectedText, StringComparison.Ordinal)),
            element => element != null,
            timeout ?? TimeSpan.FromSeconds(3),
            $"Visible text '{text}' was not found.");
    }

    public AutomationElement? TryFindVisibleText(string text, AutomationElement? scope = null, TimeSpan? timeout = null)
    {
        try
        {
            return FindVisibleText(text, scope, timeout);
        }
        catch (TimeoutException)
        {
            return null;
        }
    }

    public IReadOnlyList<string> GetVisibleTexts(AutomationElement? scope = null)
    {
        return (scope ?? MainWindow)
            .FindAllDescendants(cf => cf.ByControlType(ControlType.Text))
            .Where(element => !TryGetIsOffscreen(element) && !string.IsNullOrWhiteSpace(element.Name))
            .Select(element => NormalizeVisibleText(element.Name))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    public IReadOnlyList<string> GetVisibleButtons(AutomationElement? scope = null)
    {
        return (scope ?? MainWindow)
            .FindAllDescendants(cf => cf.ByControlType(ControlType.Button))
            .Where(element => !TryGetIsOffscreen(element))
            .Select(element => $"{SafeAutomationId(element)}|{SafeName(element)}")
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    public bool WaitUntilHidden(string automationId, TimeSpan timeout)
    {
        return WaitUntil(
            () => TryFindByAutomationId(automationId, TimeSpan.FromMilliseconds(250)) is null,
            timeout);
    }

    public bool WaitUntilVisible(string automationId, TimeSpan timeout)
    {
        return TryFindByAutomationId(automationId, timeout) != null;
    }

    public bool WaitUntilOnscreen(string automationId, TimeSpan timeout)
    {
        return WaitUntil(
            () =>
            {
                var element = TryFindByAutomationId(automationId, TimeSpan.FromMilliseconds(250));
                return element is not null && !TryGetIsOffscreen(element);
            },
            timeout);
    }

    public bool IsOnscreen(string automationId, TimeSpan? timeout = null)
    {
        var element = TryFindByAutomationId(automationId, timeout ?? TimeSpan.FromMilliseconds(250));
        return element is not null && !TryGetIsOffscreen(element);
    }

    public bool WaitUntilEnabled(string automationId, TimeSpan timeout)
    {
        return WaitUntil(
            () =>
            {
                var element = TryFindByAutomationId(automationId, TimeSpan.FromMilliseconds(250));
                return element is not null && !TryGetIsOffscreen(element) && TryGetIsEnabled(element);
            },
            timeout);
    }

    public bool WaitUntil(Func<bool> condition, TimeSpan timeout, TimeSpan? pollInterval = null)
    {
        ArgumentNullException.ThrowIfNull(condition);

        var deadline = DateTime.UtcNow + timeout;
        var interval = pollInterval ?? TimeSpan.FromMilliseconds(120);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(interval);
        }

        return condition();
    }

    public void InvokeButton(string automationId)
    {
        if (!WaitUntilOnscreen(automationId, TimeSpan.FromSeconds(5)))
        {
            throw new TimeoutException($"Button '{automationId}' is not onscreen.");
        }

        var button = FindByAutomationId(automationId);
        if (TryGetIsOffscreen(button))
        {
            throw new InvalidOperationException($"Button '{automationId}' is offscreen.");
        }

        if (button.Patterns.Invoke.IsSupported)
        {
            button.Patterns.Invoke.Pattern.Invoke();
            return;
        }

        if (automationId.StartsWith("TitleBar.", StringComparison.Ordinal))
        {
            try
            {
                var point = button.GetClickablePoint();
                Mouse.Click(point);
                return;
            }
            catch
            {
            }
        }

        ActivateElement(button);
    }

    public void EnterText(string automationId, string text)
    {
        var element = FindByAutomationId(automationId);

        if (element.Patterns.Value.IsSupported)
        {
            element.Patterns.Value.Pattern.SetValue(text);
            return;
        }

        var valueElement = element
            .FindAllDescendants()
            .FirstOrDefault(descendant => descendant.Patterns.Value.IsSupported);
        if (valueElement is not null)
        {
            valueElement.Patterns.Value.Pattern.SetValue(text);
            return;
        }

        throw new InvalidOperationException(
            $"Element '{automationId}' does not support ValuePattern for text entry.");
    }

    public string? TryGetValue(AutomationElement element)
    {
        if (element.Patterns.Value.IsSupported)
        {
            return element.Patterns.Value.Pattern.Value;
        }

        var valueElement = element
            .FindAllDescendants()
            .FirstOrDefault(descendant => descendant.Patterns.Value.IsSupported);
        return valueElement?.Patterns.Value.Pattern.Value;
    }

    public void TypeText(string automationId, string text)
    {
        var element = FindByAutomationId(automationId);
        FocusElement(element);
        Thread.Sleep(100);

        foreach (var character in text)
        {
            TypeCharacter(character);
        }
    }

    public void ActivateElement(AutomationElement element)
    {
        try
        {
            element.Focus();
        }
        catch
        {
        }

        if (element.Patterns.Invoke.IsSupported)
        {
            element.Patterns.Invoke.Pattern.Invoke();
            return;
        }

        try
        {
            var point = element.GetClickablePoint();
            Mouse.Click(point);
            return;
        }
        catch
        {
        }

        if (TryClickElementCenter(element))
        {
            return;
        }

        string elementId;
        try
        {
            elementId = element.AutomationId;
        }
        catch
        {
            elementId = "<unknown>";
        }

        throw new InvalidOperationException(
            $"Element '{elementId}' does not support Invoke or SelectionItem patterns.");
    }

    public void ClickElement(AutomationElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        try
        {
            element.Focus();
        }
        catch
        {
        }

        try
        {
            var point = element.GetClickablePoint();
            Mouse.Click(point);
            return;
        }
        catch
        {
        }

        if (TryClickElementCenter(element))
        {
            return;
        }

        ActivateElement(element);
    }

    public void DoubleClickElement(AutomationElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        try
        {
            element.Focus();
        }
        catch
        {
        }

        try
        {
            var point = element.GetClickablePoint();
            Mouse.DoubleClick(point);
            return;
        }
        catch
        {
        }

        ClickElement(element);
        Thread.Sleep(50);
        ClickElement(element);
    }

    private static bool TryClickElementCenter(AutomationElement element)
    {
        try
        {
            if (TryGetIsOffscreen(element))
            {
                return false;
            }

            var bounds = element.BoundingRectangle;
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return false;
            }

            var point = new Point(
                bounds.Left + bounds.Width / 2,
                bounds.Top + bounds.Height / 2);
            Mouse.Click(point);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void OpenContextMenu(AutomationElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        var clickablePoint = element.GetClickablePoint();
        Mouse.RightClick(clickablePoint);
    }

    public void OpenContextMenuWithKeyboard(AutomationElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        element.Focus();
        Keyboard.Press(VirtualKeyShort.SHIFT);
        try
        {
            Keyboard.Press(VirtualKeyShort.F10);
            Keyboard.Release(VirtualKeyShort.F10);
        }
        finally
        {
            Keyboard.Release(VirtualKeyShort.SHIFT);
        }
    }

    public void PressEscape()
    {
        Keyboard.Press(VirtualKeyShort.ESCAPE);
        Keyboard.Release(VirtualKeyShort.ESCAPE);
    }

    public void PressEnter()
    {
        Keyboard.Press(VirtualKeyShort.RETURN);
        Keyboard.Release(VirtualKeyShort.RETURN);
    }

    public void PressShortcut(params VirtualKeyShort[] keys)
    {
        if (keys is null || keys.Length == 0)
        {
            return;
        }

        foreach (var key in keys)
        {
            Keyboard.Press(key);
        }

        for (var i = keys.Length - 1; i >= 0; i--)
        {
            Keyboard.Release(keys[i]);
        }
    }

    public void PressUp()
    {
        PressKey(VirtualKeyShort.UP);
    }

    public void PressDown()
    {
        PressKey(VirtualKeyShort.DOWN);
    }

    public IGamepadTestInput CreateSyntheticGamepadInput()
    {
        return new SyntheticVirtualKeyGamepadTestInput(this);
    }

    public IGamepadTestInput CreateConfiguredGamepadInput()
    {
        return _gamepadInput ??= GamepadTestInputFactory.Create(this);
    }

    internal void PressSyntheticGamepadDown()
    {
        PressKey(VirtualGamepadDPadDown);
    }

    public void PressVirtualGamepadDPadDown()
    {
        CreateSyntheticGamepadInput().PressDown();
    }

    internal void PressSyntheticGamepadUp()
    {
        PressKey(VirtualGamepadDPadUp);
    }

    public void PressVirtualGamepadDPadUp()
    {
        CreateSyntheticGamepadInput().PressUp();
    }

    internal void PressSyntheticGamepadLeft()
    {
        PressKey(VirtualGamepadDPadLeft);
    }

    public void PressVirtualGamepadDPadLeft()
    {
        CreateSyntheticGamepadInput().PressLeft();
    }

    internal void PressSyntheticGamepadRight()
    {
        PressKey(VirtualGamepadDPadRight);
    }

    public void PressVirtualGamepadDPadRight()
    {
        CreateSyntheticGamepadInput().PressRight();
    }

    internal void PressSyntheticGamepadActivate()
    {
        PressKey(VirtualGamepadA);
    }

    public void PressVirtualGamepadA()
    {
        CreateSyntheticGamepadInput().PressActivate();
    }

    internal void PressSyntheticGamepadBack()
    {
        PressKey(VirtualGamepadB);
    }

    public void PressVirtualGamepadB()
    {
        CreateSyntheticGamepadInput().PressBack();
    }

    public void PressTab()
    {
        PressKey(VirtualKeyShort.TAB);
    }

    public void PressLeft()
    {
        PressKey(VirtualKeyShort.LEFT);
    }

    public void PressRight()
    {
        PressKey(VirtualKeyShort.RIGHT);
    }

    public void PressPageUp()
    {
        PressKey(VirtualKeyShort.PRIOR);
    }

    public void PressPageDown()
    {
        PressKey(VirtualKeyShort.NEXT);
    }

    public void ScrollWheel(double delta)
    {
        Mouse.Scroll(delta);
    }

    public bool? TryGetIsSelected(string automationId)
    {
        var element = TryFindByAutomationId(automationId, TimeSpan.FromSeconds(2));
        if (element == null || !element.Patterns.SelectionItem.IsSupported)
        {
            return null;
        }

        return element.Patterns.SelectionItem.Pattern.IsSelected.Value;
    }

    public string? TryGetElementName(string automationId, TimeSpan? timeout = null)
    {
        var element = TryFindByAutomationId(automationId, timeout ?? TimeSpan.FromSeconds(2));
        if (element == null)
        {
            return null;
        }

        try
        {
            return element.Name;
        }
        catch
        {
            return null;
        }
    }

    public AutomationElement? FindVisibleTextAnywhere(string text, TimeSpan? timeout = null)
    {
        var expectedText = NormalizeVisibleText(text);

        return RetryUntil(
            () => FindVisibleDescendantInApplicationWindows(
                element => string.Equals(
                    NormalizeVisibleText(element.Name),
                    expectedText,
                    StringComparison.Ordinal),
                ControlType.Text),
            element => element != null,
            timeout ?? TimeSpan.FromSeconds(3),
            $"Visible text '{text}' was not found in application windows.");
    }

    public AutomationElement? FindVisibleElementByNameAnywhere(string name, TimeSpan? timeout = null)
    {
        var expectedName = NormalizeVisibleText(name);

        return RetryUntil(
            () => FindVisibleDescendantInApplicationWindows(
                element => string.Equals(
                    NormalizeVisibleText(element.Name),
                    expectedName,
                    StringComparison.Ordinal)),
            element => element != null,
            timeout ?? TimeSpan.FromSeconds(3),
            $"Visible element '{name}' was not found in application windows.");
    }

    public AutomationElement? TryFindVisibleTextAnywhere(string text, TimeSpan? timeout = null)
    {
        try
        {
            return FindVisibleTextAnywhere(text, timeout);
        }
        catch (TimeoutException)
        {
            return null;
        }
    }

    public AutomationElement? TryFindVisibleElementByNameAnywhere(string name, TimeSpan? timeout = null)
    {
        try
        {
            return FindVisibleElementByNameAnywhere(name, timeout);
        }
        catch (TimeoutException)
        {
            return null;
        }
    }

    public void FocusElement(AutomationElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.Focus();
    }

    public void BringMainWindowToFront()
    {
        var mainWindow = ResolveMainWindow();
        try
        {
            if (mainWindow.Patterns.Window.IsSupported)
            {
                mainWindow.Patterns.Window.Pattern.SetWindowVisualState(WindowVisualState.Normal);
            }
        }
        catch
        {
        }

        try
        {
            mainWindow.Focus();
        }
        catch
        {
        }

        Thread.Sleep(80);
    }

    public void CaptureMainWindowToFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var mainWindow = ResolveMainWindow();

        try
        {
            mainWindow.CaptureToFile(path);
            return;
        }
        catch (Exception ex) when (ex is COMException or Win32Exception or InvalidOperationException or ArgumentException)
        {
            _ = TryCaptureWindowWithScreenCopy(path);
        }
    }

    public void ResizeMainWindow(int width, int height, int x = 80, int y = 80)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        BringMainWindowToFront();

        if (!TryGetMainWindowHandle(out var hwnd) || hwnd == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to resolve the SalmonEgg window handle.");
        }

        _ = NativeMethods.MoveWindow(hwnd, x, y, width, height, true);
        if (WaitForWindowSize(hwnd, width, height, TimeSpan.FromSeconds(2)))
        {
            return;
        }

        _ = NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, x, y, width, height, 0);
        if (WaitForWindowSize(hwnd, width, height, TimeSpan.FromSeconds(2)))
        {
            return;
        }

        throw new InvalidOperationException("Failed to resize the SalmonEgg window.");
    }

    private Window ResolveMainWindow()
    {
        try
        {
            var candidates = GetApplicationTopLevelWindows();

            var anchored = candidates.FirstOrDefault(ContainsMainShellAnchors);
            if (anchored is not null)
            {
                _mainWindow = anchored;
                return _mainWindow;
            }
        }
        catch
        {
        }

        return _mainWindow;
    }

    private AutomationElement? FindByAutomationIdInApplicationWindows(string automationId)
    {
        foreach (var window in GetApplicationTopLevelWindows())
        {
            try
            {
                var match = window.FindFirstDescendant(cf => cf.ByAutomationId(automationId));
                if (match is not null)
                {
                    return match;
                }
            }
            catch
            {
            }
        }

        return null;
    }

    private AutomationElement? FindVisibleDescendantInApplicationWindows(
        Func<AutomationElement, bool> predicate,
        ControlType? controlType = null)
    {
        foreach (var window in GetApplicationTopLevelWindows())
        {
            AutomationElement[] descendants;
            try
            {
                descendants = controlType is null
                    ? window.FindAllDescendants()
                    : window.FindAllDescendants(cf => cf.ByControlType(controlType.Value));
            }
            catch
            {
                continue;
            }

            foreach (var element in descendants)
            {
                try
                {
                    if (!TryGetIsOffscreen(element) && predicate(element))
                    {
                        return element;
                    }
                }
                catch
                {
                }
            }
        }

        return null;
    }

    private Window[] GetApplicationTopLevelWindows()
    {
        try
        {
            return _application.GetAllTopLevelWindows(_automation)
                .Where(window => !TryGetIsOffscreen(window))
                .ToArray();
        }
        catch
        {
            return [_mainWindow];
        }
    }

    private static bool ContainsMainShellAnchors(Window window)
    {
        foreach (var automationId in MainShellWindowAnchorAutomationIds)
        {
            try
            {
                if (window.FindFirstDescendant(cf => cf.ByAutomationId(automationId)) is not null)
                {
                    return true;
                }
            }
            catch
            {
            }
        }

        return false;
    }

    private static void PressKey(VirtualKeyShort key)
    {
        Keyboard.Press(key);
        Keyboard.Release(key);
    }

    private static void TypeCharacter(char character)
    {
        if (character == ' ')
        {
            PressKey(VirtualKeyShort.SPACE);
            return;
        }

        if (character >= 'a' && character <= 'z')
        {
            PressKey((VirtualKeyShort)char.ToUpperInvariant(character));
            return;
        }

        if (character >= 'A' && character <= 'Z')
        {
            Keyboard.Press(VirtualKeyShort.SHIFT);
            try
            {
                PressKey((VirtualKeyShort)character);
            }
            finally
            {
                Keyboard.Release(VirtualKeyShort.SHIFT);
            }

            return;
        }

        if (character >= '0' && character <= '9')
        {
            PressKey((VirtualKeyShort)character);
            return;
        }

        throw new NotSupportedException($"Unsupported GUI keyboard input character: '{character}'.");
    }

    private static string NormalizeVisibleText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Join(
            " ",
            value
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private bool TryCaptureWindowWithScreenCopy(string path)
    {
        try
        {
            if (!TryGetMainWindowHandle(out var hwnd) || hwnd == IntPtr.Zero || !NativeMethods.TryGetWindowRect(hwnd, out var rect))
            {
                return false;
            }

            var width = Math.Max(1, rect.Right - rect.Left);
            var height = Math.Max(1, rect.Bottom - rect.Top);

            using var bitmap = new Bitmap(width, height);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, new Size(width, height));
            }

            bitmap.Save(path, ImageFormat.Png);
            return File.Exists(path);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    private bool TryGetMainWindowHandle(out IntPtr hwnd)
    {
        try
        {
            using var process = Process.GetProcessById(_application.ProcessId);
            hwnd = process.MainWindowHandle;
            return hwnd != IntPtr.Zero;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            hwnd = IntPtr.Zero;
            return false;
        }
    }

    private static bool WaitForWindowSize(IntPtr hwnd, int width, int height, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (NativeMethods.TryGetWindowSize(hwnd, out var currentWidth, out var currentHeight)
                && Math.Abs(currentWidth - width) <= 2
                && Math.Abs(currentHeight - height) <= 2)
            {
                return true;
            }

            Thread.Sleep(50);
        }

        return NativeMethods.TryGetWindowSize(hwnd, out var finalWidth, out var finalHeight)
            && Math.Abs(finalWidth - width) <= 2
            && Math.Abs(finalHeight - height) <= 2;
    }

    public bool IsFocusWithinAutomationId(string automationId, int maxDepth = 16)
    {
        if (string.IsNullOrWhiteSpace(automationId))
        {
            return false;
        }

        return GetFocusedAutomationIdPath(maxDepth).Contains(automationId, StringComparer.Ordinal);
    }

    public bool IsFocusWithinAutomationIdPrefix(string prefix, int maxDepth = 16)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return false;
        }

        return GetFocusedAutomationIdPath(maxDepth)
            .Any(id => id.StartsWith(prefix, StringComparison.Ordinal));
    }

    public IReadOnlyList<string> GetFocusedAutomationIdPath(int maxDepth = 16)
    {
        if (maxDepth < 1)
        {
            maxDepth = 1;
        }

        var path = new List<string>(maxDepth);
        try
        {
            var current = _automation.FocusedElement();
            var depth = 0;
            while (current is not null && depth < maxDepth)
            {
                path.Add(SafeAutomationId(current));
                current = current.Parent;
                depth++;
            }
        }
        catch
        {
            return Array.Empty<string>();
        }

        return path;
    }

    public bool IsFocusedElement(AutomationElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        try
        {
            var focused = _automation.FocusedElement();
            return focused is not null
                && string.Equals(SafeAutomationId(focused), SafeAutomationId(element), StringComparison.Ordinal)
                && string.Equals(focused.Name, element.Name, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    public string DescribeFocusedElement(int maxDepth = 8)
    {
        if (maxDepth < 1)
        {
            maxDepth = 1;
        }

        var segments = new List<string>(maxDepth);
        try
        {
            var current = _automation.FocusedElement();
            var depth = 0;
            while (current is not null && depth < maxDepth)
            {
                var automationId = SafeAutomationId(current);
                var controlType = SafeControlType(current);
                segments.Add($"{automationId}[{controlType}]");
                current = current.Parent;
                depth++;
            }
        }
        catch (Exception ex)
        {
            return $"<focus-error:{ex.GetType().Name}:{ex.Message}>";
        }

        return segments.Count == 0
            ? "<focus-null>"
            : string.Join(" > ", segments);
    }

    public string DescribeFocusedElementDetailed(int maxDepth = 8)
    {
        if (maxDepth < 1)
        {
            maxDepth = 1;
        }

        var segments = new List<string>(maxDepth);
        try
        {
            var current = _automation.FocusedElement();
            var depth = 0;
            while (current is not null && depth < maxDepth)
            {
                segments.Add(DescribeElement(current));
                current = current.Parent;
                depth++;
            }
        }
        catch (Exception ex)
        {
            return $"<focus-error:{ex.GetType().Name}:{ex.Message}>";
        }

        return segments.Count == 0
            ? "<focus-null>"
            : string.Join(" > ", segments);
    }

    public Rectangle? TryGetFocusedBoundingRectangle()
    {
        try
        {
            return _automation.FocusedElement()?.BoundingRectangle;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        _gamepadInput?.Dispose();
        _automation.Dispose();
        _application.Dispose();
        if (_ownsProcess)
        {
            StopAllRunningInstances();
        }
    }

    private static int? LaunchInstalledMsix(string executablePath)
    {
        var manifest = MsixManifestInfo.LoadFromRepo();
        var appUserModelId = ResolveAppUserModelId(manifest);

        if (TryActivateApplication(appUserModelId, out var processId))
        {
            return processId;
        }

        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"shell:AppsFolder\\{appUserModelId}",
            UseShellExecute = true
        });

        if (process == null)
        {
            throw new InvalidOperationException(
                $"Failed to launch installed SalmonEgg package '{appUserModelId}' from '{executablePath}'.");
        }

        return null;
    }

    private static string ResolveAppUserModelId(MsixManifestInfo manifest)
    {
        return TryResolveInstalledPackageFamilyName(manifest.IdentityName, out var packageFamilyName)
            ? $"{packageFamilyName}!{manifest.ApplicationId}"
            : $"{manifest.IdentityName}!{manifest.ApplicationId}";
    }

    private static bool TryResolveInstalledPackageFamilyName(string identityName, out string packageFamilyName)
    {
        packageFamilyName = string.Empty;
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoLogo -NoProfile -ExecutionPolicy Bypass -Command \"(Get-AppxPackage -Name '{identityName}' | Select-Object -First 1 -ExpandProperty PackageFamilyName)\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        process.Start();
        var output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();

        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
        {
            return false;
        }

        packageFamilyName = output;
        return true;
    }

    private static string ResolveInstalledExecutablePath(string identityName)
    {
        if (TryResolveInstalledExecutablePath(identityName, out var executablePath, out var failureMessage))
        {
            return executablePath;
        }

        throw new InvalidOperationException(failureMessage);
    }

    internal static bool TryResolveInstalledExecutablePath(
        string identityName,
        out string executablePath,
        out string failureMessage)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoLogo -NoProfile -ExecutionPolicy Bypass -Command \"(Get-AppxPackage -Name '{identityName}' | Select-Object -First 1 -ExpandProperty InstallLocation)\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        process.Start();
        var output = process.StandardOutput.ReadToEnd().Trim();
        var error = process.StandardError.ReadToEnd().Trim();
        process.WaitForExit();

        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
        {
            executablePath = string.Empty;
            failureMessage = string.IsNullOrWhiteSpace(error)
                ? $"SalmonEgg MSIX is not installed or InstallLocation could not be resolved for '{identityName}'."
                : $"SalmonEgg MSIX is not installed or InstallLocation could not be resolved for '{identityName}'. {error}";
            return false;
        }

        var resolvedExecutablePath = Path.Combine(output, $"{ProcessName}.exe");
        if (!File.Exists(resolvedExecutablePath))
        {
            executablePath = string.Empty;
            failureMessage = $"Installed SalmonEgg executable '{resolvedExecutablePath}' was not found.";
            return false;
        }

        executablePath = resolvedExecutablePath;
        failureMessage = string.Empty;
        return true;
    }

    private static Process? FindRunningProcess(string executablePath)
    {
        return Process.GetProcessesByName(ProcessName)
            .OrderByDescending(process => process.StartTime)
            .FirstOrDefault(process =>
                TryGetProcessExecutablePath(process, out var candidatePath)
                && string.Equals(candidatePath, executablePath, StringComparison.OrdinalIgnoreCase));
    }

    private static Process WaitForProcess(
        string executablePath,
        DateTime launchedAtUtc,
        TimeSpan timeout,
        int? activatedProcessId)
    {
        return RetryUntil(
            () => FindActivatedProcess(activatedProcessId)
                ?? Process.GetProcessesByName(ProcessName)
                    .OrderByDescending(process => process.StartTime)
                    .FirstOrDefault(process =>
                        (TryGetProcessExecutablePath(process, out var candidatePath)
                            && string.Equals(candidatePath, executablePath, StringComparison.OrdinalIgnoreCase))
                        || WasProcessStartedAfter(process, launchedAtUtc)),
            process => process != null,
            timeout,
            $"Timed out waiting for SalmonEgg process from installed executable '{executablePath}'.")!;
    }

    private static Process? FindActivatedProcess(int? processId)
    {
        if (processId is null or <= 0)
        {
            return null;
        }

        try
        {
            var process = Process.GetProcessById(processId.Value);
            if (!process.HasExited)
            {
                return process;
            }

            process.Dispose();
            return null;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return null;
        }
    }

    private static bool TryGetProcessExecutablePath(Process process, out string executablePath)
    {
        try
        {
            executablePath = process.MainModule?.FileName ?? string.Empty;
            return !string.IsNullOrWhiteSpace(executablePath);
        }
        catch
        {
            executablePath = string.Empty;
            return false;
        }
    }

    private static bool WasProcessStartedAfter(Process process, DateTime launchedAtUtc)
    {
        try
        {
            return process.StartTime.ToUniversalTime() >= launchedAtUtc.AddSeconds(-2);
        }
        catch
        {
            return false;
        }
    }

    private static string? TryGetCurrentInstallExecutablePath()
    {
        try
        {
            var currentInstall = GuiTestGate.GetRequiredCurrentInstall();
            return currentInstall.IsCurrentInstall
                ? currentInstall.InstalledExecutablePath
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool ShouldStopProcess(Process process, string? targetExecutablePath)
    {
        if (string.IsNullOrWhiteSpace(targetExecutablePath))
        {
            return true;
        }

        return TryGetProcessExecutablePath(process, out var candidatePath)
            && string.Equals(candidatePath, targetExecutablePath, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryActivateApplication(string appUserModelId, out int processId)
    {
        processId = 0;
        try
        {
            var activationManagerType = Type.GetTypeFromCLSID(NativeMethods.ApplicationActivationManagerClsid, throwOnError: true)
                ?? throw new COMException("ApplicationActivationManager CLSID could not be resolved.");
            var activationManager = (NativeMethods.IApplicationActivationManager)
                Activator.CreateInstance(activationManagerType)!;
            var hresult = activationManager.ActivateApplication(
                appUserModelId,
                null,
                NativeMethods.ActivateOptions.None,
                out processId);

            if (hresult < 0)
            {
                Marshal.ThrowExceptionForHR(hresult);
            }

            return processId > 0;
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or InvalidCastException or BadImageFormatException or ArgumentException)
        {
            processId = 0;
            return false;
        }
    }

    private sealed class ActivationEnvironmentScope : IDisposable
    {
        private readonly IReadOnlyDictionary<string, string?> _previousUserValues;
        private bool _disposed;

        private ActivationEnvironmentScope(IReadOnlyDictionary<string, string?> previousUserValues)
        {
            _previousUserValues = previousUserValues;
        }

        public static ActivationEnvironmentScope ApplySalmonEggVariables()
        {
            var currentProcessVariables = Environment.GetEnvironmentVariables();
            var previousUserValues = new Dictionary<string, string?>(StringComparer.Ordinal);

            foreach (var key in currentProcessVariables.Keys.OfType<string>())
            {
                if (!key.StartsWith("SALMONEGG_", StringComparison.Ordinal))
                {
                    continue;
                }

                previousUserValues[key] = Environment.GetEnvironmentVariable(key, EnvironmentVariableTarget.User);
                Environment.SetEnvironmentVariable(
                    key,
                    currentProcessVariables[key]?.ToString(),
                    EnvironmentVariableTarget.User);
            }

            return new ActivationEnvironmentScope(previousUserValues);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (var pair in _previousUserValues)
            {
                Environment.SetEnvironmentVariable(pair.Key, pair.Value, EnvironmentVariableTarget.User);
            }
        }
    }

    private static WindowsGuiAppSession AttachToProcess(Process process, bool ownsProcess)
    {
        var automation = new UIA3Automation();
        try
        {
            var application = Application.Attach(process);
            var mainWindow = RetryUntil(
                () => application.GetMainWindow(automation),
                window => window != null && !TryGetIsOffscreen(window),
                TimeSpan.FromSeconds(20),
                "Timed out waiting for SalmonEgg main window.");

            return new WindowsGuiAppSession(application, automation, mainWindow!, ownsProcess);
        }
        catch
        {
            automation.Dispose();
            throw;
        }
    }

    private static T? RetryUntil<T>(
        Func<T?> probe,
        Func<T?, bool> success,
        TimeSpan timeout,
        string failureMessage) where T : class
    {
        var deadline = DateTime.UtcNow + timeout;
        T? last = null;
        Exception? lastException = null;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                last = probe();
                if (success(last))
                {
                    return last;
                }

                lastException = null;
            }
            catch (Exception ex) when (ex is TimeoutException or IOException or Win32Exception or COMException)
            {
                lastException = ex;
            }

            Thread.Sleep(250);
        }

        throw lastException is null
            ? new TimeoutException(failureMessage)
            : new TimeoutException($"{failureMessage} Last error: {lastException.Message}", lastException);
    }

    private static bool HasAutomationIdPrefix(AutomationElement element, string prefix)
    {
        try
        {
            return element.AutomationId?.StartsWith(prefix, StringComparison.Ordinal) == true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetIsOffscreen(AutomationElement element)
    {
        try
        {
            return element.IsOffscreen;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetIsEnabled(AutomationElement element)
    {
        try
        {
            return element.IsEnabled;
        }
        catch
        {
            return false;
        }
    }

    private static string SafeAutomationId(AutomationElement element)
    {
        try
        {
            return string.IsNullOrWhiteSpace(element.AutomationId)
                ? "<no-id>"
                : element.AutomationId;
        }
        catch
        {
            return "<id-error>";
        }
    }

    private static string SafeControlType(AutomationElement element)
    {
        try
        {
            return element.ControlType.ToString();
        }
        catch
        {
            return "Unknown";
        }
    }

    private static string SafeName(AutomationElement element)
    {
        try
        {
            return NormalizeVisibleText(element.Name);
        }
        catch
        {
            return "<name-error>";
        }
    }

    private static string DescribeElement(AutomationElement element)
        => $"{SafeAutomationId(element)}"
            + $"[{SafeControlType(element)}]"
            + $" name='{SafeName(element)}'"
            + $" class='{SafeClassName(element)}'"
            + $" offscreen={SafeIsOffscreen(element)}"
            + $" enabled={SafeIsEnabled(element)}"
            + $" bounds={SafeBoundingRectangle(element)}";

    private static string SafeClassName(AutomationElement element)
    {
        try
        {
            return string.IsNullOrWhiteSpace(element.ClassName)
                ? "<no-class>"
                : element.ClassName;
        }
        catch
        {
            return "<class-error>";
        }
    }

    private static bool SafeIsOffscreen(AutomationElement element)
    {
        try
        {
            return element.IsOffscreen;
        }
        catch
        {
            return false;
        }
    }

    private static bool SafeIsEnabled(AutomationElement element)
    {
        try
        {
            return element.IsEnabled;
        }
        catch
        {
            return false;
        }
    }

    private static string SafeBoundingRectangle(AutomationElement element)
    {
        try
        {
            var rect = element.BoundingRectangle;
            return $"{rect.Left:0},{rect.Top:0},{rect.Width:0},{rect.Height:0}";
        }
        catch
        {
            return "<bounds-error>";
        }
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool MoveWindow(IntPtr hWnd, int x, int y, int width, int height, bool repaint);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);

        public static bool TryGetWindowRect(IntPtr hWnd, out Rect rect)
        {
            if (GetWindowRect(hWnd, out rect))
            {
                return true;
            }

            rect = default;
            return false;
        }

        public static bool TryGetWindowSize(IntPtr hWnd, out int width, out int height)
        {
            if (TryGetWindowRect(hWnd, out var rect))
            {
                width = rect.Right - rect.Left;
                height = rect.Bottom - rect.Top;
                return true;
            }

            width = 0;
            height = 0;
            return false;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [Flags]
        public enum ActivateOptions
        {
            None = 0
        }

        public static readonly Guid ApplicationActivationManagerClsid = new("45BA127D-10A8-46EA-8AB7-56EA9078943C");

        [ComImport]
        [Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IApplicationActivationManager
        {
            [PreserveSig]
            int ActivateApplication(
                [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
                [MarshalAs(UnmanagedType.LPWStr)] string? arguments,
                ActivateOptions options,
                out int processId);

            [PreserveSig]
            int ActivateForFile(
                [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
                IntPtr itemArray,
                [MarshalAs(UnmanagedType.LPWStr)] string? verb,
                out int processId);

            [PreserveSig]
            int ActivateForProtocol(
                [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
                IntPtr itemArray,
                out int processId);
        }
    }

}
