using System;
using System.Threading.Tasks;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Presentation.Core.Services;

internal sealed class NoOpPlatformShellService : IPlatformShellService
{
    public static NoOpPlatformShellService Instance { get; } = new();

    private NoOpPlatformShellService()
    {
    }

    public Task<bool> OpenFolderAsync(string path) => Task.FromResult(false);

    public Task<bool> OpenFileAsync(string path) => Task.FromResult(false);

    public Task<bool> OpenUriAsync(Uri uri) => Task.FromResult(false);

    public Task<bool> CopyToClipboardAsync(string text) => Task.FromResult(false);
}
