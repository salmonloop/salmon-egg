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

       host.RunAsync().GetAwaiter().GetResult();
   }
}
