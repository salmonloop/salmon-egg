#if __WASM__
using System;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using SalmonEgg.Infrastructure.Client;

namespace SalmonEgg.Platforms.WebAssembly;

[SupportedOSPlatform("browser")]
public sealed class WasmTransportEndpointAccessContext : ITransportEndpointAccessContext
{
    public bool IsBrowserHosted => true;

    public bool IsSecureOrigin
    {
        get
        {
            using var location = JSHost.GlobalThis.GetPropertyAsJSObject("location");
            var protocol = location?.GetPropertyAsString("protocol") ?? string.Empty;
            return string.Equals(protocol, "https:", StringComparison.OrdinalIgnoreCase);
        }
    }
}
#endif
