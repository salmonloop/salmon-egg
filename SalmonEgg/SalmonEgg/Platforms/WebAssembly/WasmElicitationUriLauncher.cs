#if __WASM__
using System;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Platforms.WebAssembly;

[SupportedOSPlatform("browser")]
public sealed partial class WasmElicitationUriLauncher : IExternalUriLauncher
{
    private const string ModuleName = "salmon-egg-wasm-shell.js";
    private static bool _isReady;

    public bool IsSupported => _isReady && IsSupportedInterop();

    public static async Task InitializeAsync()
    {
        try
        {
            await JSHost.ImportAsync(ModuleName, WasmModuleUrlResolver.Resolve(ModuleName)).ConfigureAwait(false);
            _isReady = true;
        }
        catch
        {
            // Missing modules keep the capability off; never defer the import until consent.
        }
    }

    public Task<ExternalUriOpenResult> OpenAsync(ExternalUriTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(ExternalUriOpenResult.Cancelled);
        }

        if (!IsSupported)
        {
            return Task.FromResult(ExternalUriOpenResult.Unavailable);
        }

        try
        {
            return Task.FromResult(OpenInterop(target.FullUrl)
                ? ExternalUriOpenResult.Dispatched : ExternalUriOpenResult.Blocked);
        }
        catch
        {
            // Platform exceptions may embed the URL; expose a bounded result without retaining them.
            return Task.FromResult(ExternalUriOpenResult.Failed);
        }
    }

    [JSImport("supportsExternalElicitation", ModuleName)]
    private static partial bool IsSupportedInterop();

    [JSImport("openExternalElicitation", ModuleName)]
    private static partial bool OpenInterop(string url);
}
#endif
