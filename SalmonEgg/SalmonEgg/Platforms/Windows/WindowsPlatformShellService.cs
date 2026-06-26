#if WINDOWS
using System;
using System.Threading.Tasks;
using SalmonEgg.Domain.Services;
using SalmonEgg.Infrastructure.Services;
using Windows.ApplicationModel.DataTransfer;

namespace SalmonEgg.Platforms.Windows;

public sealed class WindowsPlatformShellService : IPlatformShellService
{
    private readonly PlatformShellService _desktopShell;

    public WindowsPlatformShellService(IPlatformCapabilityService capabilities)
    {
        _desktopShell = new PlatformShellService(capabilities ?? throw new ArgumentNullException(nameof(capabilities)));
    }

    public Task<bool> OpenFolderAsync(string path) => _desktopShell.OpenFolderAsync(path);

    public Task<bool> OpenFileAsync(string path) => _desktopShell.OpenFileAsync(path);

    public Task<bool> OpenUriAsync(Uri uri) => _desktopShell.OpenUriAsync(uri);

    public Task<bool> CopyToClipboardAsync(string text)
    {
        try
        {
            var package = new DataPackage();
            package.SetText(text ?? string.Empty);
            Clipboard.SetContent(package);
            Clipboard.Flush();
            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    public async Task<string?> ReadClipboardTextAsync()
    {
        try
        {
            var content = Clipboard.GetContent();
            if (!content.Contains(StandardDataFormats.Text))
            {
                return null;
            }

            return await content.GetTextAsync().AsTask().ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }
}
#endif
