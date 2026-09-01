using System;
using SalmonEgg.Infrastructure.Desktop.AcpSetup;
using SalmonEgg.Infrastructure.Services;
using Uno.UI.Hosting;

namespace SalmonEgg;

internal class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // Answered before anything else runs, including native prerequisites. The ACP wizard recovers the
        // user's real PATH by asking their login shell to start an executable that prints the environment
        // that shell produced, and this head is one of the two executables that answer it — which is what
        // lets the capture work on a machine where only the app is installed. See DesktopPrintEnvironment.
        //
        // Nothing may be initialized first. The prerequisites below dlopen a native rendering library, and
        // this invocation renders nothing; on a machine missing it, an environment probe would fail where a
        // probe has no business needing graphics at all.
        if (DesktopPrintEnvironment.TryGetMarker(args ?? Array.Empty<string>(), out var environmentMarker))
        {
            DesktopPrintEnvironment
                .WriteAsync(environmentMarker, Console.Out)
                .GetAwaiter()
                .GetResult();
            return;
        }

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
