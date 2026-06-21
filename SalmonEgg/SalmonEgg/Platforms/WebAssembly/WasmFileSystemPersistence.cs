#if __WASM__
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Infrastructure.Storage;

namespace SalmonEgg.Platforms.WebAssembly;

[SupportedOSPlatform("browser")]
public sealed partial class WasmFileSystemPersistence : IFileSystemPersistence
{
    private const string StorageModuleName = "salmon-egg-wasm-storage.js";

    private static readonly SemaphoreSlim StorageModuleLock = new(1, 1);
    private static JSObject? _storageModule;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await EnsureStorageModuleImportedAsync(cancellationToken).ConfigureAwait(false);
        await LoadFileSystemAsync();
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await EnsureStorageModuleImportedAsync(cancellationToken).ConfigureAwait(false);
        await FlushFileSystemAsync();
    }

    private static async Task EnsureStorageModuleImportedAsync(CancellationToken cancellationToken)
    {
        if (_storageModule != null)
        {
            return;
        }

        await StorageModuleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_storageModule != null)
            {
                return;
            }

            _storageModule = await JSHost
                .ImportAsync(StorageModuleName, StorageModuleName, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            StorageModuleLock.Release();
        }
    }

    [JSImport("loadFileSystem", "salmon-egg-wasm-storage.js")]
    internal static partial Task LoadFileSystemAsync();

    [JSImport("flushFileSystem", "salmon-egg-wasm-storage.js")]
    internal static partial Task FlushFileSystemAsync();
}
#endif
