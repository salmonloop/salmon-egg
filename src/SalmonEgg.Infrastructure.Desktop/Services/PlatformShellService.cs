using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Infrastructure.Services;

public sealed class PlatformShellService : IPlatformShellService
{
    private readonly IPlatformCapabilityService _capabilities;
    private readonly IPlatformRuntimeCapabilityProbe _runtimeProbe;

    public PlatformShellService(IPlatformCapabilityService capabilities)
        : this(capabilities, new PlatformRuntimeCapabilityProbe())
    {
    }

    public PlatformShellService(
        IPlatformCapabilityService capabilities,
        IPlatformRuntimeCapabilityProbe runtimeProbe)
    {
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        _runtimeProbe = runtimeProbe ?? throw new ArgumentNullException(nameof(runtimeProbe));
    }

    public Task<bool> OpenFolderAsync(string path) => OpenWithShellAsync(path);

    public Task<bool> OpenFileAsync(string path) => OpenWithShellAsync(path);

    public Task<bool> OpenUriAsync(Uri uri)
    {
        if (uri == null)
        {
            return Task.FromResult(false);
        }

        return LaunchShellTargetAsync(uri.AbsoluteUri, _runtimeProbe);
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

    public async Task<string?> ReadClipboardTextAsync()
    {
#if WINDOWS || WINDOWS_UWP
        try
        {
            var content = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
            if (!content.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text))
            {
                return null;
            }

            return await content.GetTextAsync().AsTask().ConfigureAwait(false);
        }
        catch
        {
        }
#endif
        return null;
    }

    private Task<bool> OpenWithShellAsync(string path)
    {
        if (!_capabilities.SupportsExternalFileOpen || string.IsNullOrWhiteSpace(path))
        {
            return Task.FromResult(false);
        }

        return LaunchShellTargetAsync(path, _runtimeProbe);
    }

    private static Task<bool> LaunchShellTargetAsync(
        string target,
        IPlatformRuntimeCapabilityProbe? runtimeProbe = null)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return Task.FromResult(false);
        }

        try
        {
            Process.Start(CreateLaunchProcessStartInfo(target, runtimeProbe));
            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    internal static ProcessStartInfo CreateLaunchProcessStartInfo(
        string target,
        IPlatformRuntimeCapabilityProbe? runtimeProbe = null)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true
            };
        }

        var opener = runtimeProbe?.ResolveExternalFileOpener()
            ?? new PlatformRuntimeCapabilityProbe().ResolveExternalFileOpener()
            ?? (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "open" : "xdg-open");

        var startInfo = new ProcessStartInfo
        {
            FileName = opener,
            UseShellExecute = false
        };

        if (IsGioOpenCommand(opener))
        {
            startInfo.ArgumentList.Add("open");
        }

        startInfo.ArgumentList.Add(SanitizeUnixShellTarget(target));
        return startInfo;
    }

    private static bool IsGioOpenCommand(string opener)
    {
        var fileName = System.IO.Path.GetFileName(opener);
        return string.Equals(fileName, "gio", StringComparison.Ordinal);
    }

    private static string SanitizeUnixShellTarget(string target)
        => target.StartsWith("-", StringComparison.Ordinal) ? $"./{target}" : target;
}
