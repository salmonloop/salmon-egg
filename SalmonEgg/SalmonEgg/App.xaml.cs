using System;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using SalmonEgg.Domain.Services;
using SalmonEgg.Infrastructure.Storage;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Models;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.ViewModels.Settings;

namespace SalmonEgg;

/// <summary>
/// Requirements: 5.2, 6.1
/// </summary>
public partial class App : global::Microsoft.UI.Xaml.Application
{
    public static IServiceProvider ServiceProvider { get; private set; } = null!;

    public static Microsoft.UI.Xaml.Window? MainWindowInstance => (Current as App)?.MainWindow;

    private readonly SalmonEgg.Domain.Services.IAppMaintenanceService? _maintenanceService;
    private readonly Presentation.Services.WindowBackdropService? _windowBackdropService;
    private readonly ILogger<App>? _startupLogger;

    // Set once the shell completes its first navigation; drives the unhandled-exception
    // policy (startup failures must exit with logs instead of hanging a blank window).
    private volatile bool _isShellFirstFrameReady;

    // Diagnostic-only boot trail. Conditional so Release does not evaluate message
    // arguments or retain call sites; body remains DEBUG-gated for the file write.
    [Conditional("DEBUG")]
    internal static void BootLog(string message)
    {
#if DEBUG
        try
        {
            var dir = SalmonEggPaths.GetAppDataRootPath();
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "boot.log"), $"[{DateTime.Now:O}] {message}{Environment.NewLine}");
        }
        catch
        {
            // Boot diagnostics must never disrupt startup.
        }
