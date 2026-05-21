using System;
using System.Threading.Tasks;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Infrastructure.Services;

public sealed class UnsupportedPlatformShellService : IPlatformShellService
{
    public Task<bool> OpenFolderAsync(string path) => Task.FromResult(false);

    public Task<bool> OpenFileAsync(string path) => Task.FromResult(false);

    public Task<bool> OpenUriAsync(Uri uri) => Task.FromResult(false);

    public Task<bool> CopyToClipboardAsync(string text) => Task.FromResult(false);

    public Task<string?> ReadClipboardTextAsync() => Task.FromResult<string?>(null);
}
