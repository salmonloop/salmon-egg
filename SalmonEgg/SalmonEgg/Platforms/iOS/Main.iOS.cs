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
            .App(CreateApp)
            .UseAppleUIKit()
            .Build();

        host.Run();
    }

    // Uno 6.7.103 selects UIKit's app delegate via factory.Method.ReturnType. A Func<Application>
    // lambda has the base return type; this method preserves App (unoplatform/uno@3f8aa845).
    // Keep until the upstream hosting fix (unoplatform/uno@54b88197) reaches our dependency line.
    private static App CreateApp() => new();
}
