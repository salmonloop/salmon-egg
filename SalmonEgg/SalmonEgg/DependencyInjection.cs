using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SalmonEgg.Acp.Client;
using SalmonEgg.Application.Services.Acp;
using SalmonEgg.Application.Services.AcpSetup;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Application.Validators;
using SalmonEgg.Domain.Interfaces;
using SalmonEgg.Domain.Interfaces.Storage;
using SalmonEgg.Domain.Interfaces.Transport;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Domain.Services.AcpSetup;
using SalmonEgg.Domain.Services.Security;
using SalmonEgg.Infrastructure.AcpSetup;
using SalmonEgg.Infrastructure.Client;
using SalmonEgg.Infrastructure.Logging;
using SalmonEgg.Infrastructure.Network;
using SalmonEgg.Infrastructure.Services;
using SalmonEgg.Infrastructure.Storage;
using SalmonEgg.Infrastructure.Transport;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Mvux.ShellLayout;
using SalmonEgg.Presentation.Core.Resources;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Core.Services.Input;
using SalmonEgg.Presentation.Core.Services.Navigation;
using SalmonEgg.Presentation.Core.Services.ProjectAffinity;
using SalmonEgg.Presentation.Core.Services.Search;
using SalmonEgg.Presentation.Core.ViewModels.ShellLayout;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.Services.Cloud;
using SalmonEgg.Presentation.Services.Input;
using SalmonEgg.Presentation.ViewModels;
using SalmonEgg.Presentation.ViewModels.Chat;
using SalmonEgg.Presentation.ViewModels.Navigation;
using SalmonEgg.Presentation.ViewModels.Settings;
using SalmonEgg.Presentation.ViewModels.Settings.AcpSetup;
using SalmonEgg.Presentation.ViewModels.Start;
using Serilog;
using Uno.Extensions.Reactive;
using SalmonEgg.Infrastructure.Observability;
#if !__WASM__ && !__ANDROID__ && !__IOS__
using SalmonEgg.Infrastructure.Desktop.AcpSetup;
using SalmonEgg.Infrastructure.Desktop.DependencyInjection;
#endif
#if __WASM__
using SalmonEgg.Platforms.WebAssembly;
using SalmonEgg.Platforms.WebAssembly.Observability;
#elif WINDOWS
using SalmonEgg.Platforms.Windows;
using SalmonEgg.Platforms.Windows.Observability;
#elif __ANDROID__
using SalmonEgg.Platforms.Android;
using SalmonEgg.Platforms.Mobile.Observability;
#elif __IOS__
using SalmonEgg.Platforms.Mobile.Observability;
using SalmonEgg.Platforms.iOS;
#else
using SalmonEgg.Platforms.Desktop;
using SalmonEgg.Platforms.Desktop.Observability;
#endif

namespace SalmonEgg;

/// <summary>
/// Dependency injection container configuration
/// Requirements: 7.5
/// </summary>
public static class DependencyInjection
{
    private const string GuiEnabledEnvVar = "SALMONEGG_GUI";
    private const string GuiSlowSessionLoadMsEnvVar = "SALMONEGG_GUI_SLOW_SESSION_LOAD_MS";

    /// <summary>
    /// Configures all services and dependencies
    /// </summary>
    public static IServiceCollection AddSalmonEgg(this IServiceCollection services)
    {
        services.AddLocalization();
        ConfigureLogging(services);
        ConfigureTelemetry(services);
        RegisterDomainServices(services);
        RegisterInfrastructureServices(services);
        services.AddSingleton<IStringLocalizer<CoreStrings>, UnoCoreStringLocalizer>();
        return services;
    }


