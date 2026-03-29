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

    private static void TryOpenWithShell(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
                return;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", path);
                return;
            }

            Process.Start("xdg-open", path);
        }
        catch
        {
        }
    }
}
