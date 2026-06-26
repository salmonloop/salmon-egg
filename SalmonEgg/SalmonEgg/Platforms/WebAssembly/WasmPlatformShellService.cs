#if __WASM__
using System;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Services;
using SalmonEgg.Infrastructure.Services;

namespace SalmonEgg.Platforms.WebAssembly;

[SupportedOSPlatform("browser")]
public sealed partial class WasmPlatformShellService : IPlatformShellService
{
    private const string ShellModuleName = "salmon-egg-wasm-shell.js";

    private static readonly SemaphoreSlim ShellModuleLock = new(1, 1);
    private static readonly string ShellModuleUrl = ResolveShellModuleUrl();
    private static JSObject? _shellModule;

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

    public async Task<bool> CopyToClipboardAsync(string text)
    {
        try
        {
            await EnsureShellModuleImportedAsync(CancellationToken.None).ConfigureAwait(false);
            return await CopyToClipboardInteropAsync(text ?? string.Empty).ConfigureAwait(false);
        }
        catch
        {
            return false;
        }
    }

    public async Task<string?> ReadClipboardTextAsync()
    {
        try
        {
            await EnsureShellModuleImportedAsync(CancellationToken.None).ConfigureAwait(false);
            return await ReadClipboardTextInteropAsync().ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    private static async Task EnsureShellModuleImportedAsync(CancellationToken cancellationToken)
    {
        if (_shellModule != null)
        {
            return;
        }

        await ShellModuleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_shellModule != null)
            {
                return;
            }

            _shellModule = await JSHost.ImportAsync(ShellModuleName, ShellModuleUrl, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            ShellModuleLock.Release();
        }
    }

    [JSImport("copyToClipboard", "salmon-egg-wasm-shell.js")]
    internal static partial Task<bool> CopyToClipboardInteropAsync(string text);

    [JSImport("readClipboardText", "salmon-egg-wasm-shell.js")]
    internal static partial Task<string?> ReadClipboardTextInteropAsync();

    private static string ResolveShellModuleUrl()
    {
        var appBase = NormalizePathSegment(Environment.GetEnvironmentVariable("UNO_BOOTSTRAP_APP_BASE"));
        if (string.IsNullOrWhiteSpace(appBase))
        {
            return "./" + ShellModuleName;
        }

        var webAppBasePath = NormalizePathSegment(Environment.GetEnvironmentVariable("UNO_BOOTSTRAP_WEBAPP_BASE_PATH"));
        return string.IsNullOrWhiteSpace(webAppBasePath)
            ? $"/{appBase}/_framework/{ShellModuleName}"
            : $"/{webAppBasePath}/{appBase}/_framework/{ShellModuleName}";
    }

    private static string NormalizePathSegment(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().Trim('/');
}
#endif
