using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace SalmonEgg.GuiTests.Windows;

internal sealed class WindowsGuiAppSession : IDisposable
{
    private const string ProcessName = "SalmonEgg";
    private readonly UIA3Automation _automation;
    private readonly Application _application;

    private WindowsGuiAppSession(Application application, UIA3Automation automation, Window mainWindow)
    {
        _application = application;
        _automation = automation;
        MainWindow = mainWindow;
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
            window => window != null && !window.IsOffscreen,
            TimeSpan.FromSeconds(20),
            "Timed out waiting for SalmonEgg main window.");

        return new WindowsGuiAppSession(application, automation, mainWindow!);
    }

    public AutomationElement FindByAutomationId(string automationId, TimeSpan? timeout = null)
    {
        return RetryUntil(
            () => MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(automationId)),
            element => element != null,
            timeout ?? TimeSpan.FromSeconds(10),
            $"AutomationId '{automationId}' was not found.")!;
    }

    public AutomationElement? FindFirstByAutomationIdPrefix(string prefix, TimeSpan? timeout = null)
    {
        return RetryUntil(
            () => MainWindow
                .FindAllDescendants()
                .FirstOrDefault(element => element.AutomationId?.StartsWith(prefix, StringComparison.Ordinal) == true),
            element => element != null,
            timeout ?? TimeSpan.FromSeconds(5),
            $"AutomationId prefix '{prefix}' was not found.");
    }

    public void InvokeButton(string automationId)
    {
        var button = FindByAutomationId(automationId);
        ActivateElement(button);
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

    public void Dispose()
    {
        _automation.Dispose();
        _application.Dispose();
    }

    private static void LaunchInstalledMsix(MsixManifestInfo manifest)
    {
        var packageFamilyName = ResolveInstalledPackageFamilyName(manifest.IdentityName);
        var aumid = $"{packageFamilyName}!{manifest.ApplicationId}";
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"shell:AppsFolder\\{aumid}",
            UseShellExecute = true
        });
    }

    private static string ResolveInstalledPackageFamilyName(string identityName)
    {
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
        var error = process.StandardError.ReadToEnd().Trim();
        process.WaitForExit();

        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
        {
            throw new InvalidOperationException(
                $"SalmonEgg MSIX is not installed or PackageFamilyName could not be resolved. {error}".Trim());
        }

        return output;
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

    private static T? RetryUntil<T>(
        Func<T?> probe,
        Func<T?, bool> success,
        TimeSpan timeout,
        string failureMessage) where T : class
    {
        var deadline = DateTime.UtcNow + timeout;
        T? last = null;

        while (DateTime.UtcNow < deadline)
        {
            last = probe();
            if (success(last))
            {
                return last;
            }

            Thread.Sleep(250);
        }

        throw new TimeoutException(failureMessage);
    }
}
