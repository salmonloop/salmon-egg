#if __WASM__
using System.Runtime.Versioning;
using SalmonEgg.Presentation.Core.Services.Input;

namespace SalmonEgg.Platforms.WebAssembly;

[SupportedOSPlatform("browser")]
public sealed class WasmGamepadDiagnosticsService : IGamepadDiagnosticsService
{
    public GamepadDiagnosticsSnapshot GetCurrentSnapshot()
        => WasmGamepadSnapshotReader.ReadSnapshot();
}
#endif
