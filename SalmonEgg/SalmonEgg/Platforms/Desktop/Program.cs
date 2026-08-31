using System;
using SalmonEgg.Infrastructure.Services;
using Uno.UI.Hosting;

namespace SalmonEgg;

internal class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        DesktopNativePrerequisites.Initialize();
        App.InitializeLogging();

        var host = UnoPlatformHostBuilder.Create()
            .App(() => new App())
            .UseX11()
            .UseLinuxFrameBuffer()
            .UseMacOS()
            .UseWin32()
            .Build();

        // Some hosting configurations may reset the ambient logger factory during Build().
        // Re-apply our filters before running to suppress known noisy categories (e.g., RevealBrush setters on Skia).
        App.InitializeLogging();

        host.RunAsync().GetAwaiter().GetResult();

        // The host returning is this head's process boundary: the shell is gone but the process is
        // still alive, so runtime state that is still buffered can be flushed. Blocking is correct
        // here because Main owns the remaining process lifetime.
        App.ShutdownRuntimeAsync().GetAwaiter().GetResult();
    }
}
