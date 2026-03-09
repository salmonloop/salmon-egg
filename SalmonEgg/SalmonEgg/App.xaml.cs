using System;
using Uno.Resizetizer;
using SalmonEgg.Infrastructure.Storage;

namespace SalmonEgg;

/// <summary>
/// Requirements: 5.2, 6.1
/// </summary>
public partial class App : global::Microsoft.UI.Xaml.Application
{
    public static IServiceProvider ServiceProvider { get; private set; } = null!;

    public static Microsoft.UI.Xaml.Window? MainWindowInstance => (Current as App)?.MainWindow;

    internal static void BootLog(string message)
    {
        try
        {
            var dir = SalmonEggPaths.GetAppDataRootPath();
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "boot.log"), $"[{DateTime.Now:O}] {message}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    public App()
    {
        this.InitializeComponent();

        this.UnhandledException += (_, e) =>
        {
            BootLog("App.UnhandledException: " + e.Exception);
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            BootLog("AppDomain.UnhandledException: " + e.ExceptionObject);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            BootLog("TaskScheduler.UnobservedTaskException: " + e.Exception);
            e.SetObserved();
        };

        var services = new global::Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSalmonEgg();
        ServiceProvider = services.BuildServiceProvider();
    }

    protected Microsoft.UI.Xaml.Window? MainWindow { get; private set; }

    protected override void OnLaunched(global::Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        BootLog("OnLaunched: start");
        MainWindow = new Microsoft.UI.Xaml.Window();
        BootLog("OnLaunched: window created");

#if WINDOWS
        // Native WinUI 3 backdrop. Mica is Windows 11+; fall back to Desktop Acrylic on Windows 10.
        // Avoid hard-failing at startup on older Windows builds.
        try
        {
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            {
                MainWindow.SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
                BootLog("OnLaunched: MicaBackdrop set");
            }
            else if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            {
                MainWindow.SystemBackdrop = new Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop();
                BootLog("OnLaunched: DesktopAcrylicBackdrop set");
            }
        }
        catch
        {
            BootLog("OnLaunched: backdrop set failed");
        }
#endif

#if DEBUG
        // MainWindow.UseStudio(); // Requires Uno Studio configuration
#endif

        if (MainWindow.Content is not Frame rootFrame)
        {
            rootFrame = new Frame { AllowDrop = false };
            MainWindow.Content = rootFrame;
            rootFrame.NavigationFailed += OnNavigationFailed;
            BootLog("OnLaunched: root frame created");
        }

        if (rootFrame.Content == null)
        {
            rootFrame.Navigate(typeof(MainPage), args.Arguments);
            BootLog("OnLaunched: navigated to MainPage");
        }

        // Best-effort cache cleanup based on retention settings.
        try
        {
            var appSettings = ServiceProvider.GetService<SalmonEgg.Domain.Services.IAppSettingsService>();
            var maintenance = ServiceProvider.GetService<SalmonEgg.Domain.Services.IAppMaintenanceService>();
            if (appSettings != null && maintenance != null)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var settings = await appSettings.LoadAsync().ConfigureAwait(false);
                        await maintenance.CleanupCacheAsync(settings.CacheRetentionDays).ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                });
            }
        }
        catch
        {
        }

        // MainWindow.SetWindowIcon();
        MainWindow.Activate();
        BootLog("OnLaunched: window activated");
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
            // Uno RemoteControl is a development-only feature (hot reload / diagnostics). When the
            // server isn't running it will emit noisy error logs; suppress it by default.
            builder.AddFilter("Uno.UI.RemoteControl", LogLevel.None);
            builder.AddFilter("Uno.UI.RemoteControl.RemoteControlClient", LogLevel.None);
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
