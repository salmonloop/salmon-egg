using SalmonEgg.Platforms.iOS;
using UIKit;
using Uno.UI.Hosting;

namespace SalmonEgg.iOS;

public class EntryPoint
{
    // This is the main entry point of the application.
    public static void Main(string[] args)
    {
        App.InitializeLogging();

        // UNUserNotificationCenter reports a tap only through its delegate, and a response that
        // arrives before one is assigned is never redelivered. A tap can launch the process, so the
        // delegate has to exist before the host runs — it parks the response for the shared layers.
        IosSystemNotificationService.InstallActivationDelegate();

        var host = UnoPlatformHostBuilder.Create()
            .App(() => new App())
            .UseAppleUIKit()
            .Build();

        host.Run();
    }
}
