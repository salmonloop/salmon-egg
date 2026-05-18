using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Infrastructure.Services;

public sealed class PlatformShellService : IPlatformShellService
{
    private readonly IPlatformCapabilityService _capabilities;

    public PlatformShellService(IPlatformCapabilityService capabilities)
    {
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
    }

    public Task<bool> OpenFolderAsync(string path) => OpenWithShellAsync(path);

    public Task<bool> OpenFileAsync(string path) => OpenWithShellAsync(path);

    public Task<bool> OpenUriAsync(Uri uri)
    {
        if (uri == null)
        {
            return Task.FromResult(false);
        }

        return LaunchShellTargetAsync(uri.AbsoluteUri);
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

    private Task<bool> OpenWithShellAsync(string path)
    {
        if (!_capabilities.SupportsExternalFileOpen || string.IsNullOrWhiteSpace(path))
        {
            return Task.FromResult(false);
        }

        return LaunchShellTargetAsync(path);
    }

    private static Task<bool> LaunchShellTargetAsync(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return Task.FromResult(false);
        }

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = target,
                    UseShellExecute = true
                });
                return Task.FromResult(true);
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", target);
                return Task.FromResult(true);
            }

            Process.Start("xdg-open", target);
            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }
}
