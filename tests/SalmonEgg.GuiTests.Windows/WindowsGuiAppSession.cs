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
    private readonly UIA3Automation _automation;
    private readonly Application _application;
    private readonly bool _ownsProcess;

    private WindowsGuiAppSession(Application application, UIA3Automation automation, Window mainWindow, bool ownsProcess)
    {
        _application = application;
        _automation = automation;
        MainWindow = mainWindow;
        _ownsProcess = ownsProcess;
    }

    public Window MainWindow { get; }

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
            () => _automation.GetDesktop().FindFirstDescendant(cf => cf.ByAutomationId(automationId)),
            element => element != null,
            timeout ?? TimeSpan.FromSeconds(10),
            $"AutomationId '{automationId}' was not found anywhere on the desktop.")!;
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

    public bool WaitUntilHidden(string automationId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var element = TryFindByAutomationId(automationId, TimeSpan.FromMilliseconds(250));
            if (element == null)
            {
                return true;
            }
            Thread.Sleep(250);
        }
        return false;
    }

    public bool WaitUntilVisible(string automationId, TimeSpan timeout)
    {
        return TryFindByAutomationId(automationId, timeout) != null;
    }

    public bool WaitUntilOnscreen(string automationId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var element = TryFindByAutomationId(automationId, TimeSpan.FromMilliseconds(250));
            if (element is not null && !TryGetIsOffscreen(element))
            {
                return true;
            }

            Thread.Sleep(120);
        }

        return false;
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

        throw new InvalidOperationException(
            $"Element '{automationId}' does not support ValuePattern for text entry.");
    }

    public void ActivateElement(AutomationElement element)
    {
        if (element.Patterns.SelectionItem.IsSupported)
        {
            try
            {
                element.Focus();
            }
            catch
            {
            }

            element.Patterns.SelectionItem.Pattern.Select();
            return;
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

    public void PressUp()
    {
        PressKey(VirtualKeyShort.UP);
    }

    public void PressDown()
    {
        PressKey(VirtualKeyShort.DOWN);
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
            () => _automation.GetDesktop()
                .FindAllDescendants(cf => cf.ByControlType(ControlType.Text))
                .FirstOrDefault(element =>
                    !TryGetIsOffscreen(element)
                    && string.Equals(NormalizeVisibleText(element.Name), expectedText, StringComparison.Ordinal)),
            element => element != null,
            timeout ?? TimeSpan.FromSeconds(3),
            $"Visible text '{text}' was not found anywhere on the desktop.");
    }

    public AutomationElement? FindVisibleElementByNameAnywhere(string name, TimeSpan? timeout = null)
    {
        var expectedName = NormalizeVisibleText(name);

        return RetryUntil(
            () => _automation.GetDesktop()
                .FindAllDescendants()
                .FirstOrDefault(element =>
                {
                    try
                    {
                        return !TryGetIsOffscreen(element)
                            && string.Equals(
                                NormalizeVisibleText(element.Name),
                                expectedName,
                                StringComparison.Ordinal);
                    }
                    catch
                    {
                        return false;
                    }
                }),
            element => element != null,
            timeout ?? TimeSpan.FromSeconds(3),
            $"Visible element '{name}' was not found anywhere on the desktop.");
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
        try
        {
            if (MainWindow.Patterns.Window.IsSupported)
            {
                MainWindow.Patterns.Window.Pattern.SetWindowVisualState(WindowVisualState.Normal);
            }
        }
        catch
        {
        }

        try
        {
            MainWindow.Focus();
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

        try
        {
            MainWindow.CaptureToFile(path);
            return;
        }
        catch (Exception ex) when (ex is COMException or Win32Exception or InvalidOperationException or ArgumentException)
        {
            if (!TryCaptureWindowWithScreenCopy(path))
            {
                throw;
            }
        }
    }

    private static void PressKey(VirtualKeyShort key)
    {
        Keyboard.Press(key);
        Keyboard.Release(key);
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
            using var process = Process.GetProcessById(_application.ProcessId);
            var hwnd = process.MainWindowHandle;
            if (hwnd == IntPtr.Zero || !NativeMethods.TryGetWindowRect(hwnd, out var rect))
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

    public void Dispose()
    {
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

    private static class NativeMethods
    {
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
