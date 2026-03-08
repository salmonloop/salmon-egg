using System;
using Microsoft.Extensions.Logging;
using Uno.Resizetizer;


namespace UnoAcpClient;

/// <summary>
/// Requirements: 5.2, 6.1
/// </summary>
public partial class App : global::Microsoft.UI.Xaml.Application
{
    public static IServiceProvider ServiceProvider { get; private set; } = null!;

    public App()
    {
        this.InitializeComponent();

        var services = new global::Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddUnoAcpClient();
        ServiceProvider = services.BuildServiceProvider();
    }

    protected Window? MainWindow { get; private set; }

    protected override void OnLaunched(global::Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        MainWindow = new Window();

#if WINDOWS
        // Native WinUI 3 backdrop (Mica) - requires content backgrounds to be transparent.
        MainWindow.SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
#endif

#if DEBUG
        // MainWindow.UseStudio(); // Requires Uno Studio configuration
#endif

        if (MainWindow.Content is not Frame rootFrame)
        {
            rootFrame = new Frame { AllowDrop = false };
            MainWindow.Content = rootFrame;
            rootFrame.NavigationFailed += OnNavigationFailed;
        }

        if (rootFrame.Content == null)
        {
            rootFrame.Navigate(typeof(MainPage), args.Arguments);
        }

        MainWindow.SetWindowIcon();
        MainWindow.Activate();
    }

    void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
    {
        throw new InvalidOperationException($"Failed to load {e.SourcePageType.FullName}: {e.Exception}");
    }

    public static void InitializeLogging()
    {
#if DEBUG
        var factory = LoggerFactory.Create(builder =>
        {
#if __WASM__
            builder.AddProvider(new global::Uno.Extensions.Logging.WebAssembly.WebAssemblyConsoleLoggerProvider());
#elif __IOS__
            builder.AddProvider(new global::Uno.Extensions.Logging.OSLogLoggerProvider());
            builder.AddConsole();
#else
            builder.AddConsole();
#endif
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddFilter("Uno", LogLevel.Warning);
            builder.AddFilter("Uno.UI.Runtime.Skia.Win32.Win32DragDropExtension", LogLevel.None);
            builder.AddFilter("Windows", LogLevel.Warning);
            builder.AddFilter("Microsoft", LogLevel.Warning);
        });
        global::Uno.Extensions.LogExtensionPoint.AmbientLoggerFactory = factory;
#if HAS_UNO
        global::Uno.UI.Adapter.Microsoft.Extensions.Logging.LoggingAdapter.Initialize();
#endif
#endif
    }
}
