using System;
using Uno.UI.Hosting;

namespace SalmonEgg;

internal class Program
{
    [STAThread]
    static void Main(string[] args)
   {
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
   }
}
