using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Infrastructure.Services;

public sealed class PlatformShellService : IPlatformShellService
{
    public Task OpenFolderAsync(string path)
    {
        TryOpenWithShell(path);
        return Task.CompletedTask;
    }

    public Task OpenFileAsync(string path)
    {
        TryOpenWithShell(path);
        return Task.CompletedTask;
    }

    public Task<bool> CopyToClipboardAsync(string text)
    {
#if WINDOWS || WINDOWS_UWP
        try
        {
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(text ?? string.Empty);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            return Task.FromResult(true);
        }
        catch
        {
        }
#endif
        return Task.FromResult(false);
    }

    internal static ProcessStartInfo CreateShellExecuteInfo(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                };
            }

            string safePath = path;
            if (safePath.StartsWith("-", StringComparison.Ordinal))
            {
                safePath = "./" + safePath;
            }

            var psi = new ProcessStartInfo
            {
                UseShellExecute = false
            };

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                psi.FileName = "open";
            }
            else
            {
                psi.FileName = "xdg-open";
            }

            psi.ArgumentList.Add(safePath);
            return psi;
        }
        catch
        {
            return null;
        }
    }

    private static void TryOpenWithShell(string path)
    {
        var psi = CreateShellExecuteInfo(path);
        if (psi != null)
        {
            try
            {
                Process.Start(psi);
            }
            catch
            {
            }
        }
    }
}
