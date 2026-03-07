using Uno.UI.Hosting;
using System.Threading.Tasks;

namespace UnoAcpClient;

internal class Program
{
    [STAThread]
    static async Task Main(string[] args)
   {
       App.InitializeLogging();
       var host = UnoPlatformHostBuilder.Create()
           .App(() => new App())
           .UseX11()
           .UseLinuxFrameBuffer()
           .UseMacOS()
           .UseWin32()
           .Build();
       await host.RunAsync();
   }
}
