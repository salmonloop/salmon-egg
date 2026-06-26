using System;
using System.Reflection;
using System.Threading;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SalmonEgg.Application.Services;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Application.UseCases;
using SalmonEgg.Application.Validators;
using SalmonEgg.Domain.Interfaces;
using SalmonEgg.Domain.Interfaces.Storage;
using SalmonEgg.Domain.Interfaces.Transport;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Domain.Services.Security;
using SalmonEgg.Infrastructure.Client;
using SalmonEgg.Infrastructure.Logging;
using SalmonEgg.Infrastructure.Network;
using SalmonEgg.Infrastructure.Serialization;
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
using SalmonEgg.Presentation.Services.Input;
using SalmonEgg.Presentation.ViewModels;
using SalmonEgg.Presentation.ViewModels.Chat;
using SalmonEgg.Presentation.ViewModels.Navigation;
using SalmonEgg.Presentation.ViewModels.Settings;
using SalmonEgg.Presentation.ViewModels.Start;
using Serilog;
using Uno.Extensions.Reactive;
#if WINDOWS
using SalmonEgg.Platforms.Windows;
#elif __WASM__
using SalmonEgg.Platforms.WebAssembly;
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
        RegisterDomainServices(services);
        RegisterInfrastructureServices(services);
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
        // ACP Protocol Services
        services.AddSingleton<IAcpProtocolService, AcpMessageParser>();

        // Message Parser and Validator
        services.AddSingleton<IMessageParser, MessageParser>();
        services.AddSingleton<IMessageValidator, MessageValidator>();

        // Session Manager
        services.AddSingleton<ISessionManager, Infrastructure.Services.SessionManager>();

        // Path Validator
        services.AddSingleton<IPathValidator, Infrastructure.Services.Security.PathValidator>();

        // Error Logger
        services.AddSingleton<IErrorLogger, ErrorLogger>();

        // Connection Manager (factory method supporting dynamic transport selection)
        services.AddSingleton<IConnectionManager>(sp =>
            {
                var protocolService = sp.GetRequiredService<IAcpProtocolService>();
                var logger = sp.GetRequiredService<Serilog.ILogger>();

                Infrastructure.Network.ITransport TransportFactory(TransportType type)
                {
                    var l = sp.GetRequiredService<Serilog.ILogger>();
                    return type switch
                    {
                        TransportType.HttpSse => new HttpSseTransport(l),
                        _ => new WebSocketTransport(l)
                    };
                }

                return new ConnectionManager(protocolService, logger, TransportFactory);
            });
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
#if WINDOWS
        services.AddSingleton<WindowsRawGameControllerMapper>();
        services.AddSingleton<IGamepadNativeInputBridge, WindowsGamepadNativeInputBridge>();
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
        services.AddSingleton<IGamepadNativeInputBridge, NoOpGamepadNativeInputBridge>();
#else
        services.AddSingleton<IGamepadInputService, NoOpGamepadInputService>();
        services.AddSingleton<IGamepadDiagnosticsService, NoOpGamepadDiagnosticsService>();
        services.AddSingleton<IAudioInputSignalDiagnosticsService, NoOpAudioInputSignalDiagnosticsService>();
        services.AddSingleton<IGamepadNativeInputBridge, NoOpGamepadNativeInputBridge>();
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
        services.AddSingleton<IGamepadNavigationDispatcher, MainShellGamepadNavigationDispatcher>();
        services.AddSingleton<IGamepadShortcutDispatcher, MainShellGamepadShortcutDispatcher>();
        services.AddSingleton<IGamepadContextIntentDispatcher, MainShellGamepadContextIntentDispatcher>();

        // File system persistence -- must be registered before IAppFileStore and ISecureStorage.
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

        // App settings (config/app.yaml)
        services.AddSingleton<IAppFileStore>(sp => new FileSystemAppFileStore(sp.GetRequiredService<IFileSystemPersistence>()));

        // Secure Storage
        // Windows: DPAPI (hardware-bound, user-scoped encryption).
        // All other platforms: AppFileStoreSecureStorage backed by IAppFileStore so secrets
        // flow through the same IFileSystemPersistence path (including IDBFS on WASM).
