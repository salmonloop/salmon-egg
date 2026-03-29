using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using FlaUI.Core.Definitions;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace SalmonEgg.GuiTests.Windows;

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

        var manifest = MsixManifestInfo.LoadFromRepo();
        var existing = Process.GetProcessesByName(ProcessName)
            .OrderByDescending(process => process.StartTime)
            .FirstOrDefault();

        if (existing == null)
        {
            LaunchInstalledMsix(manifest);
            existing = WaitForProcess(ProcessName, TimeSpan.FromSeconds(20));
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

        var manifest = MsixManifestInfo.LoadFromRepo();
        LaunchInstalledMsix(manifest);

        var process = RetryUntil(
            () => Process.GetProcessesByName(ProcessName)
                .OrderByDescending(candidate => candidate.StartTime)
                .FirstOrDefault(),
            candidate => candidate != null,
            TimeSpan.FromSeconds(20),
            $"Timed out waiting for process '{ProcessName}'.")!;

        return AttachToProcess(process, ownsProcess: true);
    }

    public static void StopAllRunningInstances()
    {
        foreach (var process in Process.GetProcessesByName(ProcessName))
        {
            try
            {
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
        return RetryUntil(
            () => (scope ?? MainWindow)
                .FindAllDescendants(cf => cf.ByControlType(ControlType.Text))
                .FirstOrDefault(element =>
                    !TryGetIsOffscreen(element)
                    && string.Equals(element.Name, text, StringComparison.Ordinal)),
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
            .Select(element => element.Name)
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

    public void InvokeButton(string automationId)
    {
        var button = FindByAutomationId(automationId);
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
        if (element.Patterns.Invoke.IsSupported)
        {
            element.Patterns.Invoke.Pattern.Invoke();
            return;
        }

        if (element.Patterns.SelectionItem.IsSupported)
        {
            element.Patterns.SelectionItem.Pattern.Select();
            return;
        }

        throw new InvalidOperationException(
            $"Element '{element.AutomationId}' does not support Invoke or SelectionItem patterns.");
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

    public void Dispose()
    {
        _automation.Dispose();
        _application.Dispose();
        if (_ownsProcess)
        {
            StopAllRunningInstances();
        }
    }

    private static void LaunchInstalledMsix(MsixManifestInfo manifest)
    {
        var executablePath = ResolveInstalledExecutablePath(manifest.IdentityName);
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = Path.GetDirectoryName(executablePath),
            UseShellExecute = false
        });

        if (process == null)
        {
            throw new InvalidOperationException(
                $"Failed to launch installed SalmonEgg executable '{executablePath}'.");
        }
    }

    private static string ResolveInstalledExecutablePath(string identityName)
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
            throw new InvalidOperationException(
                $"SalmonEgg MSIX is not installed or InstallLocation could not be resolved. {error}".Trim());
        }

        var executablePath = Path.Combine(output, $"{ProcessName}.exe");
        if (!File.Exists(executablePath))
        {
            throw new InvalidOperationException(
                $"Installed SalmonEgg executable '{executablePath}' was not found.");
        }

        return executablePath;
    }

    private static Process WaitForProcess(string processName, TimeSpan timeout)
    {
        return RetryUntil(
            () => Process.GetProcessesByName(processName)
                .OrderByDescending(process => process.StartTime)
                .FirstOrDefault(),
            process => process != null,
            timeout,
            $"Timed out waiting for process '{processName}'.")!;
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
}