#endif
    }

    internal static void ReloadMainShell()
    {
        try
        {
            var app = Current as App;
            var window = app?.MainWindow;
            if (window?.DispatcherQueue == null)
            {
                return;
            }

            _ = window.DispatcherQueue.TryEnqueue(() =>
            {
                if (window.Content is Frame frame)
                {
                    // BrowserWasm retains the current Page for same-frame navigation even
                    // after clearing Frame.Content. Replacing the root Frame gives every
                    // x:Uid resource a fresh native load after the language override.
                    frame.NavigationFailed -= app!.OnNavigationFailed;
                    frame.Navigated -= app.OnRootFrameFirstNavigated;

                    var replacementFrame = new Frame { AllowDrop = false };
                    // The shell theme is owned by the root content element, so the replacement
                    // must inherit it before it renders; otherwise reloaded content falls back
                    // to the default theme brushes.
                    replacementFrame.RequestedTheme = frame.RequestedTheme;
                    replacementFrame.NavigationFailed += app.OnNavigationFailed;
                    replacementFrame.Navigated += app.OnRootFrameFirstNavigated;
                    window.Content = replacementFrame;
                    replacementFrame.Navigate(typeof(MainPage), null, UiMotionController.Current.CreateNavigationTransitionInfo());
                }
            });
        }
        catch (Exception ex)
        {
            // Shell reload is best-effort, but the failure must stay visible in release logs.
            (Current as App)?._startupLogger?.LogError(ex, "Failed to schedule main shell reload.");
            BootLog("ReloadMainShell failed: " + ex);
        }
    }

    public App()
    {
        var services = new global::Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSalmonEgg();
        ServiceProvider = services.BuildServiceProvider();

        // Resolve DI dependencies before InitializeComponent() so x:Bind has stable inputs.
        _maintenanceService = ServiceProvider.GetService<SalmonEgg.Domain.Services.IAppMaintenanceService>();
        _windowBackdropService = ServiceProvider.GetService<Presentation.Services.WindowBackdropService>();
        _startupLogger = ServiceProvider.GetService<ILogger<App>>();

        this.InitializeComponent();

#if __SKIA__
        // Skia uses the same WinUI resource keys, but a few template defaults (e.g., negative margins used for pixel
        // snapping) can be clipped by the renderer. Load a small host-specific override dictionary only on Skia.
        TryAddSkiaThemeOverrides();
#endif
#if __UNO_SKIA__ || __WASM__
        // Uno's NumberBox focus template has a renderer-specific theme mismatch on Skia and BrowserWasm. Keep this
        // workaround outside the Windows WinUI 3 path until unoplatform/uno#24021 is fixed in our dependency line.
        TryApplyUnoNumberBoxThemeOverride();
#endif

        this.UnhandledException += (_, e) =>
        {
            // Log first: release builds must never swallow a crash silently.
            _startupLogger?.LogCritical(
                e.Exception,
                "Unhandled UI exception. ShellFirstFrameReady={ShellFirstFrameReady}",
                _isShellFirstFrameReady);
            BootLog("App.UnhandledException: " + e.Exception);
            // Before the first shell frame is ready, swallowing leaves a blank hung window;
            // let the process exit with logs instead. Afterwards keep the shell alive so a
            // single stray exception cannot take down the whole app.
            e.Handled = _isShellFirstFrameReady;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            _startupLogger?.LogCritical(
                e.ExceptionObject as Exception,
                "Unhandled AppDomain exception. IsTerminating={IsTerminating}",
                e.IsTerminating);
            BootLog("AppDomain.UnhandledException: " + e.ExceptionObject);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            _startupLogger?.LogError(e.Exception, "Unobserved task exception.");
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

#if __UNO_SKIA__ || __WASM__
    private void TryApplyUnoNumberBoxThemeOverride()
    {
        try
        {
            var overrides = new Microsoft.UI.Xaml.ResourceDictionary
            {
                Source = new Uri("ms-appx:///Styles/Skia/UnoNumberBoxThemeOverrides.xaml")
            };

            Resources.MergedDictionaries.Add(overrides);
            if (overrides["UnoNumberBoxStyleOverride"] is not Microsoft.UI.Xaml.Style numberBoxStyle)
            {
                throw new InvalidOperationException("The Uno NumberBox style override was not found.");
            }

            Resources[typeof(Microsoft.UI.Xaml.Controls.NumberBox)] = numberBoxStyle;
        }
        catch (Exception ex)
        {
            // Best-effort; the app should still run with the framework default if the optional resource is unavailable.
            _startupLogger?.LogWarning(ex, "Failed to apply the Uno NumberBox theme override.");
        }
    }
#endif

    protected Microsoft.UI.Xaml.Window? MainWindow { get; private set; }

    protected override async void OnLaunched(global::Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        BootLog("OnLaunched: start");
        MainWindow = new Microsoft.UI.Xaml.Window();
        BootLog("OnLaunched: window created");

        if (MainWindow.Content is not Frame rootFrame)
        {
            rootFrame = new Frame { AllowDrop = false };
            MainWindow.Content = rootFrame;
            rootFrame.NavigationFailed += OnNavigationFailed;
            rootFrame.Navigated += OnRootFrameFirstNavigated;
            BootLog("OnLaunched: root frame created");
        }

        IUiRuntimeService? uiRuntimeService = null;
        try
        {
            uiRuntimeService = ServiceProvider.GetService<IUiRuntimeService>();
            uiRuntimeService?.InitializeAnimations();
            BootLog("OnLaunched: motion policy initialized");
        }
        catch (Exception ex)
        {
            // 启动韧性:初始化失败不阻断启动,但 Release 下也必须留下可诊断的日志。
            _startupLogger?.LogWarning(ex, "Failed to initialize motion policy during launch.");
            BootLog("OnLaunched: failed to initialize motion policy");
        }

        AppPreferencesViewModel? preferences = null;
        try
        {
            var cloudSync = ServiceProvider.GetService<ICloudConfigSyncCoordinator>();
            if (cloudSync is not null)
            {
                await cloudSync.InitializeAsync();
                BootLog("OnLaunched: cloud config sync initialized");
            }
        }
        catch (Exception ex)
        {
            _startupLogger?.LogWarning(ex, "Cloud config sync initialization failed during launch.");
            BootLog("OnLaunched: cloud config sync initialization failed");
        }

        try
        {
            preferences = ServiceProvider.GetService<AppPreferencesViewModel>();
            if (preferences != null)
            {
                await preferences.InitializeAsync();
                if (uiRuntimeService != null)
                {
                    uiRuntimeService.SetAnimationsEnabled(preferences.IsAnimationEnabled);
                }
                else
                {
                    UiMotionController.Current.IsAnimationEnabled = preferences.IsAnimationEnabled;
                }

                _ = ServiceProvider.GetService<ConfigProjectionReloadCoordinator>();
                BootLog("OnLaunched: config projection reload coordinator initialized");
            }
        }
        catch (Exception ex)
        {
            _startupLogger?.LogWarning(ex, "Failed to initialize preferences during launch.");
            BootLog("OnLaunched: failed to initialize preferences");
        }

        try
        {
            _windowBackdropService?.Attach(MainWindow);
            BootLog("OnLaunched: backdrop service attached");
        }
        catch (Exception ex)
        {
            _startupLogger?.LogWarning(ex, "Window backdrop service attach failed during launch.");
            BootLog("OnLaunched: backdrop service attach failed");
        }

        if (rootFrame.Content == null)
        {
            rootFrame.Navigate(typeof(MainPage), args.Arguments, UiMotionController.Current.CreateNavigationTransitionInfo());
            BootLog("OnLaunched: navigated to MainPage");
        }

        // Best-effort cache cleanup based on retention settings.
        try
        {
            if (preferences != null && _maintenanceService != null)
            {
                var cacheRetentionDays = preferences.CacheRetentionDays;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _maintenanceService.CleanupCacheAsync(cacheRetentionDays).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _startupLogger?.LogWarning(ex, "Background cache cleanup failed.");
                    }
                });
            }
        }
        catch (Exception ex)
        {
            _startupLogger?.LogWarning(ex, "Failed to schedule cache cleanup during launch.");
        }

        // Shell identity (taskbar/Alt+Tab title + native window icon) must be in place before the
        // first activation so the shell never observes the framework defaults.
        WindowShellIdentity.Apply(MainWindow);
        MainWindow.Activate();
        BootLog("OnLaunched: window activated");
    }

    /// <summary>
    /// Runs application teardown before the process ends.
    /// </summary>
    /// <remarks>
    /// Every close path routes here so that runtime state is flushed exactly once by its owning
    /// workflow. Hosts must await this before letting the window close; the window closing itself is
    /// not a durability boundary because the flush is asynchronous.
    /// </remarks>
    internal static Task ShutdownRuntimeAsync()
    {
        var shutdown = ServiceProvider?.GetService<IApplicationShutdownWorkflow>();
        return shutdown is null
            ? Task.CompletedTask
            : shutdown.ShutdownAsync();
    }

    void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
    {
        throw new InvalidOperationException($"Failed to load {e.SourcePageType.FullName}: {e.Exception}");
    }

    private void OnRootFrameFirstNavigated(object sender, NavigationEventArgs e)
    {
        // One-shot: only the first successful shell navigation flips the crash policy.
        if (sender is Frame frame)
        {
            frame.Navigated -= OnRootFrameFirstNavigated;
        }

        _isShellFirstFrameReady = true;
        BootLog("OnLaunched: first shell frame ready");
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
#if __WASM__
            builder.AddProvider(new global::Uno.Extensions.Logging.WebAssembly.WebAssemblyConsoleLoggerProvider());
#else
            builder.AddConsole();
#endif
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