    private static void ConfigureLogging(IServiceCollection services)
    {
        var appDataPath = GetAppDataPath();
        var logger = LoggingConfiguration.ConfigureLogging(appDataPath, hostCapabilities: GetLoggingHostCapabilities());
        Log.Logger = logger;
        LogStartupMarker(logger);
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog(logger, dispose: true);
        });
        services.AddSingleton(logger);
    }

    private static void ConfigureTelemetry(IServiceCollection services)
    {
        // 注册平台特定的 Telemetry 导出器工厂
#if __WASM__
        services.AddSingleton<ITelemetryExporterFactory, WasmTelemetryExporterFactory>();
#elif WINDOWS
        services.AddSingleton<ITelemetryExporterFactory, WinUI3TelemetryExporterFactory>();
#elif __ANDROID__ || __IOS__
        services.AddSingleton<ITelemetryExporterFactory, MobileTelemetryExporterFactory>();
#else
        services.AddSingleton<ITelemetryExporterFactory, DesktopTelemetryExporterFactory>();
#endif

        // 支撑 Logs 维度的动态 logger provider：DI 里是稳定实例，内部的 OTel logger factory
        // 由 TelemetryManager 在每次 apply 时替换。单独 build 一个 LoggerProvider 不行——
        // 它收不到 Microsoft.Extensions.Logging 的写入，会造成"有 provider 但日志不上报"。
        services.AddSingleton<DynamicTelemetryLoggerProvider>();

        // 必须同时作为 ILoggerProvider 注册，否则上面那个实例不在日志管线里，OTLP Logs 维度
        // 永远收不到任何业务日志。顺序有硬要求：ConfigureLogging 里的 ClearProviders() 会
        // RemoveAll<ILoggerProvider>()，因此本注册必须发生在 ConfigureLogging 之后。
        services.AddSingleton<ILoggerProvider>(sp => sp.GetRequiredService<DynamicTelemetryLoggerProvider>());

        // 以"禁用"为初始配置注册单例：此处禁止读 AppSettings（那是异步 IO，在 DI 工厂或 App
        // 构造函数里同步阻塞会违反启动副作用所有权约束）。真实配置由 application startup
        // workflow 异步加载后经 ITelemetryRuntime.ApplyAsync 落地，运行时变更走同一入口。
        services.AddSingleton<ITelemetryManager>(sp => new TelemetryManager(
            TelemetrySettings.CreateInactiveBootstrap(),
            sp.GetRequiredService<ITelemetryExporterFactory>(),
            sp.GetRequiredService<DynamicTelemetryLoggerProvider>()));

        services.AddSingleton<ITelemetryRuntime>(sp => new TelemetryRuntime(
            sp.GetRequiredService<ITelemetryManager>(),
            GetPlatformSamplingDefaults,
            sp.GetRequiredService<ILogger<TelemetryRuntime>>(),
            typeof(App).Assembly.GetName().Version?.ToString()));

        // 订阅持久化边界，使任何写入方（设置页保存、云配置恢复）落盘后都立即重建管线。
        services.AddSingleton<TelemetrySettingsProjection>();
    }

    private static SamplingSettings GetPlatformSamplingDefaults()
    {
#if __WASM__
        return SamplingSettings.CreateWasmDefaults();
#elif __ANDROID__ || __IOS__
        return SamplingSettings.CreateMobileDefaults();
#else
        return SamplingSettings.CreateDesktopDefaults();
#endif
    }

    private static LoggingHostCapabilities GetLoggingHostCapabilities()
    {
#if __WASM__
        return LoggingHostCapabilities.BrowserWebAssembly;
#else
        return LoggingHostCapabilities.Desktop;
#endif
    }

    private static void LogStartupMarker(Serilog.ILogger logger)
    {
        var assembly = typeof(App).Assembly;
        var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "unknown";
        var fileVersion = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version
            ?? "unknown";
        var assemblyVersion = assembly.GetName().Version?.ToString() ?? "unknown";

        logger.Information(
            "SalmonEgg startup marker: AssemblyVersion={AssemblyVersion} FileVersion={FileVersion} InformationalVersion={InformationalVersion} ProcessId={ProcessId}",
            assemblyVersion,
            fileVersion,
            informationalVersion,
            Environment.ProcessId);
    }

    private static void RegisterDomainServices(IServiceCollection services)
    {
        // Session Manager
        services.AddSingleton<ISessionManager, Infrastructure.Services.SessionManager>();

        // Path Validator
        services.AddSingleton<IPathValidator, Infrastructure.Services.Security.PathValidator>();

        // Error Logger
        services.AddSingleton<IErrorLogger, ErrorLogger>();

    }

    private static void RegisterInfrastructureServices(IServiceCollection services)
    {
        // Infrastructure Services
        services.AddSingleton<IUiDispatcher>(sp =>
        {
            var queue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            var logger = sp.GetRequiredService<ILogger<WinUiDispatcher>>();
            if (queue == null)
            {
                logger.LogCritical("DispatcherQueue.GetForCurrentThread() returned null during IUiDispatcher resolution. UI marshaling will fail.");
            }
            return new WinUiDispatcher(queue!, logger);
        });
        // The same platform object both posts notifications and reports taps, because the native handle
        // is one and the same. Registering one singleton under both contracts keeps that single owner.
#if WINDOWS
        services.AddSingleton<WindowsSystemNotificationService>();
        services.AddSingleton<ISystemNotificationService>(sp =>
            sp.GetRequiredService<WindowsSystemNotificationService>());
        services.AddSingleton<ISystemNotificationActivationSource>(sp =>
            sp.GetRequiredService<WindowsSystemNotificationService>());
#elif __ANDROID__
        services.AddSingleton<AndroidSystemNotificationService>();
        services.AddSingleton<ISystemNotificationService>(sp =>
            sp.GetRequiredService<AndroidSystemNotificationService>());
        services.AddSingleton<ISystemNotificationActivationSource>(sp =>
            sp.GetRequiredService<AndroidSystemNotificationService>());
#elif __IOS__
        services.AddSingleton<IosSystemNotificationService>();
        services.AddSingleton<ISystemNotificationService>(sp =>
            sp.GetRequiredService<IosSystemNotificationService>());
        services.AddSingleton<ISystemNotificationActivationSource>(sp =>
            sp.GetRequiredService<IosSystemNotificationService>());
#elif __WASM__
#pragma warning disable CA1416 // Uno browserwasm target runs in the browser platform surface.
        services.AddSingleton<WasmSystemNotificationService>();
        services.AddSingleton<ISystemNotificationService>(sp =>
            sp.GetRequiredService<WasmSystemNotificationService>());
        services.AddSingleton<ISystemNotificationActivationSource>(sp =>
            sp.GetRequiredService<WasmSystemNotificationService>());
#pragma warning restore CA1416
#else
        // One desktop TFM covers Linux and macOS, so the split is a runtime check rather than #if.
        // macOS has no managed UserNotifications binding here, so it stays honestly unsupported.
        services.AddSingleton(sp => OperatingSystem.IsLinux()
            ? (ISystemNotificationService)new LinuxSystemNotificationService(
                sp.GetRequiredService<IStringLocalizer<CoreStrings>>())
            : new UnsupportedSystemNotificationService());
        services.AddSingleton<ISystemNotificationActivationSource>(sp =>
            (ISystemNotificationActivationSource)sp.GetRequiredService<ISystemNotificationService>());
#endif
#if WINDOWS
        services.AddSingleton<WindowsRawGameControllerMapper>();
        services.AddSingleton<WindowsGamepadInputService>();
        services.AddSingleton<WindowsGamepadDiagnosticsService>();
        services.AddSingleton<WindowsAudioInputSignalDiagnosticsService>();
        services.AddSingleton<NoOpGamepadInputService>();
        services.AddSingleton<NoOpGamepadDiagnosticsService>();
        services.AddSingleton<NoOpAudioInputSignalDiagnosticsService>();
        services.AddSingleton<IGamepadInputService>(sp =>
            sp.GetRequiredService<IPlatformCapabilityService>().SupportsGamepadInput
                ? sp.GetRequiredService<WindowsGamepadInputService>()
                : sp.GetRequiredService<NoOpGamepadInputService>());
        services.AddSingleton<IGamepadDiagnosticsService>(sp =>
            sp.GetRequiredService<IPlatformCapabilityService>().SupportsGamepadInput
                ? sp.GetRequiredService<WindowsGamepadDiagnosticsService>()
                : sp.GetRequiredService<NoOpGamepadDiagnosticsService>());
        services.AddSingleton<IAudioInputSignalDiagnosticsService>(sp =>
            sp.GetRequiredService<WindowsAudioInputSignalDiagnosticsService>());
#elif __ANDROID__
        services.AddSingleton<IGamepadInputService, NoOpGamepadInputService>();
        services.AddSingleton<IGamepadDiagnosticsService, NoOpGamepadDiagnosticsService>();
        services.AddSingleton<IAudioInputSignalDiagnosticsService, NoOpAudioInputSignalDiagnosticsService>();
#elif __WASM__
        if (OperatingSystem.IsBrowser())
        {
            services.AddSingleton<IGamepadInputService, WasmGamepadInputService>();
            services.AddSingleton<IGamepadDiagnosticsService, WasmGamepadDiagnosticsService>();
        }
        else
        {
            services.AddSingleton<IGamepadInputService, NoOpGamepadInputService>();
            services.AddSingleton<IGamepadDiagnosticsService, NoOpGamepadDiagnosticsService>();
        }

        services.AddSingleton<IAudioInputSignalDiagnosticsService, NoOpAudioInputSignalDiagnosticsService>();
#else
        services.AddSingleton<IGamepadInputService, NoOpGamepadInputService>();
        services.AddSingleton<IGamepadDiagnosticsService, NoOpGamepadDiagnosticsService>();
        services.AddSingleton<IAudioInputSignalDiagnosticsService, NoOpAudioInputSignalDiagnosticsService>();
#endif

#if WINDOWS
        services.AddSingleton<NativeVoiceInputService>();
        services.AddSingleton<IVoiceInputService>(sp => sp.GetRequiredService<NativeVoiceInputService>());
        services.AddSingleton<IVoiceInputRuntimeDiagnosticsSource>(sp => sp.GetRequiredService<NativeVoiceInputService>());
#else
        services.AddSingleton<IVoiceInputService>(NoOpVoiceInputService.Instance);
        services.AddSingleton<IVoiceInputRuntimeDiagnosticsSource>(NoOpVoiceInputService.Instance);
#endif
        services.AddSingleton<IVoiceInputDiagnosticsService, VoiceInputDiagnosticsService>();
        services.AddSingleton<IShellBackNavigationService, ShellBackNavigationService>();
        services.AddSingleton<IShellFocusScope, MainShellFocusScope>();
        services.AddSingleton<IGamepadNavigationDispatcher, MainShellGamepadNavigationDispatcher>();
        services.AddSingleton<IGamepadShortcutDispatcher, MainShellGamepadShortcutDispatcher>();
        services.AddSingleton<IGamepadContextIntentDispatcher, MainShellGamepadContextIntentDispatcher>();

#if __WASM__ || __ANDROID__ || __IOS__
        // Restricted platforms keep their platform-local storage registrations. Desktop hosts use the
        // shared composition root below so the CLI and GUI cannot drift into separate backend chains.
#if __WASM__
        if (OperatingSystem.IsBrowser())
        {
            services.AddSingleton<IFileSystemPersistence, WasmFileSystemPersistence>();
        }
        else
        {
            services.AddSingleton<IFileSystemPersistence, NoOpFileSystemPersistence>();
        }
#else
        services.AddSingleton<IFileSystemPersistence, NoOpFileSystemPersistence>();
#endif

        services.AddSingleton<IAppDataService, AppDataService>();
        services.AddSingleton<IConfigChangeSignal, ConfigChangeSignal>();
        services.AddSingleton<IConfigurationFileStore>(sp => new FileSystemAppFileStore(
            sp.GetRequiredService<IFileSystemPersistence>(),
            sp.GetRequiredService<IConfigChangeSignal>()));
        services.AddSingleton<IAppFileStore>(sp => sp.GetRequiredService<IConfigurationFileStore>());
        services.AddSingleton<IConfigurationFileTransactionStore>(sp =>
            sp.GetRequiredService<IConfigurationFileStore>());
        services.AddSingleton<PlainTextFileSecureStorage>();
#if __ANDROID__
        services.AddSingleton<ISecureStorage, AndroidKeyStoreSecureStorage>();
#elif __IOS__
        services.AddSingleton<ISecureStorage, IosKeychainSecureStorage>();
#elif __WASM__
        services.AddSingleton<ISecureStorage>(sp => sp.GetRequiredService<PlainTextFileSecureStorage>());
#endif
        services.AddSingleton<IAppSettingsService, AppSettingsService>();
        services.AddSingleton<ConfigurationManager>(sp => new ConfigurationManager(
            sp.GetRequiredService<ISecureStorage>(),
            sp.GetRequiredService<IConfigurationFileStore>(),
            sp.GetRequiredService<IAppDataService>(),
            sp.GetRequiredService<ILogger<ConfigurationManager>>()));
        services.AddSingleton<IConfigurationService>(sp => sp.GetRequiredService<ConfigurationManager>());
        services.AddSingleton<IConfigurationRecoveryService>(sp => sp.GetRequiredService<ConfigurationManager>());
        services.AddSingleton<IServerCredentialService, ServerCredentialService>();
        services.AddSingleton<IValidator<ServerConfiguration>, ServerConfigurationValidator>();
        services.AddSingleton<ConfigurationSecretSnapshotService>();
        services.AddSingleton<ConfigSyncPackageService>();
#else
        // Desktop hosts (GUI and CLI) share one composition root, which already owns
        // IAppSettingsService together with the secure storage backend it depends on.
        services.AddSalmonEggDesktopConfiguration();
#endif
        services.AddSingleton<IMcpSettingsService, McpSettingsService>();
        services.AddSingleton<IAppMaintenanceService, AppMaintenanceService>();
        services.AddSingleton<IAppDocumentService, AppDocumentService>();
        services.AddSingleton<IAppSupportInfoService>(_ => new AppSupportInfoService(typeof(App).Assembly));
        services.AddSingleton<IAiContentReportLauncher, AiContentReportLauncher>();
        services.AddSingleton<IConversationStore, ConversationStore>();
#if __WASM__ || __ANDROID__ || __IOS__
        services.AddSingleton<IPlatformRuntimeCapabilityProbe, RestrictedRuntimeCapabilityProbe>();
#else
        services.AddSingleton<IPlatformRuntimeCapabilityProbe, PlatformRuntimeCapabilityProbe>();
#endif
        services.AddSingleton<IPlatformCapabilityService, PlatformCapabilityService>();
        services.AddSingleton<ITransportSupportPolicy, TransportSupportPolicy>();
#if __WASM__
        if (OperatingSystem.IsBrowser())
        {
            services.AddSingleton<ITransportEndpointAccessContext, WasmTransportEndpointAccessContext>();
        }
        else
        {
            services.AddSingleton<ITransportEndpointAccessContext, DefaultTransportEndpointAccessContext>();
        }
#else
        services.AddSingleton<ITransportEndpointAccessContext, DefaultTransportEndpointAccessContext>();
#endif
        services.AddSingleton<ITransportEndpointAccessPolicy, TransportEndpointAccessPolicy>();
        services.AddSingleton<IPlatformIconService, PlatformIconService>();
#if WINDOWS
        services.AddSingleton<IAppStartupService, WindowsAppStartupService>();
#else
        services.AddSingleton<IAppStartupService, UnsupportedAppStartupService>();
#endif
        services.AddSingleton<AppCultureService>();
        services.AddSingleton<IAppLanguageService, UnoAppLanguageService>();
#if __WASM__ || __ANDROID__ || __IOS__
        services.AddSingleton<IStdioTransportFactory, UnsupportedStdioTransportFactory>();
#else
        services.AddSingleton<IStdioTransportFactory>(sp =>
            sp.GetRequiredService<IPlatformCapabilityService>().SupportsStdioTransport
                ? new DesktopStdioTransportFactory()
                : new UnsupportedStdioTransportFactory());
#endif
        services.AddSingleton<TransportFactory>(sp =>
            new TransportFactory(
                sp.GetRequiredService<Serilog.ILogger>(),
                sp.GetRequiredService<ITransportSupportPolicy>(),
                sp.GetRequiredService<IStdioTransportFactory>()));
        services.AddSingleton<SalmonEgg.Domain.Interfaces.ITransportFactory>(sp =>
            new EndpointValidatingTransportFactory(
                sp.GetRequiredService<TransportFactory>(),
                sp.GetRequiredService<ITransportEndpointAccessPolicy>()));
        services.AddSingleton<IDiagnosticsBundleService, SalmonEgg.Infrastructure.Services.DiagnosticsBundleService>();
        services.AddSingleton<ILiveLogStreamService, SalmonEgg.Infrastructure.Services.LiveLogStreamService>();
#if WINDOWS
        services.AddSingleton<IPlatformShellService>(sp =>
            new WindowsPlatformShellService(sp.GetRequiredService<IPlatformCapabilityService>()));
#elif __WASM__
#pragma warning disable CA1416 // Uno browserwasm target runs in the browser platform surface.
        services.AddSingleton<IPlatformShellService, WasmPlatformShellService>();
#pragma warning restore CA1416
#elif __ANDROID__ || __IOS__
        services.AddSingleton<IPlatformShellService, UnsupportedPlatformShellService>();
#else
        services.AddSingleton<IPlatformShellService>(sp =>
            sp.GetRequiredService<IPlatformCapabilityService>().SupportsExternalFileOpen
                ? new PlatformShellService(
                    sp.GetRequiredService<IPlatformCapabilityService>(),
                    sp.GetRequiredService<IPlatformRuntimeCapabilityProbe>())
                : new UnsupportedPlatformShellService());
#endif
        services.AddSingleton<IStorageLocationService, SalmonEgg.Infrastructure.Services.StorageLocationService>();
        services.AddSingleton<ConfigProjectionReloadCoordinator>();
        services.AddSingleton<IConversationPreviewStore, ConversationPreviewStore>();
        services.AddSingleton<ISessionExportService, SalmonEgg.Infrastructure.Services.SessionExportService>();
        services.AddSingleton<ILogFileCatalog, SalmonEgg.Infrastructure.Services.LogFileCatalog>();
        services.AddSingleton<CloudConfigSyncStateStore>();
        services.AddSingleton<ConfigContentFingerprint>();
        services.AddSingleton<ICloudConfigStorageProvider, OneDriveCloudConfigStorageProvider>();
        services.AddSingleton<ICloudConfigStorageProvider, WebDavCloudConfigStorageProvider>();
        services.AddSingleton<ICloudConfigStorageProvider, S3CloudConfigStorageProvider>();
        services.AddSingleton<ICloudConfigSyncCoordinator, CloudConfigSyncCoordinator>();

        services.AddSingleton<IState<ChatState>>(sp => State.Value(sp, () => ChatState.Empty));
        services.AddSingleton<IChatStore, ChatStore>();
        services.AddSingleton<IAcpConnectionDependencySnapshotProvider>(sp =>
            new AcpConnectionDependencySnapshotProvider(
                sp.GetRequiredService<IChatStore>(),
                sp.GetRequiredService<IChatConnectionStore>()));
        services.AddSingleton<IAuthoritativeRemoteSessionRouter>(sp =>
            new AuthoritativeRemoteSessionRouter(sp.GetRequiredService<IChatStore>()));
        services.AddSingleton<IState<ChatConnectionState>>(sp => State.Value(sp, () => ChatConnectionState.Empty));
        services.AddSingleton<IChatConnectionStore, ChatConnectionStore>();
        services.AddSingleton<IAcpRemoteSessionRecoveryContextResolver, AcpRemoteSessionRecoveryContextResolver>();
        services.AddSingleton<IAcpConnectionCoordinator>(sp =>
            new AcpConnectionCoordinator(
                sp.GetRequiredService<IChatConnectionStore>(),
                sp.GetRequiredService<ILogger<AcpConnectionCoordinator>>(),
                sp.GetRequiredService<IAcpMcpServerResolver>(),
                sp.GetRequiredService<IAcpRemoteSessionRecoveryContextResolver>()));
        services.AddSingleton(sp =>
            AcpConnectionEvictionOptionsLoader.LoadEnvironmentDefaults(
                sp.GetRequiredService<ILoggerFactory>().CreateLogger("AcpConnectionEvictionOptionsLoader")));
        services.AddSingleton<AcpConnectionEvictionOptionsBridge>();
        services.AddSingleton<IAcpConnectionEvictionPolicy>(sp =>
            new ConservativeAcpConnectionEvictionPolicy(
                sp.GetRequiredService<AcpConnectionEvictionOptions>()));
        services.AddSingleton<IAcpConnectionSessionCleaner>(sp =>
            new AcpConnectionSessionCleaner(
                sp.GetRequiredService<IAcpConnectionSessionRegistry>(),
                sp.GetRequiredService<IAcpConnectionEvictionPolicy>(),
                sp.GetRequiredService<AcpConnectionEvictionOptions>(),
                sp.GetRequiredService<ILogger<AcpConnectionSessionCleaner>>()));
        services.AddSingleton<IAcpConnectionPoolManager>(sp =>
            new AcpConnectionPoolManager(
                sp.GetRequiredService<IAcpConnectionSessionRegistry>(),
                sp.GetRequiredService<IAcpConnectionSessionCleaner>(),
                sp.GetRequiredService<ILogger<AcpConnectionPoolManager>>()));
        services.AddSingleton<IAcpSessionCommandOrchestrator>(sp =>
            new AcpSessionCommandOrchestrator(
                sp.GetRequiredService<ILogger<AcpSessionCommandOrchestrator>>(),
                sp.GetRequiredService<IAcpMcpServerResolver>(),
                sp.GetService<IStringLocalizer<CoreStrings>>()));
        services.AddSingleton<IAcpMcpServerProvider>(sp =>
            new SettingsAcpMcpServerProvider(sp.GetRequiredService<IMcpSettingsService>()));
        services.AddSingleton<IAcpMcpServerResolver>(sp =>
            new AcpMcpServerResolver(sp.GetRequiredService<IAcpMcpServerProvider>()));
        services.AddSingleton<IShellLayoutStore>(sp =>
        {
            var initialState = ShellLayoutState.Default with
            {
                SupportsLocalTerminal = sp.GetRequiredService<IPlatformCapabilityService>().SupportsLocalTerminal
            };
            var initialSnapshot = ShellLayoutPolicy.Compute(initialState);
            var state = State.Value(sp, () => initialState);
            var snapshot = State.Value(sp, () => initialSnapshot);
            return new ShellLayoutStore(state, snapshot, initialState, initialSnapshot);
        });
        services.AddSingleton<IShellLayoutMetricsSink, ShellLayoutMetricsSink>();
        var chatServiceDecorator = CreateChatServiceDecorator();
#if __WASM__ || __ANDROID__ || __IOS__
        services.AddSingleton<ITerminalSessionManager, UnsupportedTerminalSessionManager>();
#else
        services.AddSingleton<ITerminalSessionManager>(sp =>
            sp.GetRequiredService<IPlatformCapabilityService>().SupportsLocalTerminal
                ? new TerminalSessionManager()
                : new UnsupportedTerminalSessionManager());
#endif
        services.AddSingleton<IAcpClientFactory, AcpClientFactory>();

        // ACP setup wizard. The catalog is pure data and safe everywhere; every probing, installing and
        // testing seam gets an unsupported default so platforms without a child-process host report
        // "undetermined" instead of failing to resolve a service.
        services.AddSingleton<IAcpAgentCatalog, AcpAgentCatalog>();
#if __WASM__ || __ANDROID__ || __IOS__
        services.AddSingleton<IAcpExecutableProbe, UnsupportedAcpExecutableProbe>();
        services.AddSingleton<IAcpComponentInstaller, UnsupportedAcpComponentInstaller>();
        services.AddSingleton<IAcpSetupConnectivityTester, UnsupportedAcpSetupConnectivityTester>();
#else
        services.AddSingleton<IAcpExecutableProbe>(sp =>
            sp.GetRequiredService<IPlatformCapabilityService>().SupportsStdioTransport
                ? new DesktopAcpExecutableProbe()
                : new UnsupportedAcpExecutableProbe());
        services.AddSingleton<IAcpComponentInstaller>(sp =>
            sp.GetRequiredService<IPlatformCapabilityService>().SupportsStdioTransport
                ? new DesktopAcpComponentInstaller(sp.GetRequiredService<IAcpExecutableProbe>())
                : new UnsupportedAcpComponentInstaller());
        services.AddSingleton<IAcpSetupHandshakeProbe>(sp =>
            new StdioAcpSetupHandshakeProbe(
                sp.GetRequiredService<IStdioTransportFactory>(),
                transport => sp.GetRequiredService<IAcpClientFactory>().CreateClient(transport)));
        services.AddSingleton<IAcpSetupConnectivityTester>(sp =>
            sp.GetRequiredService<IPlatformCapabilityService>().SupportsStdioTransport
                ? new DesktopAcpSetupConnectivityTester(sp.GetRequiredService<IAcpSetupHandshakeProbe>())
                : new UnsupportedAcpSetupConnectivityTester());
#endif
        services.AddSingleton<AcpSetupWizardViewModel>();
        services.AddSingleton<AcpSetupWizardOrchestrator>(sp =>
            new AcpSetupWizardOrchestrator(
                sp.GetRequiredService<IAcpAgentCatalog>(),
                sp.GetRequiredService<IAcpExecutableProbe>(),
                sp.GetRequiredService<IAcpComponentInstaller>(),
                sp.GetRequiredService<IAcpSetupConnectivityTester>(),
                sp.GetRequiredService<IConfigurationService>()));
        services.AddSingleton<ChatServiceFactory>(sp =>
        {
            var transportFactory = sp.GetRequiredService<ITransportFactory>();
            var errorLogger = sp.GetRequiredService<IErrorLogger>();
            var sessionManager = sp.GetRequiredService<ISessionManager>();
            var acpClientFactory = sp.GetRequiredService<IAcpClientFactory>();
            var logger = sp.GetRequiredService<Serilog.ILogger>();
            return new ChatServiceFactory(
                transportFactory,
                errorLogger,
                sessionManager,
                acpClientFactory,
                logger,
                chatServiceDecorator);
        });
        services.AddTransient<ConfigurationEditorViewModel>();
        services.AddSingleton<IConversationWorkspacePreferences>(sp =>
            new AppPreferencesConversationWorkspacePreferences(sp.GetRequiredService<AppPreferencesViewModel>()));
        services.AddSingleton<IChatStateProjector, ChatStateProjector>();
        services.AddSingleton<IAcpSessionUpdateProjector, AcpSessionUpdateProjector>();

        // New Chat ViewModel (refactored)
        // Must be singleton so connection/session state survives navigation and is shared between Settings and Chat pages.
        services.AddSingleton(sp =>
            new ConversationCatalogPresenter(sp.GetRequiredService<IUiDispatcher>()));
        services.AddSingleton<IConversationCatalogReadModel>(sp =>
            sp.GetRequiredService<ConversationCatalogPresenter>());
        services.AddSingleton<IState<ConversationAttentionState>>(sp => State.Value(sp, () => ConversationAttentionState.Empty));
        services.AddSingleton<IConversationAttentionStore, ConversationAttentionStore>();
        services.AddSingleton<ConversationCatalogDisplayPresenter>();
        services.AddSingleton<IConversationCatalogDisplayReadModel>(sp =>
            sp.GetRequiredService<ConversationCatalogDisplayPresenter>());
        services.AddSingleton<IProjectAffinityResolver, ProjectAffinityResolver>();
        services.AddSingleton<IConversationProjectAffinityResolver, ConversationProjectAffinityResolver>();
#if !__WASM__ && !__ANDROID__ && !__IOS__
        services.AddSingleton<ILocalTerminalCwdResolver, LocalTerminalCwdResolver>();
        services.AddSingleton<ILocalTerminalSessionManager, LocalTerminalSessionManager>();
        services.AddSingleton<LocalTerminalPanelCoordinator>();
#endif
        services.AddSingleton<INavigationProjectPreferences>(sp =>
            new NavigationProjectPreferencesAdapter(sp.GetRequiredService<AppPreferencesViewModel>()));
        services.AddSingleton<IAddProjectCoordinator>(sp =>
            new AddProjectCoordinator(
                sp.GetRequiredService<INavigationProjectPreferences>(),
                sp.GetRequiredService<ILogger<AddProjectCoordinator>>()));
        services.AddSingleton<INavigationProjectSelectionStore>(sp =>
            new NavigationProjectSelectionStoreAdapter(sp.GetRequiredService<AppPreferencesViewModel>()));
        // ACP chat service factory — adapts ChatServiceFactory to the IAcpChatServiceFactory seam
        // used by AcpChatCoordinator.
        services.AddSingleton<IAcpChatServiceFactory>(sp =>
            new ChatServiceFactoryAdapter(sp.GetRequiredService<ChatServiceFactory>()));
        services.AddSingleton<IAcpConnectionCommands>(sp =>
        {
            _ = sp.GetRequiredService<AcpConnectionEvictionOptionsBridge>();
            return new AcpChatCoordinator(
                sp.GetRequiredService<IAcpChatServiceFactory>(),
                sp.GetRequiredService<ILogger<AcpChatCoordinator>>(),
                sp.GetRequiredService<ITransportSupportPolicy>(),
                sp.GetRequiredService<IAcpMcpServerProvider>(),
                sp.GetRequiredService<IAcpSessionCommandOrchestrator>(),
                sp.GetRequiredService<IAcpConnectionCoordinator>(),
                sp.GetRequiredService<IAcpConnectionSessionRegistry>(),
                sp.GetRequiredService<IAcpConnectionSessionCleaner>(),
                sp.GetRequiredService<IAcpConnectionPoolManager>(),
                sp.GetRequiredService<IAcpConnectionDependencySnapshotProvider>());
        });
        services.AddSingleton(sp =>
        {
            var lazyNav = new Lazy<INavigationCoordinator>(() => sp.GetRequiredService<INavigationCoordinator>());
            var lazyMainNav = new Lazy<MainNavigationViewModel>(() => sp.GetRequiredService<MainNavigationViewModel>());
            return new ConversationCatalogFacade(
                sp.GetRequiredService<ChatConversationWorkspace>(),
                sp.GetRequiredService<IConversationActivationCoordinator>(),
                sp.GetRequiredService<IShellSelectionReadModel>(),
                lazyNav,
                sp.GetRequiredService<ConversationCatalogPresenter>(),
                sp.GetRequiredService<ILogger<ConversationCatalogFacade>>(),
                sp.GetService<IConversationAttentionStore>(),
                sp.GetService<IConversationPanelCleanup>(),
                lazyMainNav);
        });
        services.AddSingleton<IConversationCatalog>(sp => sp.GetRequiredService<ConversationCatalogFacade>());
        services.AddSingleton<ChatViewModel>(sp =>
        {
            var dispatcher = sp.GetRequiredService<IUiDispatcher>();
            var vm = ActivatorUtilities.CreateInstance<ChatViewModel>(
                sp,
                dispatcher,
                sp.GetRequiredService<IShellNavigationRuntimeState>());
            sp.GetRequiredService<ConversationCatalogFacade>().SetPanelCleanup(vm);
            return vm;
        });
        services.AddSingleton<IConversationSessionSwitcher>(sp => sp.GetRequiredService<ChatViewModel>());
        services.AddSingleton<IChatRuntimeInitialization>(sp => sp.GetRequiredService<ChatViewModel>());
        services.AddSingleton<IChatRuntimePersistence>(sp => sp.GetRequiredService<ChatViewModel>());

        services.AddSingleton<ChatShellViewModel>();
        // The navigation VM owns session activation; the router only supplies a conversation id.
        services.AddSingleton<IConversationActivationEntryPoint>(sp =>
            sp.GetRequiredService<MainNavigationViewModel>());
        services.AddSingleton<IConversationOpenRouter, ConversationOpenRouter>();
        services.AddSingleton<ShellSessionActivationOverlayViewModel>();
        services.AddSingleton<IDiscoverSessionsConnectionFacade>(sp =>
            new DiscoverSessionsConnectionFacade(
                sp.GetRequiredService<IAcpChatServiceFactory>(),
                sp.GetRequiredService<ITransportSupportPolicy>(),
                sp.GetRequiredService<ILogger<DiscoverSessionsConnectionFacade>>()));
        services.AddSingleton<ISettingsChatConnection>(sp =>
            new SettingsChatConnectionAdapter(
                sp.GetRequiredService<ChatViewModel>(),
                sp.GetRequiredService<IAcpConnectionCommands>()));
        services.AddSingleton<IChatLaunchWorkflowChatFacade>(sp =>
            new ChatLaunchWorkflowChatFacadeAdapter(
                sp.GetRequiredService<ChatViewModel>(),
                sp.GetRequiredService<IChatConnectionStore>()));
        services.AddSingleton<IChatSessionCatalog>(sp =>
            new ChatViewModelSessionCatalogAdapter(sp.GetRequiredService<IConversationCatalog>()));

        // Extracted workspace is still registered so ChatViewModel can delegate local conversation state.
        services.AddSingleton<ChatConversationWorkspace>();
        services.AddSingleton<BindingCoordinator>(sp =>
            new BindingCoordinator(
                sp.GetRequiredService<ChatConversationWorkspace>(),
                sp.GetRequiredService<IChatStore>()));
        services.AddSingleton<IConversationBindingCommands>(sp => sp.GetRequiredService<BindingCoordinator>());
        services.AddSingleton<IConversationMutationPipeline, ConversationMutationPipeline>();
        services.AddSingleton<SerialAsyncWorkQueue>();
        services.AddSingleton<IWorkspaceWriter>(sp =>
            new WorkspaceWriter(sp.GetRequiredService<ChatConversationWorkspace>(), sp.GetRequiredService<IUiDispatcher>()));
        services.AddSingleton<Func<Action<SessionUpdateEventArgs>, IUiDispatcher, Action<string?>?, AcpEventAdapter>>(sp =>
            (handler, dispatcher, resyncRequired) => new AcpEventAdapter(
                handler,
                dispatcher,
                resyncRequired: resyncRequired,
                logger: sp.GetService<ILogger<AcpEventAdapter>>()));
        services.AddSingleton<IConversationActivationCoordinator>(sp =>
            new ConversationActivationCoordinator(
                sp.GetRequiredService<ChatConversationWorkspace>(),
                sp.GetRequiredService<IConversationBindingCommands>(),
                sp.GetRequiredService<IChatStore>(),
                sp.GetRequiredService<IChatConnectionStore>(),
                sp.GetRequiredService<ILogger<ConversationActivationCoordinator>>(),
                sp.GetRequiredService<IConversationMutationPipeline>(),
                sp.GetRequiredService<IShellNavigationRuntimeState>()));

        // Main shell navigation (Start + Projects -> Sessions tree)
        services.AddSingleton<INavigationSelectionProjector, NavigationSelectionProjector>();
        services.AddSingleton<ShellSelectionStateStore>();
        services.AddSingleton<ShellNavigationRuntimeStateStore>();
        services.AddSingleton<IShellSelectionReadModel>(sp => sp.GetRequiredService<ShellSelectionStateStore>());
        services.AddSingleton<IShellSelectionMutationSink>(sp => sp.GetRequiredService<ShellSelectionStateStore>());
        services.AddSingleton<IShellNavigationRuntimeState>(sp => sp.GetRequiredService<ShellNavigationRuntimeStateStore>());
        services.AddSingleton<MainNavigationViewModel>(sp =>
            new MainNavigationViewModel(
                sp.GetRequiredService<IConversationCatalog>(),
                sp.GetRequiredService<INavigationProjectPreferences>(),
                sp.GetRequiredService<IUiInteractionService>(),
                sp.GetRequiredService<INavigationCoordinator>(),
                sp.GetRequiredService<ILogger<MainNavigationViewModel>>(),
                sp.GetRequiredService<INavigationPaneState>(),
                sp.GetRequiredService<IShellLayoutMetricsSink>(),
                sp.GetRequiredService<INavigationSelectionProjector>(),
                sp.GetRequiredService<IShellSelectionReadModel>(),
                sp.GetRequiredService<IShellNavigationRuntimeState>(),
                sp.GetRequiredService<IConversationCatalogDisplayReadModel>(),
                sp.GetRequiredService<IProjectAffinityResolver>(),
                sp.GetRequiredService<IUiDispatcher>(),
                sp.GetRequiredService<IStringLocalizer<CoreStrings>>(),
                sp.GetRequiredService<IPlatformShellService>(),
                sp.GetRequiredService<IAppLanguageService>(),
                sp.GetRequiredService<IAddProjectCoordinator>()));
        services.AddSingleton<INavigationCoordinator>(sp =>
            new NavigationCoordinator(
                sp.GetRequiredService<IShellSelectionMutationSink>(),
                sp.GetRequiredService<IShellNavigationRuntimeState>(),
                sp.GetRequiredService<IConversationSessionSwitcher>(),
                sp.GetRequiredService<IDiscoverSessionsConnectionFacade>(),
                sp.GetRequiredService<INavigationProjectSelectionStore>(),
                sp.GetRequiredService<IShellNavigationService>(),
                sp.GetRequiredService<ISettingsSectionSelectionStore>(),
                sp.GetRequiredService<ILogger<NavigationCoordinator>>()));
        services.AddSingleton<ISettingsSectionSelectionStore, SettingsSectionSelectionStore>();
        services.AddSingleton<IShellStartupNavigationService>(sp =>
            new ShellStartupNavigationService(
                sp.GetRequiredService<MainNavigationViewModel>(),
                sp.GetRequiredService<IShellNavigationRuntimeState>(),
                sp.GetRequiredService<IActivationTokenShellNavigationService>(),
                sp.GetRequiredService<ISettingsSectionSelectionStore>(),
                sp.GetRequiredService<ILogger<ShellStartupNavigationService>>()));
        services.AddSingleton<IApplicationStartupWorkflow>(sp =>
        {
            // 强制解析：投影只在构造时订阅持久化事件，若无人解析它，"保存后立即生效"就不成立。
            // 挂在启动 workflow 上是因为该 workflow 必然在启动路径被解析，且遥测首次激活也在
            // 这里，订阅与激活同时就位。
            _ = sp.GetRequiredService<TelemetrySettingsProjection>();
            _ = sp.GetRequiredService<ChatCompletionNotificationCoordinator>();
            // Starting here rather than in a page keeps notification activation on the same
            // application-scoped owner as the rest of startup.
            var notificationActivation = sp.GetRequiredService<NotificationActivationCoordinator>();
            notificationActivation.Start();
            return new ApplicationStartupWorkflow(
                sp.GetRequiredService<IShellStartupNavigationService>(),
                sp.GetRequiredService<IChatRuntimeInitialization>(),
                sp.GetRequiredService<IConfigurationRecoveryService>(),
                sp.GetRequiredService<IAppSettingsService>(),
                sp.GetRequiredService<ITelemetryRuntime>(),
                notificationActivation,
                sp.GetRequiredService<ILogger<ApplicationStartupWorkflow>>());
        });
        services.AddSingleton<ChatCompletionNotificationCoordinator>();
        services.AddSingleton<NotificationActivationCoordinator>();
        services.AddSingleton<IApplicationShutdownWorkflow>(sp =>
            new ApplicationShutdownWorkflow(
                sp.GetRequiredService<IChatRuntimePersistence>(),
                sp.GetRequiredService<ITelemetryRuntime>(),
                sp.GetRequiredService<ILogger<ApplicationShutdownWorkflow>>()));

        // Global search
        services.AddSingleton<IGlobalSearchPipeline, DefaultGlobalSearchPipeline>();
        services.AddSingleton<GlobalSearchViewModel>(sp =>
            new GlobalSearchViewModel(
                sp.GetRequiredService<MainNavigationViewModel>(),
                sp.GetRequiredService<AppPreferencesViewModel>(),
                sp.GetRequiredService<INavigationCoordinator>(),
                sp.GetRequiredService<IConversationCatalogReadModel>(),
                sp.GetRequiredService<IConversationProjectAffinityResolver>(),
                sp.GetRequiredService<IGlobalSearchPipeline>(),
                sp.GetRequiredService<IStringLocalizer<CoreStrings>>(),
                sp.GetRequiredService<ILogger<GlobalSearchViewModel>>(),
                sp.GetRequiredService<IAppLanguageService>()));

        // Discover sessions
        services.AddTransient(sp =>
            new SalmonEgg.Presentation.ViewModels.Discover.DiscoverSessionsViewModel(
                sp.GetRequiredService<ILogger<SalmonEgg.Presentation.ViewModels.Discover.DiscoverSessionsViewModel>>(),
                sp.GetRequiredService<INavigationCoordinator>(),
                sp.GetRequiredService<INavigationProjectPreferences>(),
                sp.GetRequiredService<AcpProfilesViewModel>(),
                sp.GetRequiredService<IDiscoverSessionsConnectionFacade>(),
                sp.GetRequiredService<IUiDispatcher>(),
                sp.GetRequiredService<IShellLayoutStore>(),
                sp.GetRequiredService<IProjectAffinityResolver>(),
                sp.GetRequiredService<IStringLocalizer<CoreStrings>>(),
                sp.GetRequiredService<IAppLanguageService>()));

        // Start page orchestrator (Start creates session and submits)
        services.AddSingleton<StartViewModel>(sp =>
            new StartViewModel(
                sp.GetRequiredService<ChatViewModel>(),
                sp.GetRequiredService<ISessionManager>(),
                sp.GetRequiredService<AppPreferencesViewModel>(),
                sp.GetRequiredService<INavigationProjectPreferences>(),
                sp.GetRequiredService<INavigationProjectSelectionStore>(),
                sp.GetRequiredService<INavigationCoordinator>(),
                sp.GetRequiredService<MainNavigationViewModel>(),
                sp.GetRequiredService<ILogger<StartViewModel>>(),
                sp.GetRequiredService<IChatConnectionStore>(),
                sp.GetRequiredService<IChatLaunchWorkflow>(),
                sp.GetRequiredService<IConversationCatalogReadModel>(),
                sp.GetRequiredService<IStringLocalizer<CoreStrings>>(),
                sp.GetRequiredService<IAppLanguageService>(),
                sp.GetRequiredService<IUiInteractionService>()));
        services.AddSingleton<IChatLaunchWorkflow>(sp =>
            new ChatLaunchWorkflow(
                sp.GetRequiredService<IChatLaunchWorkflowChatFacade>(),
                sp.GetRequiredService<ISessionManager>(),
                sp.GetRequiredService<INavigationCoordinator>(),
                sp.GetRequiredService<ILogger<ChatLaunchWorkflow>>(),
                sp.GetRequiredService<ConversationCatalogFacade>(),
                sp.GetRequiredService<MainNavigationViewModel>()));

        // App preferences used by General/Appearance settings and window behaviors.
        services.AddSingleton<AppPreferencesViewModel>();
        services.AddSingleton<IApplicationNotificationSettings>(sp =>
            sp.GetRequiredService<AppPreferencesViewModel>());
        services.AddSingleton<WindowBackdropService>();

        // General settings
        services.AddSingleton<GeneralSettingsViewModel>();
        services.AddTransient<SettingsShellViewModel>(sp =>
            new SettingsShellViewModel(
                sp.GetRequiredService<IStringLocalizer<CoreStrings>>(),
                sp.GetRequiredService<ISettingsSectionSelectionStore>()));

        // ACP session registry: single source of truth for per-profile connection sessions.
        // Registered as a singleton concrete type first, then aliased to both interfaces
        // so ChatViewModel coordinator and Settings-page ViewModels share the same instance.
        services.AddSingleton<InMemoryAcpConnectionSessionRegistry>();
        services.AddSingleton<IAcpConnectionSessionRegistry>(sp =>
            sp.GetRequiredService<InMemoryAcpConnectionSessionRegistry>());
        services.AddSingleton<IAcpConnectionSessionEvents>(sp =>
            sp.GetRequiredService<InMemoryAcpConnectionSessionRegistry>());

        // ISettingsAcpConnectionCommands is implemented by ISettingsChatConnection.
        // Use a Lazy wrapper to defer resolution and break the circular dependency:
        //   AcpProfilesViewModel → ISettingsAcpConnectionCommands
        //                        → ISettingsChatConnection
        //                        → ChatViewModel
        //                        → AcpProfilesViewModel  (cycle!).
        // The Lazy<T> is only instantiated when AgentProfileItemViewModel first calls ConnectAsync,
        // by which time the DI graph is fully resolved.
        services.AddSingleton<ISettingsAcpConnectionCommands>(sp =>
        {
            var lazy = new Lazy<ISettingsChatConnection>(
                () => sp.GetRequiredService<ISettingsChatConnection>());
            return new LazySettingsAcpConnectionCommandsAdapter(lazy);
        });

        // ACP connection profiles — use full constructor so ProfileItems gets connection dependencies.
        services.AddSingleton<AcpProfilesViewModel>(sp =>
            new AcpProfilesViewModel(
                sp.GetRequiredService<IConfigurationService>(),
                sp.GetRequiredService<AppPreferencesViewModel>(),
                sp.GetRequiredService<ILogger<AcpProfilesViewModel>>(),
                sp.GetRequiredService<IAcpConnectionSessionRegistry>(),
                sp.GetRequiredService<IAcpConnectionSessionEvents>(),
                sp.GetRequiredService<ISettingsAcpConnectionCommands>(),
                sp.GetRequiredService<ILoggerFactory>(),
                sp.GetRequiredService<IUiDispatcher>(),
                sp.GetRequiredService<IStringLocalizer<CoreStrings>>(),
                sp.GetRequiredService<IAppLanguageService>()));


        // ACP connection settings page view model (wraps Chat + Profiles)
        services.AddSingleton<AcpConnectionSettingsViewModel>(sp =>
            new AcpConnectionSettingsViewModel(
                sp.GetRequiredService<ISettingsChatConnection>(),
                sp.GetRequiredService<AcpProfilesViewModel>(),
                sp.GetRequiredService<AppPreferencesViewModel>(),
                sp.GetRequiredService<ITransportSupportPolicy>(),
                sp.GetRequiredService<ILogger<AcpConnectionSettingsViewModel>>(),
                sp.GetRequiredService<IStringLocalizer<CoreStrings>>(),
                sp.GetRequiredService<IUiDispatcher>(),
                sp.GetRequiredService<IAppLanguageService>()));

        // Settings pages (Data/Shortcuts/Diagnostics/About)
        services.AddSingleton<CloudConfigSettingsViewModel>(sp =>
            new CloudConfigSettingsViewModel(
                sp.GetRequiredService<ICloudConfigSyncCoordinator>(),
                sp.GetRequiredService<IUiInteractionService>(),
                sp.GetRequiredService<IUiDispatcher>(),
                sp.GetRequiredService<IStringLocalizer<CoreStrings>>(),
                sp.GetRequiredService<IAppLanguageService>()));
        services.AddSingleton<DataStorageSettingsViewModel>();
        services.AddSingleton<McpSettingsViewModel>(sp =>
            new McpSettingsViewModel(
                sp.GetRequiredService<IMcpSettingsService>(),
                sp.GetRequiredService<IPlatformShellService>(),
                sp.GetRequiredService<IStringLocalizer<CoreStrings>>(),
                sp.GetRequiredService<ILogger<McpSettingsViewModel>>(),
                sp.GetRequiredService<IUiDispatcher>(),
                sp.GetRequiredService<IAppLanguageService>()));
        services.AddSingleton<ShortcutsSettingsViewModel>();
        services.AddSingleton<LiveLogViewerViewModel>(sp =>
            new LiveLogViewerViewModel(
                sp.GetRequiredService<ILiveLogStreamService>(),
                sp.GetRequiredService<IAppDataService>().LogsDirectoryPath,
                sp.GetRequiredService<ILogger<LiveLogViewerViewModel>>(),
                sp.GetRequiredService<IUiDispatcher>(),
                sp.GetRequiredService<IStringLocalizer<CoreStrings>>(),
                languageService: sp.GetRequiredService<IAppLanguageService>()));
        services.AddSingleton<VoiceInputDiagnosticsProbeViewModel>(sp =>
            new VoiceInputDiagnosticsProbeViewModel(
                sp.GetRequiredService<IVoiceInputService>(),
                sp.GetRequiredService<IAudioInputSignalDiagnosticsService>(),
                sp.GetRequiredService<IUiDispatcher>(),
                sp.GetRequiredService<IStringLocalizer<CoreStrings>>(),
                sp.GetRequiredService<ILogger<VoiceInputDiagnosticsProbeViewModel>>(),
                sp.GetRequiredService<IApplicationActivationSignalSource>(),
                sp.GetRequiredService<IAppLanguageService>()));
        services.AddSingleton<VoiceInputDiagnosticsViewModel>(sp =>
            new VoiceInputDiagnosticsViewModel(
                sp.GetRequiredService<IVoiceInputDiagnosticsService>(),
                sp.GetRequiredService<VoiceInputDiagnosticsProbeViewModel>(),
                sp.GetRequiredService<IUiDispatcher>(),
                sp.GetRequiredService<IStringLocalizer<CoreStrings>>(),
                sp.GetRequiredService<ILogger<VoiceInputDiagnosticsViewModel>>(),
                sp.GetRequiredService<IAppLanguageService>()));
        services.AddSingleton<GamepadDiagnosticsViewModel>(sp =>
            new GamepadDiagnosticsViewModel(
                sp.GetRequiredService<IGamepadDiagnosticsService>(),
                sp.GetRequiredService<IPlatformCapabilityService>(),
                sp.GetRequiredService<IUiDispatcher>(),
                sp.GetRequiredService<IStringLocalizer<CoreStrings>>(),
                sp.GetRequiredService<ILogger<GamepadDiagnosticsViewModel>>(),
                sp.GetRequiredService<IAppLanguageService>()));
        services.AddSingleton<DiagnosticsSettingsViewModel>();
        services.AddSingleton<IOpenSourceAcknowledgementsProvider, GeneratedOpenSourceAcknowledgementsProvider>();
        services.AddSingleton<AboutViewModel>();

        // Shell navigation facade (prevents Settings pages from walking the visual tree)
        services.AddSingleton<ShellNavigationService>();
        services.AddSingleton<IShellNavigationService>(sp => sp.GetRequiredService<ShellNavigationService>());
        services.AddSingleton<IActivationTokenShellNavigationService>(sp => sp.GetRequiredService<ShellNavigationService>());

        // Navigation state service (Single Source of Truth for IsPaneOpen) - Read-only adapter for SSOT
        services.AddSingleton<INavigationPaneState, ShellLayoutNavigationStateAdapter>();
        services.AddSingleton<INavigationStateService, NavigationStateService>();

        // Right panel state service (Single Source of Truth for RightPanelMode)
        services.AddSingleton<IRightPanelService, RightPanelService>();

        // UI interaction helpers (ContentDialog, FolderPicker)
#if WINDOWS
        services.AddSingleton<IFolderPickerService, WindowsFolderPickerService>();
#else
        services.AddSingleton<IFolderPickerService, UnavailableFolderPickerService>();
#endif
        services.AddSingleton<IUiInteractionService, UiInteractionService>();

        // UI runtime bridge (animations, shell reload)
        services.AddSingleton<IUiRuntimeService, UiRuntimeService>();

        // Mini floating chat window coordinator (Windows-only feature; other targets no-op via capability).
        services.AddSingleton<IMiniWindowCoordinator, MiniWindowCoordinator>();

        // Shell Layout SSOT
        services.AddSingleton<ShellLayoutViewModel>();
        services.AddSingleton<AppActivationSignalSource>();
        services.AddSingleton<IApplicationActivationSignalSource>(sp => sp.GetRequiredService<AppActivationSignalSource>());
        services.AddSingleton<IApplicationVisibilityState>(sp => sp.GetRequiredService<AppActivationSignalSource>());
        services.AddSingleton<WindowMetricsProvider>();
    }

    private static string GetAppDataPath()
    {
        return SalmonEggPaths.GetAppDataRootPath();
    }

    private static Func<IChatService, IChatService>? CreateChatServiceDecorator()
    {
        if (!IsGuiAutomationEnabled())
        {
            return null;
        }

        var rawDelay = Environment.GetEnvironmentVariable(GuiSlowSessionLoadMsEnvVar);
        if (!int.TryParse(rawDelay, out var delayMs) || delayMs <= 0)
        {
            return null;
        }

        var delay = TimeSpan.FromMilliseconds(delayMs);
        return inner => new DelayedLoadChatService(inner, delay);
    }

    private static bool IsGuiAutomationEnabled()
        => string.Equals(Environment.GetEnvironmentVariable(GuiEnabledEnvVar), "1", StringComparison.Ordinal);

}
