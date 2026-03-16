using System;
using System.Linq;
using Microsoft.UI.Xaml.Controls;
#if HAS_UNO && !WINDOWS
using Uno.UI;
#endif
using SalmonEgg.Presentation.Models;
using SalmonEgg.Infrastructure.Storage;

namespace SalmonEgg;

/// <summary>
/// Requirements: 5.2, 6.1
/// </summary>
public partial class App : global::Microsoft.UI.Xaml.Application
{
    public static IServiceProvider ServiceProvider { get; private set; } = null!;

    public static Microsoft.UI.Xaml.Window? MainWindowInstance => (Current as App)?.MainWindow;
    private static readonly Uri ReducedMotionDictionarySource = new("ms-appx:///Styles/ReducedMotion.xaml");
    private static Microsoft.UI.Xaml.ResourceDictionary? _reducedMotionDictionary;
#if HAS_UNO && !WINDOWS
    private static TimeSpan? _defaultThemeAnimationDuration;
#endif

    private readonly SalmonEgg.Domain.Services.IAppSettingsService? _appSettingsService;
    private readonly SalmonEgg.Domain.Services.IAppMaintenanceService? _maintenanceService;

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

    internal static void ReloadMainShell()
    {
        try
        {
            var window = MainWindowInstance;
            if (window?.DispatcherQueue == null)
            {
                return;
            }

            _ = window.DispatcherQueue.TryEnqueue(() =>
            {
                if (window.Content is Frame frame)
                {
                    frame.BackStack.Clear();
                    frame.Navigate(typeof(MainPage));
                }
            });
        }
        catch
        {
        }
    }

    internal static void ApplyReducedMotion(bool reducedMotionEnabled)
    {
        try
        {
            if (Current?.Resources is not Microsoft.UI.Xaml.ResourceDictionary resources)
            {
                return;
            }

#if HAS_UNO && !WINDOWS
            try
            {
                _defaultThemeAnimationDuration ??= FeatureConfiguration.ThemeAnimation.DefaultThemeAnimationDuration;
                FeatureConfiguration.ThemeAnimation.DefaultThemeAnimationDuration = reducedMotionEnabled
                    ? TimeSpan.Zero
                    : _defaultThemeAnimationDuration.Value;
            }
            catch
            {
            }
#endif

            var dictionaries = resources.MergedDictionaries;
            var existing = dictionaries.FirstOrDefault(d => d.Source == ReducedMotionDictionarySource);

            if (reducedMotionEnabled)
            {
                if (existing == null)
                {
                    _reducedMotionDictionary ??= new Microsoft.UI.Xaml.ResourceDictionary
                    {
                        Source = ReducedMotionDictionarySource
                    };
                    dictionaries.Add(_reducedMotionDictionary);
                }
            }
            else if (existing != null)
            {
                dictionaries.Remove(existing);
            }
        }
        catch
        {
        }
    }

    public App()
    {
        var services = new global::Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSalmonEgg();
        ServiceProvider = services.BuildServiceProvider();

        // Resolve DI dependencies before InitializeComponent() so x:Bind has stable inputs.
        _appSettingsService = ServiceProvider.GetService<SalmonEgg.Domain.Services.IAppSettingsService>();
        _maintenanceService = ServiceProvider.GetService<SalmonEgg.Domain.Services.IAppMaintenanceService>();

        this.InitializeComponent();

#if __SKIA__
        // Skia uses the same WinUI resource keys, but a few template defaults (e.g., negative margins used for pixel
        // snapping) can be clipped by the renderer. Load a small host-specific override dictionary only on Skia.
        TryAddSkiaThemeOverrides();
#endif

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
    }

#if __SKIA__
    private void TryAddSkiaThemeOverrides()
    {
        try
        {
            Resources.MergedDictionaries.Add(new Microsoft.UI.Xaml.ResourceDictionary
            {
                Source = new Uri("ms-appx:///Styles/Skia/SkiaThemeOverrides.xaml")
            });
        }
        catch
        {
            // Best-effort; the app should still run without overrides.
        }
    }
#endif

    protected Microsoft.UI.Xaml.Window? MainWindow { get; private set; }

    protected override async void OnLaunched(global::Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
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

        try
        {
            if (_appSettingsService != null)
            {
                var settings = await _appSettingsService.LoadAsync();
                UiMotion.Current.IsAnimationEnabled = settings.IsAnimationEnabled;
                ApplyReducedMotion(!settings.IsAnimationEnabled);
            }
        }
        catch
        {
            BootLog("OnLaunched: failed to load settings for motion");
        }

        if (rootFrame.Content == null)
        {
            rootFrame.Navigate(typeof(MainPage), args.Arguments);
            BootLog("OnLaunched: navigated to MainPage");
        }

        // Best-effort cache cleanup based on retention settings.
        try
        {
            if (_appSettingsService != null && _maintenanceService != null)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var settings = await _appSettingsService.LoadAsync().ConfigureAwait(false);
                        await _maintenanceService.CleanupCacheAsync(settings.CacheRetentionDays).ConfigureAwait(false);
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

        // Applies the generated icon.ico (from Assets/Icons/icon.svg) to the native window (Desktop/Windows).
#if HAS_UNO
        MainWindow.SetWindowIcon();
#endif
        MainWindow.Activate();
        BootLog("OnLaunched: window activated");
    }

    void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
    {
        throw new InvalidOperationException($"Failed to load {e.SourcePageType.FullName}: {e.Exception}");
    }

    public static void InitializeLogging()
    {
        var factory = LoggerFactory.Create(builder =>
        {
#if DEBUG
#if __WASM__
            builder.AddProvider(new global::Uno.Extensions.Logging.WebAssembly.WebAssemblyConsoleLoggerProvider());
#elif __IOS__
            builder.AddProvider(new global::Uno.Extensions.Logging.OSLogLoggerProvider());
            builder.AddConsole();
#else
            builder.AddConsole();
#endif
            builder.SetMinimumLevel(LogLevel.Information);
#else
            // Keep release logs minimal, but still silence known noisy categories.
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Warning);
#endif
            // Uno RemoteControl is a development-only feature (hot reload / diagnostics). When the
            // server isn't running it will emit noisy error logs; suppress it by default.
            builder.AddFilter("Uno.UI.RemoteControl", LogLevel.None);
            builder.AddFilter("Uno.UI.RemoteControl.RemoteControlClient", LogLevel.None);
            builder.AddFilter("Uno.UI.Runtime.Skia.Win32.Win32DragDropExtension", LogLevel.None);
            // Uno WinUI theme may include Reveal-related setters that are not implemented on all hosts.
            // The runtime safely ignores them, but it can emit noisy "BindingPropertyHelper" errors.
            builder.AddFilter("Uno.UI.DataBinding.BindingPropertyHelper", LogLevel.None);
            builder.AddFilter<Microsoft.Extensions.Logging.Console.ConsoleLoggerProvider>("Uno.UI.DataBinding.BindingPropertyHelper", LogLevel.None);
            builder.AddFilter("Uno", LogLevel.Warning);
            builder.AddFilter("Windows", LogLevel.Warning);
            builder.AddFilter("Microsoft", LogLevel.Warning);
        });
#if HAS_UNO
        global::Uno.Extensions.LogExtensionPoint.AmbientLoggerFactory = factory;
        global::Uno.UI.Adapter.Microsoft.Extensions.Logging.LoggingAdapter.Initialize();
#endif
    }
}
