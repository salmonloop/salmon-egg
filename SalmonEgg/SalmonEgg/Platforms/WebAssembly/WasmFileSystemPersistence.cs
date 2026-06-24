#if __WASM__
using System;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Infrastructure.Storage;
using Windows.Storage;

namespace SalmonEgg.Platforms.WebAssembly;

[SupportedOSPlatform("browser")]
public sealed partial class WasmFileSystemPersistence : IFileSystemPersistence
{
    private const string StorageModuleName = "salmon-egg-wasm-storage.js";

    private static readonly SemaphoreSlim StorageModuleLock = new(1, 1);
    private static readonly string StorageModuleUrl = ResolveStorageModuleUrl();
    private static JSObject? _storageModule;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await EnsurePersistentLocalFolderInitializedAsync().ConfigureAwait(false);
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
                .ImportAsync(StorageModuleName, StorageModuleUrl, cancellationToken)
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

    private static Task EnsurePersistentLocalFolderInitializedAsync()
        => ApplicationData.Current.LocalFolder.CreateFolderAsync("SalmonEgg", CreationCollisionOption.OpenIfExists).AsTask();

    private static string ResolveStorageModuleUrl()
    {
        var appBase = NormalizePathSegment(Environment.GetEnvironmentVariable("UNO_BOOTSTRAP_APP_BASE"));
        if (string.IsNullOrWhiteSpace(appBase))
        {
            return "./" + StorageModuleName;
        }

        var webAppBasePath = NormalizePathSegment(Environment.GetEnvironmentVariable("UNO_BOOTSTRAP_WEBAPP_BASE_PATH"));
        return string.IsNullOrWhiteSpace(webAppBasePath)
            ? $"/{appBase}/_framework/{StorageModuleName}"
            : $"/{webAppBasePath}/{appBase}/_framework/{StorageModuleName}";
    }

    private static string NormalizePathSegment(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().Trim('/');
}
#endif
