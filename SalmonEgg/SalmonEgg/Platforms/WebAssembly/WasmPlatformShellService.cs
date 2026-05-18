using System;
using System.Threading.Tasks;
using SalmonEgg.Domain.Services;
using SalmonEgg.Infrastructure.Services;

namespace SalmonEgg.Platforms.WebAssembly;

public sealed class WasmPlatformShellService : IPlatformShellService
{
    private readonly UnsupportedPlatformShellService _unsupported = new();

    public Task<bool> OpenFolderAsync(string path) => _unsupported.OpenFolderAsync(path);

    public Task<bool> OpenFileAsync(string path) => _unsupported.OpenFileAsync(path);

    public async Task<bool> OpenUriAsync(Uri uri)
    {
        if (uri == null)
        {
            return false;
        }

        try
        {
            return await global::Windows.System.Launcher.LaunchUriAsync(uri).AsTask().ConfigureAwait(false);
        }
        catch
        {
            return false;
        }
    }

    public Task<bool> CopyToClipboardAsync(string text) => _unsupported.CopyToClipboardAsync(text);
}