#if WINDOWS
        services.AddSingleton<ISecureStorage, WindowsDpapiSecureStorage>();
#else
        services.AddSingleton<ISecureStorage>(sp =>
            new AppFileStoreSecureStorage(
                sp.GetRequiredService<IAppFileStore>(),
                System.IO.Path.Combine(sp.GetRequiredService<IAppDataService>().AppDataRootPath, "SecureStorage")));
#endif
        services.AddSingleton<IAppSettingsService, AppSettingsService>();
        services.AddSingleton<IMcpSettingsService, McpSettingsService>();
        services.AddSingleton<IAppDataService, AppDataService>();
        services.AddSingleton<IAppMaintenanceService, AppMaintenanceService>();
        services.AddSingleton<IAppDocumentService, AppDocumentService>();
        services.AddSingleton<IAppSupportInfoService>(_ => new AppSupportInfoService(typeof(App).Assembly));
        services.AddSingleton<IConversationStore, ConversationStore>();
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
        services.AddSingleton<IAppStartupService, AppStartupService>();
        services.AddSingleton<IAppLanguageService, AppLanguageService>();
        services.AddSingleton<IConfigurationService, ConfigurationManager>();
        services.AddSingleton<IValidator<ServerConfiguration>, ServerConfigurationValidator>();
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
                ? new PlatformShellService(sp.GetRequiredService<IPlatformCapabilityService>())
                : new UnsupportedPlatformShellService());
#endif
        services.AddSingleton<IStorageLocationService, SalmonEgg.Infrastructure.Services.StorageLocationService>();
        services.AddSingleton<IConversationPreviewStore, ConversationPreviewStore>();
        services.AddSingleton<ISessionExportService, SalmonEgg.Infrastructure.Services.SessionExportService>();
        services.AddSingleton<ILogFileCatalog, SalmonEgg.Infrastructure.Services.LogFileCatalog>();

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
        services.AddSingleton<IAcpConnectionCoordinator>(sp =>
            new AcpConnectionCoordinator(
                sp.GetRequiredService<IChatConnectionStore>(),
                sp.GetRequiredService<ILogger<AcpConnectionCoordinator>>(),
                sp.GetRequiredService<IAcpMcpServerResolver>()));
        services.AddSingleton(sp =>
            AcpConnectionEvictionOptionsLoader.Load(
                sp.GetRequiredService<IAppSettingsService>(),
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
                sp.GetRequiredService<IAcpMcpServerResolver>()));
        services.AddSingleton<IAcpMcpServerProvider>(sp =>
            new SettingsAcpMcpServerProvider(sp.GetRequiredService<IMcpSettingsService>()));
        services.AddSingleton<IAcpMcpServerResolver>(sp =>
            new AcpMcpServerResolver(sp.GetRequiredService<IAcpMcpServerProvider>()));
        services.AddSingleton<IAcpAvailabilityPolicy>(sp =>
            new AppPreferencesAcpAvailabilityPolicy(sp.GetRequiredService<AppPreferencesViewModel>()));

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
        services.AddTransient<ConnectToServerUseCase>();
        services.AddTransient<DisconnectUseCase>();
        services.AddTransient<SendMessageUseCase>();
        services.AddSingleton<IConnectionService, ConnectionService>();
        services.AddSingleton<IMessageService, MessageService>();

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
#if !__WASM__ && !__ANDROID__ && !__IOS__
        services.AddSingleton<ILocalTerminalCwdResolver, LocalTerminalCwdResolver>();
        services.AddSingleton<ILocalTerminalSessionManager, LocalTerminalSessionManager>();
        services.AddSingleton<LocalTerminalPanelCoordinator>();
