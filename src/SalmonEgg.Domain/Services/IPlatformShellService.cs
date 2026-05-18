using System;
using System.Threading.Tasks;

namespace SalmonEgg.Domain.Services;

public interface IPlatformShellService
{
    Task<bool> OpenFolderAsync(string path);

    Task<bool> OpenFileAsync(string path);

    Task<bool> OpenUriAsync(Uri uri);

    Task<bool> CopyToClipboardAsync(string text);
}
