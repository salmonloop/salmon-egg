using System.Runtime.Versioning;
using Uno.UI.Hosting;

namespace SalmonEgg;

[SupportedOSPlatform("browser")]
public class Program
{
    public static async Task Main(string[] args)
    {
        App.InitializeLogging();
        await Platforms.WebAssembly.WasmElicitationUriLauncher.InitializeAsync();

        var host = UnoPlatformHostBuilder.Create()
            .App(() => new App())
            .UseWebAssembly()
            .Build();

        await host.RunAsync();
    }
}