#endif
        services.AddSingleton<INavigationProjectPreferences>(sp =>
            new NavigationProjectPreferencesAdapter(sp.GetRequiredService<AppPreferencesViewModel>()));
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
                sp.GetRequiredService<IAcpConnectionDependencySnapshotProvider>(),
                sp.GetRequiredService<IAcpAvailabilityPolicy>());
        });
        services.AddSingleton<IErrorRecoveryService>(sp =>
        {
            var pathValidator = sp.GetRequiredService<IPathValidator>();
            var errorLogger = sp.GetRequiredService<IErrorLogger>();
            return new ErrorRecoveryService(
                () => sp.GetRequiredService<ChatViewModel>().CurrentChatService,
                pathValidator,
                errorLogger,
                cancellationToken => sp.GetRequiredService<ChatViewModel>()
                    .ResolveCurrentMcpServersAsync(cancellationToken));
        });

        services.AddSingleton(sp =>
        {
            var lazyNav = new Lazy<INavigationCoordinator>(() => sp.GetRequiredService<INavigationCoordinator>());
            return new ConversationCatalogFacade(
                sp.GetRequiredService<ChatConversationWorkspace>(),
                sp.GetRequiredService<INavigationProjectPreferences>(),
                sp.GetRequiredService<IConversationActivationCoordinator>(),
                sp.GetRequiredService<IShellSelectionReadModel>(),
                lazyNav,
                sp.GetRequiredService<ConversationCatalogPresenter>(),
                sp.GetRequiredService<ILogger<ConversationCatalogFacade>>(),
                sp.GetService<IConversationAttentionStore>(),
                sp.GetService<IConversationPanelCleanup>());
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

        services.AddSingleton<ChatShellViewModel>();
        services.AddSingleton<ShellSessionActivationOverlayViewModel>();
        services.AddSingleton<IDiscoverSessionsConnectionFacade>(sp =>
            new DiscoverSessionsConnectionFacade(
                sp.GetRequiredService<IAcpChatServiceFactory>(),
                sp.GetRequiredService<ITransportSupportPolicy>(),
                sp.GetRequiredService<ILogger<DiscoverSessionsConnectionFacade>>(),
                sp.GetRequiredService<IAcpAvailabilityPolicy>()));
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
                sp.GetRequiredService<IConversationMutationPipeline>()));

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
                sp.GetRequiredService<IPlatformShellService>()));
        services.AddSingleton<INavigationCoordinator>(sp =>
            new NavigationCoordinator(
                sp.GetRequiredService<IShellSelectionMutationSink>(),
                sp.GetRequiredService<IShellNavigationRuntimeState>(),
                sp.GetRequiredService<IConversationSessionSwitcher>(),
                sp.GetRequiredService<IDiscoverSessionsConnectionFacade>(),
                sp.GetRequiredService<INavigationProjectSelectionStore>(),
                sp.GetRequiredService<IShellNavigationService>(),
                sp.GetRequiredService<ILogger<NavigationCoordinator>>()));
        services.AddTransient<IShellStartupNavigationService>(sp =>
            new ShellStartupNavigationService(
                sp.GetRequiredService<INavigationCoordinator>(),
                sp.GetRequiredService<ILogger<ShellStartupNavigationService>>()));

        // Global search
        services.AddSingleton<IGlobalSearchPipeline, DefaultGlobalSearchPipeline>();
        services.AddSingleton<GlobalSearchViewModel>(sp =>
            new GlobalSearchViewModel(
                sp.GetRequiredService<MainNavigationViewModel>(),
                sp.GetRequiredService<AppPreferencesViewModel>(),
                sp.GetRequiredService<INavigationCoordinator>(),
                sp.GetRequiredService<IConversationCatalogReadModel>(),
                sp.GetRequiredService<IProjectAffinityResolver>(),
                sp.GetRequiredService<IGlobalSearchPipeline>(),
                sp.GetRequiredService<IStringLocalizer<CoreStrings>>(),
                sp.GetRequiredService<ILogger<GlobalSearchViewModel>>()));

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
                sp.GetRequiredService<IStringLocalizer<CoreStrings>>()));

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
                sp.GetRequiredService<IStringLocalizer<CoreStrings>>()));
        services.AddSingleton<IChatLaunchWorkflow>(sp =>
            new ChatLaunchWorkflow(
                sp.GetRequiredService<IChatLaunchWorkflowChatFacade>(),
                sp.GetRequiredService<ISessionManager>(),
                sp.GetRequiredService<INavigationCoordinator>(),
                CreateStartCwdResolver(
                    sp.GetRequiredService<MainNavigationViewModel>(),
                    sp.GetRequiredService<AppPreferencesViewModel>()),
                sp.GetRequiredService<ILogger<ChatLaunchWorkflow>>(),
                sp.GetRequiredService<ConversationCatalogFacade>()));

        // App preferences used by General/Appearance settings and window behaviors.
        services.AddSingleton<AppPreferencesViewModel>();
        services.AddSingleton<WindowBackdropService>();

        // General settings
        services.AddSingleton<GeneralSettingsViewModel>();
        services.AddTransient<SettingsShellViewModel>(sp =>
            new SettingsShellViewModel(sp.GetRequiredService<IStringLocalizer<CoreStrings>>()));

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
                sp.GetRequiredService<IStringLocalizer<CoreStrings>>()));


        // ACP connection settings page view model (wraps Chat + Profiles)
        services.AddSingleton<AcpConnectionSettingsViewModel>(sp =>
            new AcpConnectionSettingsViewModel(
                sp.GetRequiredService<ISettingsChatConnection>(),
                sp.GetRequiredService<AcpProfilesViewModel>(),
                sp.GetRequiredService<AppPreferencesViewModel>(),
                sp.GetRequiredService<ITransportSupportPolicy>(),
                sp.GetRequiredService<ILogger<AcpConnectionSettingsViewModel>>(),
                sp.GetRequiredService<IStringLocalizer<CoreStrings>>(),
                sp.GetRequiredService<IUiDispatcher>()));

        // Settings pages (Data/Shortcuts/Diagnostics/About)
        services.AddSingleton<DataStorageSettingsViewModel>();
        services.AddSingleton<McpSettingsViewModel>(sp =>
            new McpSettingsViewModel(
                sp.GetRequiredService<IMcpSettingsService>(),
                sp.GetRequiredService<IPlatformShellService>(),
                sp.GetRequiredService<IStringLocalizer<CoreStrings>>(),
                sp.GetRequiredService<ILogger<McpSettingsViewModel>>()));
        services.AddSingleton<ShortcutsSettingsViewModel>();
        services.AddSingleton<LiveLogViewerViewModel>(sp =>
            new LiveLogViewerViewModel(
                sp.GetRequiredService<ILiveLogStreamService>(),
                sp.GetRequiredService<IAppDataService>().LogsDirectoryPath,
                sp.GetRequiredService<ILogger<LiveLogViewerViewModel>>(),
                sp.GetRequiredService<IUiDispatcher>(),
                sp.GetRequiredService<IStringLocalizer<CoreStrings>>()));
        services.AddSingleton<VoiceInputDiagnosticsProbeViewModel>();
        services.AddSingleton<VoiceInputDiagnosticsViewModel>();
        services.AddSingleton<GamepadDiagnosticsViewModel>();
        services.AddSingleton<DiagnosticsSettingsViewModel>();
        services.AddSingleton<IOpenSourceAcknowledgementsProvider, GeneratedOpenSourceAcknowledgementsProvider>();
        services.AddSingleton<AboutViewModel>();

        // Shell navigation facade (prevents Settings pages from walking the visual tree)
        services.AddSingleton<IShellNavigationService, ShellNavigationService>();

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
        services.AddSingleton<WindowMetricsProvider>();
    }

    private static string GetAppDataPath()
    {
        return SalmonEggPaths.GetAppDataRootPath();
    }

    private static Func<string?> CreateStartCwdResolver(
        MainNavigationViewModel navigationViewModel,
        AppPreferencesViewModel preferences)
    {
        ArgumentNullException.ThrowIfNull(navigationViewModel);
        ArgumentNullException.ThrowIfNull(preferences);

        return () =>
        {
            return StartSessionCwdResolver.Resolve(
                navigationViewModel.ConsumePendingProjectRootPath(),
                preferences.LastSelectedProjectId,
                preferences.Projects,
                preferences.AgentRemoteDirectories);
        };
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
