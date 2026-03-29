using System.Threading.Tasks;

namespace SalmonEgg.Domain.Services;

public interface IPlatformShellService
{
    Task OpenFolderAsync(string path);

    Task OpenFileAsync(string path);

    Task<bool> CopyToClipboardAsync(string text);
}
